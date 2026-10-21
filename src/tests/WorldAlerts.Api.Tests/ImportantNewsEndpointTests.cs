using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WorldAlerts.Api.News;
using WorldAlerts.Api.Disasters;
using WorldAlerts.Api.Markets;
using WorldAlerts.Api.Subscriptions;

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
            .Select(i => Story($"important-{i}", $"Breaking conflict report {i}", Now.AddMinutes(-i))).ToArray());

        using var factory = Api(path);
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/api/news/important"));
        Assert.Equal(100, document.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(5, document.RootElement.GetProperty("omittedCount").GetInt32());
    }

    [Fact]
    public async Task Repeated_important_headlines_count_as_one_display_story_not_omitted_items()
    {
        var path = _temp.File("duplicate-headlines.sqlite");
        var store = new NewsStore(path);
        store.Initialize();
        await store.InsertAsync(new[]
        {
            Story("same-title-1", "Breaking conflict", Now.AddMinutes(-1)),
            Story("same-title-2", "Breaking conflict", Now.AddMinutes(-2)),
        });

        using var factory = Api(path);
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/api/news/important"));
        var item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("Breaking conflict", item.GetProperty("title").GetString());
        Assert.Equal(0, document.RootElement.GetProperty("omittedCount").GetInt32());
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

    [Fact]
    public async Task Provider_rolling_24h_baseline_scales_a_real_move_despite_timestamp_jitter()
    {
        var path = _temp.File("provider-baseline.sqlite");
        var markets = new MarketStore(path); markets.Initialize();
        // No stored history row exists for this instrument, so only the provider's own
        // same-payload open price can supply the 24-hour comparison.
        await markets.InsertAsync(new[]
        {
            new MarketObservation("binance-btcusdt-ticker", "Binance", "Global", "BTCUSDT", "USDT", 106,
                Now.AddHours(-1), Now, false, "live", "24h", "https://example.org/btc",
                BaselineValue: 100, BaselineUtc: Now.AddHours(-25).AddSeconds(37),
                ComparisonKind: "rolling-24h-open"),
        });

        using var factory = Api(path);
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/api/news/important"));
        var item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("Market", item.GetProperty("family").GetString());
        Assert.Contains(item.GetProperty("eventTypes").EnumerateArray(), type => type.GetString() == "market.crypto");
        Assert.Equal("market.move-threshold", item.GetProperty("importanceRuleId").GetString());
        Assert.InRange(item.GetProperty("importanceScore").GetDouble(), 0.6, 1);
        Assert.Equal(0, document.RootElement.GetProperty("missingMarketBaselineCount").GetInt32());
    }

    [Fact]
    public async Task Rows_without_a_baseline_stay_unscorable_and_are_reported_separately()
    {
        var path = _temp.File("no-baseline.sqlite");
        var markets = new MarketStore(path); markets.Initialize();
        await markets.InsertAsync(new[]
        {
            new MarketObservation("binance-btcusdt-ticker", "Binance", "Global", "BTCUSDT", "USDT", 106,
                Now.AddHours(-1), Now, false, "live", "24h", "https://example.org/btc"),
        });

        using var factory = Api(path);
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/api/news/important"));
        Assert.Equal(0, document.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(1, document.RootElement.GetProperty("missingMarketBaselineCount").GetInt32());
        var market = Assert.Single(document.RootElement.GetProperty("familyDiagnostics").EnumerateArray(),
            family => family.GetProperty("family").GetString() == "Market");
        Assert.Equal(1, market.GetProperty("eligibleCount").GetInt32());
        Assert.Equal(0, market.GetProperty("importantEligibleCount").GetInt32());
        Assert.Equal(1, market.GetProperty("eligibleNotImportantCount").GetInt32());
        Assert.Equal(1, market.GetProperty("unscorableCount").GetInt32());
        var rule = Assert.Single(market.GetProperty("ruleCounts").EnumerateArray());
        Assert.Equal("market.missing-baseline", rule.GetProperty("ruleId").GetString());
        Assert.Equal(1, rule.GetProperty("eligibleCount").GetInt32());
        Assert.Equal(0, rule.GetProperty("importantEligibleCount").GetInt32());
    }

    [Fact]
    public async Task Below_threshold_move_with_a_provider_baseline_is_scored_and_not_counted_as_missing()
    {
        var path = _temp.File("below-threshold.sqlite");
        var markets = new MarketStore(path); markets.Initialize();
        await markets.InsertAsync(new[]
        {
            new MarketObservation("binance-btcusdt-ticker", "Binance", "Global", "BTCUSDT", "USDT", 102,
                Now.AddHours(-1), Now, false, "live", "24h", "https://example.org/btc",
                BaselineValue: 100, BaselineUtc: Now.AddHours(-25), ComparisonKind: "rolling-24h-open"),
        });

        using var factory = Api(path);
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/api/news/important"));
        Assert.Equal(0, document.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(0, document.RootElement.GetProperty("missingMarketBaselineCount").GetInt32());
        var market = Assert.Single(document.RootElement.GetProperty("familyDiagnostics").EnumerateArray(),
            family => family.GetProperty("family").GetString() == "Market");
        Assert.Equal(1, market.GetProperty("eligibleNotImportantCount").GetInt32());
        Assert.Equal(0, market.GetProperty("unscorableCount").GetInt32());
        var rule = Assert.Single(market.GetProperty("ruleCounts").EnumerateArray());
        Assert.Equal("market.below-threshold", rule.GetProperty("ruleId").GetString());
    }

    [Theory]
    [InlineData(0.0, "rolling-24h-open", 0)]
    [InlineData(-5.0, "rolling-24h-open", 0)]
    [InlineData(100.0, "rolling-24h-open", 3)]
    [InlineData(100.0, "some-other-comparison", 0)]
    [InlineData(100.0, null, 0)]
    public async Task Invalid_provider_baselines_are_rejected_and_never_scored(
        double baselineValue, string? comparisonKind, int jitterMinutes)
    {
        var path = _temp.File($"invalid-baseline-{baselineValue}-{comparisonKind}-{jitterMinutes}.sqlite");
        var markets = new MarketStore(path); markets.Initialize();
        await markets.InsertAsync(new[]
        {
            new MarketObservation("binance-btcusdt-ticker", "Binance", "Global", "BTCUSDT", "USDT", 106,
                Now.AddHours(-1), Now, false, "live", "24h", "https://example.org/btc",
                BaselineValue: baselineValue,
                BaselineUtc: Now.AddHours(-25).AddMinutes(jitterMinutes),
                ComparisonKind: comparisonKind),
        });

        using var factory = Api(path);
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/api/news/important"));
        Assert.Equal(0, document.RootElement.GetProperty("items").GetArrayLength());
        var market = Assert.Single(document.RootElement.GetProperty("familyDiagnostics").EnumerateArray(),
            family => family.GetProperty("family").GetString() == "Market");
        Assert.Equal(1, market.GetProperty("unscorableCount").GetInt32());
        var rule = Assert.Single(market.GetProperty("ruleCounts").EnumerateArray());
        Assert.Equal("market.missing-baseline", rule.GetProperty("ruleId").GetString());
    }

    [Fact]
    public async Task A_financial_news_story_is_never_reclassified_as_a_market_price_move()
    {
        var path = _temp.File("financial-news.sqlite");
        var news = new NewsStore(path); news.Initialize();
        // A tier-1 macroeconomic story: provider time is inside the window, so it is eligible,
        // but no approved rule promotes it and no approved code covers financial news.
        await news.InsertAsync(new[]
        {
            Story("macro", "Odds of fifth interest rate hike this year shorten as RBA deputy warns inflation outlook",
                Now.AddHours(-1)),
        });

        using var factory = Api(path);
        using var client = factory.CreateClient();
        using var feed = JsonDocument.Parse(await client.GetStringAsync("/api/news/important"));
        Assert.Equal(0, feed.RootElement.GetProperty("items").GetArrayLength());

        using var selection = JsonDocument.Parse(await client.GetStringAsync("/api/alerts/selection"));
        var item = Assert.Single(selection.RootElement.GetProperty("events").EnumerateArray());
        Assert.Equal("News", item.GetProperty("family").GetString());
        var types = item.GetProperty("eventTypes").EnumerateArray().Select(type => type.GetString()!).ToArray();
        Assert.Single(types);
        Assert.Equal("world-news", types[0]);
        Assert.DoesNotContain(types, code => code.StartsWith("market.", StringComparison.Ordinal));
        Assert.False(item.GetProperty("importance").GetProperty("isImportant").GetBoolean());
        Assert.Equal("news.no-approved-trigger", item.GetProperty("importance").GetProperty("ruleId").GetString());
    }

    [Fact]
    public async Task A_financial_story_promoted_by_an_approved_rule_keeps_its_news_topic()
    {
        var path = _temp.File("financial-story.sqlite");
        var news = new NewsStore(path); news.Initialize();
        await news.InsertAsync(new[]
        {
            Story("sanctions", "Sanctions imposed on three banks after sanctions list expansion", Now.AddHours(-1)),
        });

        using var factory = Api(path);
        using var client = factory.CreateClient();
        using var feed = JsonDocument.Parse(await client.GetStringAsync("/api/news/important"));
        var item = Assert.Single(feed.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("News", item.GetProperty("family").GetString());
        Assert.Equal("news.consequential-topic", item.GetProperty("importanceRuleId").GetString());
        var types = item.GetProperty("eventTypes").EnumerateArray().Select(type => type.GetString()!).ToArray();
        Assert.Contains("diplomacy", types);
        Assert.DoesNotContain(types, code => code.StartsWith("market.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_subscriber_to_economy_financial_matches_an_important_financial_story()
    {
        var path = _temp.File("financial-subscription.sqlite");
        var news = new NewsStore(path); news.Initialize();
        await news.InsertAsync(new[]
        {
            Story("rate", "Reserve Bank of Australia raises interest rates by half a point", Now.AddHours(-1)),
        });
        var subscriptions = new SubscriptionStore(path); subscriptions.Initialize();
        await subscriptions.UpsertAsync(new Subscription
        {
            EventType = "economy.financial",
            Channel = "email",
            Destination = "person@example.org",
        });

        using var factory = Api(path);
        using var client = factory.CreateClient();
        using var feed = JsonDocument.Parse(await client.GetStringAsync("/api/news/important"));
        var item = Assert.Single(feed.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("News", item.GetProperty("family").GetString());
        Assert.Equal("news.consequential-financial-event", item.GetProperty("importanceRuleId").GetString());
        Assert.Contains(item.GetProperty("eventTypes").EnumerateArray(), type => type.GetString() == "economy.financial");
        Assert.True(item.GetProperty("importanceScore").GetDouble() > 0.70);

        using var selection = JsonDocument.Parse(await client.GetStringAsync("/api/alerts/selection"));
        var selected = Assert.Single(selection.RootElement.GetProperty("events").EnumerateArray());
        Assert.True(selected.GetProperty("selected").GetBoolean());
        var match = Assert.Single(selected.GetProperty("matches").EnumerateArray());
        Assert.Equal("economy.financial", match.GetProperty("eventType").GetString());
    }

    [Fact]
    public async Task Market_observations_stay_in_the_market_family_and_never_take_the_financial_news_code()
    {
        var path = _temp.File("market-family.sqlite");
        var markets = new MarketStore(path); markets.Initialize();
        await markets.InsertAsync(new[]
        {
            new MarketObservation("binance-btcusdt-ticker", "Binance", "Global", "BTCUSDT", "USDT", 106,
                Now.AddHours(-1), Now, false, "live", "24h", "https://example.org/btc",
                BaselineValue: 100, BaselineUtc: Now.AddHours(-25), ComparisonKind: "rolling-24h-open"),
        });

        using var factory = Api(path);
        using var client = factory.CreateClient();
        using var feed = JsonDocument.Parse(await client.GetStringAsync("/api/news/important"));
        var item = Assert.Single(feed.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("Market", item.GetProperty("family").GetString());
        var types = item.GetProperty("eventTypes").EnumerateArray().Select(type => type.GetString()!).ToArray();
        Assert.Contains("market.crypto", types);
        Assert.DoesNotContain(types, code => code.StartsWith("economy.", StringComparison.Ordinal));
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
