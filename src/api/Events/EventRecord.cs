namespace WorldAlerts.Api.Events;

/// <summary>
/// Broad source family an event record was normalized from. This is a transport
/// grouping only: the approved event-type taxonomy and its acceptance rules are
/// owned by Deliverable 3 task 01 and are deliberately not frozen or invented here.
/// </summary>
public enum EventFamily
{
    News,
    Market,
    Disaster,
    Other,
}

/// <summary>Which provider/collection timestamp the effective timestamp came from.</summary>
public enum TimestampBasis
{
    /// <summary>The provider-supplied time at which the event happened.</summary>
    EventTime,

    /// <summary>The provider-supplied publication/update time.</summary>
    PublishedTime,

    /// <summary>The local collection (first-seen/fetched) time, used only as a last resort.</summary>
    FetchedTime,
}

/// <summary>
/// Data-quality flags describing suspicious or missing provider timestamps.
/// Flags are informational only: they never decide whether an event may alert.
/// </summary>
[Flags]
public enum EventTimestampAnomalies
{
    None = 0,

    /// <summary>Neither an event time nor a publication time was supplied.</summary>
    MissingProviderTimes = 1 << 0,

    /// <summary>The publication time is later than the local fetch time.</summary>
    PublishedAfterFetched = 1 << 1,

    /// <summary>The event time is later than the publication time.</summary>
    EventAfterPublished = 1 << 2,

    /// <summary>A provider timestamp lies in the future relative to the evaluation instant.</summary>
    FutureTimestamp = 1 << 3,
}

