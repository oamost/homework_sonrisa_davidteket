using WorldAlerts.Api.Collection;
using WorldAlerts.Api.Queue;
using Xunit;

namespace WorldAlerts.Api.Tests;

public sealed class RuntimeAdminOperationsReaderTests
{
    [Fact]
    public async Task Snapshot_reports_real_progress_and_failed_or_uncertain_counts_without_destinations()
    {
        var path = Path.Combine(Path.GetTempPath(), "worldalerts-admin-" + Guid.NewGuid() + ".sqlite");
        var source = new CollectionStateStore(path);
        source.Initialize();
        var queue = new NotificationQueueStore(path);
        queue.Initialize();
        var now = DedupeTestSupport.Now;
        await source.RecordAsync("un-press", "news", now, false, "HttpRequestException", TimeSpan.FromMinutes(15));
        await queue.EnqueueAsync("e", "rev", "local:1", "email", "secret-recipient@example.test", "s", "b", now);
        var clock = new FixedClock(now);
        var snapshot = await new RuntimeAdminOperationsReader(source, queue, clock).ReadAsync(default);
        Assert.Equal("failed", Assert.Single(snapshot.Sources).PollStatus);
        Assert.Equal(1, snapshot.Delivery.Queued);
        Assert.Equal(0, snapshot.Delivery.Failed);
        Assert.DoesNotContain("secret-recipient", System.Text.Json.JsonSerializer.Serialize(snapshot));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
