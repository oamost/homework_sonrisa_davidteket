namespace WorldAlerts.Api.Disasters;

/// <summary>
/// One normalized hazardous event. Missing native values stay absent (null)
/// instead of being invented. Publication/update times are kept separate from
/// the event occurrence time.
/// </summary>
public sealed record DisasterEvent(
    string SourceId,
    string NativeId,
    string EventType,
    string Title,
    DateTimeOffset? EventTimeUtc,
    DateTimeOffset? PublishedUtc,
    DateTimeOffset? UpdatedUtc,
    double? Latitude,
    double? Longitude,
    string? Country,
    string? Iso3,
    double? Severity,
    string? SeverityUnit,
    string? AlertLevel,
    double? AlertScore,
    bool? Tsunami,
    long? Significance,
    string Provenance,
    DateTimeOffset FetchedUtc,
    string? Certainty = null,
    DateTimeOffset? EffectiveUtc = null,
    DateTimeOffset? ExpiresUtc = null,
    double? DepthKm = null);

/// <summary>Outcome of parsing one disaster payload.</summary>
public sealed record DisasterParseResult(IReadOnlyList<DisasterEvent> Items, int Skipped);
