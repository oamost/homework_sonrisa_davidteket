namespace WorldAlerts.Api.Collection;

/// <summary>
/// Immutable snapshot of collection progress for a single source. Updated
/// copies are written back through <see cref="CollectionStateStore"/>.
/// Program.cs wiring, source-specific cadence values, and admin display of
/// this data are deferred to a later integration task.
/// </summary>
public sealed record SourcePollState
{
    /// <summary>Stable identifier of the polled source.</summary>
    public required string SourceId { get; init; }

    /// <summary>Source family, for example news, markets, disasters, or advisories.</summary>
    public string Family { get; init; } = "unknown";

    /// <summary>When the source was last polled, whether the attempt succeeded or failed.</summary>
    public DateTimeOffset? LastAttemptUtc { get; init; }

    /// <summary>When the source last produced data successfully.</summary>
    public DateTimeOffset? LastSuccessUtc { get; init; }

    /// <summary>Message from the most recent failed attempt, if any.</summary>
    public string? LastError { get; init; }

    /// <summary>Consecutive failed attempts since the last success.</summary>
    public int ConsecutiveFailures { get; init; }

    /// <summary>Optional explicit time at which the next poll is due.</summary>
    public DateTimeOffset? NextDueUtc { get; init; }

    /// <summary>
    /// Derives the current status (healthy/stale/failed) as of
    /// <paramref name="nowUtc"/> using explicit thresholds.
    /// </summary>
    public SourcePollStatus GetStatus(DateTimeOffset nowUtc, SourceHealthThresholds? thresholds = null)
        => SourceHealth.Classify(this, nowUtc, thresholds);
}
