namespace WorldAlerts.Api.News;

/// <summary>
/// One normalized news story. Publication time is absent when the feed did not
/// provide a usable date; it is never invented. First-seen time is always the
/// collection time and is stored separately.
/// </summary>
public sealed record NewsItem(
    string SourceId,
    string SourceName,
    string NativeId,
    string Title,
    string Link,
    DateTimeOffset? PublishedUtc,
    DateTimeOffset FirstSeenUtc,
    string Family,
    IReadOnlyList<string> Categories,
    string Provenance);

/// <summary>Outcome of parsing one RSS document.</summary>
public sealed record RssParseResult(IReadOnlyList<NewsItem> Items, int Skipped);
