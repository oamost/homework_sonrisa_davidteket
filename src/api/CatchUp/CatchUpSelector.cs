using System.Globalization;
using WorldAlerts.Api.Disasters;
using WorldAlerts.Api.Events;
using WorldAlerts.Api.Markets;
using WorldAlerts.Api.News;

namespace WorldAlerts.Api.CatchUp;

/// <summary>
/// Pure, clock-injected selector for the 24-hour all-category catch-up. It accepts
/// normalized <see cref="EventRecord"/> values (or the family-specific records,
/// which are mapped through <see cref="EventRecordMappers"/>), filters them by the
/// D3 eligibility rule, deduplicates by provider-native item id, removes ids that
/// were already delivered, orders candidates oldest-eligible-first, and returns
/// explicit coverage gaps per family/source.
/// <para>
/// This type does not send notifications, run timers, start hosted services or
/// pace deliveries. Startup invocation, persistence of the delivered-id set and
/// notification pacing/queueing belong to integration (Deliverable 3 task 19).
/// </para>
/// </summary>
public sealed class CatchUpSelector
{
    private static readonly EventFamily[] DefaultFamilies =
    [
        EventFamily.News,
        EventFamily.Market,
        EventFamily.Disaster,
    ];

    private static readonly IReadOnlySet<string> NoDeliveredKeys =
        new HashSet<string>(StringComparer.Ordinal);

    private readonly TimeProvider _timeProvider;
    private readonly CatchUpWindow _window;
    private readonly CatchUpEligibilityEvaluator _eligibility;

    /// <summary>
    /// Creates a selector. <paramref name="timeProvider"/> supplies the evaluation
    /// instant (defaults to the system clock); <paramref name="window"/> defaults to
    /// the approved 24-hour window.
    /// </summary>
    public CatchUpSelector(TimeProvider? timeProvider = null, CatchUpWindow? window = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _window = window ?? CatchUpWindow.TwentyFourHours;
        _eligibility = new CatchUpEligibilityEvaluator(_window);
    }

    /// <summary>The configured catch-up window.</summary>
    public CatchUpWindow Window => _window;

    /// <summary>
    /// Selects candidates from already-normalized records. <paramref name="deliveredKeys"/>
    /// contains keys (provider-native ids, or the composite fallback keys exposed by
    /// <see cref="CatchUpCandidate.Key"/>) that must not be notified again across
    /// restarts. <paramref name="expectedFamilies"/> defaults to news, markets and
    /// disasters and is used to report empty categories.
    /// </summary>
    public CatchUpSelection Select(
        IEnumerable<EventRecord> records,
        IReadOnlySet<string>? deliveredKeys = null,
        IReadOnlyCollection<EventFamily>? expectedFamilies = null)
    {
        ArgumentNullException.ThrowIfNull(records);

        return SelectCore(
            records,
            deliveredKeys,
            expectedFamilies,
            _timeProvider.GetUtcNow());
    }

    /// <summary>
    /// Maps family-specific records into <see cref="EventRecord"/> values and selects
    /// candidates. Mapping and evaluation use the same injected instant so anomaly
    /// flags and eligibility agree.
    /// </summary>
    public CatchUpSelection Select(
        IEnumerable<NewsItem> news,
        IEnumerable<MarketObservation> markets,
        IEnumerable<DisasterEvent> disasters,
        IReadOnlySet<string>? deliveredKeys = null,
        IReadOnlyCollection<EventFamily>? expectedFamilies = null)
    {
        ArgumentNullException.ThrowIfNull(news);
        ArgumentNullException.ThrowIfNull(markets);
        ArgumentNullException.ThrowIfNull(disasters);

        var nowUtc = _timeProvider.GetUtcNow();
        var records = news
            .Select(item => EventRecordMappers.FromNews(item, nowUtc))
            .Concat(markets.Select(observation => EventRecordMappers.FromMarket(observation, nowUtc)))
            .Concat(disasters.Select(disaster => EventRecordMappers.FromDisaster(disaster, nowUtc)));

        return SelectCore(records, deliveredKeys, expectedFamilies, nowUtc);
    }

