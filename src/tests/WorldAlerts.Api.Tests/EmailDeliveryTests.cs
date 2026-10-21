using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorldAlerts.Api.Admin;
using WorldAlerts.Api.Delivery;
using WorldAlerts.Api.Queue;

namespace WorldAlerts.Api.Tests;

public sealed class EmailDeliveryTests
{
    private const string Password = "local-test-password";
    private const string Secret = "test-smtp-secret-private";

    [Fact]
    public async Task Recipient_is_readable_and_persisted_while_smtp_password_stays_protected()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "recipient.sqlite");
        var store = new EmailSettingsStore(path, DataProtectionProvider.Create("test"));
        var (settings, error) = EmailSettingsStore.Validate(Update(true) with
        { RecipientAddress = "alerts@example.test" }, null);
        Assert.Null(error);
        await store.SaveAsync(settings!);
        var status = EmailSettingsStore.Redact(await store.ReadAsync());
        Assert.Equal("alerts@example.test", status.RecipientAddress);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(status));
        Assert.NotNull(EmailSettingsStore.Validate(Update(true) with
        { RecipientAddress = "wrong@" }, await store.ReadAsync()).Error);
    }

    [Fact]
    public async Task Unconfigured_host_starts_without_sending_or_claiming_pending_email()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "alerts.sqlite");
        var queue = new NotificationQueueStore(path);
        queue.Initialize();
        await queue.EnqueueAsync("native:startup", "revision", "local:1", "email", "test@example.org",
            "Alert", "Body", DateTimeOffset.UtcNow);
        var fake = new FakeSmtp();
        using var factory = new EmailFactory(path, fake);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
        Assert.Empty(fake.Sent);
        Assert.Equal("pending", Assert.Single(await new NotificationQueueStore(path).SnapshotAsync()).State);
    }

    [Fact]
    public async Task Private_settings_encrypt_reopen_and_redact_without_password_in_file_or_status()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "alerts.sqlite");
        var provider = DataProtectionProvider.Create(new DirectoryInfo(path + ".keys"));
        var first = new EmailSettingsStore(path, provider);
        var (settings, error) = EmailSettingsStore.Validate(Update(true), null);
        Assert.Null(error);
        await first.SaveAsync(settings!);
        var second = new EmailSettingsStore(path, provider);
        Assert.Equal(Secret, (await second.ReadAsync())!.Password);
        Assert.DoesNotContain(Secret, File.ReadAllText(path + ".email.protected"));
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(EmailSettingsStore.Redact(await second.ReadAsync())));
        Assert.Null(EmailSettingsStore.Validate(Update(false) with { Password = "" }, await second.ReadAsync()).Error);
    }

    [Fact]
    public async Task Http_endpoints_require_admin_and_csrf_and_only_send_explicitly_with_fake_transport()
    {
        using var temp = new TempDirectory();
        var fake = new FakeSmtp();
        using var factory = new EmailFactory(Path.Combine(temp.Path, "alerts.sqlite"), fake);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/email")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/admin/email/test", new { destination = "test@example.org" })).StatusCode);
        var loginPage = await client.GetStringAsync("/admin/login");
        var csrf = System.Text.RegularExpressions.Regex.Match(loginPage, "name='csrf' value=\"([^\"]+)\"").Groups[1].Value;
        var login = await client.PostAsync("/admin/login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["csrf"] = csrf, ["password"] = Password }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var statusResponse = await client.GetAsync("/api/admin/email");
        var status = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());
        var token = status.RootElement.GetProperty("csrfToken").GetString()!;
        Assert.False(status.RootElement.GetProperty("configured").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/admin/email", Update(true))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/admin/email/test", new { destination = "test@example.org" })).StatusCode);
        using var save = new HttpRequestMessage(HttpMethod.Put, "/api/admin/email")
        { Content = JsonContent.Create(Update(true) with { RecipientAddress = "reader@example.org" }) };
        save.Headers.Add("X-CSRF-TOKEN", token);
        var saved = await client.SendAsync(save);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.DoesNotContain(Secret, await saved.Content.ReadAsStringAsync());
        using var reloaded = await client.GetAsync("/api/admin/email");
        using var reloadedJson = JsonDocument.Parse(await reloaded.Content.ReadAsStringAsync());
        Assert.Equal("reader@example.org", reloadedJson.RootElement.GetProperty("recipientAddress").GetString());
        Assert.Equal("sender", reloadedJson.RootElement.GetProperty("username").GetString());
        Assert.Empty(fake.Sent);
        using var test = new HttpRequestMessage(HttpMethod.Post, "/api/admin/email/test")
        { Content = JsonContent.Create(new { destination = "test@example.org" }) };
        test.Headers.Add("X-CSRF-TOKEN", token);
        var result = await client.SendAsync(test);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Single(fake.Sent);
        Assert.Equal("test@example.org", fake.Sent[0]);
        fake.Failure = true;
        using var failed = new HttpRequestMessage(HttpMethod.Post, "/api/admin/email/test")
        { Content = JsonContent.Create(new { destination = "test@example.org" }) };
        failed.Headers.Add("X-CSRF-TOKEN", token);
        var failure = await client.SendAsync(failed);
        Assert.Equal(HttpStatusCode.BadGateway, failure.StatusCode);
        Assert.DoesNotContain(Secret, await failure.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Dispatcher_sends_only_enabled_email_and_does_not_duplicate_known_sent_after_restart()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "alerts.sqlite");
        var settings = new EmailSettingsStore(path, DataProtectionProvider.Create(new DirectoryInfo(path + ".keys")));
        var fake = new FakeSmtp();
        var queue = new NotificationQueueStore(path);
        queue.Initialize();
        var now = DateTimeOffset.UtcNow;
        await queue.EnqueueAsync("native:1", "revision", "local:1", "email", "test@example.org", "Alert", "Body", now);
        await queue.EnqueueAsync("native:2", "revision", "local:2", "slack", "", "Alert", "Body", now);
        // The worker does not claim jobs until the administrator has enabled SMTP.
        Assert.Null(await settings.ReadAsync());
        Assert.Empty(fake.Sent);
        await settings.SaveAsync(EmailSettingsStore.Validate(Update(true), null).Settings!);
        var dispatcher = new NotificationDispatcher(queue, new EmailNotificationTransport(settings, fake));
        Assert.Equal(1, await dispatcher.DispatchAsync(now, startupBurst: true, emailOnly: true));
        Assert.Single(fake.Sent);
        var reopened = new NotificationQueueStore(path);
        reopened.Initialize();
        Assert.Equal(0, await new NotificationDispatcher(reopened, new EmailNotificationTransport(settings, fake))
            .DispatchAsync(now.AddMinutes(1), emailOnly: true));
        Assert.Single(fake.Sent);
        Assert.Contains((await reopened.SnapshotAsync()), item => item.Channel == "email" && item.State == "sent");
        Assert.Contains((await reopened.SnapshotAsync()), item => item.Channel == "slack" && item.State == "pending");
    }

    private static EmailSettingsUpdate Update(bool enabled) =>
        new("smtp.example.org", 587, "StartTls", "sender", Secret, "alerts@example.org", enabled);

    private sealed class FakeSmtp : ISmtpTransport
    {
        public List<string> Sent { get; } = [];
        public bool Failure { get; set; }
        public Task SendAsync(MailMessage message, SmtpOptions options, CancellationToken cancellationToken = default)
        {
            if (Failure) throw new InvalidOperationException("private " + Secret);
            Sent.Add(message.To[0].Address);
            return Task.CompletedTask;
        }
    }

    private sealed class EmailFactory(string databasePath, FakeSmtp fake) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("WorldAlerts:Admin:PasswordHash", AdminPassword.Hash(Password));
            builder.UseSetting("WorldAlerts:DatabasePath", databasePath);
            builder.UseSetting("WorldAlerts:News:PollingEnabled", "false");
            builder.UseSetting("WorldAlerts:Markets:PollingEnabled", "false");
            builder.UseSetting("WorldAlerts:Disasters:PollingEnabled", "false");
            builder.UseSetting("WorldAlerts:Advisories:PollingEnabled", "false");
            builder.ConfigureServices(services => { services.RemoveAll<ISmtpTransport>(); services.AddSingleton<ISmtpTransport>(fake); });
        }
    }
}
