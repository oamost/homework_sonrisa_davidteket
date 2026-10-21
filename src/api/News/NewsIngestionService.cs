using Microsoft.Extensions.Logging;

namespace WorldAlerts.Api.News;

/// <summary>
/// Fetches all configured feeds once and persists their stories. A failure in one
/// feed is logged and contained, so the remaining feeds still run. There are no
/// retries in this task.
/// </summary>
public sealed class NewsIngestionService
{
    public const string UserAgent =
        "WorldAlertsNewsBot/1.0 (keyless RSS news aggregation; contact: local operator)";

    private readonly HttpClient _httpClient;
    private readonly NewsStore _store;
    private readonly NewsOptions _options;
    private readonly ILogger<NewsIngestionService> _logger;

    public NewsIngestionService(
        HttpClient httpClient,
        NewsStore store,
        NewsOptions options,
        ILogger<NewsIngestionService> logger)
    {
        _httpClient = httpClient;
        _store = store;
        _options = options;
        _logger = logger;

        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        }
    }

    public async Task<NewsPollResult> PollOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.PollingEnabled)
        {
            return NewsPollResult.Empty;
        }

        var firstSeenUtc = DateTimeOffset.UtcNow;
        var results = new List<FeedPollResult>(_options.Feeds.Count);
        foreach (var feed in _options.Feeds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await PollFeedAsync(feed, firstSeenUtc, cancellationToken).ConfigureAwait(false));
        }

        return new NewsPollResult(results);
    }

    private async Task<FeedPollResult> PollFeedAsync(
        NewsFeed feed,
        DateTimeOffset firstSeenUtc,
        CancellationToken cancellationToken)
    {
        var parsed = 0;
        var skipped = 0;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, feed.Url);
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var xml = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var parseResult = RssNewsParser.Parse(xml, feed, firstSeenUtc);
            parsed = parseResult.Items.Count;
            skipped = parseResult.Skipped;

            var inserted = await _store.InsertAsync(parseResult.Items, cancellationToken).ConfigureAwait(false);
            var duplicates = parsed - inserted;

            _logger.LogInformation(
                "News feed {FeedId} polled: parsed {Parsed}, inserted {Inserted}, duplicates {Duplicates}, skipped {Skipped}.",
                feed.Id,
                parsed,
                inserted,
                duplicates,
                skipped);

            return new FeedPollResult(feed.Id, parsed, inserted, duplicates, skipped, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "News feed {FeedId} failed with {ExceptionType}: parsed {Parsed}, inserted {Inserted}, skipped {Skipped}.",
                feed.Id,
                exception.GetType().Name,
                parsed,
                0,
                skipped);

            return new FeedPollResult(feed.Id, parsed, 0, 0, skipped, exception.GetType().Name);
        }
    }
}
