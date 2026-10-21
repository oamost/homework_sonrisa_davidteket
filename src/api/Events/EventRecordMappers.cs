using System.Globalization;
using WorldAlerts.Api.Disasters;
using WorldAlerts.Api.Markets;
using WorldAlerts.Api.News;

namespace WorldAlerts.Api.Events;

/// <summary>
/// Maps the existing family-specific records into the shared <see cref="EventRecord"/>.
/// Mapping is lossless for the fields each family exposes: anything without a
/// first-class slot is preserved in <see cref="EventRecord.Metadata"/>.
/// <para>
/// Event-type taxonomy, alert eligibility, the 24-hour window and importance/scoring
/// policy are deliberately out of scope here and are not decided by these mappers.
/// </para>
/// <list type="bullet">
/// <item><description>News: the feed family becomes <see cref="EventRecord.Category"/>; there is no occurrence time, so publication (or fetch) drives the effective timestamp. No event type is invented because task 01 has not frozen a taxonomy.</description></item>
/// <item><description>Market: the provider observation time becomes the event time, the venue doubles as the source name, and region is preserved.</description></item>
/// <item><description>Disaster: hazard type, coordinates, country/ISO3, severity and revision time are preserved; the source name falls back to the source id.</description></item>
/// </list>
/// </summary>
public static class EventRecordMappers
{
    /// <summary>Maps a normalized news item. Publication time is preserved exactly as supplied (possibly null).</summary>
    public static EventRecord FromNews(NewsItem item, DateTimeOffset? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(item);

        return EventRecord.Create(
            family: EventFamily.News,
            sourceId: item.SourceId,
            sourceName: item.SourceName,
            category: item.Family,
            eventType: null,
            region: null,
            country: null,
            iso3: null,
            eventTimeUtc: null,
            publishedTimeUtc: item.PublishedUtc,
            updatedTimeUtc: null,
            fetchedUtc: item.FirstSeenUtc,
            latitude: null,
            longitude: null,
            severity: null,
            severityUnit: null,
            sourceItemId: item.NativeId,
            provenanceUrl: item.Provenance,
            metadata: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["nativeId"] = item.NativeId,
                ["title"] = item.Title,
                ["link"] = item.Link,
                ["categories"] = string.Join(",", item.Categories),
            },
            nowUtc: nowUtc);
    }

    /// <summary>Maps a normalized market observation. The observation time is the event time.</summary>
    public static EventRecord FromMarket(MarketObservation observation, DateTimeOffset? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(observation);

        return EventRecord.Create(
            family: EventFamily.Market,
            sourceId: observation.SourceId,
            sourceName: observation.Venue,
            category: null,
            eventType: null,
            region: observation.Region,
            country: null,
            iso3: null,
            eventTimeUtc: observation.ObservationUtc,
            publishedTimeUtc: null,
            updatedTimeUtc: null,
            fetchedUtc: observation.FetchedUtc,
            latitude: null,
            longitude: null,
            severity: null,
            severityUnit: null,
            sourceItemId: null,
            provenanceUrl: observation.Provenance,
            metadata: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["venue"] = observation.Venue,
                ["instrument"] = observation.Instrument,
                ["quoteUnit"] = observation.QuoteUnit,
                ["value"] = Format(observation.Value),
                ["delayed"] = observation.Delayed ? "true" : "false",
                ["delaySemantics"] = observation.DelaySemantics,
                ["interval"] = observation.Interval,
            },
            nowUtc: nowUtc);
    }

    /// <summary>Maps a normalized disaster event, preserving hazard type, geography and severity when known.</summary>
    public static EventRecord FromDisaster(DisasterEvent disaster, DateTimeOffset? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(disaster);

        return EventRecord.Create(
            family: EventFamily.Disaster,
            sourceId: disaster.SourceId,
            sourceName: disaster.SourceId,
            category: null,
            eventType: disaster.EventType,
            region: null,
            country: disaster.Country,
            iso3: disaster.Iso3,
            eventTimeUtc: disaster.EventTimeUtc,
            publishedTimeUtc: disaster.PublishedUtc,
            updatedTimeUtc: disaster.UpdatedUtc,
            fetchedUtc: disaster.FetchedUtc,
            latitude: disaster.Latitude,
            longitude: disaster.Longitude,
            severity: disaster.Severity,
            severityUnit: disaster.SeverityUnit,
            sourceItemId: disaster.NativeId,
            provenanceUrl: disaster.Provenance,
            metadata: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["nativeId"] = disaster.NativeId,
                ["title"] = disaster.Title,
                ["alertLevel"] = disaster.AlertLevel,
                ["alertScore"] = Format(disaster.AlertScore),
                ["tsunami"] = disaster.Tsunami is { } tsunami ? (tsunami ? "true" : "false") : null,
                ["significance"] = disaster.Significance?.ToString(CultureInfo.InvariantCulture),
                ["certainty"] = disaster.Certainty,
                ["effectiveUtc"] = Format(disaster.EffectiveUtc),
                ["expiresUtc"] = Format(disaster.ExpiresUtc),
                ["depthKm"] = Format(disaster.DepthKm),
            },
            nowUtc: nowUtc);
    }

    private static string? Format(double? value) =>
        value?.ToString(CultureInfo.InvariantCulture);

    private static string? Format(DateTimeOffset? value) =>
        value?.ToString("O", CultureInfo.InvariantCulture);
}
