using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using WorldAlerts.Api.Disasters;
using WorldAlerts.Api.Markets;

namespace WorldAlerts.Api.Tests;

internal static class MarketTestSupport
{
    public static MarketSource Source(
        string kind,
        string? id = null,
        string? url = null,
        string instrument = "TEST",
        string interval = "",
        string fromCurrency = "",
        string toCurrency = "",
        IEnumerable<string>? instruments = null)
    {
        var sourceId = id ?? $"market-{kind}";
        return new MarketSource
        {
            Id = sourceId,
            Name = sourceId,
            Kind = kind,
            Url = url ?? "https://market.test/data",
            Venue = "Test Venue",
            Region = "Test Region",
            Instrument = instrument,
            QuoteUnit = "units",
            Symbol = instrument,
            Interval = interval,
            FromCurrency = fromCurrency,
            ToCurrency = toCurrency,
            Instruments = instruments?.ToList() ?? new List<string>(),
            Delayed = false,
            DelaySemantics = "test semantics",
        };
    }

    public static MarketObservation Observation(
        string instrument,
        DateTimeOffset observationUtc,
        double value = 1.0,
        string sourceId = "market-test",
        string interval = "1h",
        DateTimeOffset? fetchedUtc = null)
    {
        var fetched = fetchedUtc ?? observationUtc.AddMinutes(1);
        return new MarketObservation(
            SourceId: sourceId,
            Venue: "Test Venue",
            Region: "Test Region",
            Instrument: instrument,
            QuoteUnit: "units",
            Value: value,
            ObservationUtc: observationUtc,
            FetchedUtc: fetched,
            Delayed: false,
            DelaySemantics: "test semantics",
            Interval: interval,
            Provenance: "https://market.test/data");
    }

    public static MarketIngestionService Ingest(
        MarketStore store,
        MarketOptions options,
        FakeHttpHandler handler)
    {
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        return new MarketIngestionService(
            client,
            store,
            options,
            NullLogger<MarketIngestionService>.Instance);
    }

    public static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    public static HttpResponseMessage EmptyOk() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(string.Empty, Encoding.UTF8, "application/json"),
    };
}

internal static class DisasterTestSupport
{
    public static DisasterSource Source(
        string kind,
        string? id = null,
        string? url = null,
        IEnumerable<string>? categories = null,
        IEnumerable<string>? events = null,
        string semantics = "",
        int pageSize = 100,
        int maxPages = 3,
        int maxItems = 300)
    {
        var sourceId = id ?? $"disaster-{kind}";
        return new DisasterSource
        {
            Id = sourceId,
            Name = sourceId,
            Kind = kind,
            Url = url ?? "https://disaster.test/data",
            Categories = categories?.ToList() ?? new List<string>(),
            Events = events?.ToList() ?? new List<string>(),
            Semantics = semantics,
            PageSize = pageSize,
            MaxPages = maxPages,
            MaxItems = maxItems,
        };
    }

    public static DisasterEvent Event(
        string nativeId,
        DateTimeOffset? eventTimeUtc,
        string sourceId = "disaster-test",
        string title = "Test event",
        string eventType = "earthquake",
        DateTimeOffset? publishedUtc = null,
        DateTimeOffset? updatedUtc = null,
        double? latitude = null,
        double? longitude = null,
        string? country = null,
        string? iso3 = null,
        double? severity = null,
        string? severityUnit = null,
        string? alertLevel = null,
        double? alertScore = null,
        bool? tsunami = null,
        long? significance = null,
        string provenance = "https://disaster.test/data",
        DateTimeOffset? fetchedUtc = null,
        string? certainty = null,
        DateTimeOffset? effectiveUtc = null,
        DateTimeOffset? expiresUtc = null,
        double? depthKm = null)
    {
        return new DisasterEvent(
            SourceId: sourceId,
            NativeId: nativeId,
            EventType: eventType,
            Title: title,
            EventTimeUtc: eventTimeUtc,
            PublishedUtc: publishedUtc,
            UpdatedUtc: updatedUtc,
            Latitude: latitude,
            Longitude: longitude,
            Country: country,
            Iso3: iso3,
            Severity: severity,
            SeverityUnit: severityUnit,
            AlertLevel: alertLevel,
            AlertScore: alertScore,
            Tsunami: tsunami,
            Significance: significance,
            Provenance: provenance,
            FetchedUtc: fetchedUtc ?? (eventTimeUtc ?? publishedUtc ?? updatedUtc ?? DateTimeOffset.UnixEpoch),
            Certainty: certainty,
            EffectiveUtc: effectiveUtc,
            ExpiresUtc: expiresUtc,
            DepthKm: depthKm);
    }

    public static DisasterIngestionService Ingest(
        DisasterStore store,
        DisasterOptions options,
        FakeHttpHandler handler,
        TimeProvider? clock = null)
    {
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        return new DisasterIngestionService(
            client,
            store,
            options,
            NullLogger<DisasterIngestionService>.Instance,
            clock: clock);
    }

    public static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    public static HttpResponseMessage EmptyOk() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(string.Empty, Encoding.UTF8, "application/json"),
    };
}
