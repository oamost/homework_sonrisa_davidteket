using System.Globalization;
using WorldAlerts.Api.Events;

namespace WorldAlerts.Api.Dedupe;

/// <summary>One conservative cross-source cluster of news records.</summary>
/// <param name="CanonicalKey">Stable cluster key (the ordinal-smallest member identity key).</param>
/// <param name="MemberKeys">Identity keys of all members, ordinal-sorted.</param>
/// <param name="Members">Records in the cluster.</param>
public sealed record NewsCluster(
    string CanonicalKey,
    IReadOnlyList<string> MemberKeys,
    IReadOnlyList<EventRecord> Members);

/// <summary>
/// Conservative cross-source reconciliation for news only.
/// <para>
/// Two news records are merged when either their normalized provenance URLs are
/// identical, or their normalized titles are identical, both records have a
/// publication time within six hours, the title is long enough to be distinctive
/// (at least four words and twenty normalized characters), and the records come
/// from different configured sources.
/// </para>
/// <para>
/// Disaster, market and other families are never clustered by title. Records that
/// already share an identity (same source and native id) are grouped by identity
/// but do not cause unrelated records to merge.
/// </para>
/// <para>
/// False-merge risk remains: syndicated headlines can be identical for genuinely
/// different local stories, and URL normalization lowercases paths and drops
/// tracking parameters, so two case-distinct or parameter-distinct pages can
/// compare equal. The rule therefore errs toward leaving records separate when
/// the title is short, the times are missing or far apart, or the sources match.
/// </para>
/// </summary>
public static class NewsReconciler
{
    /// <summary>Maximum publication-time distance for the title rule.</summary>
    public static readonly TimeSpan MaxTitleTimeDistance = TimeSpan.FromHours(6);

    /// <summary>Minimum normalized title length for the title rule.</summary>
    public const int MinTitleLength = 20;

    /// <summary>Minimum word count for the title rule.</summary>
    public const int MinTitleWords = 4;

    /// <summary>Clusters the news records and leaves every other family untouched.</summary>
    public static IReadOnlyList<NewsCluster> Reconcile(IEnumerable<EventRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var news = records
            .Where(record => record is not null && record.Family == EventFamily.News)
            .ToList();
        if (news.Count == 0)
        {
            return Array.Empty<NewsCluster>();
        }

        // Complete-link clustering: every member must agree with every other
        // member; a bridge cannot join two unrelated stories transitively.
        var groups = news
            .OrderBy(record => EventIdentityResolver.Resolve(record).Key, StringComparer.Ordinal)
            .Select(record => new List<EventRecord> { record })
            .ToList();
        for (var left = 0; left < groups.Count; left++)
        {
            for (var right = left + 1; right < groups.Count;)
            {
                if (groups[left].All(a => groups[right].All(b =>
                    EventIdentityResolver.Resolve(a).Key == EventIdentityResolver.Resolve(b).Key
                    || ShouldMerge(a, b))))
                {
                    groups[left].AddRange(groups[right]);
                    groups.RemoveAt(right);
                }
                else
                {
                    right++;
                }
            }
        }

        var clusters = new List<NewsCluster>(groups.Count);
        foreach (var members in groups)
        {
            var keys = members
                .Select(record => EventIdentityResolver.Resolve(record).Key)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();

            clusters.Add(new NewsCluster(
                keys[0],
                keys,
                members
                    .OrderBy(record => record.SourceId, StringComparer.Ordinal)
                    .ThenBy(record => record.FetchedUtc)
                    .ToArray()));
        }

        clusters.Sort(static (left, right) => string.CompareOrdinal(left.CanonicalKey, right.CanonicalKey));
        return clusters;
    }

    /// <summary>
    /// Builds a map from every member identity key to its cluster canonical key.
    /// Singleton clusters are included so callers can resolve a key uniformly.
    /// </summary>
    public static IReadOnlyDictionary<string, string> BuildAliasMap(IEnumerable<EventRecord> records)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var cluster in Reconcile(records))
        {
            foreach (var key in cluster.MemberKeys)
            {
                map[key] = cluster.CanonicalKey;
            }
        }

        return map;
    }

    private static bool ShouldMerge(EventRecord left, EventRecord right)
    {
        if (left.PublishedTimeUtc is not { } leftPublished
            || right.PublishedTimeUtc is not { } rightPublished
            || (leftPublished - rightPublished).Duration() > MaxTitleTimeDistance)
        {
            return false;
        }

        var leftUrl = DedupeUrl.Normalize(left.ProvenanceUrl);
        var rightUrl = DedupeUrl.Normalize(right.ProvenanceUrl);
        if (leftUrl.Length > 0 && string.Equals(leftUrl, rightUrl, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(left.SourceId, right.SourceId, StringComparison.Ordinal))
        {
            return false;
        }

        var leftTitle = TitleOf(left);
        var rightTitle = TitleOf(right);
        if (leftTitle.Length == 0 || !string.Equals(leftTitle, rightTitle, StringComparison.Ordinal))
        {
            return false;
        }

        return leftTitle.Length >= MinTitleLength
            && leftTitle.Count(character => character == ' ') + 1 >= MinTitleWords;
    }

    private static string TitleOf(EventRecord record) =>
        record.Metadata is not null
        && record.Metadata.TryGetValue(EventMaterialFields.TitleKey, out var title)
            ? DedupeText.NormalizeTitle(title)
            : string.Empty;

}
