using Microsoft.Data.Sqlite;
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
            categories: new[] { "Science", "Space" },
            summary: "Provider summary text");

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
        Assert.Equal("Provider summary text", stored.Summary);
    }

    [Fact]
    public async Task Insert_preserves_a_null_summary()
    {
        var store = CreateStore();
        await store.InsertAsync(new[] { NewsTestSupport.Item("no-summary", FirstSeen) });

        var stored = Assert.Single(await store.GetRecentAsync(NewsStore.DefaultLimit));

        Assert.Null(stored.Summary);
    }

    [Fact]
    public async Task Initializing_an_old_schema_adds_summary_and_preserves_rows()
    {
        var path = _temp.File("old-schema.sqlite");
        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE NewsItems (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SourceId TEXT NOT NULL,
                    SourceName TEXT NOT NULL,
                    NativeId TEXT NOT NULL,
                    Title TEXT NOT NULL,
                    Link TEXT NOT NULL,
                    PublishedUtc TEXT NULL,
                    FirstSeenUtc TEXT NOT NULL,
                    Family TEXT NOT NULL,
                    CategoriesJson TEXT NOT NULL,
                    Provenance TEXT NOT NULL,
                    CONSTRAINT UQ_NewsItems_Source_Native UNIQUE (SourceId, NativeId)
                );
                INSERT INTO NewsItems (
                    SourceId, SourceName, NativeId, Title, Link, PublishedUtc,
                    FirstSeenUtc, Family, CategoriesJson, Provenance)
                VALUES (
                    'old-feed', 'Old Feed', 'old-story', 'Old story',
                    'https://news.test/old-story', NULL,
                    '2026-10-21T12:00:00.0000000Z', 'world-news', '[]',
                    'https://news.test/old.xml');
                """;
            command.ExecuteNonQuery();
        }

        var store = new NewsStore(path);
        store.Initialize();

        var old = Assert.Single(await store.GetRecentAsync(NewsStore.DefaultLimit));
        Assert.Equal("old-story", old.NativeId);
        Assert.Equal("Old story", old.Title);
        Assert.Equal(FirstSeen, old.FirstSeenUtc);
        Assert.Null(old.Summary);

        var repoll = NewsTestSupport.Item(
            "old-story", FirstSeen.AddHours(2),
            publishedUtc: FirstSeen.AddHours(1), title: "Changed provider title",
            sourceId: "old-feed", sourceName: "Changed provider name",
            summary: "Provider summary added later");
        Assert.Equal(0, await store.InsertAsync(new[] { repoll }));
        Assert.Equal(0, await store.InsertAsync(new[] { repoll }));

        await store.InsertAsync(new[]
        {
            NewsTestSupport.Item("new-story", FirstSeen.AddMinutes(1), summary: "New provider summary"),
        });
        var stored = await store.GetRecentAsync(NewsStore.DefaultLimit);
        Assert.Equal(2, stored.Count);
        var backfilled = Assert.Single(stored, item => item.NativeId == "old-story");
        Assert.Equal("Provider summary added later", backfilled.Summary);
        Assert.Equal(old.SourceId, backfilled.SourceId);
        Assert.Equal(old.SourceName, backfilled.SourceName);
        Assert.Equal(old.NativeId, backfilled.NativeId);
        Assert.Equal(old.Title, backfilled.Title);
        Assert.Equal(old.Link, backfilled.Link);
        Assert.Equal(old.PublishedUtc, backfilled.PublishedUtc);
        Assert.Equal(old.FirstSeenUtc, backfilled.FirstSeenUtc);
        Assert.Equal(old.Family, backfilled.Family);
        Assert.Equal(old.Provenance, backfilled.Provenance);
        Assert.Equal(old.Categories, backfilled.Categories);
        Assert.Equal("New provider summary", Assert.Single(stored, item => item.NativeId == "new-story").Summary);

        // Running initialization again must not attempt a duplicate ALTER TABLE.
        store.Initialize();
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Repoll_only_fills_missing_summary_and_counts_only_new_stories(string? initialSummary)
    {
        var store = CreateStore();
        var original = NewsTestSupport.Item("existing", FirstSeen, summary: initialSummary);
        Assert.Equal(1, await store.InsertAsync(new[] { original }));

        var enriched = original with
        {
            Summary = "Original provider summary",
            FirstSeenUtc = FirstSeen.AddHours(1),
            PublishedUtc = FirstSeen.AddMinutes(10),
        };
        Assert.Equal(1, await store.InsertAsync(new[]
        {
            enriched,
            NewsTestSupport.Item("new", FirstSeen.AddHours(1)),
        }));
        Assert.Equal(0, await store.InsertAsync(new[] { enriched with { Summary = "Provider revised summary" } }));

        var stored = await store.GetRecentAsync(NewsStore.DefaultLimit);
        Assert.Equal(2, stored.Count);
        var existing = Assert.Single(stored, item => item.NativeId == "existing");
        Assert.Equal("Original provider summary", existing.Summary);
        Assert.Equal(FirstSeen, existing.FirstSeenUtc);
        Assert.Null(existing.PublishedUtc);
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
