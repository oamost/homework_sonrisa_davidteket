using WorldAlerts.Api.Events;

namespace WorldAlerts.Api.CatchUp;

/// <summary>
/// One eligible catch-up item. <paramref name="Key"/> is the deduplication and
/// delivered-id key: the provider-native item id when present, otherwise a stable
/// composite built from the source, family, provenance and effective timestamp.
/// </summary>
/// <param name="Key">Stable identity used for cross-record and restart deduplication.</param>
/// <param name="Family">Source family the record belongs to.</param>
/// <param name="SourceId">Configured source identifier.</param>
/// <param name="SourceName">Human-readable source name.</param>
/// <param name="Category">Provider category when supplied.</param>
/// <param name="EligibilityTimestampUtc">Publication time, or event time when no publication exists.</param>
/// <param name="IsLateItem">True when the event predates the window but publication does not.</param>
/// <param name="Record">The normalized record behind the candidate.</param>
public sealed record CatchUpCandidate(
    string Key,
    EventFamily Family,
    string SourceId,
    string SourceName,
    string? Category,
    DateTimeOffset EligibilityTimestampUtc,
    bool IsLateItem,
    EventRecord Record);
