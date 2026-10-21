using Microsoft.Extensions.Logging;

namespace WorldAlerts.Api.Markets;

/// <summary>
/// Fetches all configured market sources once and persists their observations.
/// A failure in one source is logged and contained, so the remaining sources
/// still run. There are no retries, and concurrent polls are rejected so a
/// slow source cannot overlap the next cycle.
/// </summary>
public sealed class MarketIngestionService
{
    public const string UserAgent =
        "WorldAlertsMarketBot/1.0 (keyless market observation aggregation; contact: local operator)";

    private readonly HttpClient _httpClient;
    private readonly MarketStore _store;
    private readonly MarketOptions _options;
    private readonly ILogger<MarketIngestionService> _logger;
    private readonly SemaphoreSlim _singleFlight = new(1, 1);

    public MarketIngestionService(
        HttpClient httpClient,
        MarketStore store,
        MarketOptions options,
        ILogger<MarketIngestionService> logger)
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

    public async Task<MarketPollResult> PollOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.PollingEnabled)
        {
            return MarketPollResult.Empty;
        }

        if (!await _singleFlight.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogWarning("A market poll is already running; skipping this overlapping cycle.");
            return MarketPollResult.Empty;
        }

        try
        {
            var results = new List<MarketSourcePollResult>(_options.Sources.Count);
            foreach (var source in _options.Sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(await PollSourceAsync(source, cancellationToken).ConfigureAwait(false));
            }

            return new MarketPollResult(results);
        }
        finally
        {
            _singleFlight.Release();
        }
    }

    private async Task<MarketSourcePollResult> PollSourceAsync(
        MarketSource source,
        CancellationToken cancellationToken)
    {
        var fetchedUtc = DateTimeOffset.UtcNow;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, source.Url);
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body))
            {
                _logger.LogWarning(
                    "Market source {SourceId} returned an empty 2xx body; treating it as zero observations.",
                    source.Id);
                return new MarketSourcePollResult(source.Id, 0, 0, 0, 0, true, null);
            }

            var parseResult = MarketParser.Parse(body, source, fetchedUtc);
            var inserted = await _store.InsertAsync(parseResult.Items, cancellationToken).ConfigureAwait(false);
            var duplicates = parseResult.Items.Count - inserted;

            _logger.LogInformation(
                "Market source {SourceId} polled: parsed {Parsed}, inserted {Inserted}, duplicates {Duplicates}, skipped {Skipped}.",
                source.Id,
                parseResult.Items.Count,
                inserted,
                duplicates,
                parseResult.Skipped);

            return new MarketSourcePollResult(
                source.Id,
                parseResult.Items.Count,
                inserted,
                duplicates,
                parseResult.Skipped,
                false,
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Market source {SourceId} failed with {ExceptionType}.",
                source.Id,
                exception.GetType().Name);

            return new MarketSourcePollResult(source.Id, 0, 0, 0, 0, false, exception.GetType().Name);
        }
    }
}
