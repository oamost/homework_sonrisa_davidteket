using WorldAlerts.Api.CatchUp;
using WorldAlerts.Api.Disasters;
using WorldAlerts.Api.Events;
using WorldAlerts.Api.Markets;
using WorldAlerts.Api.News;
using WorldAlerts.Api.Scoring;
using WorldAlerts.Api.Subscriptions;

namespace WorldAlerts.Api.Selection;

/// <summary>Read-only runtime preview. Never sends, queues, or marks an event delivered.</summary>
public sealed class AlertSelectionService(
    NewsStore newsStore, MarketStore marketStore, DisasterStore disasterStore,
    SubscriptionStore subscriptionStore, MarketOptions marketOptions, TimeProvider clock)
{
    private const int RejectedDisplayLimit = 100;
    private readonly CatchUpEligibilityEvaluator _eligibility = new();
    private readonly NewsImportanceScorer _newsScorer = new();
    private readonly MarketImportanceScorer _marketScorer = new();
    private readonly DisasterImportanceScorer _disasterScorer = new();

    /// <summary>
    /// Rules that could not be evaluated because the provider data was missing or unusable.
    /// These are counted apart from events that were evaluated and simply did not meet the
    /// approved rule, so an empty family can be explained without guessing.
    /// </summary>
    private static readonly HashSet<string> UnscorableRuleIds = new(StringComparer.Ordinal)
    {
        MarketImportanceRuleIds.MissingBaseline,
        MarketImportanceRuleIds.InvalidBaseline,
        MarketImportanceRuleIds.InvalidInterval,
        "market.unknown-source-kind",
    };

    public async Task<AlertSelectionResponse> SelectAsync(CancellationToken cancellationToken = default)
    {
        var subscriptions = (await subscriptionStore.ListAsync(Subscription.DefaultUserId, cancellationToken: cancellationToken))
            .Where(s => s.Enabled && (s.Channel is "email" or "slack")).ToArray();
        return await EvaluateAsync(subscriptions, false, cancellationToken);
    }

    /// <summary>Identical importance/eligibility pipeline, without reading any subscriptions.</summary>
    public Task<AlertSelectionResponse> PreviewPublicAsync(CancellationToken cancellationToken = default)
        => EvaluateAsync([], true, cancellationToken);

    private async Task<AlertSelectionResponse> EvaluateAsync(Subscription[] subscriptions, bool publicPreview,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var news = await newsStore.GetForSelectionAsync(cancellationToken);
        var markets = await marketStore.GetForSelectionAsync(cancellationToken);
        var disasters = await disasterStore.GetForSelectionAsync(cancellationToken);
        var results = new List<AlertSelectionEvent>();
        var missingEligibleMarketBaselines = 0;

        foreach (var item in news)
        {
            var importance = _newsScorer.Score(item, now, NewsStoryCorroboration.For(item, news));
            var types = CanonicalEventTypes.News(item);
            results.Add(Build(EventRecordMappers.FromNews(item, now), item.Title, item.Summary,
                item.Link, types, importance, subscriptions, now,
                "Corroboration uses exact normalized distinctive titles within six hours; distinct headlines are not merged."));
        }

        foreach (var item in disasters)
        {
            var types = CanonicalEventTypes.Disaster(item);
            results.Add(Build(EventRecordMappers.FromDisaster(item, now), item.Title, null,
                item.Provenance, types, _disasterScorer.Score(item), subscriptions, now));
        }

        foreach (var item in markets)
        {
            var configured = marketOptions.Sources.FirstOrDefault(s =>
                string.Equals(s.Id, item.SourceId, StringComparison.OrdinalIgnoreCase));
            var family = MarketObservationMapping.FamilyForSourceKind(configured?.Kind);
            var types = family is { } f ? new[] { f switch
            {
                MarketFamily.Crypto => "market.crypto", MarketFamily.Fx => "market.fx", _ => "market.indices",
            } } : Array.Empty<string>();
            var interval = MarketObservationMapping.TryParseInterval(item.Interval, out var parsed)
                ? parsed : TimeSpan.Zero;
            // Exact same instrument/source/quote/interval and exact 24-hour timestamp.
            // Never infer a baseline from a 1h candle, a different source, or an adjacent day.
            var baseline = interval == TimeSpan.FromHours(24)
                ? markets.FirstOrDefault(other =>
                    string.Equals(other.SourceId, item.SourceId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(other.Instrument, item.Instrument, StringComparison.Ordinal)
                    && string.Equals(other.QuoteUnit, item.QuoteUnit, StringComparison.Ordinal)
                    && string.Equals(other.Interval, item.Interval, StringComparison.OrdinalIgnoreCase)
                    && other.ObservationUtc == item.ObservationUtc - interval)
                : null;
            var importance = family is { } marketFamily
                ? _marketScorer.Score(MarketObservationMapping.Create(marketFamily, item,
                    baseline?.Value, now, interval))
                : ImportanceDecision.NotImportant("market.unknown-source-kind",
                    "No configured approved market source kind maps this observation to a scoring family.");
            var result = Build(EventRecordMappers.FromMarket(item, now),
                $"{item.Instrument} at {item.Venue}",
                $"{item.Instrument}: {item.Value} {item.QuoteUnit} ({item.Interval}; {item.DelaySemantics})",
                item.Provenance, types, importance, subscriptions, now);
            results.Add(result);
            if (result.Eligibility.IsEligible && importance.RuleId == MarketImportanceRuleIds.MissingBaseline
                && interval == TimeSpan.FromHours(24)) missingEligibleMarketBaselines++;
        }

        var selected = results.Where(result => publicPreview
            ? result.Eligibility.IsEligible && result.Importance.IsImportant : result.Selected).ToArray();
        var rejected = results.Where(result => !selected.Contains(result)).Take(RejectedDisplayLimit).ToArray();
        var diagnostics = FamilyDiagnostics(results, missingEligibleMarketBaselines);
        return new AlertSelectionResponse(now, selected.Concat(rejected).ToArray(),
            results.Count - selected.Length - rejected.Length,
            Limitations(diagnostics),
            missingEligibleMarketBaselines,
            diagnostics);
    }

    /// <summary>
    /// Per-family counts that separate a family with no important event into the two different
    /// reasons: events that were evaluated and rejected, and events that could not be scored at
    /// all because the provider data was missing or unusable.
    /// </summary>
    private static IReadOnlyList<SelectionFamilyDiagnostics> FamilyDiagnostics(
        IReadOnlyList<AlertSelectionEvent> results, int missingEligibleMarketBaselines)
        => Enum.GetValues<EventFamily>()
            .Where(f => f is EventFamily.News or EventFamily.Disaster or EventFamily.Market)
            .Select(f =>
            {
                var family = f.ToString();
                var scoped = results.Where(r => r.Family == family).ToArray();
                var eligible = scoped.Where(r => r.Eligibility.IsEligible).ToArray();
                var important = eligible.Where(r => r.Importance.IsImportant).ToArray();
                var ruleCounts = scoped.GroupBy(r => r.Importance.RuleId)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => new SelectionRuleCount(
                        group.Key,
                        group.Count(),
                        group.Count(r => r.Eligibility.IsEligible),
                        group.Count(r => r.Eligibility.IsEligible && r.Importance.IsImportant)))
                    .ToArray();
                return new SelectionFamilyDiagnostics(
                    family,
                    scoped.Length,
                    eligible.Length,
                    important.Length,
                    f == EventFamily.Market ? missingEligibleMarketBaselines : 0,
                    eligible.Length - important.Length,
                    eligible.Count(r => UnscorableRuleIds.Contains(r.Importance.RuleId)),
                    ruleCounts);
            })
            .ToArray();

    private static IReadOnlyList<string> Limitations(IReadOnlyList<SelectionFamilyDiagnostics> diagnostics)
    {
        var limitations = new List<string>
        {
            "Read-only preview; no delivery, delivered-state persistence, or pacing is performed.",
            "Corroboration requires exact normalized distinctive headlines within six hours; related or rewritten headlines are not merged. Reused identical headlines can falsely merge unrelated reports.",
            "Market baselines require exact 24-hour history for the same source, instrument, quote unit and interval; missing or offset history is not interpolated.",
            "Only configured email and Slack channels are previewed; no destination or credential is included.",
            "Source coverage remains incomplete: regional-only heat/cold, no Africa-only feed, tsunami/landslide standalone feeds unproven, WHO feed dropped.",
            "All stored rows are evaluated without the public display cap; rejected preview is limited to 100 with omitted count reported.",
        };
        foreach (var diagnostic in diagnostics.Where(d => d.ImportantEligibleCount == 0))
        {
            limitations.Add(
                $"{diagnostic.Family}: no eligible important event. "
                + $"{diagnostic.EvaluatedCount} evaluated, {diagnostic.EligibleCount} eligible, "
                + $"{diagnostic.EligibleNotImportantCount} eligible but not important "
                + $"({diagnostic.UnscorableCount} of them could not be scored from provider data), "
                + $"{diagnostic.EvaluatedCount - diagnostic.EligibleCount} outside the 24-hour provider-time window.");
        }

        return limitations;
    }

    private AlertSelectionEvent Build(EventRecord record, string title, string? summary, string? link,
        IReadOnlyList<string> types, ImportanceDecision importance, Subscription[] subscriptions,
        DateTimeOffset now, string? note = null)
    {
        var eligibility = _eligibility.Evaluate(record, now);
        var matches = eligibility.IsEligible && importance.IsImportant
            ? subscriptions.Where(s => types.Contains(s.EventType, StringComparer.OrdinalIgnoreCase))
                .Select(s => new SelectionMatch(s.Id, s.EventType, s.Channel,
                    "Enabled subscription matches the approved event type and configured channel."))
                .ToArray()
            : Array.Empty<SelectionMatch>();
        var reason = !eligibility.IsEligible ? $"Excluded: {eligibility.Status}."
            : !importance.IsImportant ? $"Not important: {importance.RuleId}."
            : matches.Length == 0 ? "Important and eligible but no enabled matching subscription."
            : "Important, eligible and matched to enabled subscriptions (preview only).";
        return new AlertSelectionEvent(record.Family.ToString(), record.SourceId, record.SourceName,
            record.SourceItemId, record.ProvenanceUrl, title, summary, link,
            record.EventTimeUtc, record.PublishedTimeUtc, record.FetchedUtc,
            types, new SelectionEligibility(eligibility.Status.ToString(), eligibility.EligibilityTimestampUtc,
                eligibility.IsLateItem, eligibility.IsEligible), importance, matches, matches.Length > 0, reason, note);
    }

}

