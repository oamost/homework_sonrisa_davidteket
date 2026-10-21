namespace WorldAlerts.Api.Disasters;

/// <summary>
/// Optional source-specific archive filter applied while parsing. Without an
/// explicit per-source window, all valid historical records are retained.
/// Windows are measured backwards from the poll's fetch time, never from
/// the wall clock inside the parser. This does NOT determine 24-hour delivery
/// eligibility.
/// <para>
/// The following windows are unapproved proposals, NOT enabled defaults:
/// GDACS 7 days, EONET 30 days (volcano 90 days), IFRC 7 days,
/// Copernicus EMS 14 days. No window should be enabled without approval.
/// </para>
/// <para>
/// An absent or nonpositive configured window disables the filter.
/// A filter can yield zero retained items; that is not evidence that the
/// provider has no current events.
/// </para>
/// </summary>
public static class DisasterRecencyPolicy
{
    /// <summary>
    /// Resolves an opt-in configured window; null or nonpositive hours
    /// disable filtering.
    /// </summary>
    public static TimeSpan? ResolveWindow(int? configuredHours)
    {
        return configuredHours is > 0 ? TimeSpan.FromHours(configuredHours.Value) : null;
    }

    /// <summary>
    /// Inclusive boundary check: a timestamp exactly on the window edge is still
    /// recent. A <c>null</c> window means the filter is disabled.
    /// </summary>
    public static bool IsRecent(
        DateTimeOffset? timestamp,
        DateTimeOffset fetchedUtc,
        TimeSpan? window)
    {
        if (window is null)
        {
            return true;
        }

        return timestamp is not null
            && timestamp.Value.ToUniversalTime() >= fetchedUtc.ToUniversalTime() - window.Value
            && timestamp.Value.ToUniversalTime() <= fetchedUtc.ToUniversalTime();
    }
}
