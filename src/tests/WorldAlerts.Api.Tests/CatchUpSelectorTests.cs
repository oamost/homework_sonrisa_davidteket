using WorldAlerts.Api.CatchUp;
using WorldAlerts.Api.Disasters;
using WorldAlerts.Api.Events;
using WorldAlerts.Api.Markets;
using WorldAlerts.Api.News;

namespace WorldAlerts.Api.Tests;

/// <summary>
/// Unit tests for the 24-hour all-category catch-up selection logic (Deliverable 3
/// task 18): D3 eligibility, deterministic ordering, deduplication, delivered-id
/// suppression and explicit coverage-gap reporting. Sending, pacing and startup
/// wiring belong to task 19 and are deliberately not exercised here.
/// </summary>
public sealed class CatchUpSelectorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private static CatchUpSelector Selector(CatchUpWindow? window = null) =>
        new(new FixedTimeProvider(Now), window);

    [Fact]
    public void Recent_news_publication_is_eligible()
    {
        var selection = Selector().Select(
        [
            Record(EventFamily.News, publishedTimeUtc: Now.AddHours(-1)),
        ]);

        var candidate = Assert.Single(selection.Candidates);
        Assert.Equal("item-1", candidate.Key);
        Assert.Equal(EventFamily.News, candidate.Family);
        Assert.Equal(Now.AddHours(-1), candidate.EligibilityTimestampUtc);
        Assert.False(candidate.IsLateItem);
        Assert.Equal(Now.AddHours(-24), selection.WindowStartUtc);
    }

    [Fact]
    public void Event_older_than_window_but_published_inside_is_a_late_item()
    {
        var selection = Selector().Select(
        [
            Record(
                EventFamily.Disaster,
                eventTimeUtc: Now.AddHours(-48),
                publishedTimeUtc: Now.AddHours(-2)),
        ]);

        var candidate = Assert.Single(selection.Candidates);
        Assert.True(candidate.IsLateItem);
        Assert.Equal(Now.AddHours(-2), candidate.EligibilityTimestampUtc);
    }

    [Fact]
    public void Missing_provider_timestamps_are_excluded_and_reported_as_a_gap()
    {
        var selection = Selector().Select(
        [
            Record(
                EventFamily.News,
                eventTimeUtc: null,
                publishedTimeUtc: null,
                fetchedUtc: Now.AddHours(-1)),
        ]);

        Assert.Empty(selection.Candidates);
        var gap = Assert.Single(
            selection.CoverageGaps,
            item => item.Kind == CatchUpGapKind.MissingProviderTimestamps);
        Assert.Equal(EventFamily.News, gap.Family);
        Assert.Equal("source-1", gap.SourceId);
        Assert.Null(gap.NewestProviderTimestampUtc);
    }

    [Fact]
    public void Future_publication_is_excluded()
    {
        var selection = Selector().Select(
        [
            Record(EventFamily.News, publishedTimeUtc: Now.AddHours(1)),
        ]);

        Assert.Empty(selection.Candidates);
    }

    [Fact]
    public void Future_event_time_is_excluded_even_when_publication_is_recent()
    {
        var evaluator = new CatchUpEligibilityEvaluator();
        var record = Record(
            EventFamily.Disaster,
            eventTimeUtc: Now.AddHours(1),
            publishedTimeUtc: Now.AddHours(-1));

        var result = evaluator.Evaluate(record, Now);

        Assert.Equal(CatchUpEligibilityStatus.FutureTimestamp, result.Status);
        Assert.False(result.IsEligible);
    }

    [Fact]
    public void Market_observation_inside_the_window_is_eligible()
    {
        var selection = Selector().Select(
            Array.Empty<NewsItem>(),
            [Market(Now.AddHours(-2))],
            Array.Empty<DisasterEvent>());

        var candidate = Assert.Single(selection.Candidates);
        Assert.Equal(EventFamily.Market, candidate.Family);
        Assert.Equal(Now.AddHours(-2), candidate.EligibilityTimestampUtc);
        Assert.False(string.IsNullOrWhiteSpace(candidate.Key));
    }

    [Fact]
    public void Market_observation_outside_the_window_is_excluded_and_reported_stale()
    {
        var selection = Selector().Select(
            Array.Empty<NewsItem>(),
            [Market(Now.AddHours(-25))],
            Array.Empty<DisasterEvent>());

        Assert.Empty(selection.Candidates);
        var gap = Assert.Single(
            selection.CoverageGaps,
            item => item.Kind == CatchUpGapKind.StaleSource
                && item.Family == EventFamily.Market);
        Assert.Equal(Now.AddHours(-25), gap.NewestProviderTimestampUtc);
    }

    [Fact]
    public void Disaster_with_event_time_but_no_publication_is_eligible()
    {
        var selection = Selector().Select(
            Array.Empty<NewsItem>(),
            Array.Empty<MarketObservation>(),
            [Disaster(eventTimeUtc: Now.AddHours(-3), publishedUtc: null)]);

        var candidate = Assert.Single(selection.Candidates);
        Assert.Equal(EventFamily.Disaster, candidate.Family);
        Assert.Equal("disaster-1", candidate.Key);
        Assert.Equal(Now.AddHours(-3), candidate.EligibilityTimestampUtc);
    }

    [Fact]
    public void Duplicate_native_id_across_two_records_yields_one_candidate()
    {
        var selection = Selector().Select(
        [
            Record(EventFamily.News, sourceItemId: "dup-1", publishedTimeUtc: Now.AddHours(-1)),
            Record(EventFamily.News, sourceItemId: "dup-1", publishedTimeUtc: Now.AddHours(-2)),
        ]);

        var candidate = Assert.Single(selection.Candidates);
        Assert.Equal("dup-1", candidate.Key);
        Assert.Equal(Now.AddHours(-2), candidate.EligibilityTimestampUtc);
    }

    [Fact]
    public void Already_delivered_id_is_excluded()
    {
        var delivered = new HashSet<string>(StringComparer.Ordinal) { "delivered-1" };

        var selection = Selector().Select(
        [
            Record(EventFamily.News, sourceItemId: "delivered-1", publishedTimeUtc: Now.AddHours(-1)),
            Record(EventFamily.News, sourceItemId: "new-1", publishedTimeUtc: Now.AddHours(-2)),
        ],
        delivered);

        var candidate = Assert.Single(selection.Candidates);
        Assert.Equal("new-1", candidate.Key);
    }

    [Fact]
    public void Empty_store_reports_one_gap_per_expected_family()
    {
        var selection = Selector().Select(Array.Empty<EventRecord>());

        Assert.Empty(selection.Candidates);
        Assert.Equal(3, selection.CoverageGaps.Count);
        Assert.All(
            selection.CoverageGaps,
            gap => Assert.Equal(CatchUpGapKind.EmptyCategory, gap.Kind));
        Assert.Equal(
            [EventFamily.News, EventFamily.Market, EventFamily.Disaster],
            selection.CoverageGaps.Select(gap => gap.Family));
    }

    [Fact]
    public void Candidates_are_ordered_oldest_first_deterministically()
    {
        var selection = Selector().Select(
        [
            Record(EventFamily.News, sourceItemId: "late-c", publishedTimeUtc: Now.AddHours(-1)),
            Record(EventFamily.News, sourceItemId: "late-a", publishedTimeUtc: Now.AddHours(-3)),
            Record(EventFamily.News, sourceItemId: "late-b2", publishedTimeUtc: Now.AddHours(-2)),
            Record(EventFamily.News, sourceItemId: "late-b", publishedTimeUtc: Now.AddHours(-2)),
        ]);

        Assert.Equal(
            ["late-a", "late-b", "late-b2", "late-c"],
            selection.Candidates.Select(candidate => candidate.Key));
    }

    [Fact]
    public void Window_boundary_at_exactly_24_hours_is_eligible()
    {
        var selection = Selector().Select(
        [
            Record(EventFamily.News, sourceItemId: "boundary-inside", publishedTimeUtc: Now.AddHours(-24)),
            Record(
                EventFamily.News,
                sourceItemId: "boundary-outside",
                publishedTimeUtc: Now.AddHours(-24).AddTicks(-1)),
        ]);

        var candidate = Assert.Single(selection.Candidates);
        Assert.Equal("boundary-inside", candidate.Key);
    }

    [Fact]
    public void Window_duration_is_configurable()
    {
        var record = Record(EventFamily.News, publishedTimeUtc: Now.AddHours(-2));

        var narrow = Selector(new CatchUpWindow(TimeSpan.FromHours(1))).Select([record]);
        var wide = Selector().Select([record]);

        Assert.Empty(narrow.Candidates);
        Assert.Single(wide.Candidates);
    }

    [Fact]
    public void Source_whose_newest_item_is_older_than_the_window_reports_a_stale_gap()
    {
        var selection = Selector().Select(
        [
            Record(EventFamily.News, publishedTimeUtc: Now.AddHours(-25)),
        ]);

        Assert.Empty(selection.Candidates);
        var gap = Assert.Single(
            selection.CoverageGaps,
            item => item.Kind == CatchUpGapKind.StaleSource);
        Assert.Equal("source-1", gap.SourceId);
        Assert.Equal(Now.AddHours(-25), gap.NewestProviderTimestampUtc);
    }

    private static EventRecord Record(
        EventFamily family,
        string sourceId = "source-1",
        string? sourceItemId = "item-1",
        DateTimeOffset? eventTimeUtc = null,
        DateTimeOffset? publishedTimeUtc = null,
        DateTimeOffset? fetchedUtc = null,
        string provenanceUrl = "https://example.test/feed") =>
        EventRecord.Create(
            family: family,
            sourceId: sourceId,
            sourceName: "Test Source",
            category: null,
            eventType: null,
            region: null,
            country: null,
            iso3: null,
            eventTimeUtc: eventTimeUtc,
            publishedTimeUtc: publishedTimeUtc,
            updatedTimeUtc: null,
            fetchedUtc: fetchedUtc ?? Now,
            latitude: null,
            longitude: null,
            severity: null,
            severityUnit: null,
            sourceItemId: sourceItemId,
            provenanceUrl: provenanceUrl,
            nowUtc: Now);

    private static MarketObservation Market(DateTimeOffset observationUtc) =>
        new(
            SourceId: "market-1",
            Venue: "Test Venue",
            Region: "global",
            Instrument: "BTCUSDT",
            QuoteUnit: "USD",
            Value: 100.5,
            ObservationUtc: observationUtc,
            FetchedUtc: Now,
            Delayed: false,
            DelaySemantics: "real-time",
            Interval: "1h",
            Provenance: "https://market.test/api");

    private static DisasterEvent Disaster(
        DateTimeOffset? eventTimeUtc,
        DateTimeOffset? publishedUtc) =>
        new(
            SourceId: "disaster-1",
            NativeId: "disaster-1",
            EventType: "earthquake",
            Title: "M6.0 test event",
            EventTimeUtc: eventTimeUtc,
            PublishedUtc: publishedUtc,
            UpdatedUtc: null,
            Latitude: null,
            Longitude: null,
            Country: null,
            Iso3: null,
            Severity: 6.0,
            SeverityUnit: "magnitude",
            AlertLevel: "Green",
            AlertScore: 1.0,
            Tsunami: false,
            Significance: null,
            Provenance: "https://disaster.test/api",
            FetchedUtc: Now);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
