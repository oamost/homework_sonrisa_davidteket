using Microsoft.AspNetCore.DataProtection;
using WorldAlerts.Api.Delivery.Webhooks;
using System.Net;
using System.Net.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WorldAlerts.Api.Queue;
using WorldAlerts.Api.Admin;
using System.Net.Http.Json;
using System.Text.Json;

namespace WorldAlerts.Api.Tests;

public sealed class WebhookChannelTests
{
    [Fact]
    public async Task Protected_generic_webhook_can_be_configured_without_adding_a_transport_class()
    {
        using var temp = new TempDirectory();
        var path = temp.File("webhooks.sqlite");
        var store = new WebhookSettingsStore(path, DataProtectionProvider.Create("test"));
        var config = new WebhookConfiguration("ops", "Operations", "https://example.org/alerts", "POST",
            new Dictionary<string, string> { ["Authorization"] = "Bearer private-test-token" },
            "{\"text\":\"{{title}}\"}", true);
        Assert.Null(WebhookConfiguration.Validate(config));
        await store.SaveAsync(config);
        var reopened = new WebhookSettingsStore(path, DataProtectionProvider.Create("test"));
        Assert.Single(await reopened.ListAsync());
        Assert.DoesNotContain("private-test-token", File.ReadAllText(path + ".webhooks.protected"));
        Assert.Equal("Operations", WebhookSettingsStore.Redact(config).Label);
        Assert.DoesNotContain("private-test-token", System.Text.Json.JsonSerializer.Serialize(WebhookSettingsStore.Redact(config)));
    }

    [Theory]
    [InlineData("http://example.org/test")]
    [InlineData("https://localhost/test")]
    [InlineData("https://127.0.0.1/test")]
    [InlineData("https://192.168.1.1/test")]
    [InlineData("https://[::1]/test")]
    public void Webhooks_reject_insecure_and_private_destination_literals(string url)
    {
        var config = new WebhookConfiguration("ops", "Operations", url, "POST", [],
            "{\"text\":\"{{title}}\"}", true);
        Assert.NotNull(WebhookConfiguration.Validate(config));
    }

    [Fact]
    public async Task Outbound_validation_rejects_private_dns_and_never_forwards_redirects()
    {
        var handler = new RecordingHandler(HttpStatusCode.Redirect);
        var config = new WebhookConfiguration("ops", "Operations", "https://public.example.org/alerts", "POST", [],
            "{\"text\":\"{{title}}\"}", true);
        var sender = new WebhookSender((_, _) => Task.FromResult(new[] { IPAddress.Loopback }), handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(config, "TEST", "body"));
        Assert.Equal(0, handler.Attempts);
        sender = new WebhookSender((_, _) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") }), handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(config, "TEST", "body"));
        Assert.Equal(1, handler.Attempts);
    }

    [Fact]
    public async Task Configurable_webhook_settings_are_hosted_only_for_admin()
    {
        using var temp = new TempDirectory();
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting("WorldAlerts:DatabasePath", temp.File("webhooks.sqlite"))
            .UseSetting("WorldAlerts:News:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Markets:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Disasters:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Advisories:PollingEnabled", "false"));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/webhooks")).StatusCode);
    }

