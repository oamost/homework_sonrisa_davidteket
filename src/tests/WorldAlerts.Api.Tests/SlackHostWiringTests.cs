using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WorldAlerts.Api.Delivery;
using WorldAlerts.Api.Delivery.Slack;
using WorldAlerts.Api.Queue;

namespace WorldAlerts.Api.Tests;

public sealed class SlackHostWiringTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Slack_configuration_is_hosted_but_requires_admin_login()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting("WorldAlerts:DatabasePath", _temp.File("app.sqlite"))
            .UseSetting("WorldAlerts:News:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Markets:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Disasters:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Advisories:PollingEnabled", "false"));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var response = await client.GetAsync("/api/admin/slack");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Slack_queue_transport_sends_only_after_webhook_is_configured()
    {
        var store = new SlackSettingsStore(_temp.File("send.sqlite"), DataProtectionProvider.Create("test"));
        var sender = new RecordingSlackSender();
        var transport = new SlackNotificationTransport(store, sender);
        var job = new NotificationJob(1, "test", "r1", "local:1", "slack", "", "TEST", "not real news",
            "pending", 0, DateTimeOffset.UtcNow, null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => transport.SendAsync(job));
        Assert.Empty(sender.Messages);
        await store.SaveAsync(new SlackSettings("https://hooks.slack.com/services/T000/B000/TEST", true));
        await transport.SendAsync(job);
        Assert.Equal("TEST\nnot real news", Assert.Single(sender.Messages));
    }

    [Fact]
    public async Task Dispatching_registered_channels_routes_slack_without_email_settings()
    {
        var store = new SlackSettingsStore(_temp.File("shared.sqlite"), DataProtectionProvider.Create("test"));
        await store.SaveAsync(new SlackSettings("https://hooks.slack.com/services/T000/B000/TEST", true));
        var sender = new RecordingSlackSender();
        var transport = new RegisteredNotificationTransport(new SlackNotificationTransport(store, sender));
        var job = new NotificationJob(1, "test", "r1", "local:1", "slack", "", "TEST", "not real news",
            "pending", 0, DateTimeOffset.UtcNow, null);
        await transport.SendAsync(job);
        Assert.Single(sender.Messages);
    }

    [Fact]
    public async Task Slack_job_dispatches_on_startup_when_only_slack_is_configured()
    {
        var path = _temp.File("startup.sqlite");
        var queue = new NotificationQueueStore(path);
        queue.Initialize();
        await queue.EnqueueAsync("synthetic", "first", "local:slack", "slack", "", "TEST", "test body", DateTimeOffset.UtcNow);
        var protection = DataProtectionProvider.Create(new DirectoryInfo(path + ".keys"),
            options => options.SetApplicationName("WorldAlerts.Email"));
        await new SlackSettingsStore(path, protection).SaveAsync(
            new SlackSettings("https://hooks.slack.com/services/T000/B000/TEST", true));
        var sender = new RecordingSlackSender();
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting("WorldAlerts:DatabasePath", path)
            .UseSetting("WorldAlerts:News:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Markets:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Disasters:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Advisories:PollingEnabled", "false")
            .ConfigureTestServices(services => services.AddSingleton<ISlackSender>(sender)));
        using var client = factory.CreateClient();
        for (var i = 0; i < 50 && sender.Messages.Count == 0; i++)
            await Task.Delay(20);
        Assert.Equal("TEST\ntest body", Assert.Single(sender.Messages));
        Assert.Equal("sent", Assert.Single(await queue.SnapshotAsync()).State);
    }

    private sealed class RecordingSlackSender : ISlackSender
    {
        public List<string> Messages { get; } = [];
        public Task SendAsync(string text, CancellationToken cancellationToken = default)
        {
            Messages.Add(text);
            return Task.CompletedTask;
        }
    }
}
