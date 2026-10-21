using WorldAlerts.Api.Dedupe;
using WorldAlerts.Api.Events;

namespace WorldAlerts.Api.Tests;

/// <summary>Identity and fallback rules for task 13.</summary>
public sealed class DedupeIdentityTests
{
    [Fact]
    public void Same_source_and_native_id_is_the_same_event()
    {
        var first = EventIdentityResolver.Resolve(DedupeTestSupport.News(nativeId: "story-1"));
        var second = EventIdentityResolver.Resolve(
            DedupeTestSupport.News(nativeId: "story-1", title: "A corrected headline"));

        Assert.Equal(first.Key, second.Key);
        Assert.Equal(EventIdentityBasis.NativeId, first.Basis);
        Assert.Equal("story-1", first.NativeId);
    }

    [Fact]
    public void The_same_native_id_in_two_families_is_not_one_event()
    {
        var news = EventIdentityResolver.Resolve(DedupeTestSupport.News(nativeId: "shared-id"));
        var disaster = EventIdentityResolver.Resolve(DedupeTestSupport.Disaster(nativeId: "shared-id"));

        Assert.NotEqual(news.Key, disaster.Key);
    }

    [Fact]
    public void Missing_native_id_uses_source_normalized_link_and_published_time()
    {
        var first = EventIdentityResolver.Resolve(DedupeTestSupport.News(
            nativeId: null,
            link: "https://Example.test/Story/?utm_source=feed&b=2&a=1#fragment"));
        var second = EventIdentityResolver.Resolve(DedupeTestSupport.News(
            nativeId: null,
            link: "https://example.test/story?a=1&b=2"));

        Assert.Equal(first.Key, second.Key);
        Assert.Equal(EventIdentityBasis.NormalizedLink, first.Basis);
        Assert.Equal("https://example.test/story?a=1&b=2", first.NormalizedLink);
    }

    [Fact]
    public void Missing_native_id_with_different_links_is_not_merged()
    {
        var first = EventIdentityResolver.Resolve(DedupeTestSupport.News(
            nativeId: null,
            link: "https://example.test/story/a"));
        var second = EventIdentityResolver.Resolve(DedupeTestSupport.News(
            nativeId: null,
            link: "https://example.test/story/b"));

        Assert.NotEqual(first.Key, second.Key);
    }

    [Fact]
    public void Missing_native_id_falls_back_to_title_when_no_link_is_supplied()
    {
        var first = EventIdentityResolver.Resolve(DedupeTestSupport.News(
            nativeId: null,
            title: "Distinctive flood headline for the northern valley",
            link: string.Empty));
        var second = EventIdentityResolver.Resolve(DedupeTestSupport.News(
            nativeId: null,
            title: "Distinctive flood headline for the northern valley",
            link: string.Empty));
        var different = EventIdentityResolver.Resolve(DedupeTestSupport.News(
            nativeId: null,
            title: "Distinctive wildfire headline for the southern valley",
            link: string.Empty));

        Assert.Equal(first.Key, second.Key);
        Assert.Equal(EventIdentityBasis.NormalizedTitle, first.Basis);
        Assert.NotEqual(first.Key, different.Key);
    }

    [Fact]
    public void Missing_native_id_uses_fetch_time_when_publication_is_missing()
    {
        var first = EventIdentityResolver.Resolve(DedupeTestSupport.News(
            nativeId: null,
            publishedUtc: null,
            fetchedUtc: DedupeTestSupport.Now) with { PublishedTimeUtc = null });
        var same = EventIdentityResolver.Resolve(DedupeTestSupport.News(
            nativeId: null,
            publishedUtc: null,
            fetchedUtc: DedupeTestSupport.Now) with { PublishedTimeUtc = null });
        var later = EventIdentityResolver.Resolve(DedupeTestSupport.News(
            nativeId: null,
            publishedUtc: null,
            fetchedUtc: DedupeTestSupport.Now.AddMinutes(5)) with { PublishedTimeUtc = null });

        Assert.Equal(first.Key, same.Key);
        Assert.NotEqual(first.Key, later.Key);
    }

    [Fact]
    public void Future_publication_time_still_produces_a_stable_identity()
    {
        var future = DedupeTestSupport.Now.AddHours(2);
        var first = EventIdentityResolver.Resolve(DedupeTestSupport.News(
            nativeId: null,
            publishedUtc: future));
        var second = EventIdentityResolver.Resolve(DedupeTestSupport.News(
            nativeId: null,
            publishedUtc: future));

        Assert.Equal(first.Key, second.Key);
        Assert.True((first.TimestampUtc - future).Duration() < TimeSpan.FromSeconds(1));
        Assert.True(first.TimestampUtc > DedupeTestSupport.Now);
    }

    [Fact]
    public void Cross_family_records_with_the_same_title_and_link_are_isolated()
    {
        var news = EventIdentityResolver.Resolve(DedupeTestSupport.News(
            nativeId: null,
            link: "https://example.test/shared"));
        var disaster = EventIdentityResolver.Resolve(DedupeTestSupport.Disaster(
            nativeId: null,
            link: "https://example.test/shared"));

        Assert.NotEqual(news.Key, disaster.Key);
        Assert.NotEqual(news.Family, disaster.Family);
    }

    [Fact]
    public void Market_identity_uses_observation_not_fetch_time_or_price()
    {
        var first = DedupeTestSupport.Market();
        var again = DedupeTestSupport.Market(value: "105") with { FetchedUtc = DedupeTestSupport.Now.AddMinutes(10) };
        var next = DedupeTestSupport.Market(observationUtc: DedupeTestSupport.Now.AddMinutes(-4));
        var otherInstrument = DedupeTestSupport.Market(instrument: "ETHUSDT");
        var otherInterval = first with { Metadata = new Dictionary<string, string?>(first.Metadata) { ["interval"] = "1h" } };

        Assert.Equal(EventIdentityResolver.Resolve(first).Key, EventIdentityResolver.Resolve(again).Key);
        Assert.Equal(EventIdentityBasis.MarketObservation, EventIdentityResolver.Resolve(first).Basis);
        Assert.NotEqual(EventIdentityResolver.Resolve(first).Key, EventIdentityResolver.Resolve(next).Key);
        Assert.NotEqual(EventIdentityResolver.Resolve(first).Key, EventIdentityResolver.Resolve(otherInstrument).Key);
        Assert.NotEqual(EventIdentityResolver.Resolve(first).Key, EventIdentityResolver.Resolve(otherInterval).Key);
    }
}
