using WorldAlerts.Api.Admin;
using WorldAlerts.Api.Queue;

namespace WorldAlerts.Api.Collection;

/// <summary>Read-only, secret-free operational projection from the persisted runtime stores.</summary>
public sealed class RuntimeAdminOperationsReader(CollectionStateStore sources, NotificationQueueStore queue,
    TimeProvider clock) : IAdminOperationsReader
{
    public async Task<AdminOperationsSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var sourceRows = await sources.SnapshotAsync(cancellationToken);
        var jobs = await queue.SnapshotAsync(cancellationToken);
        return new AdminOperationsSnapshot(
            sourceRows.Select(row => new AdminSourceSnapshot(row.SourceId,
                row.GetStatus(now).ToString().ToLowerInvariant(),
                row.LastAttemptUtc is null ? "not-polled" : row.ConsecutiveFailures > 0 ? "failed" : "succeeded",
                row.LastAttemptUtc)).ToArray(),
            new AdminDeliverySnapshot("available", jobs.Count(j => j.State is "pending" or "sending"),
                jobs.Count(j => j.State is "failed" or "uncertain")));
    }
}
