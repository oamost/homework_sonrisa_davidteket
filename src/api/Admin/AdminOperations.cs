namespace WorldAlerts.Api.Admin;

// This read-only projection deliberately has no webhook URL, headers, recipient,
// message payload or credential fields. Register a reader backed by runtime state
// (including persisted queue rows) to replace the explicit unavailable snapshot.
public sealed record AdminSourceSnapshot(string SourceId, string Health, string PollStatus, DateTimeOffset? LastPollUtc);
public sealed record AdminDeliverySnapshot(string Status, int? Queued, int? Failed)
{
    public int? Sent { get; init; }
}
public sealed record AdminOperationsSnapshot(IReadOnlyList<AdminSourceSnapshot> Sources, AdminDeliverySnapshot Delivery);

public interface IAdminOperationsReader
{
    Task<AdminOperationsSnapshot> ReadAsync(CancellationToken cancellationToken);
}

internal sealed class UnavailableAdminOperationsReader : IAdminOperationsReader
{
    public Task<AdminOperationsSnapshot> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(
        new AdminOperationsSnapshot(
            [new AdminSourceSnapshot("source polling", "unavailable", "unavailable", null)],
            new AdminDeliverySnapshot("unavailable", null, null)));
}
