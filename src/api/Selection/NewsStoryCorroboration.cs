using WorldAlerts.Api.News;
using WorldAlerts.Api.Scoring;

namespace WorldAlerts.Api.Selection;

/// <summary>Conservative exact-title story association; never guesses from related keywords.</summary>
public static class NewsStoryCorroboration
{
    public static IReadOnlyList<NewsCorroboratingSource> For(NewsItem item, IReadOnlyList<NewsItem> candidates)
    {
        var title = ScoringText.Normalize(item.Title);
        if (title.Length < 20 || title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 4
            || item.PublishedUtc is null) return [];
        return candidates.Where(other => !ReferenceEquals(item, other)
                && ScoringText.Normalize(other.Title) == title
                && other.PublishedUtc is { } published
                && (published - item.PublishedUtc.Value).Duration() <= NewsImportanceScorer.DefaultCorroborationWindow)
            .Select(other => new NewsCorroboratingSource(other.SourceId, other.SourceName, other.PublishedUtc))
            .ToArray();
    }
}