    private CatchUpSelection SelectCore(
        IEnumerable<EventRecord> records,
        IReadOnlySet<string>? deliveredKeys,
        IReadOnlyCollection<EventFamily>? expectedFamilies,
        DateTimeOffset nowUtc)
    {
        var families = expectedFamilies is { Count: > 0 }
            ? expectedFamilies.ToHashSet()
            : DefaultFamilies.ToHashSet();
        var materialized = records.ToList();
        var delivered = deliveredKeys ?? NoDeliveredKeys;
        var windowStart = _window.GetStart(nowUtc);

        var eligible = new List<CatchUpCandidate>();
        foreach (var record in materialized)
        {
            if (!families.Contains(record.Family))
            {
                continue;
            }

            var eligibility = _eligibility.Evaluate(record, nowUtc);
            if (!eligibility.IsEligible || eligibility.EligibilityTimestampUtc is not { } timestamp)
            {
                continue;
            }

            var key = BuildKey(record);
            if (delivered.Contains(key))
            {
                continue;
            }

            eligible.Add(new CatchUpCandidate(
                key,
                record.Family,
                record.SourceId,
                record.SourceName,
                record.Category,
                timestamp,
                eligibility.IsLateItem,
                record));
        }

        var candidates = new List<CatchUpCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in eligible
            .OrderBy(item => item.EligibilityTimestampUtc)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .ThenBy(item => item.SourceId, StringComparer.Ordinal))
        {
            if (seen.Add(candidate.Key))
            {
                candidates.Add(candidate);
            }
        }

        return new CatchUpSelection(
            candidates,
            BuildCoverageGaps(materialized, families, windowStart),
            nowUtc,
            windowStart);
    }

    private List<CatchUpCoverageGap> BuildCoverageGaps(
        IReadOnlyList<EventRecord> records,
        IReadOnlySet<EventFamily> families,
        DateTimeOffset windowStart)
    {
        var gaps = new List<CatchUpCoverageGap>();

        foreach (var family in families.OrderBy(value => value))
        {
            var familyRecords = records
                .Where(record => record.Family == family)
                .ToList();

            if (familyRecords.Count == 0)
            {
                gaps.Add(new CatchUpCoverageGap(
                    CatchUpGapKind.EmptyCategory,
                    family,
                    null,
                    $"No stored items were supplied for the {family} family.",
                    null));
                continue;
            }

            foreach (var source in familyRecords
                .GroupBy(record => record.SourceId, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                DateTimeOffset? newest = null;
                var hasProviderTimestamp = false;

                foreach (var record in source)
                {
                    var providerTimestamp = record.PublishedTimeUtc ?? record.EventTimeUtc;
                    if (providerTimestamp is null)
                    {
                        continue;
                    }

                    hasProviderTimestamp = true;
                    if (newest is null || providerTimestamp > newest)
                    {
                        newest = providerTimestamp;
                    }
                }

                if (!hasProviderTimestamp)
                {
                    gaps.Add(new CatchUpCoverageGap(
                        CatchUpGapKind.MissingProviderTimestamps,
                        family,
                        source.Key,
                        $"Source '{source.Key}' supplied no provider event or publication timestamps.",
                        null));
                }
                else if (newest < windowStart)
                {
                    gaps.Add(new CatchUpCoverageGap(
                        CatchUpGapKind.StaleSource,
                        family,
                        source.Key,
                        $"The newest stored item from source '{source.Key}' is older than the "
                            + $"{_window.Duration.TotalHours:0.##}-hour catch-up window.",
                        newest));
                }
            }
        }

        return gaps;
    }

    private static string BuildKey(EventRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.SourceItemId))
        {
            return record.SourceItemId.Trim();
        }

        var instrument = record.Metadata.TryGetValue("instrument", out var value)
            ? value
            : null;

        return string.Join(
            '\u001F',
            record.SourceId,
            record.Family.ToString(),
            record.ProvenanceUrl,
            record.EffectiveTimestampUtc.UtcTicks.ToString(CultureInfo.InvariantCulture),
            instrument ?? string.Empty);
    }
}
