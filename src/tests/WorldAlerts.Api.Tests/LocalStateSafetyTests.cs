using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorldAlerts.Api.Delivery;
using WorldAlerts.Api.Queue;

namespace WorldAlerts.Api.Tests;

/// <summary>
/// Task 25 local-state safety: one persistent SQLite file plus the persistent
/// DataProtection key ring next to it must survive a full host restart, using
/// only offline temp files (no production database, no SMTP/Slack sends).
/// </summary>
public sealed class LocalStateSafetyTests : IDisposable
{
    private const string Secret = "local-keyring-secret-25";

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task A_full_host_restart_preserves_subscriptions_queue_jobs_and_protected_email_settings()
    {
        var databasePath = _temp.File("alerts.sqlite");
        var keyring = new DirectoryInfo(databasePath + ".keys");

        // Persist the protected SMTP settings with one provider instance that
        // uses Program's exact layout (key ring beside the database, same
        // application name), then fully dispose it: the keys must live on disk,
        // not in memory, for the hosts below to decrypt afterwards.
        var (settings, validationError) = EmailSettingsStore.Validate(Update(true), null);
        Assert.Null(validationError);
        await using (var providerScope = BuildPersistentProviderScope(keyring))
        {
            await new EmailSettingsStore(databasePath,
                providerScope.GetRequiredService<IDataProtectionProvider>())
                .SaveAsync(settings!);
        }

        var fake = new FakeSmtp();
        using (var first = CreateApi(databasePath, fake, "1"))
        using (var client = first.CreateClient())
        {
            // The first host's own provider instance must decrypt a setting
            // written by a different provider instance from on-disk keys.
            var hostSettings = first.Services.GetRequiredService<EmailSettingsStore>();
            Assert.Equal(Secret, (await hostSettings.ReadAsync())!.Password);
            Assert.DoesNotContain(Secret, File.ReadAllText(databasePath + ".email.protected"));

            var created = await client.PostAsJsonAsync("/api/subscriptions", new
            {
                eventType = "wildfire",
                channel = "  EMAIL  ",
                destination = "channel:#alerts",
            });
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);

            var queue = new NotificationQueueStore(databasePath);
            queue.Initialize();
            await queue.EnqueueAsync("localstate:1", "revision:1", "local:1", "email",
                "alerts@example.org", "Restart", "Persisted across restart", DateTimeOffset.UtcNow);
        }

        // "Normal restart": a brand-new host over the same file and key ring.
        using (var second = CreateApi(databasePath, fake, "2"))
        using (var client = second.CreateClient())
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);

            // Protected settings remain decryptable after the restart.
            var hostSettings = second.Services.GetRequiredService<EmailSettingsStore>();
            var restored = await hostSettings.ReadAsync();
            Assert.Equal(Secret, restored!.Password);
            Assert.True(restored.Enabled);

            // The subscription created over HTTP before the restart survives.
            var response = await client.GetAsync("/api/subscriptions");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("wildfire", Assert.Single(document.RootElement.EnumerateArray()).GetProperty("eventType").GetString());

            // The queued job persists in some consistent state (its state
            // identity may legitimately advance through the worker, but it
            // may not vanish).
            var queue = new NotificationQueueStore(databasePath);
            queue.Initialize();
            var jobs = Assert.Single(await queue.SnapshotAsync(), job => job.IdentityKey == "localstate:1");
            Assert.True(jobs.State is "pending" or "sent", $"unexpected queue state: {jobs.State}");
        }

        // Whatever the worker did went to the fake transport, never real SMTP.
        foreach (var sent in fake.Sent)
            Assert.Equal("alerts@example.org", sent);
    }

    [Fact]
    public async Task Protected_email_settings_round_trip_through_a_persistent_keyring_and_are_owner_only_on_unix()
    {
        var databasePath = _temp.File("alerts.sqlite");
        var keyring = new DirectoryInfo(databasePath + ".keys");

        var (settings, validationError) = EmailSettingsStore.Validate(Update(true), null);
        Assert.Null(validationError);

        await using (var writerScope = BuildPersistentProviderScope(keyring))
        {
            await new EmailSettingsStore(databasePath,
                writerScope.GetRequiredService<IDataProtectionProvider>())
                .SaveAsync(settings!);
        }

        var protectedPath = databasePath + ".email.protected";
        Assert.True(File.Exists(protectedPath));
        var ciphertext = File.ReadAllText(protectedPath);
        Assert.DoesNotContain(Secret, ciphertext, StringComparison.Ordinal);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            // EmailSettingsStore writes the protected file owner-only (0600).
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(protectedPath));
        }

        // A second provider instance, built from the same on-disk key ring,
        // must decrypt the saved password — this cannot pass from shared
        // in-memory keys because the first provider was disposed.
        await using (var readerScope = BuildPersistentProviderScope(keyring))
        {
            Assert.Equal(Secret, (await new EmailSettingsStore(databasePath,
                readerScope.GetRequiredService<IDataProtectionProvider>())
                .ReadAsync())!.Password);
        }
    }

    private static EmailSettingsUpdate Update(bool enabled) =>
        new("smtp.example.org", 587, "StartTls", "sender", Secret, "alerts@example.org", enabled);

    /// <summary>
    /// Builds a persistent provider scope matching Program's registration:
    /// the key ring folder is next to the database and the application name
    /// is "WorldAlerts.Email", so protector identities match the host's.
    /// </summary>
    private static ServiceProvider BuildPersistentProviderScope(DirectoryInfo keyring)
    {
        var services = new ServiceCollection();
        services.AddDataProtection()
            .SetApplicationName("WorldAlerts.Email")
            .PersistKeysToFileSystem(keyring);
        return services.BuildServiceProvider();
    }

    private sealed class FakeSmtp : ISmtpTransport
    {
        public List<string> Sent { get; } = [];

        public Task SendAsync(MailMessage message, SmtpOptions options, CancellationToken cancellationToken = default)
        {
            Sent.Add(message.To[0].Address);
            return Task.CompletedTask;
        }
    }

    private static WebApplicationFactory<Program> CreateApi(string databasePath, FakeSmtp fake, string round) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(
            builder => builder
                .UseSetting("WorldAlerts:DatabasePath", databasePath)
                .UseSetting("WorldAlerts:LocalStateSafety:Round", round)
                .UseSetting("WorldAlerts:News:PollingEnabled", "false")
                .UseSetting("WorldAlerts:Markets:PollingEnabled", "false")
                .UseSetting("WorldAlerts:Disasters:PollingEnabled", "false")
                .UseSetting("WorldAlerts:Advisories:PollingEnabled", "false")
                .ConfigureServices(services =>
                {
                    services.RemoveAll<ISmtpTransport>();
                    services.AddSingleton<ISmtpTransport>(fake);
                }));
}
