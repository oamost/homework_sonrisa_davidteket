using WorldAlerts.Api.Markets;

namespace WorldAlerts.Api.Tests;

public sealed class MarketIngestionTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Two_polls_store_the_same_candles_once()
    {
        var store = CreateStore();
        var source = MarketTestSupport.Source(
            MarketParser.BinanceKlines,
            id: "klines",
            url: "https://market.test/klines",
            instrument: "BTCUSDT",
            interval: "1h");
        var options = EnabledOptions(source);
        var handler = new FakeHttpHandler(_ => MarketTestSupport.Json(MarketFixtures.BinanceKlines));
        var ingestion = MarketTestSupport.Ingest(store, options, handler);

        var first = await ingestion.PollOnceAsync();
        var second = await ingestion.PollOnceAsync();

        var firstSource = Assert.Single(first.Sources);
        Assert.Equal(2, firstSource.Parsed);
        Assert.Equal(2, firstSource.Inserted);
        Assert.Equal(0, firstSource.Duplicates);
        Assert.Null(firstSource.ErrorType);

        var secondSource = Assert.Single(second.Sources);
        Assert.Equal(2, secondSource.Parsed);
        Assert.Equal(0, secondSource.Inserted);
        Assert.Equal(2, secondSource.Duplicates);
        Assert.Null(secondSource.ErrorType);

        Assert.Equal(2, (await store.GetRecentAsync(MarketStore.MaxLimit)).Count);
        Assert.Equal(2, handler.CallCount);
        Assert.Contains("WorldAlertsMarketBot", handler.LastUserAgent);
    }

    [Fact]
    public async Task A_failing_market_source_does_not_block_the_others()
    {
        var store = CreateStore();
        var good = MarketTestSupport.Source(
            MarketParser.BinanceTicker,
            id: "good",
            url: "https://market.test/good",
            instrument: "BTCUSDT",
            interval: "24h");
        var httpFailure = MarketTestSupport.Source(
            MarketParser.Cboe,
            id: "http-failure",
            url: "https://market.test/http-failure");
        var malformed = MarketTestSupport.Source(
            MarketParser.Moex,
            id: "malformed",
            url: "https://market.test/malformed");
        var options = EnabledOptions(good, httpFailure, malformed);
        var handler = new FakeHttpHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/http-failure" => FakeHttpHandler.Failure(),
            "/malformed" => MarketTestSupport.Json("{ not json"),
            _ => MarketTestSupport.Json(MarketFixtures.BinanceTicker),
        });
        var ingestion = MarketTestSupport.Ingest(store, options, handler);

        var poll = await ingestion.PollOnceAsync();

        Assert.Equal(3, poll.Sources.Count);
        var goodResult = poll.Sources.Single(result => result.SourceId == "good");
        var httpResult = poll.Sources.Single(result => result.SourceId == "http-failure");
        var malformedResult = poll.Sources.Single(result => result.SourceId == "malformed");

        Assert.Null(goodResult.ErrorType);
        Assert.Equal(1, goodResult.Inserted);
        Assert.Equal(nameof(HttpRequestException), httpResult.ErrorType);
        Assert.Equal(0, httpResult.Inserted);
        Assert.NotNull(malformedResult.ErrorType);
        Assert.Equal(0, malformedResult.Inserted);

        Assert.Single(await store.GetRecentAsync(MarketStore.MaxLimit));
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task An_empty_market_body_is_zero_observations_without_a_crash()
    {
        var store = CreateStore();
        var source = MarketTestSupport.Source(
            MarketParser.Cboe,
            id: "empty",
            url: "https://market.test/empty");
        var handler = new FakeHttpHandler(_ => MarketTestSupport.EmptyOk());
        var ingestion = MarketTestSupport.Ingest(store, EnabledOptions(source), handler);

        var poll = await ingestion.PollOnceAsync();

        var result = Assert.Single(poll.Sources);
        Assert.True(result.EmptyBody);
        Assert.Equal(0, result.Parsed);
        Assert.Equal(0, result.Inserted);
        Assert.Null(result.ErrorType);
        Assert.Empty(await store.GetRecentAsync(MarketStore.MaxLimit));
    }

    [Fact]
    public async Task Disabled_market_polling_makes_zero_http_calls()
    {
        var store = CreateStore();
        var options = new MarketOptions
        {
            PollingEnabled = false,
            Sources = new List<MarketSource>
            {
                MarketTestSupport.Source(MarketParser.BinanceTicker),
            },
        };
        var handler = new FakeHttpHandler(_ => MarketTestSupport.Json(MarketFixtures.BinanceTicker));
        var ingestion = MarketTestSupport.Ingest(store, options, handler);

        var poll = await ingestion.PollOnceAsync();

        Assert.Empty(poll.Sources);
        Assert.Equal(0, handler.CallCount);
        Assert.Empty(await store.GetRecentAsync(MarketStore.MaxLimit));
    }

    private MarketStore CreateStore()
    {
        var store = new MarketStore(_temp.File("events.sqlite"));
        store.Initialize();
        return store;
    }

    private static MarketOptions EnabledOptions(params MarketSource[] sources) => new()
    {
        PollingEnabled = true,
        PollMinutes = 10,
        RequestTimeoutSeconds = 5,
        Sources = sources.ToList(),
    };
}
