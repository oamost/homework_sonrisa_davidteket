using WorldAlerts.Api.Disasters;

namespace WorldAlerts.Api.Tests;

/// <summary>
/// Opt-in source-specific archive filtering. The fetch time is fixed so
/// boundaries are deterministic; none of these windows grants 24-hour
/// notification eligibility.
/// </summary>
public sealed class DisasterRecencyTests
{
    private static readonly DateTimeOffset Fetched =
        new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Gdacs_keeps_current_and_recently_modified_and_skips_old_or_undated()
    {
        var source = DisasterTestSupport.Source(DisasterParser.GdacsRss);
        source.RecencyWindowHours = 168;

        var result = DisasterParser.Parse(DisasterFixtures.GdacsRecency, source, Fetched);

        Assert.Equal(
            new[] { "201", "202", "205", "206" },
            result.Items.Select(item => item.NativeId));
        Assert.Equal(2, result.Skipped);
        Assert.DoesNotContain(result.Items, item => item.NativeId == "203");
        Assert.DoesNotContain(result.Items, item => item.NativeId == "204");
    }

    [Fact]
    public void Gdacs_configured_window_overrides_the_default()
    {
        var source = DisasterTestSupport.Source(DisasterParser.GdacsRss);
        source.RecencyWindowHours = 1;

        var result = DisasterParser.Parse(DisasterFixtures.GdacsRecency, source, Fetched);

        // Only the current event survives a one-hour window; the fallback event
        // date and the boundary modification are both older than one hour.
        Assert.Equal(new[] { "201" }, result.Items.Select(item => item.NativeId));
        Assert.Equal(5, result.Skipped);
    }

    [Fact]
    public void Eonet_uses_longer_volcano_window_and_skips_old_or_undated()
    {
        var source = DisasterTestSupport.Source(DisasterParser.Eonet);
        source.RecencyWindowHours = 720;
        source.VolcanoRecencyWindowHours = 2160;

        var result = DisasterParser.Parse(DisasterFixtures.EonetRecency, source, Fetched);

        Assert.Equal(
            new[] { "EONET_RECENT", "EONET_VOLCANO", "EONET_BOUNDARY" },
            result.Items.Select(item => item.NativeId));
        Assert.Equal(3, result.Skipped);
        Assert.DoesNotContain(result.Items, item => item.NativeId == "EONET_OLD");
        Assert.DoesNotContain(result.Items, item => item.NativeId == "EONET_OLD_WILDFIRE");
        Assert.DoesNotContain(result.Items, item => item.NativeId == "EONET_MISSING");
    }

    [Fact]
    public void Ifrc_maps_disaster_start_date_and_keeps_only_recent_audit_dates()
    {
        var source = DisasterTestSupport.Source(DisasterParser.Ifrc);
        source.RecencyWindowHours = 168;

        var result = DisasterParser.Parse(DisasterFixtures.IfrcRecency, source, Fetched);

        Assert.Equal(
            new[] { "401", "403", "405", "406" },
            result.Items.Select(item => item.NativeId));
        Assert.Equal(2, result.Skipped);

        var recentCreation = result.Items[0];
        Assert.Equal(
            new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero),
            recentCreation.EventTimeUtc);
        Assert.Equal(
            new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero),
            recentCreation.PublishedUtc);

        var fallback = result.Items[1];
        Assert.Equal(
            new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero),
            fallback.EventTimeUtc);

        Assert.DoesNotContain(result.Items, item => item.NativeId == "402");
        Assert.DoesNotContain(result.Items, item => item.NativeId == "404");
    }

    [Fact]
    public void Copernicus_keeps_open_or_recently_updated_and_drops_closed_archive()
    {
        var source = DisasterTestSupport.Source(DisasterParser.CopernicusEms);
        source.RecencyWindowHours = 336;

        var result = DisasterParser.Parse(DisasterFixtures.EmsRecency, source, Fetched);

        Assert.Equal(
            new[] { "502", "503", "504", "506" },
            result.Items.Select(item => item.NativeId));
        Assert.Equal(2, result.Skipped);

        var recentlyUpdated = result.Items[1];
        Assert.Equal(
            new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero),
            recentlyUpdated.UpdatedUtc);

        // Missing "closed" is treated as open because the payload cannot prove
        // the activation is historical.
        var missingClosed = result.Items[2];
        Assert.Null(missingClosed.UpdatedUtc);

        Assert.DoesNotContain(result.Items, item => item.NativeId == "501");
        Assert.DoesNotContain(result.Items, item => item.NativeId == "505");
    }

    [Fact]
    public void A_feed_with_only_archive_rows_is_a_successful_zero_event_parse()
    {
        const string body =
            """
            [
              {
                "id": 9001,
                "name": "Closed historical activation",
                "category": "Flood",
                "closed": true,
                "lastUpdate": "2003-01-01T00:00:00Z"
              }
            ]
            """;
        var source = DisasterTestSupport.Source(DisasterParser.CopernicusEms);
        source.RecencyWindowHours = 336;

        var result = DisasterParser.Parse(body, source, Fetched);

        Assert.Empty(result.Items);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public void Unconfigured_archive_retention_keeps_historical_and_undated_provider_rows()
    {
        var source = DisasterTestSupport.Source(DisasterParser.CopernicusEms);

        var result = DisasterParser.Parse(DisasterFixtures.EmsRecency, source, Fetched);

        Assert.Equal(new[] { "501", "502", "503", "504", "505", "506" },
            result.Items.Select(item => item.NativeId));
        Assert.Equal(0, result.Skipped);
        Assert.Null(result.Items[4].UpdatedUtc);
    }
}
