using System.Globalization;
using WorldAlerts.Api.Disasters;
using WorldAlerts.Api.Markets;
using WorldAlerts.Api.News;
using WorldAlerts.Api.Scoring;

namespace WorldAlerts.Api.Tests;

/// <summary>
/// Named fixtures copied from the approved task-01 acceptance examples
/// (<c>docs/evidence/task-01-example-cases.md</c>). They are offline examples,
/// not universal weights.
/// </summary>
internal static class ScoringFixtures
{
    public static readonly DateTimeOffset Now = At("2026-10-21T09:00:00Z");

    public static DateTimeOffset At(string value)
        => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    public static NewsItem News(
        string sourceId,
        string sourceName,
        string title,
        string? publishedUtc)
    {
        return new NewsItem(
            SourceId: sourceId,
            SourceName: sourceName,
            NativeId: "native-" + sourceId,
            Title: title,
            Link: "https://example.test/" + sourceId,
            PublishedUtc: publishedUtc is null ? null : At(publishedUtc),
            FirstSeenUtc: Now,
            Family: "world-news",
            Categories: Array.Empty<string>(),
            Provenance: "https://example.test/feed");
    }

    public static NewsCorroboratingSource Corroboration(
        string sourceId,
        string sourceName,
        string? publishedUtc)
        => new(sourceId, sourceName, publishedUtc is null ? null : At(publishedUtc));

    public static NewsCase N1() => new(
        News("bbc-world", "BBC World", "Breaking: earthquake strikes coastal city", "2026-10-21T08:50:00Z"),
        Array.Empty<NewsCorroboratingSource>());

    public static NewsCase N2() => new(
        News("bbc-world", "BBC World", "Coastal city evacuates after quake", "2026-10-21T08:30:00Z"),
        new[] { Corroboration("nyt-world", "NYT World", "2026-10-21T06:10:00Z") });

    public static NewsCase N3() => new(
        News("nature", "Nature", "New model improves weather forecasts", "2026-10-21T07:00:00Z"),
        Array.Empty<NewsCorroboratingSource>());

    public static NewsCase N4() => new(
        News("bbc-world", "BBC World", "Coastal city evacuates after quake", "2026-10-21T08:30:00Z"),
        new[] { Corroboration("nyt-world", "NYT World", "2026-10-21T01:00:00Z") });

    public static NewsCase N5() => new(
        News("trade-witness", "Trade Witness", "Conflict escalates in border region", "2026-10-21T06:00:00Z"),
        Array.Empty<NewsCorroboratingSource>());

    public static NewsCase N6() => new(
        News("bbc-world", "BBC World", "Markets close mixed after quiet session", "2026-10-21T08:00:00Z"),
        Array.Empty<NewsCorroboratingSource>());

    public static NewsCase ExactlySixHours() => new(
        News("bbc-world", "BBC World", "Regional talks resume", "2026-10-21T08:30:00Z"),
        new[] { Corroboration("nyt-world", "NYT World", "2026-10-21T02:30:00Z") });

    public static NewsCase JustOverSixHours() => new(
        News("bbc-world", "BBC World", "Regional talks resume", "2026-10-21T08:30:00Z"),
        new[] { Corroboration("nyt-world", "NYT World", "2026-10-21T02:29:59Z") });

    public static NewsCase MissingPublishedTime() => new(
        News("bbc-world", "BBC World", "Regional talks resume", null),
        new[] { Corroboration("nyt-world", "NYT World", "2026-10-21T06:10:00Z") });

    public static MarketImportanceInput Market(
        MarketFamily family,
        double currentValue,
        double baselineValue,
        string observationUtc,
        int intervalHours = 24)
        => new(
            family,
            currentValue,
            baselineValue,
            TimeSpan.FromHours(intervalHours),
            At(observationUtc),
            Now);

    public static MarketImportanceInput MK1() => Market(MarketFamily.Crypto, 106.2, 100.0, "2026-10-21T08:00:00Z");
    public static MarketImportanceInput MK2() => Market(MarketFamily.Crypto, 104.9, 100.0, "2026-10-21T08:00:00Z");
    public static MarketImportanceInput MK3() => Market(MarketFamily.Fx, 1.013, 1.000, "2026-10-20T16:00:00Z");
    public static MarketImportanceInput MK4() => Market(MarketFamily.Fx, 1.008, 1.000, "2026-10-20T16:00:00Z");
    public static MarketImportanceInput MK5() => Market(MarketFamily.Index, 1024.0, 1000.0, "2026-10-19T20:00:00Z");
    public static MarketImportanceInput MK6() => Market(MarketFamily.Crypto, 109.0, 100.0, "2026-10-18T20:00:00Z");

    public static DisasterEvent Disaster(
        string sourceId,
        string eventType,
        string? alertLevel = null,
        double? severity = null,
        string? severityUnit = null,
        bool? tsunami = null,
        string? eventTimeUtc = null,
        string? publishedUtc = null)
    {
        return new DisasterEvent(
            SourceId: sourceId,
            NativeId: "native-" + sourceId,
            EventType: eventType,
            Title: "Fixture " + eventType,
            EventTimeUtc: eventTimeUtc is null ? null : At(eventTimeUtc),
            PublishedUtc: publishedUtc is null ? null : At(publishedUtc),
            UpdatedUtc: null,
            Latitude: null,
            Longitude: null,
            Country: null,
            Iso3: null,
            Severity: severity,
            SeverityUnit: severityUnit,
            AlertLevel: alertLevel,
            AlertScore: null,
            Tsunami: tsunami,
            Significance: null,
            Provenance: "https://example.test/disaster",
            FetchedUtc: Now);
    }

    public static DisasterEvent DS1() => Disaster(
        "gdacs", "earthquake", alertLevel: "Orange",
        eventTimeUtc: "2026-10-21T07:00:00Z", publishedUtc: "2026-10-21T07:30:00Z");

    public static DisasterEvent DS2() => Disaster(
        "gdacs", "earthquake", alertLevel: "Green", severity: 6.3, severityUnit: "M",
        eventTimeUtc: "2026-10-21T06:00:00Z", publishedUtc: "2026-10-21T06:20:00Z");

    public static DisasterEvent DS3() => Disaster(
        "gdacs", "earthquake", alertLevel: "Green", severity: 5.2, severityUnit: "M",
        eventTimeUtc: "2026-10-21T06:00:00Z", publishedUtc: "2026-10-21T06:20:00Z");

    public static DisasterEvent DS4() => Disaster(
        "gdacs", "flood", alertLevel: "Green", tsunami: true,
        eventTimeUtc: "2026-10-21T05:00:00Z", publishedUtc: "2026-10-21T05:40:00Z");

    public static DisasterEvent DS5() => Disaster(
        "ifrc-go", "epidemic", severity: 2, severityUnit: "IFRC severity level",
        publishedUtc: "2026-10-21T03:00:00Z");

    public static DisasterEvent DS6() => Disaster(
        "ifrc-go", "flood", severity: 1, severityUnit: "IFRC severity level",
        publishedUtc: "2026-10-21T03:00:00Z");

    public static DisasterEvent DS7() => Disaster(
        "gdacs", "earthquake", alertLevel: "Red",
        publishedUtc: "2026-10-21T08:00:00Z");
}

/// <summary>One named news case and any caller-supplied corroborating sources.</summary>
internal sealed record NewsCase(
    NewsItem Item,
    IReadOnlyList<NewsCorroboratingSource> CorroboratingSources);
