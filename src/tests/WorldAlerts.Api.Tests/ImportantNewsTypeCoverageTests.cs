using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WorldAlerts.Api.Disasters;
using WorldAlerts.Api.Markets;
using WorldAlerts.Api.News;
using WorldAlerts.Api.Selection;

namespace WorldAlerts.Api.Tests;

/// <summary>
/// One deterministic fixture per approved event-type code from decision D1
/// (<c>docs/decisions/2026-10-21-task-01-acceptance-boundaries.md</c>).
/// <para>
/// Every fixture is an eligible, important event that must render on the public
/// important-events feed carrying its approved code. These are test fixtures built from
/// provider-shaped metadata; they are never presented as live provider data and they never
/// relax eligibility or importance.
/// </para>
/// </summary>
public sealed class ImportantNewsTypeCoverageTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    public void Dispose() => _temp.Dispose();

    /// <summary>The approved code and the fixture title that must carry it.</summary>
    private static readonly (string Code, string Title)[] CodeFixtures =
    [
        ("world-news", "Breaking conflict on the border"),
        ("conflict", "Armed forces attack border post"),
        ("politics", "Government dissolves parliament after election result"),
        ("diplomacy", "Treaty signed by leaders after summit"),
        ("tech.cyber", "Ransomware shuts down hospital network"),
        ("tech.ai", "AI model banned by regulator"),
        ("tech.acquisitions", "Merger approved by regulators"),
        ("tech.critical-infrastructure", "Power grid outage disrupts cities"),
        ("environment.energy", "Oil spill leaks into coastal waters"),
        ("science.transformative", "Fusion reactor demonstrates sustained reaction"),
        ("disaster.earthquake", "Green quake magnitude 6.5"),
        ("disaster.flood", "Orange flood event"),
        ("disaster.cyclone", "Orange cyclone event"),
        ("disaster.wildfire", "Orange wildfire event"),
        ("disaster.volcano", "Orange volcano event"),
        ("disaster.drought", "Orange drought event"),
        ("disaster.tsunami", "Tsunami flagged quake"),
        ("disaster.landslide", "Orange landslide event"),
        ("disaster.epidemic", "IFRC epidemic severity 2"),
        ("health.humanitarian", "IFRC population movement severity 3"),
        ("market.crypto", "BTCUSDT at Binance"),
        ("market.fx", "EUR/USD at European Central Bank reference (Frankfurter)"),
        ("market.indices", "^SPX at Cboe"),
        ("economy.financial", "Reserve Bank of Australia raises interest rates by half a point"),
    ];

    /// <summary>The 24 approved codes, listed so drift from decision D1 is visible.</summary>
    public static TheoryData<string> ApprovedCodes()
    {
        var data = new TheoryData<string>();
        foreach (var (code, _) in CodeFixtures) data.Add(code);
        return data;
    }

    [Fact]
    public async Task Every_approved_event_type_code_renders_an_eligible_important_event()
    {
        var path = _temp.File("types.sqlite");
        await SeedAsync(path);
        using var factory = Api(path);
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/api/news/important"));
        var items = document.RootElement.GetProperty("items").EnumerateArray().ToArray();

        foreach (var (code, title) in CodeFixtures)
        {
            var item = Assert.Single(items, candidate => candidate.GetProperty("title").GetString() == title);
            Assert.Contains(item.GetProperty("eventTypes").EnumerateArray(), type => type.GetString() == code);
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("importanceRuleId").GetString()));
            var score = item.GetProperty("importanceScore").GetDouble();
            Assert.True(double.IsFinite(score));
            Assert.InRange(score, 0, 1);
        }

        var diagnostics = document.RootElement.GetProperty("familyDiagnostics").EnumerateArray().ToArray();
        Assert.Contains(diagnostics, family => family.GetProperty("family").GetString() == "News"
            && family.GetProperty("evaluatedCount").GetInt32() == 11
            && family.GetProperty("eligibleCount").GetInt32() == 11
            && family.GetProperty("importantEligibleCount").GetInt32() == 11);
        Assert.Contains(diagnostics, family => family.GetProperty("family").GetString() == "Disaster"
            && family.GetProperty("importantEligibleCount").GetInt32() == 10);
        Assert.Contains(diagnostics, family => family.GetProperty("family").GetString() == "Market"
            && family.GetProperty("importantEligibleCount").GetInt32() == 3);
    }

    [Theory]
    [MemberData(nameof(ApprovedCodes))]
    public void Approved_code_inventory_has_a_deterministic_fixture(string code)
        => Assert.Contains(CodeFixtures, fixture => fixture.Code == code);

    [Theory]
    // Subjects already carried by the classifier.
    [InlineData("Government dissolves parliament after election result", "politics")]
    [InlineData("Treaty signed by leaders after summit", "diplomacy")]
    [InlineData("Ransomware shuts down hospital network", "tech.cyber")]
    [InlineData("AI model banned by regulator", "tech.ai")]
    [InlineData("Merger approved by regulators", "tech.acquisitions")]
    [InlineData("Power grid outage disrupts cities", "tech.critical-infrastructure")]
    [InlineData("Oil spill leaks into coastal waters", "environment.energy")]
    [InlineData("Fusion reactor demonstrates sustained reaction", "science.transformative")]
    // Subjects the approved consequential-topic rule already used but the classifier dropped.
    [InlineData("Hackers breach vendor database", "tech.cyber")]
    [InlineData("Telecom network outage disrupts service", "tech.critical-infrastructure")]
    [InlineData("Emissions mandate issued for heavy industry", "environment.energy")]
    [InlineData("Troops invade northern district", "conflict")]
    [InlineData("Ceasefire signed by both sides", "diplomacy")]
    public void Approved_subject_headlines_keep_their_subscription_code(string headline, string code)
    {
        var item = new NewsItem("ars-technica", "Ars Technica", "id", headline, "https://example.org/story",
            Now.AddHours(-1), Now, "consequential-tech", Array.Empty<string>(), "https://example.org/feed");
        Assert.Contains(code, CanonicalEventTypes.News(item));
    }

    [Theory]
    [InlineData("earthquake", "Green quake", "disaster.earthquake")]
    [InlineData("flood", "Orange flood", "disaster.flood")]
    [InlineData("Pluvial/Flash Flood", "Pluvial/Flash Flood - Sokna", "disaster.flood")]
    [InlineData("cyclone", "Orange cyclone", "disaster.cyclone")]
    [InlineData("storm", "Tropical Cyclone Sinlaku in Micronesia", "disaster.cyclone")]
    [InlineData("wildfire", "Orange wildfire", "disaster.wildfire")]
    [InlineData("volcano", "Orange volcano", "disaster.volcano")]
    [InlineData("volcanic eruption", "Volcano eruption in Guatemala", "disaster.volcano")]
    [InlineData("volcanic-activity", "Escalation of volcanic activity", "disaster.volcano")]
    [InlineData("drought", "Orange drought", "disaster.drought")]
    [InlineData("landslide", "Orange landslide", "disaster.landslide")]
    [InlineData("mass-movement", "Mass movement in Campania Region", "disaster.landslide")]
    [InlineData("epidemic", "Epidemic reported", "disaster.epidemic")]
    [InlineData("population movement", "Population movement reported", "health.humanitarian")]
    [InlineData("other", "Yemen Population Movement -2026", "health.humanitarian")]
    [InlineData("other", "CHL: Other - Mass Removal", "health.humanitarian")]
    [InlineData("explosion", "M 1.7 Explosion near Lovelock", null)]
    [InlineData("other", "Public Event in Bavaria, Germany", null)]
    public void Provider_hazard_labels_keep_their_approved_code(string eventType, string title, string? expected)
    {
        var item = new DisasterEvent("ifrc-go", "id", eventType, title, Now, Now, null, null, null,
            null, null, null, null, null, null, null, null, "https://example.org", Now);
        var codes = CanonicalEventTypes.Disaster(item);
        if (expected is null) Assert.Empty(codes);
        else Assert.Contains(expected, codes);
    }

    private static async Task SeedAsync(string path)
    {
        var news = new NewsStore(path); news.Initialize();
        await news.InsertAsync(new[]
        {
            Story("world-news", "bbc-world", "BBC World", "Breaking conflict on the border"),
            Story("conflict", "ars-technica", "Ars Technica", "Armed forces attack border post"),
            Story("politics", "ars-technica", "Ars Technica", "Government dissolves parliament after election result"),
            Story("diplomacy", "ars-technica", "Ars Technica", "Treaty signed by leaders after summit"),
            Story("cyber", "ars-technica", "Ars Technica", "Ransomware shuts down hospital network"),
            Story("ai", "ars-technica", "Ars Technica", "AI model banned by regulator"),
            Story("acquisitions", "ars-technica", "Ars Technica", "Merger approved by regulators"),
            Story("infrastructure", "ars-technica", "Ars Technica", "Power grid outage disrupts cities"),
            Story("energy", "ars-technica", "Ars Technica", "Oil spill leaks into coastal waters"),
            Story("science", "ars-technica", "Ars Technica", "Fusion reactor demonstrates sustained reaction"),
            Story("financial", "guardian-world", "The Guardian World",
                "Reserve Bank of Australia raises interest rates by half a point"),
        });

        var disasters = new DisasterStore(path); disasters.Initialize();
        await disasters.InsertAsync(new[]
        {
            Hazard("quake", "gdacs", "earthquake", "Green quake magnitude 6.5", 6.5, "magnitude", "Green", false),
            Hazard("flood", "gdacs", "flood", "Orange flood event", null, null, "Orange", false),
            Hazard("cyclone", "gdacs", "cyclone", "Orange cyclone event", null, null, "Orange", false),
            Hazard("wildfire", "gdacs", "wildfire", "Orange wildfire event", null, null, "Orange", false),
            Hazard("volcano", "gdacs", "volcano", "Orange volcano event", null, null, "Orange", false),
            Hazard("drought", "gdacs", "drought", "Orange drought event", null, null, "Orange", false),
            Hazard("tsunami", "gdacs", "earthquake", "Tsunami flagged quake", 5.1, "magnitude", "Green", true),
            Hazard("landslide", "gdacs", "landslide", "Orange landslide event", null, null, "Orange", false),
            Hazard("epidemic", "ifrc-go", "epidemic", "IFRC epidemic severity 2", 2, "IFRC severity level", null, false),
            Hazard("movement", "ifrc-go", "Population Movement", "IFRC population movement severity 3", 3,
                "IFRC severity level", null, false),
        });

        var markets = new MarketStore(path); markets.Initialize();
        await markets.InsertAsync(new[]
        {
            Baseline("crypto-24h", "Binance", "Global", "BTCUSDT", "USDT", 100, "24h"),
            Current("crypto-24h", "Binance", "Global", "BTCUSDT", "USDT", 106, "24h"),
            Baseline("fx-daily", "European Central Bank reference (Frankfurter)", "Euro area / United States",
                "EUR/USD", "USD per EUR", 1.1000, "1d"),
            Current("fx-daily", "European Central Bank reference (Frankfurter)", "Euro area / United States",
                "EUR/USD", "USD per EUR", 1.1200, "1d"),
            Baseline("index-daily", "Cboe", "United States", "^SPX", "points", 100, "daily"),
            Current("index-daily", "Cboe", "United States", "^SPX", "points", 103, "daily"),
        });
    }

    private static NewsItem Story(string id, string sourceId, string sourceName, string title) =>
        new(sourceId, sourceName, id, title, $"https://example.org/{id}", Now.AddHours(-2), Now,
            "world-news", Array.Empty<string>(), "https://example.org/feed");

    private static DisasterEvent Hazard(string id, string sourceId, string eventType, string title,
        double? severity, string? severityUnit, string? alertLevel, bool tsunami) =>
        new(sourceId, id, eventType, title, Now.AddHours(-2), Now.AddHours(-1), null, null, null, null, null,
            severity, severityUnit, alertLevel, null, tsunami, null, $"https://example.org/{id}", Now);

    /// <summary>The approved 24-hour comparison baseline for one market source.</summary>
    private static MarketObservation Baseline(string sourceId, string venue, string region, string instrument,
        string quoteUnit, double value, string interval) =>
        new(sourceId, venue, region, instrument, quoteUnit, value, Now.AddHours(-25), Now, false, "live",
            interval, $"https://example.org/{sourceId}");

    /// <summary>The current observation compared against the 24-hour baseline.</summary>
    private static MarketObservation Current(string sourceId, string venue, string region, string instrument,
        string quoteUnit, double value, string interval) =>
        new(sourceId, venue, region, instrument, quoteUnit, value, Now.AddHours(-1), Now, false, "live",
            interval, $"https://example.org/{sourceId}");

    private static WebApplicationFactory<Program> Api(string path) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting("WorldAlerts:DatabasePath", path)
            .UseSetting("WorldAlerts:News:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Markets:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Disasters:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Advisories:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Markets:Sources:0:Id", "crypto-24h")
            .UseSetting("WorldAlerts:Markets:Sources:0:Kind", "binance-ticker")
            .UseSetting("WorldAlerts:Markets:Sources:1:Id", "fx-daily")
            .UseSetting("WorldAlerts:Markets:Sources:1:Kind", "frankfurter")
            .UseSetting("WorldAlerts:Markets:Sources:2:Id", "index-daily")
            .UseSetting("WorldAlerts:Markets:Sources:2:Kind", "cboe")
            .ConfigureTestServices(services => services.AddSingleton<TimeProvider>(new FixedClock())));

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
