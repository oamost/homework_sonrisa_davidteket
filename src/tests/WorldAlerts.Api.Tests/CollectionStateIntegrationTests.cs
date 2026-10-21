using WorldAlerts.Api.Collection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using WorldAlerts.Api.News;
using WorldAlerts.Api.Markets;
using WorldAlerts.Api.Disasters;
using WorldAlerts.Api.Advisories;
using Xunit;

namespace WorldAlerts.Api.Tests;

public sealed class CollectionStateIntegrationTests
{
    [Theory]
    [InlineData("markets")]
    [InlineData("disasters")]
    [InlineData("advisories")]
    public async Task Real_family_ingestion_records_source_failure_and_skips_repeat_before_due(string family)
    {
        var path = Path.Combine(Path.GetTempPath(), "worldalerts-collection-" + Guid.NewGuid() + ".sqlite");
        var progress = new CollectionStateStore(path);
        progress.Initialize();
        var clock = new MutableClock(DedupeTestSupport.Now);
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Failure());
        var client = new HttpClient(handler);
        switch (family)
        {
            case "markets":
                var markets = new MarketStore(path); markets.Initialize();
                var marketSource = new MarketSource { Id = "source", Url = "https://data.test/market", Kind = "frankfurter" };
                var marketService = new MarketIngestionService(client, markets,
                    new MarketOptions { Sources = [marketSource] }, NullLogger<MarketIngestionService>.Instance,
                    progress, clock);
                await marketService.PollOnceAsync();
                await marketService.PollOnceAsync();
                break;
            case "disasters":
                var disasters = new DisasterStore(path); disasters.Initialize();
                var disasterSource = new DisasterSource { Id = "source", Url = "https://data.test/disaster", Kind = "gdacs-rss" };
                var disasterService = new DisasterIngestionService(client, disasters,
                    new DisasterOptions { Sources = [disasterSource] }, NullLogger<DisasterIngestionService>.Instance,
                    progress, clock);
                await disasterService.PollOnceAsync();
                await disasterService.PollOnceAsync();
                break;
            case "advisories":
                var advisories = new AdvisoryStore(path); advisories.Initialize();
                var advisorySource = new AdvisorySource { Id = "source", Url = "https://data.test/advisory" };
                var advisoryService = new AdvisoryIngestionService(client, advisories,
                    new AdvisoryOptions { Sources = [advisorySource] }, NullLogger<AdvisoryIngestionService>.Instance,
                    progress, clock);
                await advisoryService.PollOnceAsync();
                await advisoryService.PollOnceAsync();
                break;
        }
        Assert.Equal(1, handler.CallCount);
        var stored = Assert.Single(progress.ListAll());
        Assert.Equal(family, stored.Family);
        Assert.Equal("HttpRequestException", stored.LastError);
        Assert.Equal(1, stored.ConsecutiveFailures);
    }

    [Fact]
    public async Task Real_news_poller_observes_per_source_cadence_and_persists_failure_across_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), "worldalerts-collection-" + Guid.NewGuid() + ".sqlite");
        var news = new NewsStore(path);
        news.Initialize();
        var progress = new CollectionStateStore(path);
        progress.Initialize();
        var now = DedupeTestSupport.Now;
        var clock = new MutableClock(now);
        var firstFeed = NewsTestSupport.CreateFeed("first", "https://news.test/first");
        firstFeed.PollMinutes = 45;
        var secondFeed = NewsTestSupport.CreateFeed("second", "https://news.test/second");
        var options = new NewsOptions { PollMinutes = 15, Feeds = [firstFeed, secondFeed] };
        var handler = new FakeHttpHandler(request => request.RequestUri!.AbsolutePath == "/second"
            ? FakeHttpHandler.Failure()
            : FakeHttpHandler.Xml(NewsTestSupport.Rss("""
                <item><title>Breaking conflict</title><guid>one</guid>
                <link>https://news.test/one</link><pubDate>Wed, 21 Oct 2026 10:00:00 GMT</pubDate></item>
                """)));
        var client = new HttpClient(handler);
        var service = new NewsIngestionService(client, news, options,
            NullLogger<NewsIngestionService>.Instance, progress, clock);
        Assert.Equal(2, (await service.PollOnceAsync()).Feeds.Count);
        Assert.Equal(2, handler.CallCount);
        clock.Now = now.AddMinutes(15);
        Assert.Single((await service.PollOnceAsync()).Feeds);
        Assert.Equal(3, handler.CallCount);
        progress = new CollectionStateStore(path);
        progress.Initialize();
        service = new NewsIngestionService(client, news, options, NullLogger<NewsIngestionService>.Instance,
            progress, clock);
        clock.Now = now.AddMinutes(45);
        Assert.Equal(2, (await service.PollOnceAsync()).Feeds.Count);
        Assert.Equal(5, handler.CallCount);
        var states = await progress.SnapshotAsync();
        Assert.Equal(3, states.Single(x => x.SourceId == "second").ConsecutiveFailures);
        Assert.Equal("HttpRequestException", states.Single(x => x.SourceId == "second").LastError);
        Assert.Equal("Failed", states.Single(x => x.SourceId == "second").GetStatus(clock.Now).ToString());
    }
    [Fact]
    public async Task Legacy_progress_is_migrated_without_resetting_last_success_or_failure_count()
    {
        var path = Path.Combine(Path.GetTempPath(), "worldalerts-collection-" + Guid.NewGuid() + ".sqlite");
        await using (var db = new SqliteConnection($"Data Source={path}"))
        {
            await db.OpenAsync();
            await using var cmd = db.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE SourcePollProgress (SourceId TEXT PRIMARY KEY, Family TEXT NOT NULL,
                    LastAttemptUtc TEXT, LastSuccessUtc TEXT, LastError TEXT,
                    ConsecutiveFailures INTEGER NOT NULL, NextDueUtc TEXT);
                INSERT INTO SourcePollProgress VALUES ('bbc','news','2026-10-21T11:00:00.0000000Z',
                    '2026-10-21T10:00:00.0000000Z','HttpRequestException',2,'2026-10-21T11:15:00.0000000Z');
                """;
            await cmd.ExecuteNonQueryAsync();
        }
        var store = new CollectionStateStore(path);
        store.Initialize();
        var row = Assert.Single(await store.SnapshotAsync());
        Assert.Equal(2, row.ConsecutiveFailures);
        Assert.Equal(DedupeTestSupport.Now.AddHours(-2), row.LastSuccessUtc);
        await store.RecordAsync("bbc", "news", DedupeTestSupport.Now, false, "HttpRequestException", TimeSpan.FromMinutes(15));
        Assert.Equal(3, Assert.Single(store.ListAll()).ConsecutiveFailures);
        store.Initialize();
        Assert.Single(store.ListAll());
    }

    [Fact]
    public async Task Poll_gate_rejects_same_source_overlap_but_allows_other_sources()
    {
        var path = Path.Combine(Path.GetTempPath(), "worldalerts-collection-" + Guid.NewGuid() + ".sqlite");
        var store = new CollectionStateStore(path);
        store.Initialize();
        var now = DedupeTestSupport.Now;
        var clock = new FixedClock(now);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var first = store.PollAsync("bbc", "news", TimeSpan.FromMinutes(15), clock, async ct =>
        {
            Interlocked.Increment(ref count);
            entered.SetResult();
            await release.Task.WaitAsync(ct);
            return null;
        });
        await entered.Task;
        await store.PollAsync("bbc", "news", TimeSpan.FromMinutes(15), clock, ct =>
        {
            Interlocked.Increment(ref count);
            return Task.FromResult<string?>(null);
        });
        await store.PollAsync("un", "news", TimeSpan.FromMinutes(15), clock,
            ct => Task.FromResult<string?>(null));
        release.SetResult();
        await first;
        Assert.Equal(1, count);
        Assert.Equal(2, store.ListAll().Count);
    }

    [Fact]
    public async Task Failed_source_does_not_block_others_and_progress_survives_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), "worldalerts-collection-" + Guid.NewGuid() + ".sqlite");
        var store = new CollectionStateStore(path);
        store.Initialize();
        var now = DedupeTestSupport.Now;
        Assert.True(await store.IsDueAsync("bbc", "news", TimeSpan.FromMinutes(15), now));
        await store.RecordAsync("bbc", "news", now, success: false, "HttpRequestException", TimeSpan.FromMinutes(15));
        await store.RecordAsync("un", "news", now, success: true, null, TimeSpan.FromMinutes(15));
        var restarted = new CollectionStateStore(path);
        restarted.Initialize();
        Assert.False(await restarted.IsDueAsync("bbc", "news", TimeSpan.FromMinutes(15), now.AddMinutes(14)));
        Assert.True(await restarted.IsDueAsync("bbc", "news", TimeSpan.FromMinutes(15), now.AddMinutes(15)));
        var rows = await restarted.SnapshotAsync();
        Assert.Equal("stale", rows.Single(x => x.SourceId == "bbc").GetStatus(now).ToString().ToLowerInvariant());
        Assert.Equal(1, rows.Single(x => x.SourceId == "bbc").ConsecutiveFailures);
        Assert.Equal("healthy", rows.Single(x => x.SourceId == "un").GetStatus(now).ToString().ToLowerInvariant());
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
