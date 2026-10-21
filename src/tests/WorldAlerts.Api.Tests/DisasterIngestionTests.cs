using WorldAlerts.Api.Disasters;

namespace WorldAlerts.Api.Tests;

public sealed class DisasterIngestionTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Two_polls_store_the_same_events_once()
    {
        var store = CreateStore();
        var source = DisasterTestSupport.Source(
            DisasterParser.Usgs,
            id: "usgs",
            url: "https://disaster.test/usgs");
        var handler = new FakeHttpHandler(_ => DisasterTestSupport.Json(DisasterFixtures.Usgs));
        var ingestion = DisasterTestSupport.Ingest(store, EnabledOptions(source), handler);

        var first = await ingestion.PollOnceAsync();
        var second = await ingestion.PollOnceAsync();

        var firstSource = Assert.Single(first.Sources);
        Assert.Equal(1, firstSource.Parsed);
        Assert.Equal(1, firstSource.Inserted);
        Assert.Equal(0, firstSource.Duplicates);
        Assert.Null(firstSource.ErrorType);

        var secondSource = Assert.Single(second.Sources);
        Assert.Equal(1, secondSource.Parsed);
        Assert.Equal(0, secondSource.Inserted);
        Assert.Equal(1, secondSource.Duplicates);
        Assert.Null(secondSource.ErrorType);

        Assert.Single(await store.GetRecentAsync(DisasterStore.MaxLimit));
        Assert.Equal(2, handler.CallCount);
        Assert.Contains("WorldAlertsDisasterBot", handler.LastUserAgent);
    }

    [Fact]
    public async Task A_failing_disaster_source_does_not_block_the_others()
    {
        var store = CreateStore();
        var good = DisasterTestSupport.Source(
            DisasterParser.GdacsRss,
            id: "good",
            url: "https://disaster.test/good");
        var httpFailure = DisasterTestSupport.Source(
            DisasterParser.Eonet,
            id: "http-failure",
            url: "https://disaster.test/http-failure");
        var malformed = DisasterTestSupport.Source(
            DisasterParser.Usgs,
            id: "malformed",
            url: "https://disaster.test/malformed");
        var handler = new FakeHttpHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/http-failure" => FakeHttpHandler.Failure(),
            "/malformed" => DisasterTestSupport.Json("{ not json"),
            _ => FakeHttpHandler.Xml(DisasterFixtures.GdacsRss),
        });
        var ingestion = DisasterTestSupport.Ingest(store, EnabledOptions(good, httpFailure, malformed), handler);

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

        Assert.Single(await store.GetRecentAsync(DisasterStore.MaxLimit));
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task Empty_ifrc_body_is_warned_and_yields_no_events()
    {
        var store = CreateStore();
        var ifrc = DisasterTestSupport.Source(
            DisasterParser.Ifrc,
            id: "ifrc",
            url: "https://disaster.test/ifrc");
        var handler = new FakeHttpHandler(_ => DisasterTestSupport.EmptyOk());
        var ingestion = DisasterTestSupport.Ingest(store, EnabledOptions(ifrc), handler);

        var poll = await ingestion.PollOnceAsync();

        var result = Assert.Single(poll.Sources);
        Assert.True(result.EmptyBody);
        Assert.Equal(0, result.Parsed);
        Assert.Equal(0, result.Inserted);
        Assert.Null(result.ErrorType);
        Assert.Empty(await store.GetRecentAsync(DisasterStore.MaxLimit));
    }

    [Fact]
    public async Task Disabled_disaster_polling_makes_zero_http_calls()
    {
        var store = CreateStore();
        var options = new DisasterOptions
        {
            PollingEnabled = false,
            Sources = new List<DisasterSource>
            {
                DisasterTestSupport.Source(DisasterParser.Usgs),
            },
        };
        var handler = new FakeHttpHandler(_ => DisasterTestSupport.Json(DisasterFixtures.Usgs));
        var ingestion = DisasterTestSupport.Ingest(store, options, handler);

        var poll = await ingestion.PollOnceAsync();

        Assert.Empty(poll.Sources);
        Assert.Equal(0, handler.CallCount);
        Assert.Empty(await store.GetRecentAsync(DisasterStore.MaxLimit));
    }

    private DisasterStore CreateStore()
    {
        var store = new DisasterStore(_temp.File("events.sqlite"));
        store.Initialize();
        return store;
    }

    private static DisasterOptions EnabledOptions(params DisasterSource[] sources) => new()
    {
        PollingEnabled = true,
        PollMinutes = 10,
        RequestTimeoutSeconds = 5,
        Sources = sources.ToList(),
    };
}
