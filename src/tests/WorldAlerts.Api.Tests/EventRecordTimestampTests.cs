using WorldAlerts.Api.Events;

namespace WorldAlerts.Api.Tests;

/// <summary>
/// Unit tests for timestamp precedence and anomaly flags on the normalized event
/// record. Alert eligibility and scoring are deliberately out of scope for task 04.
/// </summary>
public sealed class EventRecordTimestampTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private static EventRecord Record(
        DateTimeOffset? eventTimeUtc,
        DateTimeOffset? publishedTimeUtc,
        DateTimeOffset fetchedUtc,
        DateTimeOffset? nowUtc = null) =>
        EventRecord.Create(
            family: EventFamily.Disaster,
            sourceId: "src",
            sourceName: "Source",
            category: null,
            eventType: "earthquake",
            region: null,
            country: null,
            iso3: null,
            eventTimeUtc: eventTimeUtc,
            publishedTimeUtc: publishedTimeUtc,
            updatedTimeUtc: null,
            fetchedUtc: fetchedUtc,
            latitude: null,
            longitude: null,
            severity: null,
            severityUnit: null,
            sourceItemId: null,
            provenanceUrl: "https://example.test/feed",
            nowUtc: nowUtc ?? Now);

    [Fact]
    public void Effective_timestamp_prefers_the_event_time()
    {
        var record = Record(
            eventTimeUtc: Now.AddHours(-2),
            publishedTimeUtc: Now.AddHours(-1),
            fetchedUtc: Now);

        Assert.Equal(Now.AddHours(-2), record.EffectiveTimestampUtc);
        Assert.Equal(TimestampBasis.EventTime, record.EffectiveTimestampBasis);
        Assert.Equal(EventTimestampAnomalies.None, record.Anomalies);
    }

    [Fact]
    public void Effective_timestamp_uses_publication_when_event_time_is_missing()
    {
        var record = Record(
            eventTimeUtc: null,
            publishedTimeUtc: Now.AddHours(-1),
            fetchedUtc: Now);

        Assert.Equal(Now.AddHours(-1), record.EffectiveTimestampUtc);
        Assert.Equal(TimestampBasis.PublishedTime, record.EffectiveTimestampBasis);
        Assert.Equal(EventTimestampAnomalies.None, record.Anomalies);
    }

    [Fact]
    public void Effective_timestamp_falls_back_to_fetch_when_provider_times_are_missing()
    {
        var record = Record(
            eventTimeUtc: null,
            publishedTimeUtc: null,
            fetchedUtc: Now);

        Assert.Equal(Now, record.EffectiveTimestampUtc);
        Assert.Equal(TimestampBasis.FetchedTime, record.EffectiveTimestampBasis);
        Assert.Equal(EventTimestampAnomalies.MissingProviderTimes, record.Anomalies);
    }

    [Fact]
    public void Publication_after_fetch_is_flagged_without_changing_precedence()
    {
        var record = Record(
            eventTimeUtc: null,
            publishedTimeUtc: Now.AddMinutes(30),
            fetchedUtc: Now,
            nowUtc: Now.AddHours(1));

        Assert.Equal(Now.AddMinutes(30), record.EffectiveTimestampUtc);
        Assert.Equal(TimestampBasis.PublishedTime, record.EffectiveTimestampBasis);
        Assert.Equal(EventTimestampAnomalies.PublishedAfterFetched, record.Anomalies);
    }

    [Fact]
    public void Event_after_publication_is_flagged_and_both_times_are_kept()
    {
        var eventTime = Now.AddHours(-1);
        var published = Now.AddHours(-2);

        var record = Record(
            eventTimeUtc: eventTime,
            publishedTimeUtc: published,
            fetchedUtc: Now);

        Assert.Equal(eventTime, record.EventTimeUtc);
        Assert.Equal(published, record.PublishedTimeUtc);
        Assert.Equal(eventTime, record.EffectiveTimestampUtc);
        Assert.Equal(EventTimestampAnomalies.EventAfterPublished, record.Anomalies);
    }

    [Fact]
    public void Future_event_time_is_flagged()
    {
        var record = Record(
            eventTimeUtc: Now.AddHours(1),
            publishedTimeUtc: null,
            fetchedUtc: Now);

        Assert.Equal(EventTimestampAnomalies.FutureTimestamp, record.Anomalies);
    }

    [Fact]
    public void Future_publication_time_is_flagged_alongside_publication_after_fetch()
    {
        var record = Record(
            eventTimeUtc: null,
            publishedTimeUtc: Now.AddHours(1),
            fetchedUtc: Now);

        Assert.Equal(
            EventTimestampAnomalies.FutureTimestamp | EventTimestampAnomalies.PublishedAfterFetched,
            record.Anomalies);
    }
}
