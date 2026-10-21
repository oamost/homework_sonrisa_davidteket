using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using WorldAlerts.Api.Subscriptions;

namespace WorldAlerts.Api.Tests;

public sealed class SubscriptionEndpointTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Subscriptions_survive_factory_restart_and_filter_by_event_type()
    {
        var databasePath = _temp.File("events.sqlite");

        // Seed before the first host starts to prove startup reads an existing DB.
        var store = new SubscriptionStore(databasePath);
        store.Initialize();
        var seeded = await store.UpsertAsync(new Subscription
        {
            EventType = "earthquake",
            Channel = "email",
            Destination = "seeded@example.com",
        });

        long wildfireId;
        using (var factory = CreateApi(databasePath))
        using (var client = factory.CreateClient())
        {
            var all = await ReadArrayAsync(client, "/api/subscriptions");
            Assert.Equal(seeded.Id, all[0].GetProperty("id").GetInt64());
            Assert.Equal("seeded@example.com", all[0].GetProperty("destination").GetString());

            var created = await client.PostAsJsonAsync("/api/subscriptions", new
            {
                eventType = "wildfire",
                channel = "  SLACK  ",
                destination = "channel:#alerts",
                enabled = false,
            });
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            using var createdDocument = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            wildfireId = createdDocument.RootElement.GetProperty("id").GetInt64();
            Assert.Equal("wildfire", createdDocument.RootElement.GetProperty("eventType").GetString());
            Assert.Equal("slack", createdDocument.RootElement.GetProperty("channel").GetString());
            Assert.False(createdDocument.RootElement.GetProperty("enabled").GetBoolean());

            var filtered = await ReadArrayAsync(client, "/api/subscriptions?eventType=earthquake");
            Assert.Equal("earthquake", Assert.Single(filtered).GetProperty("eventType").GetString());
        }

        using (var restarted = CreateApi(databasePath))
        using (var client = restarted.CreateClient())
        {
            var all = await ReadArrayAsync(client, "/api/subscriptions");
            Assert.Equal(2, all.Length);
            Assert.Contains(all, item => item.GetProperty("id").GetInt64() == wildfireId);
            Assert.All(all, item => Assert.Equal(Subscription.DefaultUserId, item.GetProperty("userId").GetString()));

            var filtered = await ReadArrayAsync(client, "/api/subscriptions?eventType=wildfire");
            var only = Assert.Single(filtered);
            Assert.False(only.GetProperty("enabled").GetBoolean());
            Assert.Equal("channel:#alerts", only.GetProperty("destination").GetString());
        }
    }

    [Fact]
    public async Task Posting_the_same_subscription_upserts_in_place()
    {
        var databasePath = _temp.File("events.sqlite");
        using var factory = CreateApi(databasePath);
        using var client = factory.CreateClient();

        var first = await client.PostAsJsonAsync("/api/subscriptions", new
        {
            eventType = "flood",
            channel = "email",
            destination = "first@example.com",
        });
        using var firstDocument = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var firstId = firstDocument.RootElement.GetProperty("id").GetInt64();

        var second = await client.PostAsJsonAsync("/api/subscriptions", new
        {
            eventType = "flood",
            channel = " EMAIL ",
            destination = "second@example.com",
            enabled = false,
        });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var secondDocument = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal(firstId, secondDocument.RootElement.GetProperty("id").GetInt64());

        var all = await ReadArrayAsync(client, "/api/subscriptions");
        var only = Assert.Single(all);
        Assert.Equal("second@example.com", only.GetProperty("destination").GetString());
        Assert.False(only.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Invalid_subscription_posts_return_400_without_echoing_the_destination()
    {
        var databasePath = _temp.File("events.sqlite");
        using var factory = CreateApi(databasePath);
        using var client = factory.CreateClient();

        const string secret = "secret-destination@example.com";
        var response = await client.PostAsJsonAsync("/api/subscriptions", new
        {
            eventType = "earthquake",
            channel = "   ",
            destination = secret,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(secret, body, StringComparison.Ordinal);
        Assert.Empty(await ReadArrayAsync(client, "/api/subscriptions"));
    }

    [Fact]
    public async Task Delete_returns_204_then_404()
    {
        var databasePath = _temp.File("events.sqlite");
        using var factory = CreateApi(databasePath);
        using var client = factory.CreateClient();

        var created = await client.PostAsJsonAsync("/api/subscriptions", new
        {
            eventType = "volcano.eruption",
            channel = "webhook",
        });
        using var document = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = document.RootElement.GetProperty("id").GetInt64();

        var deleted = await client.DeleteAsync($"/api/subscriptions/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var missing = await client.DeleteAsync($"/api/subscriptions/{id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        Assert.Empty(await ReadArrayAsync(client, "/api/subscriptions"));
    }

    private static async Task<JsonElement[]> ReadArrayAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        return document.RootElement.Clone().EnumerateArray().ToArray();
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
