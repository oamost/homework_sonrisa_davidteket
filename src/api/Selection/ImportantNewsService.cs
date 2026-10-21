using WorldAlerts.Api.Queue;
using WorldAlerts.Api.Subscriptions;

namespace WorldAlerts.Api.Selection;

/// <summary>Public read-only cross-family preview, independent of subscriptions and delivery.</summary>
public sealed class ImportantNewsService(AlertSelectionService selection, TestEventService? tests = null)
{
    private const int DisplayLimit = 100;

    public async Task<ImportantNewsResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        var preview = await selection.PreviewPublicAsync(cancellationToken);
        var eligible = preview.Events.Where(e => e.Eligibility.IsEligible && e.Importance.IsImportant)
            .Select(e => new ImportantNewsItem(
                e.Title, e.Summary, e.SourceId, e.SourceName, e.SourceItemId,
                e.Link ?? e.ProvenanceUrl, e.ProvenanceUrl, e.PublishedTimeUtc,
                e.Importance.RuleId, e.Importance.Factors, e.Family, e.EventTypes,
                e.EventTimeUtc, e.Importance.Score)).ToArray();
        var synthetic = tests is null ? [] : await tests.RecentAsync(cancellationToken);
        var slots = Math.Max(0, DisplayLimit - synthetic.Count);
        var realItems = ChooseDiverse(eligible, slots);
        return new ImportantNewsResponse(preview.EvaluatedUtc,
            synthetic.Concat(realItems).Take(DisplayLimit).ToArray(),
            Math.Max(0, eligible.Length - realItems.Count), preview.Limitations,
            preview.MissingMarketBaselineCount, preview.OmittedRejectedCount,
            preview.FamilyDiagnostics ?? []);
    }

    /// <summary>
    /// A fixed feed limit must not hide entire event types just because one large RSS
    /// family was enumerated first. Cover every represented approved code, then rank
    /// remaining events by the existing importance score and provider timestamps.
    /// Nothing is invented to fill a category that has no eligible important events.
    /// </summary>
    public static IReadOnlyList<ImportantNewsItem> ChooseDiverse(
        IReadOnlyList<ImportantNewsItem> eligible, int limit)
    {
        if (limit <= 0 || eligible.Count == 0) return [];
        // Recent provider-published stories should lead the landing page, not
        // yesterday's higher-scoring item. Coverage of event types is preserved.
        var ranked = eligible.OrderByDescending(item => item.PublishedUtc ?? item.EventTimeUtc)
            .ThenByDescending(item => item.ImportanceScore)
            .ThenBy(item => item.SourceId, StringComparer.Ordinal)
            .ThenBy(item => item.SourceItemId, StringComparer.Ordinal).ToArray();
        var output = new List<ImportantNewsItem>(Math.Min(limit, ranked.Length));
        var covered = new HashSet<string>(StringComparer.Ordinal);
        var approved = new HashSet<string>(EventTypePreferenceStore.ApprovedTypes, StringComparer.Ordinal);
        var selected = new HashSet<ImportantNewsItem>();

        // A single event can cover multiple approved types. Prefer candidates adding
        // the most uncovered types, and preserve scorer rank when the count ties.
        while (output.Count < limit)
        {
            ImportantNewsItem? best = null;
            var bestNew = 0;
            foreach (var candidate in ranked)
            {
                if (selected.Contains(candidate)) continue;
                var newTypes = candidate.EventTypes.Count(code => approved.Contains(code) && !covered.Contains(code));
                if (newTypes > bestNew) { best = candidate; bestNew = newTypes; }
            }
            if (best is null) break;
            selected.Add(best);
            output.Add(best);
            foreach (var code in best.EventTypes) if (approved.Contains(code)) covered.Add(code);
        }
        // Also keep represented families with no approved type (if ever produced).
        foreach (var family in new[] { "News", "Disaster", "Market" })
        {
            if (output.Count == limit) break;
            if (output.Any(item => item.Family == family)) continue;
            var representative = ranked.FirstOrDefault(item => item.Family == family && !selected.Contains(item));
            if (representative is not null) { selected.Add(representative); output.Add(representative); }
        }
        foreach (var item in ranked)
        {
            if (output.Count == limit) break;
            if (selected.Add(item)) output.Add(item);
        }
        return output;
    }
}

public sealed record ImportantNewsItem(string Title, string? Summary, string SourceId,
    string SourceName, string? SourceItemId, string Link, string ProvenanceUrl,
    DateTimeOffset? PublishedUtc, string ImportanceRuleId, IReadOnlyList<string> Reasons,
    string Family, IReadOnlyList<string> EventTypes, DateTimeOffset? EventTimeUtc, double ImportanceScore);

public sealed record ImportantNewsResponse(DateTimeOffset EvaluatedUtc,
    IReadOnlyList<ImportantNewsItem> Items, int OmittedCount, IReadOnlyList<string> Limitations,
    int MissingMarketBaselineCount, int OmittedRejectedCount,
    IReadOnlyList<SelectionFamilyDiagnostics> FamilyDiagnostics);
