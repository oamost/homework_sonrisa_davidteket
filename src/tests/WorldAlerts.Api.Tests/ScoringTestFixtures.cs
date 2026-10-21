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

}

/// <summary>One named news case and any caller-supplied corroborating sources.</summary>
internal sealed record NewsCase(
    NewsItem Item,
    IReadOnlyList<NewsCorroboratingSource> CorroboratingSources);
