using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using WorldAlerts.Api.News;

namespace WorldAlerts.Api.Tests;

public sealed class NewsEndpointTests : IDisposable
{
    private static readonly DateTimeOffset FirstSeen =
        new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task News_endpoint_returns_seeded_stories_newest_first_with_explicit_nulls()
    {
        var databasePath = _temp.File("events.sqlite");
        var store = new NewsStore(databasePath);
        store.Initialize();
        await store.InsertAsync(new[]
        {
            NewsTestSupport.Item(
                "published",
                FirstSeen,
                publishedUtc: new DateTimeOffset(2026, 10, 21, 10, 0, 0, TimeSpan.Zero),
                title: "Published story",
                categories: new[] { "World" }),
            NewsTestSupport.Item(
                "undated",
                FirstSeen.AddMinutes(-60),
                title: "Undated story",
                categories: new[] { "Tech", "Science" }),
        });

        using var factory = CreateApi(databasePath);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/news");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        Assert.Equal(2, root.GetArrayLength());

        var newest = root[0];
        Assert.Equal("Undated story", newest.GetProperty("title").GetString());
        Assert.Equal("undated", newest.GetProperty("nativeId").GetString());
        Assert.Equal("test-feed", newest.GetProperty("sourceId").GetString());
        Assert.Equal("Test Feed", newest.GetProperty("sourceName").GetString());
        Assert.Equal("world-news", newest.GetProperty("family").GetString());
        Assert.Equal("https://news.test/rss.xml", newest.GetProperty("provenance").GetString());
        Assert.Equal(JsonValueKind.Null, newest.GetProperty("publishedUtc").ValueKind);
        Assert.Equal(
            new[] { "Tech", "Science" },
            newest.GetProperty("categories").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(
            FirstSeen.AddMinutes(-60).ToString("O"),
            DateTimeOffset.Parse(newest.GetProperty("firstSeenUtc").GetString()!).ToString("O"));

        var older = root[1];
        Assert.Equal("Published story", older.GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.String, older.GetProperty("publishedUtc").ValueKind);
    }

    [Fact]
    public async Task News_endpoint_clamps_the_limit_and_leaves_health_working()
    {
        var databasePath = _temp.File("events.sqlite");
        var store = new NewsStore(databasePath);
        store.Initialize();
        await store.InsertAsync(new[]
        {
            NewsTestSupport.Item("one", FirstSeen, title: "One"),
            NewsTestSupport.Item("two", FirstSeen.AddMinutes(1), title: "Two"),
            NewsTestSupport.Item("three", FirstSeen.AddMinutes(2), title: "Three"),
        });

        using var factory = CreateApi(databasePath);
        using var client = factory.CreateClient();

        Assert.Equal(1, await ReadArrayLengthAsync(client, "/api/news?limit=0"));
        Assert.Equal(1, await ReadArrayLengthAsync(client, "/api/news?limit=1"));
        Assert.Equal(3, await ReadArrayLengthAsync(client, "/api/news?limit=500"));

        var health = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
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
                .UseSetting("WorldAlerts:News:PollingEnabled", "false"));
}
