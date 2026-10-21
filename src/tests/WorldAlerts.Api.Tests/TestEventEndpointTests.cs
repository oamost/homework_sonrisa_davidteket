using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.DataProtection;
using WorldAlerts.Api.Delivery;
using WorldAlerts.Api.Delivery.Slack;
using WorldAlerts.Api.Delivery.Webhooks;
using WorldAlerts.Api.Queue;
using WorldAlerts.Api.Subscriptions;

namespace WorldAlerts.Api.Tests;

public sealed class TestEventEndpointTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Public_test_requires_same_origin_csrf_and_rate_limits_repeat_requests()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting("WorldAlerts:DatabasePath", _temp.File("test-events.sqlite"))
            .UseSetting("WorldAlerts:News:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Markets:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Disasters:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Advisories:PollingEnabled", "false"));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        using var tokenResponse = await client.GetAsync("/api/test-events/token");
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        var token = (await tokenResponse.Content.ReadFromJsonAsync<TestToken>())!.CsrfToken;
        using var noToken = await client.PostAsJsonAsync("/api/test-events", new { });
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        using var crossOrigin = new HttpRequestMessage(HttpMethod.Post, "/api/test-events")
        { Content = JsonContent.Create(new { }) };
        crossOrigin.Headers.Add("Origin", "https://attacker.example");
        crossOrigin.Headers.Add("X-CSRF-TOKEN", token);
        using var rejected = await client.SendAsync(crossOrigin);
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        using var first = new HttpRequestMessage(HttpMethod.Post, "/api/test-events")
        { Content = JsonContent.Create(new { }) };
        first.Headers.Add("X-CSRF-TOKEN", token);
        using var accepted = await client.SendAsync(first);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var second = new HttpRequestMessage(HttpMethod.Post, "/api/test-events")
        { Content = JsonContent.Create(new { }) };
        second.Headers.Add("X-CSRF-TOKEN", token);
        using var limited = await client.SendAsync(second);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        var preview = await client.GetStringAsync("/api/news/important");
        Assert.Contains("TEST — not real news", preview);
    }

    [Fact]
    public async Task Synthetic_test_respects_saved_world_news_and_queues_each_configured_channel()
    {
        var path = _temp.File("routing.sqlite");
        var prefs = new EventTypePreferenceStore(path); prefs.Initialize();
        var subscriptions = new SubscriptionStore(path); subscriptions.Initialize();
        var protection = DataProtectionProvider.Create("test");
        var emails = new EmailSettingsStore(path, protection);
        await emails.SaveAsync(new EmailSettings("smtp.example.test", 587, "StartTls", "sender", "secret",
            "sender@example.test", true) { RecipientAddress = "recipient@example.test" });
        var slacks = new SlackSettingsStore(path, protection);
        await slacks.SaveAsync(new SlackSettings("https://hooks.slack.com/services/T/B/TEST1234", true));
        var webhooks = new WebhookSettingsStore(path, protection);
        await webhooks.SaveAsync(new WebhookConfiguration("ops", "Operations", "https://example.org/hook", "POST",
            [], "{\"text\":\"{{title}}\"}", true));
        var queue = new NotificationQueueStore(path); queue.Initialize();
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero));
        var service = new TestEventService(path, prefs, subscriptions, emails, slacks, queue, clock, webhooks);
        service.Initialize();
        await prefs.SaveAsync(Subscription.DefaultUserId, []);
        Assert.Contains("no notification", (await service.EmitAsync()).Message);
        Assert.Empty(await queue.SnapshotAsync());
        clock.UtcNow = clock.UtcNow.AddMinutes(2);
        await prefs.SaveAsync(Subscription.DefaultUserId, ["world-news"]);
        Assert.Contains("queued", (await service.EmitAsync()).Message);
        var jobs = await queue.SnapshotAsync();
        Assert.Equal(new[] { "email", "slack", "webhook" }, jobs.Select(job => job.Channel).OrderBy(x => x));
        Assert.Contains(jobs, job => job.Channel == "webhook" && job.Destination == "ops");
    }

    [Fact]
    public async Task Test_event_does_not_queue_enabled_legacy_row_without_channel_configuration()
    {
        var path = _temp.File("legacy-unconfigured.sqlite");
        var prefs = new EventTypePreferenceStore(path); prefs.Initialize();
        var subscriptions = new SubscriptionStore(path); subscriptions.Initialize();
        await subscriptions.UpsertAsync(new Subscription { EventType = "world-news", Channel = "slack",
            Destination = "channel:#alerts", Enabled = true });
        var protection = DataProtectionProvider.Create("test");
        var queue = new NotificationQueueStore(path); queue.Initialize();
        var service = new TestEventService(path, prefs, subscriptions,
            new EmailSettingsStore(path, protection), new SlackSettingsStore(path, protection),
            queue, new MutableTimeProvider(DateTimeOffset.UtcNow));
        service.Initialize();
        Assert.Contains("no configured channel", (await service.EmitAsync()).Message);
        Assert.Empty(await queue.SnapshotAsync());
    }

    private sealed record TestToken(string CsrfToken);
    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
