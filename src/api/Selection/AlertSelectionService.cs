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

    public async Task<AlertSelectionResponse> SelectAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var news = await newsStore.GetForSelectionAsync(cancellationToken);
        var markets = await marketStore.GetForSelectionAsync(cancellationToken);
        var disasters = await disasterStore.GetForSelectionAsync(cancellationToken);
        var subscriptions = (await subscriptionStore.ListAsync(Subscription.DefaultUserId, cancellationToken: cancellationToken))
            .Where(s => s.Enabled && (s.Channel is "email" or "slack")).ToArray();
        var results = new List<AlertSelectionEvent>();

        foreach (var item in news)
        {
            // No persisted verified same-story association exists; matching headlines or source names
            // cannot safely establish independent corroboration.
            var importance = _newsScorer.Score(item, now);
            var types = NewsTypes(item);
            results.Add(Build(EventRecordMappers.FromNews(item, now), item.Title, item.Summary,
                item.Link, types, importance, subscriptions, now,
                "No verified same-story association is persisted; corroboration was not asserted."));
        }

        foreach (var item in disasters)
        {
            var types = DisasterTypes(item);
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
            results.Add(Build(EventRecordMappers.FromMarket(item, now),
                $"{item.Instrument} at {item.Venue}",
                $"{item.Instrument}: {item.Value} {item.QuoteUnit} ({item.Interval}; {item.DelaySemantics})",
                item.Provenance, types, importance, subscriptions, now));
        }

        var selected = results.Where(result => result.Selected).ToArray();
        var rejected = results.Where(result => !result.Selected).Take(RejectedDisplayLimit).ToArray();
        return new AlertSelectionResponse(now, selected.Concat(rejected).ToArray(),
            results.Count - selected.Length - rejected.Length,
            new[]
            {
                "Read-only preview; no delivery, delivered-state persistence, or pacing is performed.",
                "Corroboration is unavailable without persisted verified same-story associations; no fuzzy headline grouping is used.",
                "Market baselines require exact 24-hour history for the same source, instrument, quote unit and interval; missing or offset history is not interpolated.",
                "Only configured email and Slack channels are previewed; no destination or credential is included.",
                "Source coverage remains incomplete: regional-only heat/cold, no Africa-only feed, tsunami/landslide standalone feeds unproven, WHO feed dropped.",
                "All stored rows are evaluated without the public display cap; rejected preview is limited to 100 with omitted count reported.",
            });
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

    private static string[] NewsTypes(NewsItem item)
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "conflict", "politics", "diplomacy", "tech.cyber", "tech.ai", "tech.acquisitions",
            "tech.critical-infrastructure", "environment.energy", "science.transformative",
        };
        return new[] { "world-news" }.Concat(new[] { item.Family }.Concat(item.Categories)
            .Where(known.Contains)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string[] DisasterTypes(DisasterEvent item)
    {
        var hazard = item.EventType.Trim().ToLowerInvariant() switch
        {
            "earthquake" or "eq" => "disaster.earthquake",
            "flood" or "fl" => "disaster.flood",
            "cyclone" or "tc" => "disaster.cyclone",
            "wildfire" or "fire" or "wf" => "disaster.wildfire",
            "volcano" or "vo" => "disaster.volcano",
            "drought" or "dr" => "disaster.drought",
            "tsunami" => "disaster.tsunami",
            "landslide" or "ls" => "disaster.landslide",
            "epidemic" => "disaster.epidemic",
            "humanitarian" => "health.humanitarian",
            _ => null,
        };
        return new[] { hazard, item.Tsunami is true ? "disaster.tsunami" : null }
            .OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
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
    IReadOnlyList<AlertSelectionEvent> Events, int OmittedRejectedCount, IReadOnlyList<string> Limitations);
