using WorldAlerts.Api.Disasters;

namespace WorldAlerts.Api.Tests;

public sealed class DisasterParserTests
{
    private static readonly DateTimeOffset Fetched =
        new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Gdacs_rss_preserves_alert_level_score_severity_and_location()
    {
        var source = DisasterTestSupport.Source(DisasterParser.GdacsRss);

        var result = DisasterParser.Parse(DisasterFixtures.GdacsRss, source, Fetched);

        var item = Assert.Single(result.Items);
        Assert.Equal(1, result.Skipped);
        Assert.Equal("123", item.NativeId);
        Assert.Equal("earthquake", item.EventType);
        Assert.Equal("Earthquake in Japan", item.Title);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero), item.EventTimeUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero), item.PublishedUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 11, 0, 0, TimeSpan.Zero), item.UpdatedUtc);
        Assert.Equal(35.1, item.Latitude);
        Assert.Equal(139.2, item.Longitude);
        Assert.Equal("Japan", item.Country);
        Assert.Equal("JPN", item.Iso3);
        Assert.Equal(5.4, item.Severity);
        Assert.Equal("M", item.SeverityUnit);
        Assert.Equal("Orange", item.AlertLevel);
        Assert.Equal(1.5, item.AlertScore);
        Assert.Equal(source.Url, item.Provenance);
    }

    [Fact]
    public void Eonet_uses_the_latest_geometry_and_preserves_magnitude_units()
    {
        var source = DisasterTestSupport.Source(DisasterParser.Eonet);
        // This test is about normalized-field preservation, so keep the
        // geometry-less event that the default recency filter would drop.
        source.RecencyWindowHours = 0;

        var result = DisasterParser.Parse(DisasterFixtures.Eonet, source, Fetched);

        Assert.Equal(2, result.Items.Count);
        var fire = result.Items[0];
        Assert.Equal("EONET_1", fire.NativeId);
        Assert.Equal("wildfires", fire.EventType);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero), fire.EventTimeUtc);
        Assert.Equal(750, fire.Severity);
        Assert.Equal("acres", fire.SeverityUnit);
        Assert.Equal(38.3, fire.Latitude);
        Assert.Equal(-120.4, fire.Longitude);
        Assert.Null(fire.PublishedUtc);

        var storm = result.Items[1];
        Assert.Null(storm.EventTimeUtc);
        Assert.Null(storm.Severity);
        Assert.Null(storm.Latitude);
    }

    [Fact]
    public void Usgs_geojson_preserves_magnitude_tsunami_and_significance()
    {
        var source = DisasterTestSupport.Source(DisasterParser.Usgs);

        var result = DisasterParser.Parse(DisasterFixtures.Usgs, source, Fetched);

        var item = Assert.Single(result.Items);
        Assert.Equal("us7000abcd", item.NativeId);
        Assert.Equal("earthquake", item.EventType);
        Assert.Equal("M 4.5 - 10 km south of Test", item.Title);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1761046800000), item.EventTimeUtc);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1761047400000), item.UpdatedUtc);
        Assert.Equal(35.1, item.Latitude);
        Assert.Equal(139.2, item.Longitude);
        Assert.Equal(4.5, item.Severity);
        Assert.Equal("mb", item.SeverityUnit);
        Assert.True(item.Tsunami);
        Assert.Equal(312, item.Significance);
        Assert.Equal("yellow", item.AlertLevel);
        Assert.Equal("https://earthquake.usgs.gov/earthquakes/eventpage/us7000abcd", item.Provenance);
    }

    [Fact]
    public void Ifrc_json_preserves_dates_country_alert_level_and_point()
    {
        var source = DisasterTestSupport.Source(DisasterParser.Ifrc);

        var result = DisasterParser.Parse(DisasterFixtures.Ifrc, source, Fetched);

        var item = Assert.Single(result.Items);
        Assert.Equal("987", item.NativeId);
        Assert.Equal("Flood", item.EventType);
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero), item.EventTimeUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 6, 0, 0, TimeSpan.Zero), item.PublishedUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 7, 0, 0, TimeSpan.Zero), item.UpdatedUtc);
        Assert.Equal("Test Country", item.Country);
        Assert.Equal("TST", item.Iso3);
        Assert.Equal("Orange", item.AlertLevel);
        Assert.Equal(1.5, item.Latitude);
        Assert.Equal(2.5, item.Longitude);
        Assert.Equal("https://disaster.test/data/987/", item.Provenance);
    }

    [Fact]
    public void Empty_ifrc_body_is_zero_events_not_a_crash()
    {
        var result = DisasterParser.Parse(
            string.Empty,
            DisasterTestSupport.Source(DisasterParser.Ifrc),
            Fetched);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public void Future_provider_timestamps_are_not_considered_recent()
    {
        var source = DisasterTestSupport.Source(DisasterParser.Eonet);
        source.RecencyWindowHours = 24;
        const string body = """
            {"events":[{"id":"future","title":"Future geometry","categories":[{"id":"wildfires"}],"geometry":[{"date":"2026-10-22T12:00:00Z","type":"Point","coordinates":[1,2]}]}]}
            """;

        var result = DisasterParser.Parse(body, source, Fetched);

        Assert.Empty(result.Items);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public void Gdacs_pubdate_is_the_fallback_when_update_and_event_dates_are_absent()
    {
        var source = DisasterTestSupport.Source(DisasterParser.GdacsRss);
        source.RecencyWindowHours = 168;
        const string body = """
            <rss><channel><item><title>New notice</title><guid>new</guid><pubDate>Tue, 20 Oct 2026 12:00:00 GMT</pubDate></item><item><title>Old notice</title><guid>old</guid><pubDate>Thu, 01 Jan 2026 12:00:00 GMT</pubDate></item></channel></rss>
            """;

        var result = DisasterParser.Parse(body, source, Fetched);

        Assert.Equal("new", Assert.Single(result.Items).NativeId);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public void Gdacs_publication_and_modification_remain_distinct()
    {
        var source = DisasterTestSupport.Source(DisasterParser.GdacsRss);
        const string body = """
            <rss><channel><item><title>Modified notice</title><guid>notice</guid><gdacs:iscurrent xmlns:gdacs="http://www.gdacs.org">false</gdacs:iscurrent><gdacs:datemodified xmlns:gdacs="http://www.gdacs.org">2026-10-20T12:00:00Z</gdacs:datemodified><pubDate>Thu, 01 Jan 2026 12:00:00 GMT</pubDate></item></channel></rss>
            """;

        var item = Assert.Single(DisasterParser.Parse(body, source, Fetched).Items);

        Assert.Equal(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero), item.PublishedUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero), item.UpdatedUtc);
        Assert.Null(item.EventTimeUtc);
    }

    [Fact]
    public void Ifrc_start_creation_and_update_are_separate_provider_timestamps()
    {
        var source = DisasterTestSupport.Source(DisasterParser.Ifrc);
        const string body = """
            {"results":[{"id":7,"name":"Flood","disaster_start_date":"2026-01-01T00:00:00Z","created_at":"2026-01-02T00:00:00Z","updated_at":"2026-10-20T00:00:00Z"},{"id":8,"name":"Undated"}]}
            """;

        var result = DisasterParser.Parse(body, source, Fetched);

        Assert.Equal(2, result.Items.Count);
        var item = result.Items[0];
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), item.EventTimeUtc);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), item.PublishedUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero), item.UpdatedUtc);
        Assert.Null(result.Items[1].EventTimeUtc);
        Assert.Null(result.Items[1].PublishedUtc);
        Assert.Null(result.Items[1].UpdatedUtc);
    }

    [Fact]
    public void Closed_ems_uses_recent_activation_when_update_is_missing()
    {
        var source = DisasterTestSupport.Source(DisasterParser.CopernicusEms);
        source.RecencyWindowHours = 24;
        const string body = """
            [{"id":1,"name":"Recent activation","closed":true,"activationTime":"2026-10-20T12:00:00Z"},{"id":2,"name":"Historical activation","closed":true,"activationTime":"2026-01-01T00:00:00Z"}]
            """;

        var result = DisasterParser.Parse(body, source, Fetched);

        Assert.Equal("1", Assert.Single(result.Items).NativeId);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public void Eonet_uses_newest_geometry_even_when_provider_lists_it_first()
    {
        var source = DisasterTestSupport.Source(DisasterParser.Eonet);
        source.RecencyWindowHours = 24;
        const string body = """
            {"events":[{"id":"fire","title":"Fire","categories":[{"id":"wildfires"}],"geometry":[{"date":"2026-10-20T12:00:00Z","type":"Point","coordinates":[10,20]},{"date":"2026-01-01T00:00:00Z","type":"Point","coordinates":[30,40]}]}]}
            """;

        var result = DisasterParser.Parse(body, source, Fetched);

        var item = Assert.Single(result.Items);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero), item.EventTimeUtc);
        Assert.Equal(20, item.Latitude);
    }

    [Fact]
    public void Unconfigured_archive_policy_keeps_historical_and_undated_rows_without_fabricating_times()
    {
        var source = DisasterTestSupport.Source(DisasterParser.CopernicusEms);
        const string body = """
            [{"id":1,"name":"Closed old","closed":true,"activationTime":"2026-01-01T00:00:00Z","lastUpdate":"2026-01-02T00:00:00Z"},{"id":2,"name":"Undated","closed":true}]
            """;

        var result = DisasterParser.Parse(body, source, Fetched);

        Assert.Equal(2, result.Items.Count);
        Assert.Null(result.Items[1].EventTimeUtc);
        Assert.Null(result.Items[1].PublishedUtc);
        Assert.Null(result.Items[1].UpdatedUtc);
    }
}
