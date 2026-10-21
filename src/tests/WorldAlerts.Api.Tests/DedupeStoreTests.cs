using WorldAlerts.Api.Dedupe;

namespace WorldAlerts.Api.Tests;

public sealed class DedupeStoreTests
{
    [Fact]
    public async Task Concurrent_upserts_preserve_one_identity_and_one_revision()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dedupe-{Guid.NewGuid():N}.db");
        var store = new DedupeStore(path);
        store.Initialize();
        var service = new EventDedupeService(store, () => DedupeTestSupport.Now);
        var record = DedupeTestSupport.News();

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.UpsertAsync(record)));

        Assert.Single(results, result => result.Kind == EventRevisionKind.New);
        Assert.Equal(1, await store.CountAsync());
        Assert.All(results, result => Assert.Equal(1, result.RevisionCount));
    }

    [Fact]
    public async Task Late_acknowledgment_cannot_suppress_newer_revision_or_another_recipient()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dedupe-{Guid.NewGuid():N}.db");
        var store = new DedupeStore(path);
        store.Initialize();
        var service = new EventDedupeService(store, () => DedupeTestSupport.Now);
        var original = await service.UpsertAsync(DedupeTestSupport.News());
        var changed = await service.UpsertAsync(DedupeTestSupport.News(title: "Corrected flood headline"));

        Assert.False(await service.MarkDeliveredAsync(original, "subscriber-a", "email"));
        Assert.False(await service.ShouldNotifyAsync(original, "subscriber-a", "email"));
        Assert.True(await service.ShouldNotifyAsync(changed, "subscriber-a", "email"));
        Assert.True(await service.MarkDeliveredAsync(changed, "subscriber-a", "email"));
        Assert.False(await service.ShouldNotifyAsync(changed, "subscriber-a", "email"));
        Assert.True(await service.ShouldNotifyAsync(changed, "subscriber-b", "email"));
        Assert.True(await service.ShouldNotifyAsync(changed, "subscriber-a", "webhook"));

        var reopened = new EventDedupeService(new DedupeStore(path));
        Assert.False(await reopened.ShouldNotifyAsync(changed, "subscriber-a", "email"));
        Assert.False(await reopened.MarkDeliveredAsync(original, "subscriber-a", "email"));
    }

    [Fact]
    public async Task Forged_fingerprint_and_revision_number_are_not_acknowledged()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dedupe-{Guid.NewGuid():N}.db");
        var store = new DedupeStore(path);
        store.Initialize();
        var service = new EventDedupeService(store);
        var revision = await service.UpsertAsync(DedupeTestSupport.News());

        Assert.False(await service.MarkDeliveredAsync(revision with { Fingerprint = "made-up" }, "a", "email"));
        Assert.False(await service.MarkDeliveredAsync(revision with { RevisionCount = 99 }, "a", "email"));
        Assert.True(await service.ShouldNotifyAsync(revision, "a", "email"));
    }

    [Fact]
    public async Task Returning_to_previously_delivered_material_produces_a_new_revision_pending_policy_approval()
    {
        var store = new DedupeStore(Path.Combine(Path.GetTempPath(), $"dedupe-{Guid.NewGuid():N}.db"));
        store.Initialize();
        var service = new EventDedupeService(store);
        var original = DedupeTestSupport.News();
        var first = await service.UpsertAsync(original);
        Assert.True(await service.MarkDeliveredAsync(first, "a", "email"));
        var second = await service.UpsertAsync(DedupeTestSupport.News(title: "Changed headline"));
        Assert.True(await service.MarkDeliveredAsync(second, "a", "email"));

        var reverted = await service.UpsertAsync(original);

        Assert.Equal(first.Fingerprint, reverted.Fingerprint);
        Assert.Equal(3, reverted.RevisionCount);
        Assert.True(await service.ShouldNotifyAsync(reverted, "a", "email"));
        Assert.False(await service.MarkDeliveredAsync(first, "a", "email"));
    }
}
