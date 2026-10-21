using WorldAlerts.Api.Disasters;

namespace WorldAlerts.Api.Tests;

public sealed class DisasterStoreTests : IDisposable
{
    private static readonly DateTimeOffset Fetched =
        new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Insert_persists_all_event_fields_and_survives_reopening()
    {
        var path = _temp.File("events.sqlite");
        var store = new DisasterStore(path);
        store.Initialize();
        var item = DisasterTestSupport.Event(
            "us7000abcd",
            new DateTimeOffset(2026, 10, 20, 18, 0, 0, TimeSpan.Zero),
            title: "Stored event",
            eventType: "earthquake",
            publishedUtc: new DateTimeOffset(2026, 10, 20, 18, 5, 0, TimeSpan.Zero),
            updatedUtc: new DateTimeOffset(2026, 10, 20, 18, 10, 0, TimeSpan.Zero),
            latitude: 35.1,
            longitude: 139.2,
            country: "Japan",
            iso3: "JPN",
            severity: 4.5,
            severityUnit: "mb",
            alertLevel: "yellow",
            alertScore: 1.2,
            tsunami: true,
            significance: 312,
            provenance: "https://earthquake.usgs.gov/event/us7000abcd",
            fetchedUtc: Fetched,
            certainty: "Likely",
            effectiveUtc: new DateTimeOffset(2026, 10, 20, 17, 55, 0, TimeSpan.Zero),
            expiresUtc: new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero),
            depthKm: 10.5);

        Assert.Equal(1, await store.InsertAsync(new[] { item }));

        var reopened = new DisasterStore(path);
        reopened.Initialize();
        var stored = Assert.Single(await reopened.GetRecentAsync(DisasterStore.DefaultLimit));

        Assert.Equal(item.SourceId, stored.SourceId);
        Assert.Equal(item.NativeId, stored.NativeId);
        Assert.Equal(item.EventType, stored.EventType);
        Assert.Equal(item.Title, stored.Title);
        Assert.Equal(item.EventTimeUtc, stored.EventTimeUtc);
        Assert.Equal(item.PublishedUtc, stored.PublishedUtc);
        Assert.Equal(item.UpdatedUtc, stored.UpdatedUtc);
        Assert.Equal(item.Latitude, stored.Latitude);
        Assert.Equal(item.Longitude, stored.Longitude);
        Assert.Equal(item.Country, stored.Country);
        Assert.Equal(item.Iso3, stored.Iso3);
        Assert.Equal(item.Severity, stored.Severity);
        Assert.Equal(item.SeverityUnit, stored.SeverityUnit);
        Assert.Equal(item.AlertLevel, stored.AlertLevel);
        Assert.Equal(item.AlertScore, stored.AlertScore);
        Assert.Equal(item.Tsunami, stored.Tsunami);
        Assert.Equal(item.Significance, stored.Significance);
        Assert.Equal(item.Provenance, stored.Provenance);
        Assert.Equal(item.FetchedUtc, stored.FetchedUtc);
        Assert.Equal(item.Certainty, stored.Certainty);
        Assert.Equal(item.EffectiveUtc, stored.EffectiveUtc);
        Assert.Equal(item.ExpiresUtc, stored.ExpiresUtc);
        Assert.Equal(item.DepthKm, stored.DepthKm);
    }

    [Fact]
    public async Task Reinserting_the_same_source_and_native_id_inserts_zero_rows()
    {
        var store = CreateStore();
        var items = new[]
        {
            DisasterTestSupport.Event("one", Fetched),
            DisasterTestSupport.Event("two", Fetched.AddMinutes(1), sourceId: "other-source"),
        };

        Assert.Equal(2, await store.InsertAsync(items));
        Assert.Equal(0, await store.InsertAsync(items));

        Assert.Equal(2, (await store.GetRecentAsync(DisasterStore.MaxLimit)).Count);
    }

    [Fact]
    public async Task Events_are_newest_first_nulls_last_and_the_limit_is_clamped()
    {
        var store = CreateStore();
        await store.InsertAsync(new[]
        {
            DisasterTestSupport.Event(
                "dated",
                new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero)),
            DisasterTestSupport.Event("undated", null),
        });

        var ordered = await store.GetRecentAsync(10);
        Assert.Equal(new[] { "dated", "undated" }, ordered.Select(item => item.NativeId));
        Assert.Null(ordered[^1].EventTimeUtc);

        var items = Enumerable.Range(0, 205)
            .Select(index => DisasterTestSupport.Event(
                $"event-{index}",
                new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero).AddSeconds(index)))
            .ToArray();
        await store.InsertAsync(items);

        Assert.Equal(DisasterStore.MaxLimit, (await store.GetRecentAsync(500)).Count);
        Assert.Single(await store.GetRecentAsync(0));
        Assert.Single(await store.GetRecentAsync(-10));
    }

    private DisasterStore CreateStore()
    {
        var store = new DisasterStore(_temp.File("events.sqlite"));
        store.Initialize();
        return store;
    }
}
