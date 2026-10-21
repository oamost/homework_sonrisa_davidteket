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
}
