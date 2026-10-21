using WorldAlerts.Api.Queue;
using Xunit;

namespace WorldAlerts.Api.Tests;

public sealed class NotificationDispatchTests
{
    [Fact]
    public async Task Pending_email_and_slack_are_sent_and_not_replayed_after_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), "worldalerts-dispatch-" + Guid.NewGuid() + ".sqlite");
        var queue = new NotificationQueueStore(path);
        queue.Initialize();
        var now = DedupeTestSupport.Now;
        await queue.EnqueueAsync("event", "v1", "local:1", "email", "person@example.test", "Flood", "Details", now);
        await queue.EnqueueAsync("event", "v1", "local:2", "slack", "", "Flood", "Details", now);
        var fake = new RecordingTransport();
        var dispatcher = new NotificationDispatcher(queue, fake);
        Assert.Equal(2, await dispatcher.DispatchAsync(now, startupBurst: true));
        Assert.Equal(new[] { "email", "slack" }, fake.Channels.OrderBy(x => x));
        queue = new NotificationQueueStore(path);
        queue.Initialize();
        Assert.Equal(0, await new NotificationDispatcher(queue, fake).DispatchAsync(now.AddMinutes(1)));
        Assert.Equal(2, (await queue.SnapshotAsync()).Count(x => x.State == "sent"));
    }

    [Fact]
    public async Task Temporary_transport_error_is_retried_without_secret_in_failure_state()
    {
        var path = Path.Combine(Path.GetTempPath(), "worldalerts-dispatch-" + Guid.NewGuid() + ".sqlite");
        var queue = new NotificationQueueStore(path);
        queue.Initialize();
        var now = DedupeTestSupport.Now;
        await queue.EnqueueAsync("event", "v1", "local:1", "slack", "", "subject", "body", now);
        var fake = new RecordingTransport { Fail = true };
        var dispatcher = new NotificationDispatcher(queue, fake);
        Assert.Equal(1, await dispatcher.DispatchAsync(now));
        Assert.Equal("pending", Assert.Single(await queue.SnapshotAsync()).State);
        Assert.Equal("InvalidOperationException", Assert.Single(await queue.SnapshotAsync()).FailureType);
        fake.Fail = false;
        Assert.Equal(0, await dispatcher.DispatchAsync(now.AddSeconds(59)));
        Assert.Equal(1, await dispatcher.DispatchAsync(now.AddMinutes(1)));
        Assert.Equal("sent", Assert.Single(await queue.SnapshotAsync()).State);
    }

    private sealed class RecordingTransport : INotificationTransport
    {
        public bool Fail { get; set; }
        public List<string> Channels { get; } = [];
        public Task SendAsync(NotificationJob job, CancellationToken ct = default)
        {
            if (Fail) throw new InvalidOperationException("secret should not be persisted");
            Channels.Add(job.Channel);
            return Task.CompletedTask;
        }
    }
}
