using WorldAlerts.Api.News;

namespace WorldAlerts.Api.Tests;

public sealed class NewsStoreTests : IDisposable
{
    private static readonly DateTimeOffset FirstSeen =
        new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Insert_persists_all_story_fields()
    {
        var store = CreateStore();
        var item = NewsTestSupport.Item(
            "story-1",
            FirstSeen,
            publishedUtc: new DateTimeOffset(2026, 10, 20, 9, 15, 0, TimeSpan.Zero),
            title: "Stored story",
            categories: new[] { "Science", "Space" });

        var inserted = await store.InsertAsync(new[] { item });
        var stored = Assert.Single(await store.GetRecentAsync(NewsStore.DefaultLimit));

        Assert.Equal(1, inserted);
        Assert.Equal(item.SourceId, stored.SourceId);
        Assert.Equal(item.SourceName, stored.SourceName);
        Assert.Equal(item.NativeId, stored.NativeId);
        Assert.Equal(item.Title, stored.Title);
        Assert.Equal(item.Link, stored.Link);
        Assert.Equal(item.PublishedUtc, stored.PublishedUtc);
        Assert.Equal(item.FirstSeenUtc, stored.FirstSeenUtc);
        Assert.Equal(item.Family, stored.Family);
        Assert.Equal(item.Provenance, stored.Provenance);
        Assert.Equal(new[] { "Science", "Space" }, stored.Categories);
    }

    [Fact]
    public async Task Reinserting_the_same_story_inserts_zero_rows()
    {
        var store = CreateStore();
        var items = new[]
        {
            NewsTestSupport.Item("story-1", FirstSeen),
            NewsTestSupport.Item("story-2", FirstSeen.AddMinutes(1)),
        };

        Assert.Equal(2, await store.InsertAsync(items));
        Assert.Equal(0, await store.InsertAsync(items));

        Assert.Equal(2, (await store.GetRecentAsync(NewsStore.MaxLimit)).Count);
    }

    [Fact]
    public async Task Stories_are_ordered_by_publication_time_else_first_seen()
    {
        var store = CreateStore();
        await store.InsertAsync(new[]
        {
            NewsTestSupport.Item(
                "published-recently",
                FirstSeen.AddHours(1),
                publishedUtc: new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero)),
            NewsTestSupport.Item("never-published", FirstSeen.AddHours(2)),
            NewsTestSupport.Item(
                "published-oldest",
                FirstSeen.AddHours(3),
                publishedUtc: new DateTimeOffset(2026, 10, 19, 10, 0, 0, TimeSpan.Zero)),
        });

        var stored = await store.GetRecentAsync(NewsStore.DefaultLimit);

        Assert.Equal(
            new[] { "never-published", "published-recently", "published-oldest" },
            stored.Select(item => item.NativeId));
    }

    [Fact]
    public async Task Limit_is_clamped_between_one_and_two_hundred()
    {
        var store = CreateStore();
        var items = Enumerable.Range(0, 205)
            .Select(index => NewsTestSupport.Item($"story-{index}", FirstSeen.AddSeconds(index)))
            .ToArray();
        await store.InsertAsync(items);

        Assert.Single(await store.GetRecentAsync(0));
        Assert.Single(await store.GetRecentAsync(-10));
        Assert.Equal(NewsStore.MaxLimit, (await store.GetRecentAsync(500)).Count);
    }

    [Fact]
    public async Task Stories_survive_reopening_the_database_file()
    {
        var path = _temp.File("events.sqlite");
        var firstStore = new NewsStore(path);
        firstStore.Initialize();
        await firstStore.InsertAsync(new[]
        {
            NewsTestSupport.Item("persisted", FirstSeen, title: "Persisted story"),
        });

        var reopened = new NewsStore(path);
        reopened.Initialize();
        var stored = Assert.Single(await reopened.GetRecentAsync(NewsStore.DefaultLimit));

        Assert.Equal("persisted", stored.NativeId);
        Assert.Equal("Persisted story", stored.Title);
        Assert.Equal(FirstSeen, stored.FirstSeenUtc);
    }

    private NewsStore CreateStore()
    {
        var store = new NewsStore(_temp.File("events.sqlite"));
        store.Initialize();
        return store;
    }
}
