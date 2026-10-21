using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WorldAlerts.Api.News;
using WorldAlerts.Api.Disasters;
using WorldAlerts.Api.Markets;

namespace WorldAlerts.Api.Tests;

public sealed class ImportantNewsEndpointTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Important_eligible_stories_are_public_without_subscriptions_and_preserve_provider_fields()
    {
        var path = _temp.File("important.sqlite");
        var store = new NewsStore(path);
        store.Initialize();
        await store.InsertAsync(new[]
        {
            Story("important", "Breaking conflict", Now.AddHours(-2), "Provider summary"),
            Story("routine", "Arts report", Now.AddHours(-1)),
            Story("stale", "Breaking conflict", Now.AddHours(-25)),
            Story("future", "Breaking conflict", Now.AddMinutes(1)),
            Story("missing", "Breaking conflict", null),
        });

        using var factory = Api(path);
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/news/important");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var item = Assert.Single(root.GetProperty("items").EnumerateArray());
        Assert.Equal("Breaking conflict", item.GetProperty("title").GetString());
        Assert.Equal("Provider summary", item.GetProperty("summary").GetString());
        Assert.Equal("BBC World", item.GetProperty("sourceName").GetString());
        Assert.Equal("bbc-world", item.GetProperty("sourceId").GetString());
        Assert.Equal("https://example.org/important", item.GetProperty("link").GetString());
        Assert.Equal("https://example.org/feed", item.GetProperty("provenanceUrl").GetString());
        Assert.Equal(Now.AddHours(-2), item.GetProperty("publishedUtc").GetDateTimeOffset());
        Assert.True(item.GetProperty("reasons").EnumerateArray().Any());
        Assert.Equal(0.75, item.GetProperty("importanceScore").GetDouble(), 6);
        Assert.Equal("News", item.GetProperty("family").GetString());
        Assert.Contains(item.GetProperty("eventTypes").EnumerateArray(), type => type.GetString() == "world-news");
        Assert.Equal("news.tier-1-urgency-keyword", item.GetProperty("importanceRuleId").GetString());
        Assert.Equal(0, root.GetProperty("omittedCount").GetInt32());
        Assert.Equal(Now, root.GetProperty("evaluatedUtc").GetDateTimeOffset());
    }

    [Fact]
    public async Task All_persisted_rows_are_evaluated_before_display_limit()
    {
        var path = _temp.File("volume.sqlite");
        var store = new NewsStore(path);
        store.Initialize();
        await store.InsertAsync(Enumerable.Range(0, 210)
            .Select(i => Story($"routine-{i}", "Arts report", Now.AddMinutes(-i)))
            .Append(Story("important", "Breaking conflict", Now.AddHours(-23), null)).ToArray());

        using var factory = Api(path);
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/api/news/important"));
        var item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("important", item.GetProperty("sourceItemId").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("summary").ValueKind);
    }

    [Fact]
    public async Task Response_reports_important_stories_omitted_after_display_limit()
    {
        var path = _temp.File("many-important.sqlite");
        var store = new NewsStore(path);
        store.Initialize();
        await store.InsertAsync(Enumerable.Range(0, 105)
            .Select(i => Story($"important-{i}", "Breaking conflict", Now.AddMinutes(-i))).ToArray());

        using var factory = Api(path);
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/api/news/important"));
        Assert.Equal(100, document.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(5, document.RootElement.GetProperty("omittedCount").GetInt32());
    }

    [Fact]
    public async Task Public_preview_shares_selection_pipeline_for_news_disasters_markets_and_reports_missing_baselines()
    {
        var path = _temp.File("mixed.sqlite");
        var news = new NewsStore(path); news.Initialize();
        await news.InsertAsync([Story("news", "Breaking conflict", Now.AddHours(-1))]);
        var disaster = new DisasterStore(path); disaster.Initialize();
        await disaster.InsertAsync([new DisasterEvent("gdacs", "quake", "earthquake", "Major quake",
            Now.AddHours(-2), Now.AddHours(-1), null, null, null, null, null, 6.5,
            "magnitude", "Green", null, false, null, "https://example.org/quake", Now)]);
        var markets = new MarketStore(path); markets.Initialize();
        await markets.InsertAsync([
            new MarketObservation("binance-btcusdt-ticker", "Binance", "Global", "BTCUSDT", "USDT", 100,
                Now.AddHours(-25), Now, false, "live", "24h", "https://example.org/btc"),
            new MarketObservation("binance-btcusdt-ticker", "Binance", "Global", "BTCUSDT", "USDT", 106,
                Now.AddHours(-1), Now, false, "live", "24h", "https://example.org/btc"),
            new MarketObservation("binance-btcusdt-ticker", "Binance", "Global", "ETHUSDT", "USDT", 120,
                Now.AddHours(-1), Now, false, "live", "24h", "https://example.org/eth"),
        ]);
        using var factory = Api(path);
        using var client = factory.CreateClient();
        using var response = JsonDocument.Parse(await client.GetStringAsync("/api/news/important"));
        var items = response.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(3, items.Length);
        Assert.Contains(items, item => item.GetProperty("family").GetString() == "News");
        Assert.Contains(items, item => item.GetProperty("family").GetString() == "Disaster"
            && item.GetProperty("eventTypes").EnumerateArray().Any(type => type.GetString() == "disaster.earthquake"));
        Assert.Contains(items, item => item.GetProperty("family").GetString() == "Market"
            && item.GetProperty("importanceScore").GetDouble() is > 0.6 and < 1);
        Assert.Equal(1, response.RootElement.GetProperty("missingMarketBaselineCount").GetInt32());
        var diagnostics = response.RootElement.GetProperty("familyDiagnostics").EnumerateArray().ToArray();
        Assert.Contains(diagnostics, family => family.GetProperty("family").GetString() == "News"
            && family.GetProperty("evaluatedCount").GetInt32() == 1
            && family.GetProperty("importantEligibleCount").GetInt32() == 1);
        Assert.Contains(diagnostics, family => family.GetProperty("family").GetString() == "Disaster"
            && family.GetProperty("importantEligibleCount").GetInt32() == 1);
        Assert.Contains(diagnostics, family => family.GetProperty("family").GetString() == "Market"
            && family.GetProperty("evaluatedCount").GetInt32() == 3
            && family.GetProperty("eligibleCount").GetInt32() == 2
            && family.GetProperty("importantEligibleCount").GetInt32() == 1
            && family.GetProperty("missingBaselineCount").GetInt32() == 1);
        using var selection = JsonDocument.Parse(await client.GetStringAsync("/api/alerts/selection"));
        Assert.All(items, item => Assert.Contains(selection.RootElement.GetProperty("events").EnumerateArray(), candidate =>
            candidate.GetProperty("title").GetString() == item.GetProperty("title").GetString()
            && candidate.GetProperty("importance").GetProperty("ruleId").GetString() == item.GetProperty("importanceRuleId").GetString()));
    }

    private static NewsItem Story(string id, string title, DateTimeOffset? published, string? summary = null) =>
        new("bbc-world", "BBC World", id, title, $"https://example.org/{id}", published,
            Now, "world-news", Array.Empty<string>(), "https://example.org/feed", summary);

    private static WebApplicationFactory<Program> Api(string path) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting("WorldAlerts:DatabasePath", path)
            .UseSetting("WorldAlerts:News:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Markets:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Disasters:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Advisories:PollingEnabled", "false")
            .ConfigureTestServices(services => services.AddSingleton<TimeProvider>(new FixedClock())));

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
