using WorldAlerts.Api.Dedupe;

namespace WorldAlerts.Api.Tests;

public sealed class DedupeNewsTests
{
    [Fact]
    public void Reused_url_at_distinct_publication_times_is_not_one_story()
    {
        var first = DedupeTestSupport.News(nativeId: "one", link: "https://example.test/latest");
        var next = DedupeTestSupport.News(nativeId: "two", sourceId: "feed-b",
            link: "https://example.test/latest", publishedUtc: DedupeTestSupport.Now.AddDays(-2));

        Assert.Equal(2, NewsReconciler.Reconcile([first, next]).Count);
    }

    [Fact]
    public void Similar_but_unrelated_titles_do_not_cluster()
    {
        var first = DedupeTestSupport.News(link: "https://example.test/a");
        var second = DedupeTestSupport.News(nativeId: "two", sourceId: "feed-b",
            title: "A distinctive headline about a regional storm", link: "https://example.test/b");

        Assert.Equal(2, NewsReconciler.Reconcile([first, second]).Count);
    }

    [Fact]
    public void Tracking_url_variants_from_distinct_sources_cluster_with_matching_publication()
    {
        var first = DedupeTestSupport.News(link: "https://example.test/story/1?utm_source=feed&article=1");
        var second = DedupeTestSupport.News(nativeId: "two", sourceId: "feed-b",
            title: "Different headline", link: "https://EXAMPLE.test/story/1?article=1#top");

        Assert.Single(NewsReconciler.Reconcile([first, second]));
    }

    [Fact]
    public void A_bridge_does_not_merge_unrelated_ends_of_a_cluster()
    {
        var a = DedupeTestSupport.News(nativeId: "a", link: "https://example.test/a",
            publishedUtc: DedupeTestSupport.Now.AddHours(-10));
        var b = DedupeTestSupport.News(nativeId: "b", sourceId: "feed-b",
            link: "https://example.test/b", publishedUtc: DedupeTestSupport.Now.AddHours(-5));
        var c = DedupeTestSupport.News(nativeId: "c", sourceId: "feed-c",
            link: "https://example.test/c", publishedUtc: DedupeTestSupport.Now);

        Assert.True(NewsReconciler.Reconcile([a, b, c]).Count >= 2);
    }

    [Fact]
    public async Task Later_lexically_smaller_source_does_not_rewrite_existing_aliases()
    {
        var store = new DedupeStore(Path.Combine(Path.GetTempPath(), $"dedupe-{Guid.NewGuid():N}.db"));
        store.Initialize();
        var service = new EventDedupeService(store);
        var first = DedupeTestSupport.News(nativeId: "z", sourceId: "feed-z");
        var second = DedupeTestSupport.News(nativeId: "b", sourceId: "feed-b");
        var late = DedupeTestSupport.News(nativeId: "a", sourceId: "feed-a");
        await service.ReconcileNewsAsync([first, second]);
        var canonical = await store.ResolveCanonicalKeyAsync(EventIdentityResolver.Resolve(second).Key);

        await service.ReconcileNewsAsync([first, second, late]);

        Assert.Equal(canonical, await store.ResolveCanonicalKeyAsync(EventIdentityResolver.Resolve(second).Key));
        Assert.Equal(canonical, await store.ResolveCanonicalKeyAsync(EventIdentityResolver.Resolve(late).Key));
    }
}
