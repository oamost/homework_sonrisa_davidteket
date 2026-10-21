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
              <description>  A &amp; B &lt;b&gt;live&lt;/b&gt;  </description>
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
        Assert.Equal("A & B live", item.Summary);
    }

    [Fact]
    public void Reads_html_description_and_collapses_whitespace_and_entities()
    {
        var xml = NewsTestSupport.Rss(
            """
            <item>
              <title>HTML summary</title>
              <link>https://news.test/html-summary</link>
              <guid>html-summary</guid>
              <description><![CDATA[<p>First&nbsp;line &amp; more</p>

                <p>Second <strong>line</strong></p>]]></description>
            </item>
            """);

        var item = Assert.Single(RssNewsParser.Parse(xml, NewsTestSupport.Feed, FirstSeen).Items);

        Assert.Equal("First line & more Second line", item.Summary);
        Assert.Equal("HTML summary", item.Title);
        Assert.Equal("https://news.test/html-summary", item.Link);
    }

    [Fact]
    public void Falls_back_to_summary_then_content_encoded()
    {
        var xml = NewsTestSupport.Rss(
            """
            <item>
              <title>Summary element</title>
              <link>https://news.test/summary-element</link>
              <guid>summary-element</guid>
              <summary>  From summary  </summary>
            </item>
            """,
            """
            <item>
              <title>Encoded element</title>
              <link>https://news.test/encoded-element</link>
              <guid>encoded-element</guid>
              <content:encoded><![CDATA[<p>From encoded</p>]]></content:encoded>
            </item>
            """);

        var result = RssNewsParser.Parse(xml, NewsTestSupport.Feed, FirstSeen);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal("From summary", result.Items[0].Summary);
        Assert.Equal("From encoded", result.Items[1].Summary);
    }

    [Fact]
    public void Absent_or_empty_summary_stays_null()
    {
        var xml = NewsTestSupport.Rss(
            """
            <item>
              <title>No summary</title>
              <link>https://news.test/no-summary</link>
              <guid>no-summary</guid>
            </item>
            """,
            """
            <item>
              <title>Empty summary</title>
              <link>https://news.test/empty-summary</link>
              <guid>empty-summary</guid>
              <description>   </description>
              <summary><![CDATA[]]></summary>
            </item>
            """);

        var result = RssNewsParser.Parse(xml, NewsTestSupport.Feed, FirstSeen);

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, item => Assert.Null(item.Summary));
    }

    [Fact]
    public void Truncates_long_summaries_at_the_documented_limit()
    {
        var longText = new string('x', RssNewsParser.MaxSummaryLength + 250);
        var xml = NewsTestSupport.Rss(
            $"""
            <item>
              <title>Long summary</title>
              <link>https://news.test/long-summary</link>
              <guid>long-summary</guid>
              <description>{longText}</description>
            </item>
            """);

        var item = Assert.Single(RssNewsParser.Parse(xml, NewsTestSupport.Feed, FirstSeen).Items);

        Assert.Equal(RssNewsParser.MaxSummaryLength, item.Summary!.Length);
        Assert.Equal(new string('x', RssNewsParser.MaxSummaryLength), item.Summary);
    }

    [Fact]
    public void Removes_script_and_style_text_from_summary()
    {
        var xml = NewsTestSupport.Rss(
            """
            <item>
              <title>Script summary</title>
              <link>https://news.test/script-summary</link>
              <guid>script-summary</guid>
              <description><![CDATA[<p>Visible</p><script>alert('x')</script><style>.a{color:red}</style><p>Also visible</p>]]></description>
            </item>
            """,
            """
            <item>
              <title>Escaped script summary</title>
              <link>https://news.test/escaped-script-summary</link>
              <guid>escaped-script-summary</guid>
              <description>&lt;script&gt;alert('y')&lt;/script&gt;&lt;p&gt;Escaped visible&lt;/p&gt;</description>
            </item>
            """);

        var result = RssNewsParser.Parse(xml, NewsTestSupport.Feed, FirstSeen);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal("Visible Also visible", result.Items[0].Summary);
        Assert.Equal("Escaped visible", result.Items[1].Summary);
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
