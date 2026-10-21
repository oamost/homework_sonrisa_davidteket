using WorldAlerts.Api.News;
using WorldAlerts.Api.Scoring;

namespace WorldAlerts.Api.Tests;

/// <summary>
/// News-only fixtures for the task-01 eligibility cases and the hardening cases
/// that the shared scoring fixture file does not cover. These remain offline
/// acceptance examples, not universal weights.
/// </summary>
internal static class NewsScoringFixtures
{
    // Task-01 news cases. Event time is not part of NewsItem, so only published
    // time is available to the importance rule; eligibility is a separate later
    // concern. T3 is future-dated and is exercised only to record that the pure
    // importance function has no eligibility clock.
    public static NewsCase T1() => new(
        ScoringFixtures.News(
            "bbc-world",
            "BBC World",
            "Breaking: coastal city quake",
            "2026-10-21T08:00:00Z"),
        Array.Empty<NewsCorroboratingSource>());

    public static NewsCase T2() => new(
        ScoringFixtures.News(
            "bbc-world",
            "BBC World",
            "Breaking: coastal city quake",
            null),
        Array.Empty<NewsCorroboratingSource>());

    public static NewsCase T3() => new(
        ScoringFixtures.News(
            "bbc-world",
            "BBC World",
            "Breaking: coastal city quake",
            "2026-10-22T10:00:00Z"),
        Array.Empty<NewsCorroboratingSource>());

    public static NewsCase T4() => new(
        ScoringFixtures.News(
            "bbc-world",
            "BBC World",
            "Breaking: coastal city quake",
            "2026-10-20T01:00:00Z"),
        Array.Empty<NewsCorroboratingSource>());

    public static NewsCase T5() => new(
        ScoringFixtures.News(
            "bbc-world",
            "BBC World",
            "Breaking: coastal city quake",
            "2026-10-20T23:00:00Z"),
        Array.Empty<NewsCorroboratingSource>());

    // Hardening cases.
    public static NewsCase WordBoundaryFalsePositive() => new(
        ScoringFixtures.News(
            "bbc-world",
            "BBC World",
            "Software update improves data centers",
            "2026-10-21T08:00:00Z"),
        Array.Empty<NewsCorroboratingSource>());

    public static NewsCase WordBoundaryTruePositive() => new(
        ScoringFixtures.News(
            "bbc-world",
            "BBC World",
            "Declaration of war follows border shelling",
            "2026-10-21T08:00:00Z"),
        Array.Empty<NewsCorroboratingSource>());

    public static NewsCase CaseAndWhitespaceKeyword() => new(
        ScoringFixtures.News(
            "bbc-world",
            "BBC World",
            "  BREAKING:\n\tEarthquake   strikes coast ",
            "2026-10-21T08:50:00Z"),
        Array.Empty<NewsCorroboratingSource>());

    public static NewsCase MissingTitle() => new(
        ScoringFixtures.News(
            "bbc-world",
            "BBC World",
            "   ",
            "2026-10-21T08:00:00Z"),
        Array.Empty<NewsCorroboratingSource>());

    public static NewsCase UnknownSource() => new(
        ScoringFixtures.News(
            string.Empty,
            string.Empty,
            "Regional talks resume",
            null),
        Array.Empty<NewsCorroboratingSource>());

    public static NewsCase DuplicateCorroboratorIds() => new(
        ScoringFixtures.News(
            "nature",
            "Nature",
            "Regional talks resume",
            "2026-10-21T08:30:00Z"),
        new[]
        {
            ScoringFixtures.Corroboration("nyt-world", "NYT World", "2026-10-21T06:10:00Z"),
            ScoringFixtures.Corroboration(" NYT-World ", "NYT World", "2026-10-21T06:20:00Z"),
        });

    public static NewsCase SameSourceDifferentCase() => new(
        ScoringFixtures.News(
            "bbc-world",
            "BBC World",
            "Regional talks resume",
            "2026-10-21T08:30:00Z"),
        new[]
        {
            ScoringFixtures.Corroboration(" BBC-World ", "BBC World", "2026-10-21T06:10:00Z"),
        });

    public static NewsCase FutureCorroborator() => new(
        ScoringFixtures.News(
            "bbc-world",
            "BBC World",
            "Regional talks resume",
            "2026-10-21T08:30:00Z"),
        new[]
        {
            ScoringFixtures.Corroboration("nyt-world", "NYT World", "2026-10-21T10:00:00Z"),
        });

    public static NewsCase FuturePrimary() => new(
        ScoringFixtures.News(
            "bbc-world",
            "BBC World",
            "Regional talks resume",
            "2026-10-21T10:00:00Z"),
        new[]
        {
            ScoringFixtures.Corroboration("nyt-world", "NYT World", "2026-10-21T06:10:00Z"),
        });

    public static NewsCase MissingCorroboratorTime() => new(
        ScoringFixtures.News(
            "bbc-world",
            "BBC World",
            "Regional talks resume",
            "2026-10-21T08:30:00Z"),
        new[]
        {
            ScoringFixtures.Corroboration("nyt-world", "NYT World", null),
        });

    public static NewsCase NonTierOneCorroboration() => new(
        ScoringFixtures.News(
            "bbc-world",
            "BBC World",
            "Regional talks resume",
            "2026-10-21T08:30:00Z"),
        new[]
        {
            ScoringFixtures.Corroboration("nature", "Nature", "2026-10-21T06:10:00Z"),
        });

    public static NewsCase SpecialistWithTwoTierOneCorroborators() => new(
        ScoringFixtures.News(
            "nature",
            "Nature",
            "Regional talks resume",
            "2026-10-21T08:30:00Z"),
        new[]
        {
            ScoringFixtures.Corroboration("bbc-world", "BBC World", "2026-10-21T06:10:00Z"),
            ScoringFixtures.Corroboration("nyt-world", "NYT World", "2026-10-21T07:00:00Z"),
        });

    public static NewsCase FuturePrimaryWithTwoValidCorroborators() => new(
        ScoringFixtures.News(
            "bbc-world",
            "BBC World",
            "Regional talks resume",
            "2026-10-21T10:00:00Z"),
        new[]
        {
            ScoringFixtures.Corroboration("nyt-world", "NYT World", "2026-10-21T06:10:00Z"),
            ScoringFixtures.Corroboration("guardian-world", "The Guardian World", "2026-10-21T07:00:00Z"),
        });

    public static NewsCase MissingPrimaryTimeWithTwoValidCorroborators() => new(
        ScoringFixtures.News(
            "bbc-world",
            "BBC World",
            "Regional talks resume",
            null),
        new[]
        {
            ScoringFixtures.Corroboration("nyt-world", "NYT World", "2026-10-21T06:10:00Z"),
            ScoringFixtures.Corroboration("guardian-world", "The Guardian World", "2026-10-21T07:00:00Z"),
        });

    public static NewsCase ThreeIndependentTierOneSources() => new(
        ScoringFixtures.News(
            "bbc-world",
            "BBC World",
            "Regional talks resume",
            "2026-10-21T08:30:00Z"),
        new[]
        {
            ScoringFixtures.Corroboration("nyt-world", "NYT World", "2026-10-21T06:10:00Z"),
            ScoringFixtures.Corroboration("aljazeera-all", "Al Jazeera English", "2026-10-21T07:00:00Z"),
        });
}
