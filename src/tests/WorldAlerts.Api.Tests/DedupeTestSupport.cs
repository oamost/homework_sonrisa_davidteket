using WorldAlerts.Api.Events;

namespace WorldAlerts.Api.Tests;

/// <summary>Shared fixtures for deduplication and revision tests.</summary>
internal static class DedupeTestSupport
{
    public static readonly DateTimeOffset Now =
        new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    public static EventRecord News(
        string? nativeId = "story-1",
        string sourceId = "feed-a",
        string title = "A distinctive headline about a regional flood",
        string link = "https://example.test/story/1",
        DateTimeOffset? publishedUtc = null,
        DateTimeOffset? fetchedUtc = null,
        string? summary = null)
    {
        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["nativeId"] = nativeId,
            ["title"] = title,
            ["link"] = link,
        };
        if (summary is not null)
        {
            metadata["summary"] = summary;
        }

        return EventRecord.Create(
            family: EventFamily.News,
            sourceId: sourceId,
            sourceName: sourceId,
            category: "world-news",
            eventType: null,
            region: null,
            country: null,
            iso3: null,
            eventTimeUtc: null,
            publishedTimeUtc: publishedUtc ?? Now.AddHours(-1),
            updatedTimeUtc: null,
            fetchedUtc: fetchedUtc ?? Now,
            latitude: null,
            longitude: null,
            severity: null,
            severityUnit: null,
            sourceItemId: nativeId,
            provenanceUrl: link,
            metadata: metadata,
            nowUtc: Now);
    }

    public static EventRecord Disaster(
        string? nativeId = "disaster-1",
        string sourceId = "gdacs",
        string title = "Earthquake in the northern region",
        double? severity = 4.5,
        string alertLevel = "Orange",
        string link = "https://example.test/disaster/1",
        DateTimeOffset? eventTimeUtc = null,
        DateTimeOffset? publishedUtc = null,
        DateTimeOffset? fetchedUtc = null)
    {
        return EventRecord.Create(
            family: EventFamily.Disaster,
            sourceId: sourceId,
            sourceName: sourceId,
            category: null,
            eventType: "earthquake",
            region: null,
            country: "Example",
            iso3: "EXM",
            eventTimeUtc: eventTimeUtc ?? Now.AddHours(-2),
            publishedTimeUtc: publishedUtc ?? Now.AddHours(-1),
            updatedTimeUtc: null,
            fetchedUtc: fetchedUtc ?? Now,
            latitude: null,
            longitude: null,
            severity: severity,
            severityUnit: "magnitude",
            sourceItemId: nativeId,
            provenanceUrl: link,
            metadata: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["nativeId"] = nativeId,
                ["title"] = title,
                ["alertLevel"] = alertLevel,
                ["alertScore"] = "1.5",
                ["tsunami"] = "false",
            },
            nowUtc: Now);
    }

    public static EventRecord Market(
        string sourceId = "binance",
        string instrument = "BTCUSDT",
        string value = "100.0",
        DateTimeOffset? observationUtc = null,
        string link = "https://example.test/market/btc")
    {
        return EventRecord.Create(
            family: EventFamily.Market,
            sourceId: sourceId,
            sourceName: "Binance",
            category: null,
            eventType: null,
            region: "global",
            country: null,
            iso3: null,
            eventTimeUtc: observationUtc ?? Now.AddMinutes(-5),
            publishedTimeUtc: null,
            updatedTimeUtc: null,
            fetchedUtc: Now,
            latitude: null,
            longitude: null,
            severity: null,
            severityUnit: null,
            sourceItemId: null,
            provenanceUrl: link,
            metadata: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["venue"] = "Binance",
                ["instrument"] = instrument,
                ["quoteUnit"] = "USDT",
                ["value"] = value,
                ["delayed"] = "false",
                ["delaySemantics"] = "realtime",
                ["interval"] = "24h",
            },
            nowUtc: Now);
    }
}
