using WorldAlerts.Api.Markets;

namespace WorldAlerts.Api.Tests;

public sealed class MarketParserTests
{
    private static readonly DateTimeOffset Fetched =
        new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Binance_ticker_uses_last_price_and_close_time()
    {
        var source = MarketTestSupport.Source(
            MarketParser.BinanceTicker,
            instrument: "BTCUSDT",
            interval: "24h");

        var result = MarketParser.Parse(MarketFixtures.BinanceTicker, source, Fetched);

        var item = Assert.Single(result.Items);
        Assert.Equal(0, result.Skipped);
        Assert.Equal("market-binance-ticker", item.SourceId);
        Assert.Equal("Test Venue", item.Venue);
        Assert.Equal("BTCUSDT", item.Instrument);
        Assert.Equal(67250.10, item.Value);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1761127199999), item.ObservationUtc);
        Assert.Equal(Fetched, item.FetchedUtc);
        Assert.False(item.Delayed);
        Assert.Equal("24h", item.Interval);
    }

    [Fact]
    public void Binance_klines_store_one_observation_per_open_time_and_skip_bad_rows()
    {
        var source = MarketTestSupport.Source(
            MarketParser.BinanceKlines,
            instrument: "BTCUSDT",
            interval: "1h");

        var result = MarketParser.Parse(MarketFixtures.BinanceKlines, source, Fetched);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(67200.0, result.Items[0].Value);
        Assert.Equal(67450.0, result.Items[1].Value);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1761040800000), result.Items[0].ObservationUtc);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1761044400000), result.Items[1].ObservationUtc);
        Assert.All(result.Items, item => Assert.Equal("1h", item.Interval));
    }

    [Fact]
    public void Frankfurter_stores_the_ecb_reference_date_not_an_intraday_time()
    {
        var source = MarketTestSupport.Source(
            MarketParser.Frankfurter,
            instrument: "EUR/USD",
            interval: "1d",
            toCurrency: "USD");
        source.Delayed = true;
        source.DelaySemantics = "daily reference";

        var result = MarketParser.Parse(MarketFixtures.Frankfurter, source, Fetched);

        var item = Assert.Single(result.Items);
        Assert.Equal(1.1642, item.Value);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero), item.ObservationUtc);
        Assert.True(item.Delayed);
        Assert.Equal("daily reference", item.DelaySemantics);
        Assert.Equal("1d", item.Interval);
    }

    [Fact]
    public void Cboe_uses_the_provider_timestamp_and_marks_delayed_semantics()
    {
        var source = MarketTestSupport.Source(
            MarketParser.Cboe,
            instrument: "SPX",
            interval: "quote");
        source.Delayed = true;
        source.DelaySemantics = "delayed quote";

        var result = MarketParser.Parse(MarketFixtures.Cboe, source, Fetched);

        var item = Assert.Single(result.Items);
        Assert.Equal(6754.12, item.Value);
        Assert.Equal("^SPX", item.Instrument);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 20, 0, 0, TimeSpan.Zero), item.ObservationUtc);
        Assert.True(item.Delayed);
        Assert.Equal("delayed quote", item.DelaySemantics);
    }

    [Fact]
    public void Twse_stores_each_roc_dated_closing_price_and_skips_missing_values()
    {
        var source = MarketTestSupport.Source(
            MarketParser.Twse,
            instrument: "TWSE listed securities",
            interval: "1d",
            instruments: new[] { "0050", "2330" });

        var result = MarketParser.Parse(MarketFixtures.Twse, source, Fetched);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(new[] { "0050", "2330" }, result.Items.Select(item => item.Instrument));
        Assert.Equal(135.60, result.Items[0].Value);
        Assert.Equal(1100.0, result.Items[1].Value);
        Assert.All(
            result.Items,
            item => Assert.Equal(new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero), item.ObservationUtc));
    }

    [Fact]
    public void Twse_compact_roc_date_1151020_maps_to_2026_10_20()
    {
        var source = MarketTestSupport.Source(
            MarketParser.Twse,
            interval: "1d",
            instruments: new[] { "0050" });

        var result = MarketParser.Parse(MarketFixtures.Twse, source, Fetched);

        var item = Assert.Single(result.Items);
        Assert.Equal("0050", item.Instrument);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero), item.ObservationUtc);
        Assert.NotEqual(new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero), item.ObservationUtc);
    }

    [Fact]
    public void Twse_allowlist_limits_rows_and_an_empty_allowlist_stores_nothing()
    {
        var onlyOne = MarketTestSupport.Source(
            MarketParser.Twse,
            interval: "1d",
            instruments: new[] { "0050" });
        var none = MarketTestSupport.Source(
            MarketParser.Twse,
            interval: "1d");

        var one = MarketParser.Parse(MarketFixtures.Twse, onlyOne, Fetched);
        var zero = MarketParser.Parse(MarketFixtures.Twse, none, Fetched);

        Assert.Equal("0050", Assert.Single(one.Items).Instrument);
        Assert.Empty(zero.Items);
        Assert.Equal(3, zero.Skipped);
    }

    [Fact]
    public void Moex_reads_the_marketdata_columns_and_exchange_timestamp()
    {
        var source = MarketTestSupport.Source(
            MarketParser.Moex,
            instrument: "IMOEX",
            interval: "quote");

        var result = MarketParser.Parse(MarketFixtures.Moex, source, Fetched);

        var item = Assert.Single(result.Items);
        Assert.Equal("IMOEX", item.Instrument);
        Assert.Equal(3120.45, item.Value);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 15, 39, 59, TimeSpan.Zero), item.ObservationUtc);
        Assert.Equal(Fetched, item.FetchedUtc);
    }

    [Fact]
    public void Empty_bodies_parse_as_zero_observations()
    {
        var result = MarketParser.Parse("  ", MarketTestSupport.Source(MarketParser.Cboe), Fetched);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.Skipped);
    }
}
