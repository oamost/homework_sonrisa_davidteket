using WorldAlerts.Api.Events;

namespace WorldAlerts.Api.CatchUp;

/// <summary>Kind of coverage gap detected while selecting catch-up candidates.</summary>
public enum CatchUpGapKind
{
    /// <summary>No stored items were supplied for an expected family.</summary>
    EmptyCategory,

    /// <summary>The source's newest stored provider timestamp is older than the window.</summary>
    StaleSource,

    /// <summary>The source supplied no provider event or publication timestamps at all.</summary>
    MissingProviderTimestamps,
}

/// <summary>
/// Data describing a coverage gap so the admin view can surface it. Gaps are
/// returned with the selection rather than written to logs.
/// </summary>
/// <param name="Kind">Gap classification.</param>
/// <param name="Family">Affected source family.</param>
/// <param name="SourceId">Affected source, or null for an empty category.</param>
/// <param name="Detail">Human-readable explanation.</param>
/// <param name="NewestProviderTimestampUtc">
/// Newest provider timestamp observed for the source; null when the category is
/// empty or the source supplied no provider timestamps.
/// </param>
public sealed record CatchUpCoverageGap(
    CatchUpGapKind Kind,
    EventFamily Family,
    string? SourceId,
    string Detail,
    DateTimeOffset? NewestProviderTimestampUtc);
