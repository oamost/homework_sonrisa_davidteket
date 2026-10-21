using Microsoft.Data.Sqlite;
using WorldAlerts.Api.Markets;

namespace WorldAlerts.Api.Tests;

public sealed class MarketProviderBaselineTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private static readonly DateTimeOffset Fetched = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Ticker_uses_open_price_and_open_time_from_same_24h_payload_not_previous_utc_close()
    {
        var parsed = MarketParser.Parse(MarketFixtures.BinanceTicker,
            MarketTestSupport.Source(MarketParser.BinanceTicker, interval: "24h"), Fetched);
        var item = Assert.Single(parsed.Items);
        Assert.Equal(67250.10, item.Value);
        Assert.Equal(67100.10, item.BaselineValue);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1761040800000), item.BaselineUtc);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1761127199999), item.ObservationUtc);
        Assert.Equal("rolling-24h-open", item.ComparisonKind);
        Assert.NotEqual("previous-utc-day-close", item.ComparisonKind);
        Assert.Null(parsed.BaselineDiagnostic);
    }

    [Theory]
    [InlineData("{}", "missing")]
    [InlineData("{\"openPrice\":\"0\",\"openTime\":1761040800000,\"closeTime\":1761127199999}", "invalid")]
    [InlineData("{\"openPrice\":\"NaN\",\"openTime\":1761040800000,\"closeTime\":1761127199999}", "invalid")]
    [InlineData("{\"openPrice\":\"67100\",\"openTime\":1761040800000,\"closeTime\":1761040800000}", "window")]
    [InlineData("{\"openPrice\":\"67100\",\"openTime\":1761040800000,\"closeTime\":1761130800000}", "window")]
    public void Missing_invalid_or_mismatched_baseline_is_not_usable(string fields, string reason)
    {
        var extra = fields == "{}" ? "" : fields[1..^1] + ",";
        var parsed = MarketParser.Parse("{" + extra + "\"lastPrice\":\"67250\"}",
            MarketTestSupport.Source(MarketParser.BinanceTicker, interval: "24h"), Fetched);
        var item = Assert.Single(parsed.Items);
        Assert.Null(item.BaselineValue);
        Assert.Null(item.BaselineUtc);
        Assert.Null(item.ComparisonKind);
        Assert.Contains(reason, parsed.BaselineDiagnostic!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Migration_keeps_old_rows_null_and_repoll_backfills_only_same_identity()
    {
        var path = _temp.File("markets.sqlite");
        var legacy = new MarketStore(path);
        legacy.Initialize();
        var observation = Assert.Single(MarketParser.Parse(MarketFixtures.BinanceTicker,
            MarketTestSupport.Source(MarketParser.BinanceTicker, interval: "24h"), Fetched).Items);
        var withoutBaseline = observation with { BaselineValue = null, BaselineUtc = null, ComparisonKind = null };
        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE MarketObservations; CREATE TABLE MarketObservations (Id INTEGER PRIMARY KEY AUTOINCREMENT, SourceId TEXT NOT NULL, Venue TEXT NOT NULL, Region TEXT NOT NULL, Instrument TEXT NOT NULL, QuoteUnit TEXT NOT NULL, Value REAL NOT NULL, ObservationUtc TEXT NOT NULL, FetchedUtc TEXT NOT NULL, Delayed INTEGER NOT NULL, DelaySemantics TEXT NOT NULL, Interval TEXT NOT NULL, Provenance TEXT NOT NULL, CONSTRAINT UQ_MarketObservations_Source_Instrument_Interval_Observation UNIQUE (SourceId, Instrument, Interval, ObservationUtc));";
            await command.ExecuteNonQueryAsync();
        }
        // Simulate a previously persisted row using the exact legacy columns.
        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO MarketObservations (SourceId,Venue,Region,Instrument,QuoteUnit,Value,ObservationUtc,FetchedUtc,Delayed,DelaySemantics,Interval,Provenance) VALUES ($source,$venue,$region,$instrument,$quote,$value,$observed,$fetched,0,$delay,$interval,$provenance)";
            command.Parameters.AddWithValue("$source", withoutBaseline.SourceId);
            command.Parameters.AddWithValue("$venue", withoutBaseline.Venue);
            command.Parameters.AddWithValue("$region", withoutBaseline.Region);
            command.Parameters.AddWithValue("$instrument", withoutBaseline.Instrument);
            command.Parameters.AddWithValue("$quote", withoutBaseline.QuoteUnit);
            command.Parameters.AddWithValue("$value", withoutBaseline.Value);
            command.Parameters.AddWithValue("$observed", withoutBaseline.ObservationUtc.UtcDateTime.ToString("O"));
            command.Parameters.AddWithValue("$fetched", withoutBaseline.FetchedUtc.UtcDateTime.ToString("O"));
            command.Parameters.AddWithValue("$delay", withoutBaseline.DelaySemantics);
            command.Parameters.AddWithValue("$interval", withoutBaseline.Interval);
            command.Parameters.AddWithValue("$provenance", withoutBaseline.Provenance);
            await command.ExecuteNonQueryAsync();
        }

        var store = new MarketStore(path);
        store.Initialize();
        store.Initialize();
        var before = Assert.Single(await store.GetForSelectionAsync());
        Assert.Null(before.BaselineValue);
        Assert.Equal(0, await store.InsertAsync(new[] { withoutBaseline }));
        Assert.Equal(0, await store.InsertAsync(new[] { observation }));
        var after = Assert.Single(await store.GetRecentAsync(50));
        Assert.Equal(observation.BaselineValue, after.BaselineValue);
        Assert.Equal(observation.BaselineUtc, after.BaselineUtc);
        Assert.Equal(observation.ComparisonKind, after.ComparisonKind);
        Assert.Equal(before.Value, after.Value);
        Assert.Equal(before.FetchedUtc, after.FetchedUtc);
    }
}
