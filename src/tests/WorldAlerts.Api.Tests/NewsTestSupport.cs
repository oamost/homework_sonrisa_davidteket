using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using WorldAlerts.Api.News;

namespace WorldAlerts.Api.Tests;

internal static class NewsTestSupport
{
    public static readonly NewsFeed Feed = new()
    {
        Id = "test-feed",
        Name = "Test Feed",
        Url = "https://news.test/rss.xml",
        Family = "world-news",
    };

    public static NewsFeed CreateFeed(string id, string url) => new()
    {
        Id = id,
        Name = $"{id} display name",
        Url = url,
        Family = "world-news",
    };

    public static NewsItem Item(
        string nativeId,
        DateTimeOffset firstSeenUtc,
        DateTimeOffset? publishedUtc = null,
        string title = "Test story",
        string? link = null,
        string sourceId = "test-feed",
        string sourceName = "Test Feed",
        string family = "world-news",
        IReadOnlyList<string>? categories = null,
        string? summary = null) =>
        new(
            SourceId: sourceId,
            SourceName: sourceName,
            NativeId: nativeId,
            Title: title,
            Link: link ?? $"https://news.test/{nativeId}",
            PublishedUtc: publishedUtc,
            FirstSeenUtc: firstSeenUtc,
            Family: family,
            Categories: categories ?? Array.Empty<string>(),
            Provenance: "https://news.test/rss.xml",
            Summary: summary);

    public static string Rss(params string[] items) =>
        $"""
        <?xml version="1.0" encoding="utf-8"?>
        <rss version="2.0" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:content="http://purl.org/rss/1.0/modules/content/">
          <channel>
            <title>Test feed</title>
            {string.Concat(items)}
          </channel>
        </rss>
        """;

    public static NewsIngestionService Ingest(
        NewsStore store,
        NewsOptions options,
        FakeHttpHandler handler)
    {
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        return new NewsIngestionService(client, store, options, NullLogger<NewsIngestionService>.Instance);
    }
}

internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        _responder = responder;

    public int CallCount { get; private set; }

    public List<string> RequestedUris { get; } = new();

    public string? LastUserAgent { get; private set; }

    public static HttpResponseMessage Xml(string xml) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(xml, Encoding.UTF8, "application/rss+xml"),
    };

    public static HttpResponseMessage Failure() => new(HttpStatusCode.InternalServerError)
    {
        Content = new StringContent("feed unavailable"),
    };

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        CallCount++;
        RequestedUris.Add(request.RequestUri?.ToString() ?? string.Empty);
        LastUserAgent = request.Headers.UserAgent.ToString();
        return Task.FromResult(_responder(request));
    }
}

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"world-alerts-news-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup of a test-owned temporary directory.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup of a test-owned temporary directory.
        }
    }
}
