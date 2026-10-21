using WorldAlerts.Api.Subscriptions;

namespace WorldAlerts.Api.Tests;

public sealed class SubscriptionStoreTests : IDisposable
{
    private static readonly DateTimeOffset Start =
        new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Upsert_and_list_trims_and_lowercases_the_channel()
    {
        var store = CreateStore();

        var stored = await store.UpsertAsync(new Subscription
        {
            EventType = "  earthquake  ",
            Channel = "  EMAIL  ",
            Destination = "  ops@example.com  ",
        });
        var listed = Assert.Single(await store.ListAsync());

        Assert.Equal("earthquake", stored.EventType);
        Assert.Equal("email", stored.Channel);
        Assert.Equal("ops@example.com", stored.Destination);
        Assert.True(stored.Enabled);
        Assert.Equal(Subscription.DefaultUserId, stored.UserId);
        Assert.Equal(stored.Id, listed.Id);
        Assert.Equal(stored, listed);
    }

    [Fact]
    public async Task Subscriptions_survive_reopening_the_database_file()
    {
        var path = _temp.File("world-alerts.sqlite");
        var firstStore = new SubscriptionStore(path, new MutableTimeProvider(Start));
        firstStore.Initialize();
        var saved = await firstStore.UpsertAsync(new Subscription
        {
            EventType = "wildfire",
            Channel = "slack",
            Destination = "channel:#alerts",
            Enabled = false,
        });

        var reopened = new SubscriptionStore(path);
        reopened.Initialize();
        var listed = Assert.Single(await reopened.ListAsync());

        Assert.Equal(saved, listed);
        Assert.Equal("wildfire", listed.EventType);
        Assert.Equal("slack", listed.Channel);
        Assert.False(listed.Enabled);
    }

    [Fact]
    public async Task Saving_the_same_key_twice_updates_in_place_without_duplicating()
    {
        var clock = new MutableTimeProvider(Start);
        var store = CreateStore(clock: clock);

        var first = await store.UpsertAsync(new Subscription
        {
            EventType = "earthquake",
            Channel = "EMAIL",
            Destination = "ops@example.com",
        });

        clock.UtcNow = Start.AddMinutes(5);
        var second = await store.UpsertAsync(new Subscription
        {
            EventType = "earthquake",
            Channel = "email",
            Destination = "duty@example.com",
            Enabled = false,
        });

        var listed = Assert.Single(await store.ListAsync());
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.Id, listed.Id);
        Assert.Equal("duty@example.com", listed.Destination);
        Assert.False(listed.Enabled);
        Assert.Equal(Start, listed.CreatedUtc);
        Assert.Equal(Start.AddMinutes(5), listed.UpdatedUtc);
    }

    [Fact]
    public async Task Enabled_and_destination_changes_persist_across_reopen()
    {
        var path = _temp.File("world-alerts.sqlite");
        var clock = new MutableTimeProvider(Start);

        var firstStore = new SubscriptionStore(path, clock);
        firstStore.Initialize();
        var created = await firstStore.UpsertAsync(new Subscription
        {
            EventType = "flood",
            Channel = "sms",
            Destination = "+10000000000",
            Enabled = true,
        });

        clock.UtcNow = Start.AddHours(1);
        var secondStore = new SubscriptionStore(path, clock);
        secondStore.Initialize();
        var updated = await secondStore.UpsertAsync(new Subscription
        {
            EventType = "flood",
            Channel = "sms",
            Destination = null,
            Enabled = false,
        });

        var thirdStore = new SubscriptionStore(path, clock);
        thirdStore.Initialize();
        var listed = Assert.Single(await thirdStore.ListAsync());

        Assert.Equal(created.Id, updated.Id);
        Assert.Equal(created.Id, listed.Id);
        Assert.Null(listed.Destination);
        Assert.False(listed.Enabled);
        Assert.Equal(Start, listed.CreatedUtc);
        Assert.Equal(Start.AddHours(1), listed.UpdatedUtc);
    }

    [Fact]
    public async Task Delete_removes_only_the_targeted_row()
    {
        var store = CreateStore();
        var earthquakeEmail = await store.UpsertAsync(
            new Subscription { EventType = "earthquake", Channel = "email" });
        var earthquakeSlack = await store.UpsertAsync(
            new Subscription { EventType = "earthquake", Channel = "slack" });
        await store.UpsertAsync(new Subscription { EventType = "wildfire", Channel = "email" });

        Assert.True(await store.DeleteAsync(earthquakeEmail.Id));
        Assert.True(await store.DeleteAsync(
            Subscription.DefaultUserId, "  earthquake  ", "  Slack  "));

        var remaining = Assert.Single(await store.ListAsync());
        Assert.Equal("wildfire", remaining.EventType);
        Assert.Equal("email", remaining.Channel);

        Assert.NotEqual(earthquakeEmail.Id, earthquakeSlack.Id);
        Assert.False(await store.DeleteAsync(earthquakeEmail.Id));
        Assert.False(await store.DeleteAsync("local", "earthquake", "slack"));
    }

    [Fact]
    public async Task Subscriptions_are_listed_in_deterministic_order()
    {
        var store = CreateStore();
        await store.UpsertAsync(new Subscription { EventType = "wildfire", Channel = "email" });
        await store.UpsertAsync(new Subscription { EventType = "earthquake", Channel = "slack" });
        await store.UpsertAsync(new Subscription { EventType = "earthquake", Channel = "email" });
        await store.UpsertAsync(new Subscription { EventType = "drought", Channel = "email" });

        var listed = await store.ListAsync();
        Assert.Equal(
            new[] { "drought|email", "earthquake|email", "earthquake|slack", "wildfire|email" },
            listed.Select(subscription => $"{subscription.EventType}|{subscription.Channel}"));
    }

    [Theory]
    [InlineData("", "email")]
    [InlineData("   ", "slack")]
    [InlineData("earthquake", "")]
    [InlineData("earthquake", "   ")]
    public async Task Empty_event_type_or_channel_is_rejected(string eventType, string channel)
    {
        var store = CreateStore();

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => store.UpsertAsync(new Subscription { EventType = eventType, Channel = channel }));

        Assert.Contains("Subscription", error.Message, StringComparison.Ordinal);
        Assert.Empty(await store.ListAsync());
    }

    [Fact]
    public async Task Arbitrary_future_channel_ids_are_accepted()
    {
        var store = CreateStore();
        var channels = new[] { "email", "carrier-pigeon/v2", "matrix-room:alerts", "webhook+2027" };

        foreach (var channel in channels)
        {
            await store.UpsertAsync(new Subscription
            {
                EventType = "volcano.eruption",
                Channel = channel,
            });
        }

        var listed = await store.ListAsync();
        Assert.Equal(
            channels.OrderBy(channel => channel, StringComparer.Ordinal),
            listed.Select(subscription => subscription.Channel));
        Assert.All(listed, subscription => Assert.Equal("volcano.eruption", subscription.EventType));
    }

    private SubscriptionStore CreateStore(
        string? path = null,
        TimeProvider? clock = null)
    {
        var store = new SubscriptionStore(path ?? _temp.File("world-alerts.sqlite"), clock);
        store.Initialize();
        return store;
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        public MutableTimeProvider(DateTimeOffset utcNow) => UtcNow = utcNow;

        public DateTimeOffset UtcNow { get; set; }

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
