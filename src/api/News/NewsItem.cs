namespace WorldAlerts.Api.News;

/// <summary>
/// One normalized news story. Publication time is absent when the feed did not
/// provide a usable date; it is never invented. First-seen time is always the
/// collection time and is stored separately.
/// <para>
/// <see cref="Summary"/> is provider-supplied text (typically a feed description),
/// not authored or expanded by this service. It may be absent, and when present it
/// is truncated to <see cref="RssNewsParser.MaxSummaryLength"/> characters. A
/// summary is not an importance judgment, score, or alert decision.
/// </para>
/// </summary>
/// <param name="Summary">
/// Provider description text with HTML markup removed, or <see langword="null"/>
/// when the feed supplied none. May be truncated; not an importance judgment.
/// </param>
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
    string Provenance,
    string? Summary = null);

/// <summary>Outcome of parsing one RSS document.</summary>
public sealed record RssParseResult(IReadOnlyList<NewsItem> Items, int Skipped);
