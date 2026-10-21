using WorldAlerts.Api.Queue;

namespace WorldAlerts.Api.Selection;

/// <summary>Public read-only cross-family preview, independent of subscriptions and delivery.</summary>
public sealed class ImportantNewsService(AlertSelectionService selection, TestEventService? tests = null)
{
    private const int DisplayLimit = 100;

    public async Task<ImportantNewsResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        var preview = await selection.PreviewPublicAsync(cancellationToken);
        var eligible = preview.Events.Where(e => e.Eligibility.IsEligible && e.Importance.IsImportant).ToArray();
        var realItems = eligible.Take(DisplayLimit).Select(e => new ImportantNewsItem(
            e.Title, e.Summary, e.SourceId, e.SourceName, e.SourceItemId,
            e.Link ?? e.ProvenanceUrl, e.ProvenanceUrl, e.PublishedTimeUtc,
            e.Importance.RuleId, e.Importance.Factors, e.Family, e.EventTypes,
            e.EventTimeUtc, e.Importance.Score)).ToArray();
        var synthetic = tests is null ? [] : await tests.RecentAsync(cancellationToken);
        var displayed = synthetic.Concat(realItems).Take(DisplayLimit).ToArray();
        return new ImportantNewsResponse(preview.EvaluatedUtc, displayed,
            eligible.Length - displayed.Length, preview.Limitations,
            preview.MissingMarketBaselineCount, preview.OmittedRejectedCount,
            preview.FamilyDiagnostics ?? []);
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
