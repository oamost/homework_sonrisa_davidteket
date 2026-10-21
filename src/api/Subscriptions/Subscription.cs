namespace WorldAlerts.Api.Subscriptions;

/// <summary>
/// One saved choice: an event-type code from the approved taxonomy delivered over
/// a channel. Codes are stored as supplied (trimmed) because the definitive
/// taxonomy is not frozen yet; nothing here enumerates valid event types.
/// </summary>
public sealed record Subscription
{
    /// <summary>Single-user local installs use this identity until multi-user exists.</summary>
    public const string DefaultUserId = "local";

    public long Id { get; init; }

    public string UserId { get; init; } = DefaultUserId;

    /// <summary>Approved event-type code; arbitrary non-empty codes are persisted.</summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>Stable channel id, normalized to trimmed lower case (e.g. <c>email</c>).</summary>
    public string Channel { get; init; } = string.Empty;

    /// <summary>
    /// Opaque destination reference (for example an address or webhook label).
    /// Never a credential: secrets belong to the delivery tasks, not this table.
    /// </summary>
    public string? Destination { get; init; }

    public bool Enabled { get; init; } = true;

    public DateTimeOffset CreatedUtc { get; init; }

    public DateTimeOffset UpdatedUtc { get; init; }
}
