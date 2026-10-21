using WorldAlerts.Api.Collection;

namespace WorldAlerts.Api.Tests;

public sealed class CollectionStateStoreTests : IDisposable
{
    private static readonly DateTimeOffset Attempted = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void TryInitialize_IsIdempotent()
    {
        var store = new CollectionStateStore(_temp.File("collection.sqlite"));
        Assert.True(store.TryInitialize());
        Assert.True(store.TryInitialize());
    }

    [Fact]
    public void TryInitialize_ReturnsFalse_WhenParentPathIsAFile()
    {
        File.WriteAllText(_temp.File("blocker"), "not a directory");
        var store = new CollectionStateStore(_temp.File("blocker/collection.sqlite"));
        Assert.False(store.TryInitialize());
    }

    [Fact]
    public void Persistence_SurvivesNewStoreInstance_Restart()
    {
        var first = new CollectionStateStore(_temp.File("collection.sqlite"));
        Assert.True(first.TryInitialize());
        first.RecordSuccess("news:reuters", "news", Attempted, PollCadence.DueUtcAfter(Attempted, TimeSpan.FromMinutes(15)));

        var restarted = new CollectionStateStore(_temp.File("collection.sqlite"));
        var restored = restarted.TryGet("news:reuters");

        Assert.NotNull(restored);
        Assert.Equal("news", restored!.Family);
        Assert.Equal(Attempted, restored.LastAttemptUtc);
        Assert.Equal(Attempted, restored.LastSuccessUtc);
        Assert.Equal(0, restored.ConsecutiveFailures);
        Assert.Equal(PollCadence.DueUtcAfter(Attempted, TimeSpan.FromMinutes(15)), restored.NextDueUtc);

        var all = restarted.ListAll();
        Assert.Single(all);
        Assert.Equal("news:reuters", all[0].SourceId);
    }

    [Fact]
    public void Failures_Increment_And_Recovers_OnSuccess()
    {
        var store = new CollectionStateStore(_temp.File("collection.sqlite"));
        Assert.True(store.TryInitialize());

        store.RecordFailure("markets:binance", "markets", Attempted, "timeout", null);
        store.RecordFailure("markets:binance", "markets", Attempted.AddSeconds(60), "timeout", null);
        var failed = store.TryGet("markets:binance");
        Assert.NotNull(failed);
        Assert.Equal(2, failed!.ConsecutiveFailures);
        Assert.Equal("timeout", failed.LastError);
        Assert.Null(failed.LastSuccessUtc);

        store.RecordSuccess("markets:binance", "markets", Attempted.AddMinutes(5), null);
        var recovered = store.TryGet("markets:binance");
        Assert.NotNull(recovered);
        Assert.Equal(0, recovered!.ConsecutiveFailures);
        Assert.Null(recovered.LastError);
        Assert.Equal(Attempted.AddMinutes(5), recovered.LastSuccessUtc);

        // A later failure preserves the last success and restarts the counter.
        store.RecordFailure("markets:binance", "markets", Attempted.AddMinutes(10), "down", null);
        var afterFailure = store.TryGet("markets:binance");
        Assert.NotNull(afterFailure);
        Assert.Equal(1, afterFailure!.ConsecutiveFailures);
        Assert.Equal("down", afterFailure.LastError);
        Assert.Equal(Attempted.AddMinutes(5), afterFailure.LastSuccessUtc);
    }

    [Fact]
    public void EmptyState_HasNoRows_And_TryGetReturnsNull()
    {
        var store = new CollectionStateStore(_temp.File("collection.sqlite"));
        Assert.True(store.TryInitialize());
        Assert.Empty(store.ListAll());
        Assert.Null(store.TryGet("unknown:feed"));
    }
}
