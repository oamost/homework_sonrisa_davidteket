using System.Globalization;
using System.Xml.Linq;

namespace WorldAlerts.Api.News;

/// <summary>
/// Minimal RSS 2.0 reader. Element lookups use local names so namespaced
/// extensions such as <c>dc:date</c> are accepted; values are trimmed and XML
/// entities/CDATA are decoded by <see cref="XDocument"/>.
/// </summary>
public static class RssNewsParser
{
    // Checked in order. "date" also covers dc:date because matching is by local name.
    private static readonly string[] DateElementNames = { "pubDate", "date", "published", "updated" };

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
                Provenance: feed.Url));
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
