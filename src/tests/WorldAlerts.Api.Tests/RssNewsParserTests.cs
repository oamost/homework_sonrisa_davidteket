using WorldAlerts.Api.News;

namespace WorldAlerts.Api.Tests;

public sealed class RssNewsParserTests
{
    private static readonly DateTimeOffset FirstSeen =
        new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Parses_a_valid_item_with_entities_cdata_and_categories()
    {
        var xml = NewsTestSupport.Rss(
            """
            <item>
              <title>  Tom &amp; Jerry &lt;live&gt;  </title>
              <link> https://news.test/a </link>
              <guid> story-1 </guid>
              <pubDate>Wed, 21 Oct 2026 09:30:00 GMT</pubDate>
              <category>Politics</category>
              <category><![CDATA[Economy & Trade]]></category>
            </item>
            """);

        var result = RssNewsParser.Parse(xml, NewsTestSupport.Feed, FirstSeen);

        var item = Assert.Single(result.Items);
        Assert.Equal(0, result.Skipped);
        Assert.Equal("test-feed", item.SourceId);
        Assert.Equal("Test Feed", item.SourceName);
        Assert.Equal("story-1", item.NativeId);
        Assert.Equal("Tom & Jerry <live>", item.Title);
        Assert.Equal("https://news.test/a", item.Link);
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 9, 30, 0, TimeSpan.Zero), item.PublishedUtc);
        Assert.Equal(FirstSeen, item.FirstSeenUtc);
        Assert.Equal("world-news", item.Family);
        Assert.Equal(new[] { "Politics", "Economy & Trade" }, item.Categories);
        Assert.Equal("https://news.test/rss.xml", item.Provenance);
    }

    [Fact]
    public void Falls_back_to_link_when_guid_is_missing()
    {
        var xml = NewsTestSupport.Rss(
            """
            <item>
              <title>No guid here</title>
              <link>https://news.test/fallback</link>
              <pubDate>Wed, 21 Oct 2026 08:00:00 GMT</pubDate>
            </item>
            """);

        var item = Assert.Single(RssNewsParser.Parse(xml, NewsTestSupport.Feed, FirstSeen).Items);

        Assert.Equal("https://news.test/fallback", item.NativeId);
    }

    [Fact]
    public void Missing_publication_date_is_stored_as_null()
    {
        var xml = NewsTestSupport.Rss(
            """
            <item>
              <title>Undated</title>
              <link>https://news.test/undated</link>
              <guid>undated-1</guid>
            </item>
            """);

        var item = Assert.Single(RssNewsParser.Parse(xml, NewsTestSupport.Feed, FirstSeen).Items);

        Assert.Null(item.PublishedUtc);
        Assert.Equal(FirstSeen, item.FirstSeenUtc);
        Assert.Empty(item.Categories);
    }

    [Fact]
    public void Invalid_publication_date_is_stored_as_null()
    {
        var xml = NewsTestSupport.Rss(
            """
            <item>
              <title>Bad date</title>
              <link>https://news.test/bad-date</link>
              <pubDate>not a real date</pubDate>
            </item>
            """);

        var item = Assert.Single(RssNewsParser.Parse(xml, NewsTestSupport.Feed, FirstSeen).Items);

        Assert.Null(item.PublishedUtc);
    }

    [Fact]
    public void Invalid_pubDate_falls_back_to_dc_date()
    {
        var xml = NewsTestSupport.Rss(
            """
            <item>
              <title>Namespace date</title>
              <link>https://news.test/dc-date</link>
              <pubDate>garbage</pubDate>
              <dc:date>2026-10-20T07:15:00Z</dc:date>
            </item>
            """);

        var item = Assert.Single(RssNewsParser.Parse(xml, NewsTestSupport.Feed, FirstSeen).Items);

        Assert.Equal(new DateTimeOffset(2026, 10, 20, 7, 15, 0, TimeSpan.Zero), item.PublishedUtc);
    }

    [Fact]
    public void Skips_items_without_title_or_link_and_counts_them()
    {
        var xml = NewsTestSupport.Rss(
            """
            <item>
              <guid>only-an-id</guid>
              <category>Orphan</category>
            </item>
            """,
            """
            <item>
              <title>Kept</title>
              <link>https://news.test/kept</link>
              <guid>kept-1</guid>
            </item>
            """);

        var result = RssNewsParser.Parse(xml, NewsTestSupport.Feed, FirstSeen);

        var item = Assert.Single(result.Items);
        Assert.Equal(1, result.Skipped);
        Assert.Equal("kept-1", item.NativeId);
    }

    [Fact]
    public void Preserves_a_late_published_timestamp_separately_from_first_seen()
    {
        var xml = NewsTestSupport.Rss(
            """
            <item>
              <title>Published yesterday</title>
              <link>https://news.test/late</link>
              <guid>late-1</guid>
              <pubDate>Mon, 19 Oct 2026 22:00:00 GMT</pubDate>
            </item>
            """);

        var item = Assert.Single(RssNewsParser.Parse(xml, NewsTestSupport.Feed, FirstSeen).Items);

        Assert.Equal(new DateTimeOffset(2026, 10, 19, 22, 0, 0, TimeSpan.Zero), item.PublishedUtc);
        Assert.Equal(FirstSeen, item.FirstSeenUtc);
    }
}
