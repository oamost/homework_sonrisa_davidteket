using Microsoft.Extensions.Logging.Abstractions;
using WorldAlerts.Api.Advisories;
using WorldAlerts.Api.Disasters;

namespace WorldAlerts.Api.Tests;

public sealed class SourceCoverageIngestionTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Eonet_queries_each_category_merges_duplicates_and_tolerates_empty_arrays()
    {
        var store = CreateDisasterStore();
        var source = DisasterTestSupport.Source(
            DisasterParser.Eonet,
            id: "eonet",
            url: "https://disaster.test/eonet?status=open&limit=50",
            categories: new[] { "volcanoes", "landslides", "drought" });
        var handler = new FakeHttpHandler(request =>
        {
            var query = request.RequestUri!.Query;
            if (query.Contains("category=volcanoes", StringComparison.Ordinal))
            {
                return DisasterTestSupport.Json(SourceCoverageFixtures.EonetVolcanoes);
            }

            return query.Contains("category=landslides", StringComparison.Ordinal)
                ? DisasterTestSupport.Json(SourceCoverageFixtures.EonetDuplicateAndLandslide)
                : DisasterTestSupport.Json(SourceCoverageFixtures.EonetEmpty);
        });
        var ingestion = DisasterTestSupport.Ingest(store, EnabledOptions(source), handler);

        var poll = await ingestion.PollOnceAsync();

        var result = Assert.Single(poll.Sources);
        Assert.Equal(3, handler.CallCount);
        Assert.Equal(3, result.Parsed);
        Assert.Equal(3, result.Inserted);
        Assert.Equal(0, result.Duplicates);
        Assert.Null(result.ErrorType);
        Assert.Contains(handler.RequestedUris, uri => uri.Contains("category=volcanoes", StringComparison.Ordinal));
        Assert.Contains(handler.RequestedUris, uri => uri.Contains("category=landslides", StringComparison.Ordinal));
        Assert.Contains(handler.RequestedUris, uri => uri.Contains("category=drought", StringComparison.Ordinal));
        Assert.Equal(3, (await store.GetRecentAsync(DisasterStore.MaxLimit)).Count);
    }

    [Fact]
    public async Task Nws_empty_feature_collection_is_a_successful_poll_with_zero_events()
    {
        var store = CreateDisasterStore();
        var source = DisasterTestSupport.Source(
            DisasterParser.Nws,
            id: "nws-alerts",
            url: "https://disaster.test/nws",
            events: new[] { "Heat Advisory" });
        var handler = new FakeHttpHandler(_ => DisasterTestSupport.Json(SourceCoverageFixtures.NwsEmpty));
        var ingestion = DisasterTestSupport.Ingest(store, EnabledOptions(source), handler);

        var poll = await ingestion.PollOnceAsync();

        var result = Assert.Single(poll.Sources);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(0, result.Parsed);
        Assert.Equal(0, result.Inserted);
        Assert.False(result.EmptyBody);
        Assert.Null(result.ErrorType);
        Assert.Empty(await store.GetRecentAsync(DisasterStore.MaxLimit));
    }

    [Fact]
    public async Task Dwd_jsonp_poll_filters_deduplicates_and_persists_heat_and_cold_warnings()
    {
        var store = CreateDisasterStore();
        var source = DisasterTestSupport.Source(
            DisasterParser.DwdWarnings,
            id: "dwd-warnings",
            url: "https://disaster.test/dwd",
            events: new[] { "HITZE", "FROST" });
        var handler = new FakeHttpHandler(_ => DisasterTestSupport.Json(SourceCoverageFixtures.DwdWarnings));
        var ingestion = DisasterTestSupport.Ingest(store, EnabledOptions(source), handler);

        var poll = await ingestion.PollOnceAsync();

        var result = Assert.Single(poll.Sources);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(3, result.Parsed);
        Assert.Equal(3, result.Inserted);
        Assert.Equal(0, result.Duplicates);
        Assert.Null(result.ErrorType);
        Assert.Equal(3, (await store.GetRecentAsync(DisasterStore.MaxLimit)).Count);
    }

    [Fact]
    public async Task Copernicus_ems_pages_with_limit_and_offset_and_stops_on_a_short_page()
    {
        var store = CreateDisasterStore();
        var source = DisasterTestSupport.Source(
            DisasterParser.CopernicusEms,
            id: "copernicus-ems",
            url: "https://disaster.test/ems",
            pageSize: 2,
            maxPages: 3,
            maxItems: 10);
        var handler = new FakeHttpHandler(request =>
        {
            var query = request.RequestUri!.Query;
            return query.Contains("offset=0", StringComparison.Ordinal)
                ? DisasterTestSupport.Json(SourceCoverageFixtures.Ems)
                : DisasterTestSupport.Json(SourceCoverageFixtures.EmsPageTwo);
        });
        var ingestion = DisasterTestSupport.Ingest(store, EnabledOptions(source), handler);

        var poll = await ingestion.PollOnceAsync();

        var result = Assert.Single(poll.Sources);
        Assert.Equal(2, handler.CallCount);
        Assert.Equal(3, result.Parsed);
        Assert.Equal(3, result.Inserted);
        Assert.Null(result.ErrorType);
        Assert.Contains(handler.RequestedUris, uri => uri.Contains("limit=2", StringComparison.Ordinal));
        Assert.Contains(handler.RequestedUris, uri => uri.Contains("offset=0", StringComparison.Ordinal));
        Assert.Contains(handler.RequestedUris, uri => uri.Contains("offset=2", StringComparison.Ordinal));
        Assert.Equal(3, (await store.GetRecentAsync(DisasterStore.MaxLimit)).Count);
    }

    [Fact]
    public async Task Cisa_kev_reingest_is_idempotent()
    {
        var store = new AdvisoryStore(_temp.File("advisories.sqlite"));
        store.Initialize();
        var source = new AdvisorySource
        {
            Id = "cisa-kev",
            Name = "CISA KEV",
            Url = "https://www.cisa.gov/known-exploited-vulnerabilities.json",
        };
        var options = new AdvisoryOptions
        {
            PollingEnabled = true,
            PollMinutes = 60,
            RequestTimeoutSeconds = 5,
            Sources = new List<AdvisorySource> { source },
        };
        var handler = new FakeHttpHandler(_ => DisasterTestSupport.Json(SourceCoverageFixtures.CisaKev));
        var ingestion = new AdvisoryIngestionService(
            new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) },
            store,
            options,
            NullLogger<AdvisoryIngestionService>.Instance);

        var first = await ingestion.PollOnceAsync();
        var second = await ingestion.PollOnceAsync();

        var firstResult = Assert.Single(first.Sources);
        Assert.Equal(2, firstResult.Parsed);
        Assert.Equal(2, firstResult.Inserted);
        Assert.Equal(0, firstResult.Duplicates);
        Assert.Null(firstResult.ErrorType);

        var secondResult = Assert.Single(second.Sources);
        Assert.Equal(2, secondResult.Parsed);
        Assert.Equal(0, secondResult.Inserted);
        Assert.Equal(2, secondResult.Duplicates);
        Assert.Null(secondResult.ErrorType);

        Assert.Equal(2, (await store.GetRecentAsync(AdvisoryStore.MaxLimit)).Count);
        Assert.Equal(2, handler.CallCount);
    }

    private DisasterStore CreateDisasterStore()
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
