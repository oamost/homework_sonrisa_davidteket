using WorldAlerts.Api.Disasters;
using WorldAlerts.Api.News;
using WorldAlerts.Api.Scoring;
using WorldAlerts.Api.Selection;

namespace WorldAlerts.Api.Tests;

public sealed class CoverageGapTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private static NewsItem Story(string title, string id = "bbc-world", string name = "BBC World",
        DateTimeOffset? published = null, string family = "world-news", params string[] categories)
        => new(id, name, title, title, "https://example.org/" + id, published ?? Now.AddHours(-1),
            Now, family, categories, "https://example.org/feed");

    [Theory]
    [InlineData("Government dissolves parliament after election", "politics")]
    [InlineData("Treaty signed by leaders after summit", "diplomacy")]
    [InlineData("Ransomware shuts down hospital network", "cybersecurity")]
    [InlineData("AI model banned by regulator", "AI")]
    [InlineData("Merger approved by regulators", "acquisitions")]
    [InlineData("Power grid outage disrupts cities", "critical infrastructure")]
    [InlineData("Oil spill leaks into coastal waters", "environment/energy")]
    [InlineData("Fusion reactor demonstrates sustained reaction", "transformative science")]
    [InlineData("Armed forces invaded neighboring territory", "conflict")]
    public void Consequential_action_and_subject_trigger(string title, string topic)
    {
        var result = new NewsImportanceScorer().Score(Story(title, "ars-technica", "Ars Technica"), Now);
        Assert.True(result.IsImportant, topic);
        Assert.Equal("news.consequential-topic", result.RuleId);
        Assert.Equal(0.65, result.Score, 6);
    }

    [Theory]
    [InlineData("Analysis: government dissolves parliament", "politics")]
    [InlineData("Treaty talks continue", "diplomacy")]
    [InlineData("Ransomware trends explained", "cyber")]
    [InlineData("AI model review", "ai")]
    [InlineData("Merger rumors grow", "acquisitions")]
    [InlineData("Power grid upgrade planned", "critical infrastructure")]
    [InlineData("Oil spill policy discussion", "environment")]
    [InlineData("Fusion reactor research paper published", "science")]
    [InlineData("Armed forces hold routine exercise", "conflict")]
    public void Topic_without_consequential_action_is_not_important(string title, string family)
    {
        var result = new NewsImportanceScorer().Score(Story(title, "ars-technica", "Ars Technica", family: family), Now);
        Assert.False(result.IsImportant);
        Assert.Equal(0, result.Score);
    }

    [Theory]
    [InlineData("cyber", "tech.cyber")]
    [InlineData("ai", "tech.ai")]
    [InlineData("environment", "environment.energy")]
    [InlineData("transformative-science", "science.transformative")]
    [InlineData("politics", "politics")]
    [InlineData("diplomacy", "diplomacy")]
    [InlineData("acquisitions", "tech.acquisitions")]
    public void News_category_aliases_map_to_subscription_codes(string category, string expected)
        => Assert.Contains(expected, CanonicalEventTypes.News(Story("Routine update", categories: [category])));

    [Fact]
    public void Broad_feed_family_does_not_assign_every_item_to_its_topic()
        => Assert.DoesNotContain("tech.cyber", CanonicalEventTypes.News(Story("Routine update", family: "cyber")));

    [Theory]
    [InlineData("Cyclone", "disaster.cyclone")]
    [InlineData("Wildfires", "disaster.wildfire")]
    [InlineData("Volcanoes", "disaster.volcano")]
    [InlineData("Landslides", "disaster.landslide")]
    [InlineData("Epidemic", "disaster.epidemic")]
    [InlineData("Population Movement", "health.humanitarian")]
    public void Provider_hazard_aliases_map_to_subscription_codes(string type, string expected)
    {
        var item = new DisasterEvent("ifrc-go", "id", type, "Event", Now, Now, null, null, null,
            null, null, null, null, null, null, null, null, "https://example.org", Now);
        Assert.Contains(expected, CanonicalEventTypes.Disaster(item));
    }

    [Fact]
    public void Generic_severe_storm_is_not_assumed_to_be_a_cyclone()
    {
        var item = new DisasterEvent("eonet", "id", "Severe Storms", "Thunderstorms", Now, Now,
            null, null, null, null, null, null, null, null, null, null, null,
            "https://example.org", Now);
        Assert.DoesNotContain("disaster.cyclone", CanonicalEventTypes.Disaster(item));
        Assert.Contains("disaster.cyclone", CanonicalEventTypes.Disaster(item with { Title = "Tropical Cyclone Ida" }));
    }

    [Fact]
    public void Corroboration_requires_same_distinctive_story_and_independent_organizations()
    {
        var title = "Government signs historic international trade agreement";
        var bbc = Story(title);
        var bbcVariant = Story(title, "bbc-politics", "BBC Politics", Now.AddMinutes(-10));
        var nyt = Story(title, "nyt-world", "NYT World", Now.AddMinutes(-20));
        var unrelated = Story("Different trade agreement signed in another country", "guardian-world", "Guardian");
        var scorer = new NewsImportanceScorer();
        Assert.False(scorer.Score(bbc, Now, NewsStoryCorroboration.For(bbc, [bbc, bbcVariant, unrelated])).IsImportant);
        Assert.Equal(NewsImportanceRuleIds.TierOneCorroboration,
            scorer.Score(bbc, Now, NewsStoryCorroboration.For(bbc, [bbc, bbcVariant, nyt])).RuleId);
    }

    [Theory]
    [InlineData("Reserve Bank of Australia raises interest rates by half a point", true)]
    [InlineData("Billionaire Mukesh Ambani launches India's biggest-ever IPO", true)]
    [InlineData("Odds of fifth interest rate hike this year shorten", false)]
    [InlineData("Local startup announces $4 million IPO", false)]
    [InlineData("Routine gallery update", false)]
    [InlineData("Markets close mixed after quiet session", false)]
    public void Financial_news_code_is_assigned_only_for_consequential_financial_events(
        string headline, bool expected)
    {
        var item = new NewsItem("guardian-world", "The Guardian World", "id", headline,
            "https://example.org/story", Now.AddHours(-1), Now, "world-news", Array.Empty<string>(),
            "https://example.org/feed");
        var codes = CanonicalEventTypes.News(item);
        if (expected) Assert.Contains("economy.financial", codes);
        else Assert.DoesNotContain("economy.financial", codes);
    }

    [Fact]
    public void Provider_financial_topic_tags_assign_the_financial_code()
    {
        var item = new NewsItem("guardian-world", "The Guardian World", "id",
            "Interest rate outlook discussed at the Reserve Bank", "https://example.org/story", Now.AddHours(-1),
            Now, "world-news", ["Interest rates", "Inflation", "Australian economy"], "https://example.org/feed");
        Assert.Contains("economy.financial", CanonicalEventTypes.News(item));
    }

    [Fact]
    public void A_generic_business_tag_does_not_assign_the_financial_code()
    {
        var item = new NewsItem("guardian-world", "The Guardian World", "id", "Routine gallery update",
            "https://example.org/story", Now.AddHours(-1), Now, "world-news", ["Business", "World news"],
            "https://example.org/feed");
        Assert.DoesNotContain("economy.financial", CanonicalEventTypes.News(item));
    }

    [Fact]
    public void Numeric_strengths_are_finite_bounded_and_rank_provider_evidence_without_changing_boolean_rules()
    {
        var news = new NewsImportanceScorer();
        var headline = Story("Breaking conflict on border");
        var urgent = news.Score(headline, Now);
        var untriggered = news.Score(Story("Routine gallery update"), Now);
        Assert.True(urgent.IsImportant);
        Assert.False(untriggered.IsImportant);
        Assert.InRange(urgent.Score, 0, 1);
        Assert.True(double.IsFinite(urgent.Score));
        Assert.True(urgent.Score > untriggered.Score);

        var disasters = new DisasterImportanceScorer();
        var orange = ScoringFixtures.DS1();
        var red = disasters.Score(orange with { AlertLevel = "Red" });
        var weaker = disasters.Score(orange);
        Assert.True(red.IsImportant && weaker.IsImportant);
        Assert.InRange(red.Score, 0, 1);
        Assert.True(red.Score > weaker.Score);

        var markets = new MarketImportanceScorer();
        var standard = ScoringFixtures.MK1();
        var stronger = markets.Score(standard with { CurrentValue = standard.CurrentValue * 1.2 });
        var ordinary = markets.Score(standard);
        var below = markets.Score(ScoringFixtures.MK2());
        Assert.True(stronger.IsImportant && ordinary.IsImportant);
        Assert.False(below.IsImportant);
        Assert.All(new[] { stronger, ordinary, below }, decision =>
        {
            Assert.True(double.IsFinite(decision.Score));
            Assert.InRange(decision.Score, 0, 1);
        });
        Assert.True(stronger.Score > ordinary.Score);
        Assert.True(ordinary.Score > below.Score);
    }
}
