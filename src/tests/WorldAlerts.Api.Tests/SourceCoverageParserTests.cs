using WorldAlerts.Api.Advisories;
using WorldAlerts.Api.Disasters;

namespace WorldAlerts.Api.Tests;

public sealed class SourceCoverageParserTests
{
    private static readonly DateTimeOffset Fetched =
        new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Eonet_merges_categories_and_deduplicates_native_event_ids()
    {
        var source = DisasterTestSupport.Source(
            DisasterParser.Eonet,
            id: "eonet",
            categories: new[] { "volcanoes", "landslides", "drought" });

        var result = DisasterParser.ParseMany(
            new[]
            {
                SourceCoverageFixtures.EonetVolcanoes,
                SourceCoverageFixtures.EonetDuplicateAndLandslide,
                SourceCoverageFixtures.EonetEmpty,
            },
            source,
            Fetched);

        Assert.Equal(3, result.Items.Count);
        Assert.Equal(new[] { "EONET_1", "EONET_2", "EONET_3" }, result.Items.Select(item => item.NativeId));
        Assert.Equal("Volcano A", result.Items[0].Title);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public void Dwd_warnings_filters_heat_and_cold_and_deduplicates_within_a_poll()
    {
        var source = DisasterTestSupport.Source(
            DisasterParser.DwdWarnings,
            id: "dwd-warnings",
            events: new[] { "HITZE", "EXTREME HITZE", "FROST", "STRENGE FROST" });

        var result = DisasterParser.Parse(SourceCoverageFixtures.DwdWarnings, source, Fetched);

        Assert.Equal(3, result.Items.Count);
        Assert.Equal(0, result.Skipped);

        var firstHeat = result.Items[0];
        Assert.Equal("dwd:Insel Helgoland:HITZE:1792512000000:1792566000000", firstHeat.NativeId);
        Assert.Equal("hitze", firstHeat.EventType);
        Assert.Equal("Amtliche WARNUNG vor HITZE - Insel Helgoland", firstHeat.Title);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1792512000000), firstHeat.EventTimeUtc);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1792548705000), firstHeat.PublishedUtc);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1792512000000), firstHeat.EffectiveUtc);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1792566000000), firstHeat.ExpiresUtc);
        Assert.Equal("Germany", firstHeat.Country);
        Assert.Equal("DEU", firstHeat.Iso3);
        Assert.Equal(3, firstHeat.Severity!.Value);
        Assert.Equal("DWD warning level", firstHeat.SeverityUnit);
        Assert.Equal("3", firstHeat.AlertLevel);

        var secondHeat = result.Items[1];
        Assert.Equal("dwd:Ostfriesische Küste:HITZE:1792529130000:na", secondHeat.NativeId);
        Assert.Null(secondHeat.ExpiresUtc);

        var frost = result.Items[2];
        Assert.Equal("dwd:Test Frost Region:FROST:1792600000000:1792650000000", frost.NativeId);
        Assert.Equal("frost", frost.EventType);
        Assert.DoesNotContain(
            result.Items,
            item => item.EventType.Contains("wind", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("warnWetter.loadWarnings({\"time\":1792548705000,\"warnings\":null});")]
    [InlineData("warnWetter.loadWarnings({\"time\":1792548705000,\"warnings\":{}});")]
    public void Dwd_empty_or_null_warnings_is_a_successful_zero_event_parse(string body)
    {
        var source = DisasterTestSupport.Source(DisasterParser.DwdWarnings, id: "dwd-warnings");

        var result = DisasterParser.Parse(body, source, Fetched);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public void Nws_parses_severity_certainty_times_centroid_and_missing_geometry()
    {
        var source = DisasterTestSupport.Source(
            DisasterParser.Nws,
            id: "nws-alerts",
            semantics: "US/territories only");

        var result = DisasterParser.Parse(SourceCoverageFixtures.NwsAlerts, source, Fetched);

        Assert.Equal(2, result.Items.Count);
        var heat = result.Items[0];
        Assert.Equal("urn:test:1", heat.NativeId);
        Assert.Equal("Heat Advisory", heat.EventType);
        Assert.Equal("Moderate", heat.AlertLevel);
        Assert.Equal("NWS severity", heat.SeverityUnit);
        Assert.Equal("Likely", heat.Certainty);
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero), heat.EventTimeUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 11, 0, 0, TimeSpan.Zero), heat.EffectiveUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero), heat.ExpiresUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 10, 0, 0, TimeSpan.Zero), heat.PublishedUtc);
        Assert.Equal("United States", heat.Country);
        Assert.Equal("USA", heat.Iso3);
        Assert.Equal(34.0, heat.Latitude!.Value);
        Assert.Equal(-96.0, heat.Longitude!.Value);
        Assert.Contains("US/territories only", heat.Provenance);

        var tsunami = result.Items[1];
        Assert.Null(tsunami.Latitude);
        Assert.Null(tsunami.Longitude);
        Assert.Equal("Severe", tsunami.AlertLevel);
    }

    [Fact]
    public void Nws_empty_feature_collection_is_a_successful_zero_event_parse()
    {
        var source = DisasterTestSupport.Source(DisasterParser.Nws, id: "nws-alerts");

        var result = DisasterParser.Parse(SourceCoverageFixtures.NwsEmpty, source, Fetched);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public void Ems_maps_known_and_unknown_categories_countries_and_centroid()
    {
        var source = DisasterTestSupport.Source(DisasterParser.CopernicusEms, id: "copernicus-ems");

        var result = DisasterParser.Parse(SourceCoverageFixtures.Ems, source, Fetched);

        Assert.Equal(2, result.Items.Count);
        var flood = result.Items[0];
        Assert.Equal("flood", flood.EventType);
        Assert.Equal("Greece", flood.Country);
        Assert.Equal("GRC", flood.Iso3);
        Assert.Equal(37.9, flood.Latitude!.Value);
        Assert.Equal(23.7, flood.Longitude!.Value);
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero), flood.EventTimeUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 6, 0, 0, TimeSpan.Zero), flood.PublishedUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 7, 0, 0, TimeSpan.Zero), flood.UpdatedUtc);

        var unknown = result.Items[1];
        Assert.Equal("space-weather", unknown.EventType);
        Assert.Equal("Testland", unknown.Country);
        Assert.Null(unknown.Iso3);
        Assert.Null(unknown.Latitude);
        Assert.Null(unknown.Longitude);
    }

    [Fact]
    public void Emsc_maps_time_magnitude_depth_region_and_attribution()
    {
        var source = DisasterTestSupport.Source(
            DisasterParser.Emsc,
            id: "emsc",
            semantics: "CC BY 4.0");

        var result = DisasterParser.Parse(SourceCoverageFixtures.Emsc, source, Fetched);

        var item = Assert.Single(result.Items);
        Assert.Equal("emsc-1", item.NativeId);
        Assert.Equal("earthquake", item.EventType);
        Assert.Equal("M 4.2 - Greece", item.Title);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 18, 0, 0, TimeSpan.Zero), item.EventTimeUtc);
        Assert.Equal(4.2, item.Severity!.Value);
        Assert.Equal("ML", item.SeverityUnit);
        Assert.Equal(10.5, item.DepthKm!.Value);
        Assert.Equal(37.9, item.Latitude!.Value);
        Assert.Equal(23.7, item.Longitude!.Value);
        Assert.Contains("CC BY 4.0", item.Provenance);
    }

    [Fact]
    public void Cisa_kev_parses_cve_vendor_product_dates_and_ransomware_flag()
    {
        var source = new AdvisorySource
        {
            Id = "cisa-kev",
            Name = "CISA KEV",
            Url = "https://www.cisa.gov/known-exploited-vulnerabilities.json",
        };

        var result = AdvisoryParser.Parse(SourceCoverageFixtures.CisaKev, source, Fetched);

        Assert.Equal(2, result.Items.Count);
        var known = result.Items[0];
        Assert.Equal("CVE-2026-0001", known.CveId);
        Assert.Equal("Acme", known.Vendor);
        Assert.Equal("Widget", known.Product);
        Assert.Equal("Acme Widget remote code execution", known.Name);
        Assert.Equal(new DateOnly(2026, 10, 1), known.DateAdded);
        Assert.Equal(new DateOnly(2026, 10, 22), known.DueDate);
        Assert.True(known.KnownRansomware);
        Assert.Equal(source.Url, known.Provenance);
        Assert.False(result.Items[1].KnownRansomware);
    }
}
