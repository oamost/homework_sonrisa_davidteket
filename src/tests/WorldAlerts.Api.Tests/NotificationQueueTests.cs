using WorldAlerts.Api.Queue;
using Xunit;

namespace WorldAlerts.Api.Tests;

public sealed class NotificationQueueTests
{
    [Fact]
    public async Task Immediate_test_claims_only_synthetic_jobs_not_real_news()
    {
        using var temp = new TempDirectory();
        var store = new NotificationQueueStore(temp.File("test-only.sqlite"));
        store.Initialize();
        var now = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        await store.EnqueueAsync("real:story", "v1", "test", "email", "real@example.test", "real", "body", now);
        await store.EnqueueAsync("synthetic-test:3", "test", "test", "slack", "", "TEST", "body", now);
        var claimed = await store.ClaimAsync(now, 50, startupBurst: true, testOnly: true);
        Assert.Single(claimed);
        Assert.Equal("synthetic-test:3", claimed[0].IdentityKey);
        var pending = Assert.Single(await store.SnapshotAsync(), j => j.IdentityKey == "real:story");
        Assert.Equal("pending", pending.State);
    }

    [Fact]
    public async Task Restart_preserves_ack_and_quarantines_unacknowledged_external_send()
    {
        var path = Path.Combine(Path.GetTempPath(), "worldalerts-queue-" + Guid.NewGuid() + ".sqlite");
        var now = DedupeTestSupport.Now;
        var first = new NotificationQueueStore(path);
        first.Initialize();
        await first.EnqueueAsync("event", "revision", "local:1", "email", "person@example.test", "subject", "body", now);
        await first.EnqueueAsync("event2", "revision", "local:2", "slack", "", "subject", "body", now);
        Assert.Equal(2, (await first.ClaimAsync(now, 50)).Count);
        var reopened = new NotificationQueueStore(path);
        reopened.Initialize();
        Assert.Empty(await reopened.ClaimAsync(now.AddMinutes(1), 50));
        var states = await reopened.SnapshotAsync();
        Assert.Equal(2, states.Count(s => s.State == "uncertain"));
        Assert.Equal(0, states.Count(s => s.State == "sent"));
    }

    [Fact]
    public async Task Retry_is_bounded_at_one_five_fifteen_minutes_after_first_failure()
    {
        var path = Path.Combine(Path.GetTempPath(), "worldalerts-queue-" + Guid.NewGuid() + ".sqlite");
        var store = new NotificationQueueStore(path);
        store.Initialize();
        var now = DedupeTestSupport.Now;
        await store.EnqueueAsync("event", "revision", "local:1", "email", "person@example.test", "subject", "body", now);
        var item = Assert.Single(await store.ClaimAsync(now, 1));
        foreach (var minutes in new[] { 1, 5, 15 })
        {
            await store.FailAsync(item.Id, now, "TransportException");
            Assert.Empty(await store.ClaimAsync(now.AddMinutes(minutes).AddSeconds(-1), 1));
            now = now.AddMinutes(minutes);
            item = Assert.Single(await store.ClaimAsync(now, 1));
        }
        await store.FailAsync(item.Id, now, "TransportException");
        Assert.Empty(await store.ClaimAsync(now.AddHours(1), 1));
        Assert.Equal("failed", Assert.Single(await store.SnapshotAsync()).State);
    }

    [Fact]
    public async Task Claimed_messages_remain_paced_across_process_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), "worldalerts-queue-" + Guid.NewGuid() + ".sqlite");
        var store = new NotificationQueueStore(path);
        store.Initialize();
        var now = DedupeTestSupport.Now;
        for (var i = 0; i < 70; i++)
            await store.EnqueueAsync($"event{i}", "rev", $"local:{i}", "email", "person@example.test", "subject", "body", now);
        var burst = await store.ClaimAsync(now, 50, startupBurst: true);
        Assert.Equal(50, burst.Count);
        foreach (var item in burst) await store.AcknowledgeAsync(item.Id, now);
        var reopened = new NotificationQueueStore(path);
        reopened.Initialize();
        Assert.Equal(10, (await reopened.ClaimAsync(now, 10)).Count);
        Assert.Empty(await reopened.ClaimAsync(now.AddSeconds(59), 10));
        Assert.Equal(10, (await reopened.ClaimAsync(now.AddMinutes(1), 10)).Count);
    }
}
