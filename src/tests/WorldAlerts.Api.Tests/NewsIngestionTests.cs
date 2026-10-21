using WorldAlerts.Api.News;

namespace WorldAlerts.Api.Tests;

public sealed class NewsIngestionTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Two_polls_of_the_same_feed_do_not_create_duplicates()
    {
        var store = CreateStore();
        var feed = NewsTestSupport.CreateFeed("feed-a", "https://news.test/feed-a");
        var options = EnabledOptions(feed);
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Xml(NewsTestSupport.Rss(
            """
            <item>
              <title>Story one</title>
              <link>https://news.test/one</link>
              <guid>one</guid>
              <pubDate>Wed, 21 Oct 2026 10:00:00 GMT</pubDate>
            </item>
            """,
            """
            <item>
              <title>Story two</title>
              <link>https://news.test/two</link>
              <guid>two</guid>
            </item>
            """)));
        var ingestion = NewsTestSupport.Ingest(store, options, handler);

        var firstPoll = await ingestion.PollOnceAsync();
        var secondPoll = await ingestion.PollOnceAsync();

        var firstFeed = Assert.Single(firstPoll.Feeds);
        Assert.Equal(2, firstFeed.Parsed);
        Assert.Equal(2, firstFeed.Inserted);
        Assert.Equal(0, firstFeed.Duplicates);
        Assert.Null(firstFeed.ErrorType);

        var secondFeed = Assert.Single(secondPoll.Feeds);
        Assert.Equal(2, secondFeed.Parsed);
        Assert.Equal(0, secondFeed.Inserted);
        Assert.Equal(2, secondFeed.Duplicates);
        Assert.Null(secondFeed.ErrorType);

        Assert.Equal(2, (await store.GetRecentAsync(NewsStore.MaxLimit)).Count);
        Assert.Equal(2, handler.CallCount);
        Assert.Contains("WorldAlertsNewsBot", handler.LastUserAgent);
    }

    [Fact]
    public async Task A_failing_feed_does_not_block_other_feeds()
    {
        var store = CreateStore();
        var good = NewsTestSupport.CreateFeed("good-feed", "https://news.test/good");
        var httpFailure = NewsTestSupport.CreateFeed("http-failure", "https://news.test/http-failure");
        var malformed = NewsTestSupport.CreateFeed("malformed", "https://news.test/malformed");
        var options = EnabledOptions(good, httpFailure, malformed);
        var handler = new FakeHttpHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                "/http-failure" => FakeHttpHandler.Failure(),
                "/malformed" => FakeHttpHandler.Xml("<rss><channel><item>"),
                _ => FakeHttpHandler.Xml(NewsTestSupport.Rss(
                    """
                    <item>
                      <title>Good story</title>
                      <link>https://news.test/good-story</link>
                      <guid>good-1</guid>
                    </item>
                    """)),
            };
        });
        var ingestion = NewsTestSupport.Ingest(store, options, handler);

        var poll = await ingestion.PollOnceAsync();

        Assert.Equal(3, poll.Feeds.Count);
        var goodResult = poll.Feeds.Single(result => result.FeedId == "good-feed");
        var httpResult = poll.Feeds.Single(result => result.FeedId == "http-failure");
        var malformedResult = poll.Feeds.Single(result => result.FeedId == "malformed");

        Assert.Null(goodResult.ErrorType);
        Assert.Equal(1, goodResult.Inserted);
        Assert.Equal(nameof(HttpRequestException), httpResult.ErrorType);
        Assert.Equal(0, httpResult.Inserted);
        Assert.NotNull(malformedResult.ErrorType);
        Assert.Equal(0, malformedResult.Inserted);

        Assert.Single(await store.GetRecentAsync(NewsStore.MaxLimit));
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task Disabled_polling_makes_zero_http_calls()
    {
        var store = CreateStore();
        var options = new NewsOptions
        {
            PollingEnabled = false,
            Feeds = new List<NewsFeed> { NewsTestSupport.Feed },
        };
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Xml(NewsTestSupport.Rss(
            """
            <item>
              <title>Should not be fetched</title>
              <link>https://news.test/never</link>
            </item>
            """)));
        var ingestion = NewsTestSupport.Ingest(store, options, handler);

        var poll = await ingestion.PollOnceAsync();

        Assert.Empty(poll.Feeds);
        Assert.Equal(0, handler.CallCount);
        Assert.Empty(await store.GetRecentAsync(NewsStore.MaxLimit));
    }

    private NewsStore CreateStore()
    {
        var store = new NewsStore(_temp.File("events.sqlite"));
        store.Initialize();
        return store;
    }

    private static NewsOptions EnabledOptions(params NewsFeed[] feeds) => new()
    {
        PollingEnabled = true,
        PollMinutes = 15,
        RequestTimeoutSeconds = 5,
        Feeds = feeds.ToList(),
    };
}
