using WorldAlerts.Api.Disasters;
using WorldAlerts.Api.Events;
using WorldAlerts.Api.Markets;
using WorldAlerts.Api.News;

namespace WorldAlerts.Api.Tests;

/// <summary>
/// Unit tests for mapping the existing family-specific records into the normalized
/// <see cref="EventRecord"/>. Alert eligibility and scoring are deliberately out of
/// scope for task 04 and are not asserted here.
/// </summary>
public sealed class EventRecordMappingTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private static NewsItem News(
        DateTimeOffset? publishedUtc = null,
        string sourceName = "Test Feed",
        string family = "world-news",
        string[]? categories = null) =>
        new(
            SourceId: "news-test",
            SourceName: sourceName,
            NativeId: "story-1",
            Title: "Test story",
            Link: "https://news.test/story-1",
            PublishedUtc: publishedUtc,
            FirstSeenUtc: Now,
            Family: family,
            Categories: categories ?? new[] { "world", "security" },
            Provenance: "https://news.test/rss.xml");

    private static MarketObservation Market(
        DateTimeOffset? observationUtc = null,
        string region = "global") =>
        new(
            SourceId: "market-test",
            Venue: "Test Venue",
            Region: region,
            Instrument: "BTCUSDT",
            QuoteUnit: "USD",
            Value: 100.5,
            ObservationUtc: observationUtc ?? Now.AddHours(-2),
            FetchedUtc: Now,
            Delayed: true,
            DelaySemantics: "delayed by 15 minutes",
            Interval: "1h",
            Provenance: "https://market.test/api");

    private static DisasterEvent Disaster(
        DateTimeOffset? eventTimeUtc,
        DateTimeOffset? publishedUtc,
        DateTimeOffset? updatedUtc = null,
        double? latitude = 35.0,
        double? longitude = 139.0,
        string? country = "Japan",
        string? iso3 = "JPN",
        double? severity = 6.0,
        string? severityUnit = "magnitude") =>
        new(
            SourceId: "disaster-test",
            NativeId: "evt-1",
            EventType: "earthquake",
            Title: "M6.0 test event",
            EventTimeUtc: eventTimeUtc,
            PublishedUtc: publishedUtc,
            UpdatedUtc: updatedUtc,
            Latitude: latitude,
            Longitude: longitude,
            Country: country,
            Iso3: iso3,
            Severity: severity,
            SeverityUnit: severityUnit,
            AlertLevel: "orange",
            AlertScore: 1.5,
            Tsunami: false,
            Significance: 600,
            Provenance: "https://disaster.test/feed",
            FetchedUtc: Now,
            Certainty: "observed",
            EffectiveUtc: Now.AddHours(-3),
            ExpiresUtc: Now.AddHours(6),
            DepthKm: 10.0);

    [Fact]
    public void News_maps_identity_category_provenance_and_metadata()
    {
        var record = EventRecordMappers.FromNews(
            News(publishedUtc: Now.AddHours(-1)),
            Now);

        Assert.Equal(EventFamily.News, record.Family);
        Assert.Equal("news-test", record.SourceId);
        Assert.Equal("Test Feed", record.SourceName);
        Assert.Equal("world-news", record.Category);
        Assert.Null(record.EventType);
        Assert.Null(record.Region);
        Assert.Null(record.Country);
        Assert.Null(record.Iso3);
        Assert.Null(record.EventTimeUtc);
        Assert.Equal(Now.AddHours(-1), record.PublishedTimeUtc);
        Assert.Null(record.UpdatedTimeUtc);
        Assert.Equal(Now, record.FetchedUtc);
        Assert.Null(record.Latitude);
        Assert.Null(record.Longitude);
        Assert.Null(record.Severity);
        Assert.Null(record.SeverityUnit);
        Assert.Equal("story-1", record.SourceItemId);
        Assert.Equal("https://news.test/rss.xml", record.ProvenanceUrl);
        Assert.Equal("story-1", record.Metadata["nativeId"]);
        Assert.Equal("Test story", record.Metadata["title"]);
        Assert.Equal("https://news.test/story-1", record.Metadata["link"]);
        Assert.Equal("world,security", record.Metadata["categories"]);
        Assert.Equal(Now.AddHours(-1), record.EffectiveTimestampUtc);
        Assert.Equal(TimestampBasis.PublishedTime, record.EffectiveTimestampBasis);
        Assert.Equal(EventTimestampAnomalies.None, record.Anomalies);
    }

    [Fact]
    public void News_without_publication_time_uses_first_seen_and_is_flagged()
    {
        var record = EventRecordMappers.FromNews(News(publishedUtc: null), Now);

        Assert.Null(record.PublishedTimeUtc);
        Assert.Equal(Now, record.EffectiveTimestampUtc);
        Assert.Equal(TimestampBasis.FetchedTime, record.EffectiveTimestampBasis);
        Assert.Equal(EventTimestampAnomalies.MissingProviderTimes, record.Anomalies);
    }

    [Fact]
    public void News_publication_after_fetch_and_in_the_future_is_flagged_through_the_mapper()
    {
        var record = EventRecordMappers.FromNews(News(publishedUtc: Now.AddHours(1)), Now);

        Assert.Equal(Now.AddHours(1), record.PublishedTimeUtc);
        Assert.Equal(
            EventTimestampAnomalies.PublishedAfterFetched | EventTimestampAnomalies.FutureTimestamp,
            record.Anomalies);
    }

    [Fact]
    public void Market_maps_observation_time_region_and_quote_metadata()
    {
        var record = EventRecordMappers.FromMarket(Market(), Now);

        Assert.Equal(EventFamily.Market, record.Family);
        Assert.Equal("market-test", record.SourceId);
        Assert.Equal("Test Venue", record.SourceName);
        Assert.Null(record.Category);
        Assert.Null(record.EventType);
        Assert.Equal("global", record.Region);
        Assert.Null(record.Country);
        Assert.Null(record.Iso3);
        Assert.Equal(Now.AddHours(-2), record.EventTimeUtc);
        Assert.Null(record.PublishedTimeUtc);
        Assert.Null(record.UpdatedTimeUtc);
        Assert.Equal(Now, record.FetchedUtc);
        Assert.Null(record.Severity);
        Assert.Null(record.SourceItemId);
        Assert.Equal("https://market.test/api", record.ProvenanceUrl);
        Assert.Equal("Test Venue", record.Metadata["venue"]);
        Assert.Equal("BTCUSDT", record.Metadata["instrument"]);
        Assert.Equal("USD", record.Metadata["quoteUnit"]);
        Assert.Equal("100.5", record.Metadata["value"]);
        Assert.Equal("true", record.Metadata["delayed"]);
        Assert.Equal("delayed by 15 minutes", record.Metadata["delaySemantics"]);
        Assert.Equal("1h", record.Metadata["interval"]);
        Assert.Equal(Now.AddHours(-2), record.EffectiveTimestampUtc);
        Assert.Equal(TimestampBasis.EventTime, record.EffectiveTimestampBasis);
        Assert.Equal(EventTimestampAnomalies.None, record.Anomalies);
    }

    [Fact]
    public void Disaster_maps_type_geography_severity_and_revision_time()
    {
        var record = EventRecordMappers.FromDisaster(
            Disaster(
                eventTimeUtc: Now.AddHours(-2),
                publishedUtc: Now.AddHours(-1),
                updatedUtc: Now.AddMinutes(-30)),
            Now);

        Assert.Equal(EventFamily.Disaster, record.Family);
        Assert.Equal("disaster-test", record.SourceId);
        Assert.Equal("disaster-test", record.SourceName);
        Assert.Null(record.Category);
        Assert.Equal("earthquake", record.EventType);
        Assert.Null(record.Region);
        Assert.Equal("Japan", record.Country);
        Assert.Equal("JPN", record.Iso3);
        Assert.Equal(Now.AddHours(-2), record.EventTimeUtc);
        Assert.Equal(Now.AddHours(-1), record.PublishedTimeUtc);
        Assert.Equal(Now.AddMinutes(-30), record.UpdatedTimeUtc);
        Assert.Equal(35.0, record.Latitude);
        Assert.Equal(139.0, record.Longitude);
        Assert.Equal(6.0, record.Severity);
        Assert.Equal("magnitude", record.SeverityUnit);
        Assert.Equal("evt-1", record.SourceItemId);
        Assert.Equal("https://disaster.test/feed", record.ProvenanceUrl);
        Assert.Equal("M6.0 test event", record.Metadata["title"]);
        Assert.Equal("orange", record.Metadata["alertLevel"]);
        Assert.Equal("1.5", record.Metadata["alertScore"]);
        Assert.Equal("false", record.Metadata["tsunami"]);
        Assert.Equal("600", record.Metadata["significance"]);
        Assert.Equal("observed", record.Metadata["certainty"]);
        Assert.Equal("10", record.Metadata["depthKm"]);
        Assert.Equal(Now.AddHours(-2), record.EffectiveTimestampUtc);
        Assert.Equal(TimestampBasis.EventTime, record.EffectiveTimestampBasis);
        Assert.Equal(EventTimestampAnomalies.None, record.Anomalies);
    }

    [Fact]
    public void Disaster_with_null_region_and_severity_preserves_nulls()
    {
        var record = EventRecordMappers.FromDisaster(
            Disaster(
                eventTimeUtc: null,
                publishedUtc: Now.AddHours(-1),
                latitude: null,
                longitude: null,
                country: null,
                iso3: null,
                severity: null,
                severityUnit: null),
            Now);

        Assert.Null(record.Region);
        Assert.Null(record.Country);
        Assert.Null(record.Iso3);
        Assert.Null(record.Latitude);
        Assert.Null(record.Longitude);
        Assert.Null(record.Severity);
        Assert.Null(record.SeverityUnit);
        Assert.Equal("M6.0 test event", record.Metadata["title"]);
        Assert.Equal(Now.AddHours(-1), record.EffectiveTimestampUtc);
        Assert.Equal(TimestampBasis.PublishedTime, record.EffectiveTimestampBasis);
        Assert.Equal(EventTimestampAnomalies.None, record.Anomalies);
    }

    [Fact]
    public void Conflicting_disaster_times_are_preserved_and_flagged()
    {
        var eventTime = Now.AddMinutes(-30);
        var published = Now.AddHours(-2);

        var record = EventRecordMappers.FromDisaster(
            Disaster(eventTimeUtc: eventTime, publishedUtc: published),
            Now);

        Assert.Equal(eventTime, record.EventTimeUtc);
        Assert.Equal(published, record.PublishedTimeUtc);
        Assert.Equal(eventTime, record.EffectiveTimestampUtc);
        Assert.Equal(EventTimestampAnomalies.EventAfterPublished, record.Anomalies);
    }

    [Fact]
    public void Only_a_fetch_time_available_falls_back_to_fetch_and_is_flagged()
    {
        var record = EventRecordMappers.FromDisaster(
            Disaster(eventTimeUtc: null, publishedUtc: null),
            Now);

        Assert.Null(record.EventTimeUtc);
        Assert.Null(record.PublishedTimeUtc);
        Assert.Equal(Now, record.EffectiveTimestampUtc);
        Assert.Equal(TimestampBasis.FetchedTime, record.EffectiveTimestampBasis);
        Assert.Equal(EventTimestampAnomalies.MissingProviderTimes, record.Anomalies);
    }

    [Fact]
    public void Provenance_and_source_specific_metadata_are_preserved_for_every_family()
    {
        var records = new[]
        {
            EventRecordMappers.FromNews(News(publishedUtc: Now.AddHours(-1)), Now),
            EventRecordMappers.FromMarket(Market(), Now),
            EventRecordMappers.FromDisaster(
                Disaster(eventTimeUtc: Now.AddHours(-2), publishedUtc: Now.AddHours(-1)),
                Now),
        };

        Assert.All(records, record =>
        {
            Assert.False(string.IsNullOrWhiteSpace(record.ProvenanceUrl));
            Assert.NotEmpty(record.Metadata);
            Assert.Equal(EventTimestampAnomalies.None, record.Anomalies);
        });
        Assert.Equal(
            new[] { "news-test", "market-test", "disaster-test" },
            records.Select(record => record.SourceId).ToArray());
    }
}
