using WorldAlerts.Api.Markets;

namespace WorldAlerts.Api.Tests;

public sealed class MarketStoreTests : IDisposable
{
    private static readonly DateTimeOffset Fetched =
        new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Insert_persists_all_observation_fields_and_survives_reopening()
    {
        var path = _temp.File("events.sqlite");
        var store = new MarketStore(path);
        store.Initialize();
        var observation = new MarketObservation(
            SourceId: "binance-btcusdt-1h",
            Venue: "Binance",
            Region: "Global",
            Instrument: "BTCUSDT",
            QuoteUnit: "USDT",
            Value: 67250.10,
            ObservationUtc: new DateTimeOffset(2026, 10, 20, 18, 0, 0, TimeSpan.Zero),
            FetchedUtc: Fetched,
            Delayed: true,
            DelaySemantics: "delayed test marker",
            Interval: "1h",
            Provenance: "https://market.test/klines");

        Assert.Equal(1, await store.InsertAsync(new[] { observation }));

        var reopened = new MarketStore(path);
        reopened.Initialize();
        var stored = Assert.Single(await reopened.GetRecentAsync(MarketStore.DefaultLimit));

        Assert.Equal(observation.SourceId, stored.SourceId);
        Assert.Equal(observation.Venue, stored.Venue);
        Assert.Equal(observation.Region, stored.Region);
        Assert.Equal(observation.Instrument, stored.Instrument);
        Assert.Equal(observation.QuoteUnit, stored.QuoteUnit);
        Assert.Equal(observation.Value, stored.Value);
        Assert.Equal(observation.ObservationUtc, stored.ObservationUtc);
        Assert.Equal(observation.FetchedUtc, stored.FetchedUtc);
        Assert.True(stored.Delayed);
        Assert.Equal(observation.DelaySemantics, stored.DelaySemantics);
        Assert.Equal(observation.Interval, stored.Interval);
        Assert.Equal(observation.Provenance, stored.Provenance);
    }

    [Fact]
    public async Task Reinserting_the_same_key_inserts_zero_rows_across_intervals()
    {
        var store = CreateStore();
        var first = MarketTestSupport.Observation(
            "BTCUSDT",
            new DateTimeOffset(2026, 10, 20, 18, 0, 0, TimeSpan.Zero),
            interval: "1h");
        var second = MarketTestSupport.Observation(
            "BTCUSDT",
            new DateTimeOffset(2026, 10, 20, 18, 0, 0, TimeSpan.Zero),
            interval: "4h");

        Assert.Equal(2, await store.InsertAsync(new[] { first, second }));
        Assert.Equal(0, await store.InsertAsync(new[] { first, second }));

        Assert.Equal(2, (await store.GetRecentAsync(MarketStore.MaxLimit)).Count);
    }

    [Fact]
    public async Task Observations_are_newest_first_and_the_limit_is_clamped()
    {
        var store = CreateStore();
        var items = Enumerable.Range(0, 205)
            .Select(index => MarketTestSupport.Observation(
                $"instrument-{index}",
                new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero).AddSeconds(index)))
            .ToArray();
        await store.InsertAsync(items);

        var recent = await store.GetRecentAsync(MarketStore.DefaultLimit);
        Assert.Equal(MarketStore.DefaultLimit, recent.Count);
        Assert.Equal("instrument-204", recent[0].Instrument);
        Assert.Equal("instrument-155", recent[^1].Instrument);

        Assert.Single(await store.GetRecentAsync(0));
        Assert.Single(await store.GetRecentAsync(-10));
        Assert.Equal(MarketStore.MaxLimit, (await store.GetRecentAsync(500)).Count);
    }

    private MarketStore CreateStore()
    {
        var store = new MarketStore(_temp.File("events.sqlite"));
        store.Initialize();
        return store;
    }
}