/// <summary>
/// One normalized event record shared by every event family (news, markets,
/// disasters and other approved categories). It carries source identity,
/// category/family, optional region and type, separate event/publication/fetch
/// timestamps, optional location and severity, provenance and a metadata bag for
/// source-specific fields.
/// <para>
/// Alert eligibility, the 24-hour lookback rule and importance/scoring policy are
/// deliberately out of scope here (Deliverable 3 task 01 owns them). This type only
/// exposes a deterministic <see cref="EffectiveTimestampUtc"/> plus its
/// <see cref="EffectiveTimestampBasis"/> and non-blocking anomaly flags.
/// </para>
/// </summary>
/// <param name="Family">Broad source family; not the frozen event-type taxonomy.</param>
/// <param name="SourceId">Stable identifier of the configured source.</param>
/// <param name="SourceName">Human-readable source name; falls back to the source id when the family exposes none.</param>
/// <param name="Category">Provider-declared category/family label when supplied (for news, the feed family); otherwise null.</param>
/// <param name="EventType">Provider-specific event type when known (for disasters, the hazard type); otherwise null.</param>
/// <param name="Region">Region label when the source supplies one (for markets, the observation region).</param>
/// <param name="Country">Country name when known.</param>
/// <param name="Iso3">ISO 3166-1 alpha-3 code when known.</param>
/// <param name="EventTimeUtc">When the event occurred, when known.</param>
/// <param name="PublishedTimeUtc">When the provider published/updated the item, when known.</param>
/// <param name="UpdatedTimeUtc">Provider revision time when it is distinct from the publication time.</param>
/// <param name="FetchedUtc">Local collection time; for news this is the first-seen time.</param>
/// <param name="Latitude">Latitude in decimal degrees when known.</param>
/// <param name="Longitude">Longitude in decimal degrees when known.</param>
/// <param name="Severity">Provider severity value when known.</param>
/// <param name="SeverityUnit">Unit/semantics of <paramref name="Severity"/> when known.</param>
/// <param name="SourceItemId">Provider-native item identifier when known, for later deduplication.</param>
/// <param name="ProvenanceUrl">URL/document that identifies where the item came from.</param>
/// <param name="Metadata">Source-specific fields preserved as invariant-culture strings.</param>
/// <param name="Anomalies">Timestamp anomalies detected by <see cref="EvaluateAnomalies"/>.</param>
public sealed record EventRecord(
    EventFamily Family,
    string SourceId,
    string SourceName,
    string? Category,
    string? EventType,
    string? Region,
    string? Country,
    string? Iso3,
    DateTimeOffset? EventTimeUtc,
    DateTimeOffset? PublishedTimeUtc,
    DateTimeOffset? UpdatedTimeUtc,
    DateTimeOffset FetchedUtc,
    double? Latitude,
    double? Longitude,
    double? Severity,
    string? SeverityUnit,
    string? SourceItemId,
    string ProvenanceUrl,
    IReadOnlyDictionary<string, string?> Metadata,
    EventTimestampAnomalies Anomalies)
{
    /// <summary>
    /// Deterministic ordering/age timestamp: event time when known, else publication
    /// time, else the fetch/first-seen time. It is intentionally not an eligibility
    /// decision; the 24-hour alert window is owned by Deliverable 3 task 01.
    /// </summary>
    public DateTimeOffset EffectiveTimestampUtc =>
        EventTimeUtc ?? PublishedTimeUtc ?? FetchedUtc;

    /// <summary>Which of the three timestamps <see cref="EffectiveTimestampUtc"/> was taken from.</summary>
    public TimestampBasis EffectiveTimestampBasis =>
        EventTimeUtc is not null
            ? TimestampBasis.EventTime
            : PublishedTimeUtc is not null
                ? TimestampBasis.PublishedTime
                : TimestampBasis.FetchedTime;

    /// <summary>
    /// Evaluates the timestamp anomaly flags for the supplied provider times. The
    /// future check is made against <paramref name="nowUtc"/> so callers (and tests)
    /// get deterministic results. No alert eligibility is derived from the result.
    /// </summary>
    public static EventTimestampAnomalies EvaluateAnomalies(
        DateTimeOffset? eventTimeUtc,
        DateTimeOffset? publishedTimeUtc,
        DateTimeOffset fetchedUtc,
        DateTimeOffset nowUtc)
    {
        var anomalies = EventTimestampAnomalies.None;

        if (eventTimeUtc is null && publishedTimeUtc is null)
        {
            anomalies |= EventTimestampAnomalies.MissingProviderTimes;
        }

        if (publishedTimeUtc is { } published && published > fetchedUtc)
        {
            anomalies |= EventTimestampAnomalies.PublishedAfterFetched;
        }

        if (eventTimeUtc is { } eventTime
            && publishedTimeUtc is { } publication
            && eventTime > publication)
        {
            anomalies |= EventTimestampAnomalies.EventAfterPublished;
        }

        if ((eventTimeUtc is { } futureEvent && futureEvent > nowUtc)
            || (publishedTimeUtc is { } futurePublished && futurePublished > nowUtc))
        {
            anomalies |= EventTimestampAnomalies.FutureTimestamp;
        }

        return anomalies;
    }

    /// <summary>
    /// Creates a record and computes its anomaly flags. <paramref name="nowUtc"/>
    /// defaults to the current UTC instant; tests and replay callers should pass an
    /// explicit instant. Eligibility and scoring are out of scope and not decided here.
    /// </summary>
    public static EventRecord Create(
        EventFamily family,
        string sourceId,
        string sourceName,
        string? category,
        string? eventType,
        string? region,
        string? country,
        string? iso3,
        DateTimeOffset? eventTimeUtc,
        DateTimeOffset? publishedTimeUtc,
        DateTimeOffset? updatedTimeUtc,
        DateTimeOffset fetchedUtc,
        double? latitude,
        double? longitude,
        double? severity,
        string? severityUnit,
        string? sourceItemId,
        string provenanceUrl,
        IReadOnlyDictionary<string, string?>? metadata = null,
        DateTimeOffset? nowUtc = null)
    {
        var anomalies = EvaluateAnomalies(
            eventTimeUtc,
            publishedTimeUtc,
            fetchedUtc,
            nowUtc ?? DateTimeOffset.UtcNow);

        return new EventRecord(
            family,
            sourceId,
            sourceName,
            category,
            eventType,
            region,
            country,
            iso3,
            eventTimeUtc,
            publishedTimeUtc,
            updatedTimeUtc,
            fetchedUtc,
            latitude,
            longitude,
            severity,
            severityUnit,
            sourceItemId,
            provenanceUrl,
            metadata ?? new Dictionary<string, string?>(),
            anomalies);
    }
}
