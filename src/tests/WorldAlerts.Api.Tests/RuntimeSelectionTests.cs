using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WorldAlerts.Api.Disasters;
using WorldAlerts.Api.Markets;
using WorldAlerts.Api.News;
using WorldAlerts.Api.Subscriptions;

namespace WorldAlerts.Api.Tests;

public sealed class RuntimeSelectionTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Endpoint_selects_only_eligible_important_matching_events_without_exposing_destinations()
    {
        var path = _temp.File("selection.sqlite");
        var news = new NewsStore(path); news.Initialize();
        await news.InsertAsync(new[]
        {
            Story("breaking", "Breaking conflict", Now.AddHours(-2), "bbc-world"),
            Story("quiet", "Arts report", Now.AddHours(-1), "bbc-world"),
            Story("old", "Breaking conflict", Now.AddHours(-26), "bbc-world"),
            Story("future", "Breaking conflict", Now.AddHours(1), "bbc-world"),
            Story("missing", "Breaking conflict", null, "bbc-world"),
        });
        var disasters = new DisasterStore(path); disasters.Initialize();
        await disasters.InsertAsync(new[] { new DisasterEvent("gdacs", "quake", "earthquake", "Major quake", Now.AddHours(-3), Now.AddHours(-2), null, null, null, "Japan", "JPN", 6.4, "magnitude", "Green", null, false, null, "https://example.org/quake", Now) });
        var subscriptions = new SubscriptionStore(path); subscriptions.Initialize();
        await subscriptions.UpsertAsync(new Subscription { EventType = "world-news", Channel = "email", Destination = "secret@example.org" });
        await subscriptions.UpsertAsync(new Subscription { EventType = "disaster.earthquake", Channel = "slack", Destination = "https://hooks.example.org/private" });
        await subscriptions.UpsertAsync(new Subscription { EventType = "conflict", Channel = "email", Enabled = false, Destination = "hidden" });
        using var factory = Api(path);
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/alerts/selection");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret@example.org", json);
        Assert.DoesNotContain("hooks.example.org", json);
        Assert.DoesNotContain("hidden", json);
        using var document = JsonDocument.Parse(json);
        var items = document.RootElement.GetProperty("events").EnumerateArray().ToArray();
        Assert.Contains(items, x => x.GetProperty("title").GetString() == "Breaking conflict" && x.GetProperty("selected").GetBoolean() && x.GetProperty("link").GetString() == "https://example.org/breaking");
        Assert.All(items.SelectMany(x => x.GetProperty("matches").EnumerateArray()), x => Assert.NotEqual("conflict", x.GetProperty("eventType").GetString()));
        Assert.Contains(items, x => x.GetProperty("title").GetString() == "Breaking conflict" && x.GetProperty("summary").GetString() == "Provider summary" && x.GetProperty("sourceId").GetString() == "bbc-world");
        Assert.Contains(items, x => x.GetProperty("title").GetString() == "Major quake" && x.GetProperty("selected").GetBoolean() && x.GetProperty("eventTypes").EnumerateArray().Any(y => y.GetString() == "disaster.earthquake"));
        Assert.Contains(items, x => x.GetProperty("title").GetString() == "Arts report" && !x.GetProperty("importance").GetProperty("isImportant").GetBoolean());
        Assert.Contains(items, x => x.GetProperty("title").GetString() == "Breaking conflict" && x.GetProperty("eligibility").GetProperty("status").GetString() == "OutsideWindow");
        Assert.Contains(items, x => x.GetProperty("eligibility").GetProperty("status").GetString() == "FutureTimestamp");
        Assert.Contains(items, x => x.GetProperty("eligibility").GetProperty("status").GetString() == "MissingProviderTimestamps");
    }

    [Fact]
    public async Task More_than_200_newer_rejected_rows_do_not_hide_an_eligible_important_story()
    {
        var path = _temp.File("volume.sqlite");
        var news = new NewsStore(path); news.Initialize();
        await news.InsertAsync(Enumerable.Range(0, 210)
            .Select(i => Story($"quiet-{i}", "Arts report", Now.AddMinutes(-i), "bbc-world"))
            .Append(Story("important-oldest", "Breaking conflict", Now.AddHours(-23), "bbc-world")).ToArray());
        var subscriptions = new SubscriptionStore(path); subscriptions.Initialize();
        await subscriptions.UpsertAsync(new Subscription { EventType = "world-news", Channel = "email" });
        using var factory = Api(path);
        using var client = factory.CreateClient();
        using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/alerts/selection"));
        Assert.Contains(doc.RootElement.GetProperty("events").EnumerateArray(), x =>
            x.GetProperty("title").GetString() == "Breaking conflict" && x.GetProperty("selected").GetBoolean());
        Assert.Equal(110, doc.RootElement.GetProperty("omittedRejectedCount").GetInt32());
    }

    [Fact]
    public async Task Market_requires_same_instrument_source_and_compatible_day_baseline()
    {
        var path = _temp.File("markets.sqlite");
        var markets = new MarketStore(path); markets.Initialize();
        await markets.InsertAsync(new[]
        {
            Observation("btc-24h", "BTCUSDT", "24h", 100, Now.AddHours(-25)),
            Observation("btc-24h", "BTCUSDT", "24h", 106, Now.AddHours(-1)),
            Observation("btc-1h", "BTCUSDT", "1h", 100, Now.AddHours(-25)),
            Observation("btc-1h", "BTCUSDT", "1h", 110, Now.AddHours(-1)),
            Observation("eth-24h", "ETHUSDT", "24h", 110, Now.AddHours(-1)),
        });
        var subscriptions = new SubscriptionStore(path); subscriptions.Initialize();
        await subscriptions.UpsertAsync(new Subscription { EventType = "market.crypto", Channel = "email" });
        using var factory = Api(path);
        using var client = factory.CreateClient();
        using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/alerts/selection"));
        var items = doc.RootElement.GetProperty("events").EnumerateArray().Where(x => x.GetProperty("family").GetString() == "Market").ToArray();
        Assert.Contains(items, x => x.GetProperty("sourceId").GetString() == "btc-24h" && x.GetProperty("selected").GetBoolean());
        Assert.Contains(items, x => x.GetProperty("sourceId").GetString() == "btc-1h" && !x.GetProperty("selected").GetBoolean() && x.GetProperty("importance").GetProperty("factors").ToString().Contains("1h move interval"));
        Assert.Contains(items, x => x.GetProperty("sourceId").GetString() == "eth-24h" && !x.GetProperty("selected").GetBoolean() && x.GetProperty("importance").GetProperty("factors").ToString().Contains("baseline", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Fresh_for_48h_importance_but_37h_old_index_is_ineligible_for_24h_selection()
    {
        var path = _temp.File("index.sqlite");
        var markets = new MarketStore(path); markets.Initialize();
        await markets.InsertAsync(new[]
        {
            new MarketObservation("index-daily", "Cboe", "US", "^SPX", "points", 100, Now.AddHours(-61), Now, true, "delayed", "daily", "https://example.org/spx"),
            new MarketObservation("index-daily", "Cboe", "US", "^SPX", "points", 102.4, Now.AddHours(-37), Now, true, "delayed", "daily", "https://example.org/spx"),
        });
        var subscriptions = new SubscriptionStore(path); subscriptions.Initialize();
        await subscriptions.UpsertAsync(new Subscription { EventType = "market.indices", Channel = "email" });
        using var factory = Api(path);
        using var client = factory.CreateClient();
        using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/alerts/selection"));
        Assert.Contains(doc.RootElement.GetProperty("events").EnumerateArray(), x =>
            x.GetProperty("sourceId").GetString() == "index-daily"
            && x.GetProperty("summary").GetString()!.Contains("102.4")
            && x.GetProperty("importance").GetProperty("isImportant").GetBoolean()
            && x.GetProperty("eligibility").GetProperty("status").GetString() == "OutsideWindow"
            && !x.GetProperty("selected").GetBoolean());
    }

    private static NewsItem Story(string id, string title, DateTimeOffset? published, string source) =>
        new(source, "BBC World", id, title, $"https://example.org/{id}", published, Now, "world-news", Array.Empty<string>(), $"https://example.org/{id}", "Provider summary");

    private static MarketObservation Observation(string source, string instrument, string interval, double value, DateTimeOffset at) =>
        new(source, "Binance", "Global", instrument, "USDT", value, at, Now, false, "candle", interval, "https://example.org/market");

    private static WebApplicationFactory<Program> Api(string path) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting("WorldAlerts:DatabasePath", path)
            .UseSetting("WorldAlerts:News:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Markets:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Disasters:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Advisories:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Markets:Sources:0:Id", "btc-24h")
            .UseSetting("WorldAlerts:Markets:Sources:0:Kind", "binance-klines")
            .UseSetting("WorldAlerts:Markets:Sources:1:Id", "btc-1h")
            .UseSetting("WorldAlerts:Markets:Sources:1:Kind", "binance-klines")
            .UseSetting("WorldAlerts:Markets:Sources:2:Id", "eth-24h")
            .UseSetting("WorldAlerts:Markets:Sources:2:Kind", "binance-klines")
            .UseSetting("WorldAlerts:Markets:Sources:3:Id", "index-daily")
            .UseSetting("WorldAlerts:Markets:Sources:3:Kind", "cboe")
            .ConfigureTestServices(services => services.AddSingleton<TimeProvider>(new FixedClock(Now))));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
