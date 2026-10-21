using System.Globalization;
using WorldAlerts.Api.News;

namespace WorldAlerts.Api.Scoring;

/// <summary>
/// One independent source already identified by the caller as carrying the same
/// story. Deduplication is deliberately outside this scorer.
/// </summary>
/// <param name="SourceId">Source id used for independence and tier lookup.</param>
/// <param name="SourceName">Source display name used for tier lookup and factors.</param>
/// <param name="PublishedUtc">Provider publication time, or null when absent.</param>
public sealed record NewsCorroboratingSource(
    string SourceId,
    string SourceName,
    DateTimeOffset? PublishedUtc);

/// <summary>
/// Deterministic news importance scorer for the approved acceptance fixtures.
/// </summary>
/// <remarks>
/// The approved examples in
/// <c>docs/decisions/2026-10-21-task-01-acceptance-boundaries.md</c> are:
/// a tier-1 source plus a breaking/conflict/disaster keyword, or two
/// independent tier-1 sources within six hours. The six-hour window and the
/// default tier map are acceptance fixtures, not universal weights. No clock is
/// read inside this class; callers supply <c>nowUtc</c>.
/// <para>
/// Only metadata on <see cref="NewsItem"/> and the caller-supplied corroborating
/// sources is used. The caller is responsible for deciding that corroborating
/// entries describe the same story; this scorer checks independence, tier and
/// publication-time distance only. Missing or future publication times never
/// count as corroboration and are reported as explicit factors.
/// </para>
/// </remarks>
public sealed class NewsImportanceScorer
{
    /// <summary>The approved six-hour corroboration window.</summary>
    public static readonly TimeSpan DefaultCorroborationWindow = TimeSpan.FromHours(6);

    /// <summary>The approved urgency keyword families used by the fixtures.</summary>
    public static readonly IReadOnlyList<string> DefaultUrgencyKeywords = new[]
    {
        "breaking",
        "conflict",
        "war",
        "invasion",
        "airstrike",
        "air strike",
        "missile",
        "hostage",
        "coup",
        "disaster",
        "earthquake",
        "tsunami",
        "flood",
        "cyclone",
        "hurricane",
        "typhoon",
        "wildfire",
        "volcano",
        "drought",
        "landslide",
        "epidemic",
        "pandemic",
    };

    private readonly NewsSourceTiers _sourceTiers;
    private readonly TimeSpan _corroborationWindow;
    private readonly IReadOnlyList<string> _urgencyKeywords;

    /// <summary>Creates a scorer, optionally overriding the approved fixtures.</summary>
    /// <param name="sourceTiers">Source tier map; defaults to <see cref="NewsSourceTiers.Default"/>.</param>
    /// <param name="corroborationWindow">Corroboration window; defaults to six hours.</param>
    /// <param name="urgencyKeywords">Keyword list; defaults to <see cref="DefaultUrgencyKeywords"/>.</param>
    public NewsImportanceScorer(
        NewsSourceTiers? sourceTiers = null,
        TimeSpan? corroborationWindow = null,
        IEnumerable<string>? urgencyKeywords = null)
    {
        _sourceTiers = sourceTiers ?? NewsSourceTiers.Default;
        _corroborationWindow = corroborationWindow ?? DefaultCorroborationWindow;
        if (_corroborationWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(corroborationWindow), "The corroboration window must be positive.");
        }

