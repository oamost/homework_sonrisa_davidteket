namespace WorldAlerts.Api.Collection;

/// <summary>
/// One source's collection status, materialized for callers. This slice
/// returns data only: no endpoint, no hosted service, and admin display is
/// deferred to a later integration task.
/// </summary>
/// <param name="SourceId">Stable identifier of the polled source.</param>
/// <param name="Family">Source family.</param>
/// <param name="Status">Derived status (healthy, stale, or failed).</param>
/// <param name="LastAttemptUtc">Most recent poll attempt.</param>
/// <param name="LastSuccessUtc">Most recent successful poll.</param>
/// <param name="ConsecutiveFailures">Consecutive failures since the last success.</param>
/// <param name="LastError">Message from the most recent failed attempt, if any.</param>
/// <param name="NextDueUtc">When the next poll is due, if known.</param>
public sealed record SourceStatusEntry(
    string SourceId,
    string Family,
    SourcePollStatus Status,
    DateTimeOffset? LastAttemptUtc,
    DateTimeOffset? LastSuccessUtc,
    int ConsecutiveFailures,
    string? LastError,
    DateTimeOffset? NextDueUtc);

/// <summary>
/// Status service returning collection status data for polled sources.
/// It deliberately has no endpoint and no hosted service; Program.cs
/// wiring is deferred to a later integration task.
/// </summary>
public sealed class SourceStatusService
{
    private readonly SourceHealthThresholds _thresholds;

    /// <param name="thresholds">
    /// Explicit stale/failed thresholds; defaults to
    /// <see cref="SourceHealthThresholds.Default"/> when omitted.
    /// </param>
    public SourceStatusService(SourceHealthThresholds? thresholds = null)
    {
        _thresholds = thresholds ?? SourceHealthThresholds.Default;
    }

    /// <summary>Classifies every supplied state as of <paramref name="nowUtc"/>.</summary>
    public IReadOnlyList<SourceStatusEntry> GetStatus(IEnumerable<SourcePollState> states, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(states);
        var entries = new List<SourceStatusEntry>();
        foreach (var state in states)
        {
            entries.Add(ToEntry(state, nowUtc));
        }

        return entries;
    }

    /// <summary>Classifies every state persisted in <paramref name="store"/> as of <paramref name="nowUtc"/>.</summary>
    public IReadOnlyList<SourceStatusEntry> GetStatus(CollectionStateStore store, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(store);
        return GetStatus(store.ListAll(), nowUtc);
    }

    private SourceStatusEntry ToEntry(SourcePollState state, DateTimeOffset nowUtc) => new(
        state.SourceId,
        state.Family,
        SourceHealth.Classify(state, nowUtc, _thresholds),
        state.LastAttemptUtc,
        state.LastSuccessUtc,
        state.ConsecutiveFailures,
        state.LastError,
        state.NextDueUtc);
}
