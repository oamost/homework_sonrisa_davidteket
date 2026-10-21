namespace WorldAlerts.Api.Collection;

/// <summary>Status classification of a polled source's data freshness.</summary>
public enum SourcePollStatus
{
    /// <summary>The source has succeeded recently and is within the stale threshold.</summary>
    Healthy = 0,

    /// <summary>The source has never succeeded, or its last success is too old.</summary>
    Stale = 1,

    /// <summary>The source has failed at least <see cref="SourceHealthThresholds.FailedAfterConsecutiveFailures"/> consecutive attempts.</summary>
    Failed = 2,
}

/// <summary>
/// Explicit thresholds for source-health classification. A source is failed
/// once its consecutive failures reach <see cref="FailedAfterConsecutiveFailures"/>;
/// otherwise it is stale when it has never succeeded or its last success is
/// older than <see cref="StaleAfter"/>; otherwise it is healthy.
/// </summary>
public sealed class SourceHealthThresholds
{
    /// <summary>Default stale/failed thresholds used when none are supplied.</summary>
    public static SourceHealthThresholds Default { get; } = new();

    /// <summary>A source whose last success is older than this span is stale.</summary>
    public TimeSpan StaleAfter { get; init; } = TimeSpan.FromMinutes(20);

    /// <summary>A source with this many consecutive failed attempts is failed.</summary>
    public int FailedAfterConsecutiveFailures { get; init; } = 3;
}

/// <summary>
/// Classifies a <see cref="SourcePollState"/> into
/// <see cref="SourcePollStatus"/> using explicit thresholds. Program.cs
/// wiring and admin display of the results are deferred to a later
/// integration task.
/// </summary>
public static class SourceHealth
{
    /// <summary>
    /// Classifies <paramref name="state"/> as healthy, stale, or failed as of
    /// <paramref name="nowUtc"/> using <paramref name="thresholds"/> (defaults
    /// to <see cref="SourceHealthThresholds.Default"/> when omitted).
    /// </summary>
    public static SourcePollStatus Classify(
        SourcePollState state,
        DateTimeOffset nowUtc,
        SourceHealthThresholds? thresholds = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        var effective = thresholds ?? SourceHealthThresholds.Default;

        if (state.ConsecutiveFailures >= effective.FailedAfterConsecutiveFailures)
        {
            return SourcePollStatus.Failed;
        }

        if (state.LastSuccessUtc is null
            || nowUtc - state.LastSuccessUtc.Value > effective.StaleAfter)
        {
            return SourcePollStatus.Stale;
        }

        return SourcePollStatus.Healthy;
    }
}
