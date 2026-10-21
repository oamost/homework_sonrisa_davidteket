using WorldAlerts.Api.Selection;

namespace WorldAlerts.Api.Tests;

public sealed class ImportantNewsDiversityTests
{
    private static ImportantNewsItem Item(string title, string family, string type, double score) =>
        new(title, null, "fixture", "Fixture", title, "https://example.org/story", "https://example.org/feed",
            DateTimeOffset.Parse("2026-10-21T12:00:00Z"), "test.rule", [], family,
            [type], DateTimeOffset.Parse("2026-10-21T12:00:00Z"), score);

    [Fact]
    public void Recent_headlines_are_provider_dated_deduplicated_and_not_fabricated()
    {
        var now = new DateTimeOffset(2026, 10, 21, 13, 0, 0, TimeSpan.Zero);
        WorldAlerts.Api.News.NewsItem Story(string title, DateTimeOffset? published, string link) =>
            new("bbc-world", "BBC World", link, title, link, published, now, "world-news",
                [], "https://feeds.bbci.co.uk/news/world/rss.xml");
        var result = ImportantNewsService.LatestProviderHeadlines([
            Story("Earthquake today", now.AddHours(-1), "https://example.org/a"),
            Story("Earthquake today", now.AddHours(-2), "https://example.org/b"),
            Story("Yesterday", now.AddHours(-25), "https://example.org/c"),
            Story("Unknown timestamp", null, "https://example.org/d"),
            Story("Future timestamp", now.AddHours(1), "https://example.org/e"),
        ], now);
        var item = Assert.Single(result);
        Assert.Equal("Earthquake today", item.Title);
        Assert.Equal(now.AddHours(-1), item.PublishedUtc);
    }

    [Fact]
    public void Important_preview_deduplicates_same_headline_from_multiple_feeds()
    {
        var first = Item("Government announces policy", "News", "politics", 0.9);
        var second = first with { SourceId = "bbc-health", SourceName = "BBC Health" };
        var result = ImportantNewsService.ChooseDiverse([first, second], 100);
        Assert.Single(result);
    }

    [Fact]
    public void No_family_or_approved_event_type_is_starved_by_many_news_items()
    {
        var input = Enumerable.Range(0, 150)
            .Select(i => Item($"News {i}", "News", "world-news", 0.95))
            .Append(Item("Earthquake", "Disaster", "disaster.earthquake", 0.8))
            .Append(Item("Forex", "Market", "market.fx", 0.7))
            .Append(Item("Cyber", "News", "tech.cyber", 0.65)).ToArray();
        var result = ImportantNewsService.ChooseDiverse(input, 100);
        Assert.Equal(100, result.Count);
        Assert.Contains(result, x => x.Title == "Earthquake");
        Assert.Contains(result, x => x.Title == "Forex");
        Assert.Contains(result, x => x.Title == "Cyber");
    }

    [Fact]
    public void Chooses_no_false_positive_when_no_eligible_items_exist()
    {
        Assert.Empty(ImportantNewsService.ChooseDiverse([], 100));
        Assert.Empty(ImportantNewsService.ChooseDiverse([Item("News", "News", "world-news", 0.9)], 0));
    }
}