public sealed record SelectionMatch(long SubscriptionId, string EventType, string Channel, string Reason);

public sealed record SelectionEligibility(string Status, DateTimeOffset? EligibilityTimestampUtc,
    bool IsLateItem, bool IsEligible);

public sealed record AlertSelectionEvent(string Family, string SourceId, string SourceName,
    string? SourceItemId, string ProvenanceUrl, string Title, string? Summary, string? Link,
    DateTimeOffset? EventTimeUtc, DateTimeOffset? PublishedTimeUtc, DateTimeOffset FetchedUtc,
    IReadOnlyList<string> EventTypes, SelectionEligibility Eligibility, ImportanceDecision Importance,
    IReadOnlyList<SelectionMatch> Matches, bool Selected, string MatchReason, string? Limitation);

public sealed record AlertSelectionResponse(DateTimeOffset EvaluatedUtc,
    IReadOnlyList<AlertSelectionEvent> Events, int OmittedRejectedCount, IReadOnlyList<string> Limitations,
    int MissingMarketBaselineCount = 0, IReadOnlyList<SelectionFamilyDiagnostics>? FamilyDiagnostics = null);

public sealed record SelectionFamilyDiagnostics(string Family, int EvaluatedCount, int EligibleCount,
    int ImportantEligibleCount, int MissingBaselineCount,
    int EligibleNotImportantCount = 0, int UnscorableCount = 0,
    IReadOnlyList<SelectionRuleCount>? RuleCounts = null);

/// <summary>Counts for one approved importance rule inside one family.</summary>
/// <param name="RuleId">Stable rule identifier reported by the importance decision.</param>
/// <param name="EvaluatedCount">Evaluated events that reached this rule.</param>
/// <param name="EligibleCount">Evaluated events inside the 24-hour provider-time window.</param>
/// <param name="ImportantEligibleCount">Eligible events this rule marked important.</param>
public sealed record SelectionRuleCount(string RuleId, int EvaluatedCount, int EligibleCount,
    int ImportantEligibleCount);
