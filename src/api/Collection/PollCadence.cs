namespace WorldAlerts.Api.Collection;

/// <summary>
/// Due-time and cadence calculation for continuous collection. A source is
/// never polled faster than its configured interval: an explicit
/// <see cref="SourcePollState.NextDueUtc"/> earlier than the rate cap is
/// ignored. Program.cs wiring, source-specific cadence values, and admin
/// display are deferred to a later integration task.
/// </summary>
public static class PollCadence
{
    /// <summary>Safe default interval applied to unknown or unconfigured source families.</summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMinutes(15);

    private static readonly Dictionary<string, TimeSpan> FamilyDefaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["news"] = TimeSpan.FromMinutes(15),
        ["markets"] = TimeSpan.FromMinutes(10),
        ["disasters"] = TimeSpan.FromMinutes(15),
        ["advisories"] = TimeSpan.FromMinutes(30),
    };

    /// <summary>
    /// Returns the configured safe interval for <paramref name="family"/>,
    /// falling back to <see cref="DefaultPollInterval"/> for unknown families.
    /// </summary>
    public static TimeSpan GetInterval(string? family)
        => family is not null && FamilyDefaults.TryGetValue(family, out var interval)
            ? interval
            : DefaultPollInterval;

    /// <summary>Computes when the next poll is due after an attempt at <paramref name="attemptUtc"/>.</summary>
    public static DateTimeOffset DueUtcAfter(DateTimeOffset attemptUtc, TimeSpan interval) => attemptUtc.Add(interval);

    /// <summary>
    /// Returns whether the source is due for polling at <paramref name="nowUtc"/>.
    /// A source that has never been attempted is immediately due; otherwise the
    /// poll is due no earlier than one configured interval after the last attempt.
    /// </summary>
    public static bool IsDue(SourcePollState state, DateTimeOffset nowUtc, TimeSpan? interval = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        var configured = interval ?? GetInterval(state.Family);
        if (configured < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), configured, "Poll intervals must not be negative.");
        }

        if (state.LastAttemptUtc is null)
        {
            return true;
        }

        var rateCap = DueUtcAfter(state.LastAttemptUtc.Value, configured);
        var dueAt = state.NextDueUtc is { } scheduled && scheduled > rateCap ? scheduled : rateCap;
        return nowUtc >= dueAt;
    }
}
