using WorldAlerts.Api.Dedupe;
using WorldAlerts.Api.Disasters;
using WorldAlerts.Api.Markets;
using WorldAlerts.Api.News;
using WorldAlerts.Api.Queue;
using WorldAlerts.Api.Selection;
using WorldAlerts.Api.Subscriptions;
using Xunit;

namespace WorldAlerts.Api.Tests;

public sealed class AlertPipelineTests
{
    [Fact]
    public async Task Selected_late_and_disaster_events_enqueue_only_matching_recipients_once_across_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), "worldalerts-pipeline-" + Guid.NewGuid() + ".sqlite");
        var now = DedupeTestSupport.Now;
        var news = new NewsStore(path); news.Initialize();
        var disasters = new DisasterStore(path); disasters.Initialize();
        var markets = new MarketStore(path); markets.Initialize();
        var subscriptions = new SubscriptionStore(path); subscriptions.Initialize();
        await subscriptions.UpsertAsync(new Subscription { EventType = "world-news", Channel = "email", Destination = "person@example.test" });
        await subscriptions.UpsertAsync(new Subscription { EventType = "disaster.earthquake", Channel = "slack" });
        await news.InsertAsync(new[] { new NewsItem("bbc-world", "BBC World", "late-1", "Breaking conflict",
            "https://example.test/late", now.AddHours(-1), now, "world-news", [], "https://example.test/late", null) });
        await disasters.InsertAsync(new[] { new DisasterEvent("gdacs", "quake-1", "earthquake", "Major quake",
            now.AddHours(-2), now.AddHours(-1), null, null, null, "Chile", "CHL", 7.1,
            "magnitude", "Orange", null, false, null, "https://example.test/quake", now) });
        var dedupe = new DedupeStore(path); dedupe.Initialize();
        var queue = new NotificationQueueStore(path); queue.Initialize();
        var selection = new AlertSelectionService(news, markets, disasters, subscriptions,
            new MarketOptions(), new FixedClock(now));
        var pipeline = new AlertPipeline(selection, news, markets, disasters, subscriptions,
            new EventDedupeService(dedupe, () => now), queue, new FixedClock(now));
        Assert.Equal(2, await pipeline.EnqueueSelectedAsync());
        Assert.Equal(0, await pipeline.EnqueueSelectedAsync());
        Assert.Equal(0, await new AlertPipeline(selection, news, markets, disasters, subscriptions,
            new EventDedupeService(new DedupeStore(path), () => now), new NotificationQueueStore(path),
            new FixedClock(now)).EnqueueSelectedAsync());
        var jobs = await queue.SnapshotAsync();
        Assert.Equal(new[] { "email", "slack" }, jobs.Select(x => x.Channel).OrderBy(x => x).ToArray());
        Assert.DoesNotContain(jobs, x => x.Destination.Contains("hooks.", StringComparison.Ordinal));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
