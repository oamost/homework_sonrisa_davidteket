using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using WorldAlerts.Api.Advisories;
using WorldAlerts.Api.Disasters;
using WorldAlerts.Api.Markets;

namespace WorldAlerts.Api.Tests;

public sealed class WorldDataEndpointTests : IDisposable
{
    private static readonly DateTimeOffset Fetched =
        new(2026, 10, 21, 12, 30, 0, TimeSpan.Zero);

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Markets_endpoint_returns_seeded_observations_newest_first_and_clamps_the_limit()
    {
        var databasePath = _temp.File("events.sqlite");
        var store = new MarketStore(databasePath);
        store.Initialize();
        await store.InsertAsync(new[]
        {
            new MarketObservation(
                "binance-btcusdt-1h",
                "Binance",
                "Global",
                "BTCUSDT",
                "USDT",
                67250.10,
                new DateTimeOffset(2026, 10, 21, 10, 0, 0, TimeSpan.Zero),
                Fetched,
                false,
                "hourly candle",
                "1h",
                "https://api.binance.com/api/v3/klines?symbol=BTCUSDT"),
            new MarketObservation(
                "frankfurter-eurusd",
                "ECB",
                "Euro area / United States",
                "EUR/USD",
                "USD per EUR",
                1.1642,
                new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero),
                Fetched,
                true,
                "daily reference",
                "1d",
                "https://api.frankfurter.app/latest?from=EUR&to=USD"),
            MarketTestSupport.Observation(
                "older",
                new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero)),
        });

        using var factory = CreateApi(databasePath);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/markets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        Assert.Equal(3, root.GetArrayLength());

        var newest = root[0];
        Assert.Equal("frankfurter-eurusd", newest.GetProperty("sourceId").GetString());
        Assert.Equal("ECB", newest.GetProperty("venue").GetString());
        Assert.Equal("Euro area / United States", newest.GetProperty("region").GetString());
        Assert.Equal("EUR/USD", newest.GetProperty("instrument").GetString());
        Assert.Equal("USD per EUR", newest.GetProperty("quoteUnit").GetString());
        Assert.Equal(1.1642, newest.GetProperty("value").GetDouble());
        Assert.True(newest.GetProperty("delayed").GetBoolean());
        Assert.Equal("daily reference", newest.GetProperty("delaySemantics").GetString());
        Assert.Equal("1d", newest.GetProperty("interval").GetString());
        Assert.Equal("https://api.frankfurter.app/latest?from=EUR&to=USD", newest.GetProperty("provenance").GetString());
        Assert.Equal(
            new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero),
            DateTimeOffset.Parse(newest.GetProperty("observationUtc").GetString()!));

        Assert.Equal("binance-btcusdt-1h", root[1].GetProperty("sourceId").GetString());
        Assert.Equal("older", root[2].GetProperty("instrument").GetString());

        Assert.Equal(1, await ReadArrayLengthAsync(client, "/api/markets?limit=0"));
        Assert.Equal(1, await ReadArrayLengthAsync(client, "/api/markets?limit=1"));
        Assert.Equal(3, await ReadArrayLengthAsync(client, "/api/markets?limit=500"));
        Assert.Equal(
            1,
            await ReadArrayLengthAsync(client, "/api/markets?source=FRANKFURTER-EURUSD"));
        Assert.Equal(
            0,
            await ReadArrayLengthAsync(client, "/api/markets?source=does-not-exist"));

        var health = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task Disasters_endpoint_returns_seeded_events_newest_first_with_explicit_nulls()
    {
        var databasePath = _temp.File("events.sqlite");
        var store = new DisasterStore(databasePath);
        store.Initialize();
        await store.InsertAsync(new[]
        {
            DisasterTestSupport.Event(
                "recent",
                new DateTimeOffset(2026, 10, 21, 10, 0, 0, TimeSpan.Zero),
                sourceId: "usgs",
                title: "Recent earthquake",
                publishedUtc: new DateTimeOffset(2026, 10, 21, 10, 5, 0, TimeSpan.Zero),
                latitude: 35.1,
                longitude: 139.2,
                severity: 4.5,
                severityUnit: "mb",
                tsunami: true,
                significance: 312,
                provenance: "https://earthquake.usgs.gov/event/recent",
                fetchedUtc: Fetched),
            DisasterTestSupport.Event(
                "older",
                new DateTimeOffset(2026, 10, 19, 10, 0, 0, TimeSpan.Zero),
                sourceId: "gdacs",
                title: "Older flood",
                eventType: "flood",
                alertLevel: "Orange",
                alertScore: 1.5,
                fetchedUtc: Fetched),
            DisasterTestSupport.Event(
                "undated",
                null,
                sourceId: "ifrc-go",
                title: "Undated IFRC event",
                eventType: "event",
                publishedUtc: new DateTimeOffset(2026, 10, 18, 8, 0, 0, TimeSpan.Zero),
                country: "Test Country",
                iso3: "TST",
                fetchedUtc: Fetched),
        });

        using var factory = CreateApi(databasePath);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/disasters");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        Assert.Equal(3, root.GetArrayLength());

        var newest = root[0];
        Assert.Equal("recent", newest.GetProperty("nativeId").GetString());
        Assert.Equal("Recent earthquake", newest.GetProperty("title").GetString());
        Assert.Equal("earthquake", newest.GetProperty("eventType").GetString());
        Assert.Equal(4.5, newest.GetProperty("severity").GetDouble());
        Assert.Equal("mb", newest.GetProperty("severityUnit").GetString());
        Assert.True(newest.GetProperty("tsunami").GetBoolean());
        Assert.Equal(312, newest.GetProperty("significance").GetInt64());
        Assert.Equal(35.1, newest.GetProperty("latitude").GetDouble());
        Assert.Equal(139.2, newest.GetProperty("longitude").GetDouble());
        Assert.Equal(JsonValueKind.Null, newest.GetProperty("country").ValueKind);
        Assert.Equal(JsonValueKind.Null, newest.GetProperty("alertLevel").ValueKind);

        Assert.Equal("older", root[1].GetProperty("nativeId").GetString());
        Assert.Equal("Orange", root[1].GetProperty("alertLevel").GetString());
        Assert.Equal(1.5, root[1].GetProperty("alertScore").GetDouble());

        var undated = root[2];
        Assert.Equal("undated", undated.GetProperty("nativeId").GetString());
        Assert.Equal(JsonValueKind.Null, undated.GetProperty("eventTimeUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, undated.GetProperty("latitude").ValueKind);
        Assert.Equal(JsonValueKind.Null, undated.GetProperty("severity").ValueKind);
        Assert.Equal(JsonValueKind.Null, undated.GetProperty("tsunami").ValueKind);
        Assert.Equal("Test Country", undated.GetProperty("country").GetString());

        Assert.Equal(1, await ReadArrayLengthAsync(client, "/api/disasters?limit=0"));
        Assert.Equal(1, await ReadArrayLengthAsync(client, "/api/disasters?limit=1"));
        Assert.Equal(3, await ReadArrayLengthAsync(client, "/api/disasters?limit=500"));
        Assert.Equal(
            0,
            await ReadArrayLengthAsync(client, "/api/disasters?source=does-not-exist"));

        var ifrcResponse = await client.GetAsync("/api/disasters?source=IFRC-GO");
        Assert.Equal(HttpStatusCode.OK, ifrcResponse.StatusCode);
        using var ifrcDocument = JsonDocument.Parse(await ifrcResponse.Content.ReadAsStringAsync());
        var ifrcItem = Assert.Single(ifrcDocument.RootElement.EnumerateArray());
        Assert.Equal("ifrc-go", ifrcItem.GetProperty("sourceId").GetString());
        Assert.Equal(JsonValueKind.Null, ifrcItem.GetProperty("eventTimeUtc").ValueKind);

        var health = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        var news = await client.GetAsync("/api/news");
        Assert.Equal(HttpStatusCode.OK, news.StatusCode);
    }

    [Fact]
    public async Task World_endpoints_keep_separate_tables_and_default_to_fifty_items()
    {
        var databasePath = _temp.File("events.sqlite");
        var marketStore = new MarketStore(databasePath);
        marketStore.Initialize();
        var disasterStore = new DisasterStore(databasePath);
        disasterStore.Initialize();

        await marketStore.InsertAsync(Enumerable.Range(0, 60)
            .Select(index => MarketTestSupport.Observation(
                $"instrument-{index}",
                new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero).AddSeconds(index)))
            .ToArray());
        await disasterStore.InsertAsync(Enumerable.Range(0, 60)
            .Select(index => DisasterTestSupport.Event(
                $"event-{index}",
                new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero).AddSeconds(index),
                fetchedUtc: Fetched))
            .ToArray());

        using var factory = CreateApi(databasePath);
        using var client = factory.CreateClient();

        Assert.Equal(50, await ReadArrayLengthAsync(client, "/api/markets"));
        Assert.Equal(50, await ReadArrayLengthAsync(client, "/api/disasters"));
        Assert.Equal(60, await ReadArrayLengthAsync(client, "/api/markets?limit=200"));
        Assert.Equal(60, await ReadArrayLengthAsync(client, "/api/disasters?limit=200"));
    }

    [Fact]
    public async Task Advisories_endpoint_returns_seeded_rows_newest_first_and_filters_source()
    {
        var databasePath = _temp.File("events.sqlite");
        var store = new AdvisoryStore(databasePath);
        store.Initialize();
        await store.InsertAsync(new[]
        {
            new Advisory(
                "cisa-kev",
                "CVE-2026-0001",
                "Acme",
                "Widget",
                "Acme Widget RCE",
                new DateOnly(2026, 10, 1),
                new DateOnly(2026, 10, 22),
                true,
                "https://www.cisa.gov/kev",
                Fetched),
            new Advisory(
                "cisa-kev",
                "CVE-2026-0002",
                "Example",
                "Gateway",
                "Example Gateway bypass",
                new DateOnly(2026, 10, 5),
                new DateOnly(2026, 10, 26),
                false,
                "https://www.cisa.gov/kev",
                Fetched),
        });

        using var factory = CreateApi(databasePath);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/advisories");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        Assert.Equal(2, root.GetArrayLength());
        Assert.Equal("CVE-2026-0002", root[0].GetProperty("cveId").GetString());
        Assert.Equal("Example", root[0].GetProperty("vendor").GetString());
        Assert.Equal("Gateway", root[0].GetProperty("product").GetString());
        Assert.Equal("2026-10-05", root[0].GetProperty("dateAdded").GetString());
        Assert.False(root[0].GetProperty("knownRansomware").GetBoolean());

        Assert.Equal(1, await ReadArrayLengthAsync(client, "/api/advisories?limit=0"));
        Assert.Equal(2, await ReadArrayLengthAsync(client, "/api/advisories?limit=500"));
        Assert.Equal(2, await ReadArrayLengthAsync(client, "/api/advisories?source=CISA-KEV"));
        Assert.Equal(0, await ReadArrayLengthAsync(client, "/api/advisories?source=does-not-exist"));
    }

    private static async Task<int> ReadArrayLengthAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        return document.RootElement.GetArrayLength();
    }

    private static WebApplicationFactory<Program> CreateApi(string databasePath) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(
            builder => builder
                .UseSetting("WorldAlerts:DatabasePath", databasePath)
                .UseSetting("WorldAlerts:News:PollingEnabled", "false")
                .UseSetting("WorldAlerts:Markets:PollingEnabled", "false")
                .UseSetting("WorldAlerts:Disasters:PollingEnabled", "false")
                .UseSetting("WorldAlerts:Advisories:PollingEnabled", "false"));
}