        _urgencyKeywords = (urgencyKeywords ?? DefaultUrgencyKeywords)
            .Select(ScoringText.Normalize)
            .Where(keyword => keyword.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Scores one news item against the approved rules.
    /// </summary>
    /// <param name="item">Normalized news item; only metadata is read.</param>
    /// <param name="nowUtc">Caller-supplied evaluation time used to reject future timestamps.</param>
    /// <param name="corroboratingSources">
    /// Other sources the caller has already associated with the same story.
    /// This scorer does not deduplicate stories; it only checks source
    /// independence, tier and publication-time distance.
    /// </param>
    public ImportanceDecision Score(
        NewsItem item,
        DateTimeOffset nowUtc,
        IReadOnlyList<NewsCorroboratingSource>? corroboratingSources = null)
    {
        ArgumentNullException.ThrowIfNull(item);

        var factors = new List<string>();
        var sourceLabel = SourceLabel(item.SourceId, item.SourceName);
        var tier = _sourceTiers.ResolveTier(item.SourceId, item.SourceName);
        var isTierOne = string.Equals(tier, NewsSourceTier.TierOne, StringComparison.OrdinalIgnoreCase);
        factors.Add(isTierOne
            ? $"Source '{sourceLabel}' is tier-1."
            : $"Source '{sourceLabel}' is not tier-1 ({(tier ?? "unclassified")}).");

        string? keyword = null;
        if (string.IsNullOrWhiteSpace(item.Title))
        {
            factors.Add("Headline is missing, so no approved urgency keyword can be matched.");
        }
        else
        {
            keyword = ScoringText.FindTokenSequence(
                ScoringText.Normalize(item.Title),
                _urgencyKeywords);
            factors.Add(keyword is null
                ? "Headline contains no approved urgency keyword."
                : $"Headline contains approved urgency keyword '{keyword}'.");
        }

        if (isTierOne && keyword is not null)
        {
            return ImportanceDecision.Important(NewsImportanceRuleIds.TierOneUrgencyKeyword, factors.ToArray());
        }

        if (HasCorroboration(item, nowUtc, corroboratingSources, isTierOne, factors))
        {
            return ImportanceDecision.Important(NewsImportanceRuleIds.TierOneCorroboration, factors.ToArray());
        }

        return ImportanceDecision.NotImportant(NewsImportanceRuleIds.NoApprovedTrigger, factors.ToArray());
    }

    private bool HasCorroboration(
        NewsItem item,
        DateTimeOffset nowUtc,
        IReadOnlyList<NewsCorroboratingSource>? corroboratingSources,
        bool isPrimaryTierOne,
        List<string> factors)
    {
        var publications = new Dictionary<string, (string Label, DateTimeOffset PublishedUtc)>(StringComparer.Ordinal);

        var primaryLabel = SourceLabel(item.SourceId, item.SourceName);
        if (!isPrimaryTierOne)
        {
            factors.Add(
                $"Primary source '{primaryLabel}' is not tier-1, "
                + "so it does not count toward the two-source rule.");

            if (item.PublishedUtc is null)
            {
                factors.Add($"Primary source '{primaryLabel}' has no publication time; it cannot establish corroboration.");
            }
            else if (item.PublishedUtc > nowUtc)
            {
                factors.Add(
                    $"Primary source '{primaryLabel}' has a future publication time "
                    + $"({FormatInstant(item.PublishedUtc.Value)} relative to {FormatInstant(nowUtc)}); excluded.");
            }
        }
        else
        {
            AddPublication(
                publications,
                item.SourceId,
                primaryLabel,
                item.PublishedUtc,
                nowUtc,
                "Primary source",
                factors);
        }

        foreach (var source in corroboratingSources ?? Array.Empty<NewsCorroboratingSource>())
        {
            var label = SourceLabel(source.SourceId, source.SourceName);
            var tier = _sourceTiers.ResolveTier(source.SourceId, source.SourceName);
            if (!string.Equals(tier, NewsSourceTier.TierOne, StringComparison.OrdinalIgnoreCase))
            {
                factors.Add(
                    $"Ignoring corroborating source '{label}' because it is not tier-1 "
                    + $"({(tier ?? "unclassified")}).");
                continue;
            }

            AddPublication(
                publications,
                source.SourceId,
                label,
                source.PublishedUtc,
                nowUtc,
                "Corroborating tier-1 source",
                factors);
        }

        if (publications.Count < 2)
        {
            factors.Add(
                $"Only {publications.Count} independent tier-1 source(s) with a usable "
                + "publication time are available.");
            return false;
        }

        var independent = publications.Values.ToArray();
        TimeSpan? closest = null;
        for (var first = 0; first < independent.Length; first++)
        {
            for (var second = first + 1; second < independent.Length; second++)
            {
                var distance = (independent[first].PublishedUtc - independent[second].PublishedUtc).Duration();
                closest = closest is null || distance < closest ? distance : closest;
                if (distance <= _corroborationWindow)
                {
                    factors.Add(
                        $"{independent.Length} independent tier-1 sources published within "
                        + $"{FormatHours(distance)} ({FormatHours(_corroborationWindow)} window).");
                    return true;
                }
            }
        }

        factors.Add(
            $"Closest independent tier-1 publications are {FormatHours(closest ?? TimeSpan.Zero)} apart, "
            + $"outside the {FormatHours(_corroborationWindow)} window.");
        return false;
    }

    private static void AddPublication(
        Dictionary<string, (string Label, DateTimeOffset PublishedUtc)> publications,
        string? sourceId,
        string label,
        DateTimeOffset? publishedUtc,
        DateTimeOffset nowUtc,
        string role,
        List<string> factors)
    {
        if (publishedUtc is null)
        {
            factors.Add($"{role} '{label}' has no publication time; it cannot establish corroboration.");
            return;
        }

        if (publishedUtc > nowUtc)
        {
            factors.Add(
                $"{role} '{label}' has a future publication time ({FormatInstant(publishedUtc.Value)} "
                + $"relative to {FormatInstant(nowUtc)}); excluded.");
            return;
        }

        var key = ScoringText.Normalize(sourceId);
        if (key.Length == 0)
        {
            key = "(unknown source id)";
        }

        if (publications.ContainsKey(key))
        {
            factors.Add(
                $"Duplicate source id for '{label}' counted once; "
                + "corroboration requires independent sources.");
            return;
        }

        publications.Add(key, (label, publishedUtc.Value));
    }

    private static string SourceLabel(string? sourceId, string? sourceName)
    {
        if (!string.IsNullOrWhiteSpace(sourceName))
        {
            return sourceName;
        }

        return string.IsNullOrWhiteSpace(sourceId) ? "(unknown source)" : sourceId;
    }

    private static string FormatInstant(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string FormatHours(TimeSpan value)
        => value.TotalHours.ToString("0.##", CultureInfo.InvariantCulture) + "h";
}