    [Fact]
    public async Task Webhook_only_configuration_dispatches_pending_job_at_startup()
    {
        using var temp = new TempDirectory();
        var path = temp.File("webhook-startup.sqlite");
        var queue = new NotificationQueueStore(path); queue.Initialize();
        await queue.EnqueueAsync("test", "r1", "local:configured:webhook:ops", "webhook", "ops",
            "TEST", "body", DateTimeOffset.UtcNow);
        var protection = DataProtectionProvider.Create(new DirectoryInfo(path + ".keys"),
            options => options.SetApplicationName("WorldAlerts.Email"));
        await new WebhookSettingsStore(path, protection).SaveAsync(new WebhookConfiguration(
            "ops", "Operations", "https://example.org/alerts", "POST", [], "{\"text\":\"{{title}}\"}", true));
        var handler = new RecordingHandler(HttpStatusCode.OK);
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting("WorldAlerts:DatabasePath", path)
            .UseSetting("WorldAlerts:News:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Markets:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Disasters:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Advisories:PollingEnabled", "false")
            .ConfigureTestServices(services => services.AddSingleton(new WebhookSender((_, _) =>
                Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") }), handler))));
        using var client = factory.CreateClient();
        for (var i = 0; i < 50 && handler.Attempts == 0; i++) await Task.Delay(20);
        Assert.Equal(1, handler.Attempts);
        Assert.Equal("sent", Assert.Single(await queue.SnapshotAsync()).State);
    }

    [Fact]
    public async Task Admin_saves_redacted_config_and_explicit_test_sends_once()
    {
        using var temp = new TempDirectory();
        var secret = "private-token-do-not-echo";
        var handler = new RecordingHandler(HttpStatusCode.OK);
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting("WorldAlerts:DatabasePath", temp.File("admin-webhooks.sqlite"))
            .UseSetting("WorldAlerts:Admin:PasswordHash", AdminPassword.Hash("test-admin-password"))
            .UseSetting("WorldAlerts:News:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Markets:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Disasters:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Advisories:PollingEnabled", "false")
            .ConfigureTestServices(services => services.AddSingleton(new WebhookSender((_, _) =>
                Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") }), handler))));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true });
        var loginHtml = await client.GetStringAsync("/admin/login");
        var loginToken = System.Text.RegularExpressions.Regex.Match(loginHtml, "name='csrf' value=\"([^\"]+)\"").Groups[1].Value;
        using var login = await client.PostAsync("/admin/login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["csrf"] = loginToken, ["password"] = "test-admin-password" }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        using var status = await client.GetAsync("/api/admin/webhooks");
        var csrf = JsonDocument.Parse(await status.Content.ReadAsStringAsync()).RootElement.GetProperty("csrfToken").GetString()!;
        var config = new WebhookConfiguration("ops", "Operations", "https://example.org/alerts", "POST",
            new Dictionary<string, string> { ["Authorization"] = secret }, "{\"text\":\"{{title}}\"}", true);
        using var missingCsrf = await client.PutAsJsonAsync("/api/admin/webhooks", config);
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        using var put = new HttpRequestMessage(HttpMethod.Put, "/api/admin/webhooks") { Content = JsonContent.Create(config) };
        put.Headers.Add("X-CSRF-TOKEN", csrf);
        using var saved = await client.SendAsync(put);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.DoesNotContain(secret, await saved.Content.ReadAsStringAsync());
        Assert.DoesNotContain("example.org", await saved.Content.ReadAsStringAsync());
        Assert.Equal(0, handler.Attempts);
        using var malformed = new HttpRequestMessage(HttpMethod.Put, "/api/admin/webhooks")
        { Content = JsonContent.Create(new { id = "ops", label = "Operations", url = "", method = "POST",
            headers = (object?)null, jsonTemplate = "{}", enabled = true }) };
        malformed.Headers.Add("X-CSRF-TOKEN", csrf);
        using var invalid = await client.SendAsync(malformed);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var test = new HttpRequestMessage(HttpMethod.Post, "/api/admin/webhooks/test")
        { Content = JsonContent.Create(new { id = "ops", consent = true }) };
        test.Headers.Add("X-CSRF-TOKEN", csrf);
        using var accepted = await client.SendAsync(test);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(1, handler.Attempts);
    }

    private sealed class RecordingHandler(HttpStatusCode response) : HttpMessageHandler
    {
        public int Attempts { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            return Task.FromResult(new HttpResponseMessage(response)
            { Headers = { Location = new Uri("https://127.0.0.1/private") } });
        }
    }
}
