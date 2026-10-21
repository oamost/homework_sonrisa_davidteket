using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace WorldAlerts.Api.News;

/// <summary>
/// Minimal RSS 2.0 reader. Element lookups use local names so namespaced
/// extensions such as <c>dc:date</c> and <c>content:encoded</c> are accepted;
/// values are trimmed and XML entities/CDATA are decoded by <see cref="XDocument"/>.
/// </summary>
public static class RssNewsParser
{
    /// <summary>
    /// Maximum stored summary length. Provider descriptions are plain text after
    /// markup removal and may be truncated at this many characters. Truncation is
    /// not an importance judgment.
    /// </summary>
    public const int MaxSummaryLength = 1000;

    // Checked in order. "date" also covers dc:date because matching is by local name.
    private static readonly string[] DateElementNames = { "pubDate", "date", "published", "updated" };

    // "encoded" covers content:encoded because matching is by local name.
    private static readonly string[] SummaryElementNames = { "description", "summary", "encoded" };

    private static readonly Regex HtmlTagPattern = new("<[^>]*>", RegexOptions.Compiled);
    private static readonly Regex NonContentPattern = new(
        @"<\s*(script|style)\b[^>]*>.*?<\s*/\s*\1\s*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled);

    public static RssParseResult Parse(string xml, NewsFeed feed, DateTimeOffset firstSeenUtc)
    {
        ArgumentNullException.ThrowIfNull(xml);
        ArgumentNullException.ThrowIfNull(feed);

        var document = XDocument.Parse(xml, LoadOptions.None);
        var items = new List<NewsItem>();
        var skipped = 0;

        foreach (var element in document.Descendants().Where(candidate => IsNamed(candidate, "item")))
        {
            var title = ChildValue(element, "title") ?? string.Empty;
            var link = ChildValue(element, "link") ?? LinkHref(element) ?? string.Empty;
            var nativeId = ChildValue(element, "guid");
            if (string.IsNullOrWhiteSpace(nativeId))
            {
                nativeId = link;
            }

            // A story must have at least a title or a link, and some stable native id.
            if ((string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(link))
                || string.IsNullOrWhiteSpace(nativeId))
            {
                skipped++;
                continue;
            }

            items.Add(new NewsItem(
                SourceId: feed.Id,
                SourceName: feed.Name,
                NativeId: nativeId,
                Title: title,
                Link: link,
                PublishedUtc: FindPublishedUtc(element),
                FirstSeenUtc: firstSeenUtc.ToUniversalTime(),
                Family: feed.Family,
                Categories: ReadCategories(element),
                Provenance: feed.Url,
                Summary: ReadSummary(element)));
        }

        return new RssParseResult(items, skipped);
    }

    private static DateTimeOffset? FindPublishedUtc(XElement item)
    {
        foreach (var name in DateElementNames)
        {
            foreach (var child in item.Elements().Where(candidate => IsNamed(candidate, name)))
            {
                if (TryParseDate(child.Value, out var value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static bool TryParseDate(string? text, out DateTimeOffset value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!DateTimeOffset.TryParse(
                text.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                out var parsed))
        {
            return false;
        }

        value = parsed.ToUniversalTime();
        return true;
    }

    private static IReadOnlyList<string> ReadCategories(XElement item) =>
        item.Elements()
            .Where(candidate => IsNamed(candidate, "category"))
            .Select(candidate => candidate.Value.Trim())
            .Where(value => value.Length > 0)
            .ToArray();

    /// <summary>
    /// Reads provider summary text from the first non-empty description, summary,
    /// or content:encoded element. Markup is removed, entities are decoded,
    /// whitespace is collapsed, and the result is capped at
    /// <see cref="MaxSummaryLength"/> characters. Absent text stays null; nothing
    /// is invented.
    /// </summary>
    private static string? ReadSummary(XElement item)
    {
        foreach (var name in SummaryElementNames)
        {
            var raw = item.Elements()
                .FirstOrDefault(candidate => IsNamed(candidate, name))
                ?.Value;
            var cleaned = CleanSummary(raw);
            if (cleaned is not null)
            {
                return cleaned;
            }
        }

        return null;
    }

    private static string? CleanSummary(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var withoutMarkup = HtmlTagPattern.Replace(NonContentPattern.Replace(raw, " "), " ");
        var decoded = WebUtility.HtmlDecode(withoutMarkup);
        // Some feeds escape their markup, so a second pass after entity decoding
        // removes tags that only became visible now.
        var stripped = HtmlTagPattern.Replace(NonContentPattern.Replace(decoded, " "), " ");
        var collapsed = WhitespacePattern.Replace(stripped, " ").Trim();
        if (collapsed.Length == 0)
        {
            return null;
        }

        return collapsed.Length <= MaxSummaryLength
            ? collapsed
            : collapsed[..MaxSummaryLength].TrimEnd();
    }

    private static string? ChildValue(XElement parent, string localName)
    {
        var value = parent.Elements()
            .FirstOrDefault(candidate => IsNamed(candidate, localName))
            ?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static string? LinkHref(XElement item)
    {
        var value = item.Elements()
            .FirstOrDefault(candidate => IsNamed(candidate, "link"))
            ?.Attribute("href")
            ?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static bool IsNamed(XElement element, string localName) =>
        string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase);
}
