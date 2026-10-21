using Microsoft.Extensions.Logging;
using WorldAlerts.Api.Collection;

namespace WorldAlerts.Api.Advisories;

/// <summary>
/// Fetches all configured advisory sources once and persists their rows. A
/// failure in one source is logged and contained, so the remaining sources still
/// run. There are no retries, and concurrent polls are rejected.
/// </summary>
public sealed class AdvisoryIngestionService
{
    public const string UserAgent =
        "WorldAlertsAdvisoryBot/1.0 (keyless cyber-advisory aggregation; contact: local operator)";

    private readonly HttpClient _httpClient;
    private readonly AdvisoryStore _store;
    private readonly AdvisoryOptions _options;
    private readonly ILogger<AdvisoryIngestionService> _logger;
    private readonly SemaphoreSlim _singleFlight = new(1, 1);
    private readonly CollectionStateStore? _progress;
    private readonly TimeProvider _clock;

    public AdvisoryIngestionService(
        HttpClient httpClient,
        AdvisoryStore store,
        AdvisoryOptions options,
        ILogger<AdvisoryIngestionService> logger,
        CollectionStateStore? progress = null,
        TimeProvider? clock = null)
    {
        _httpClient = httpClient;
        _store = store;
        _options = options;
        _logger = logger;
        _progress = progress;
        _clock = clock ?? TimeProvider.System;

        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        }
    }

    public async Task<AdvisoryPollResult> PollOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.PollingEnabled)
        {
            return AdvisoryPollResult.Empty;
        }

        if (!await _singleFlight.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogWarning("An advisory poll is already running; skipping this overlapping cycle.");
            return AdvisoryPollResult.Empty;
        }

        try
        {
            var results = new List<AdvisorySourcePollResult>(_options.Sources.Count);
            foreach (var source in _options.Sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_progress is null)
                    results.Add(await PollSourceAsync(source, cancellationToken).ConfigureAwait(false));
                else
                    await _progress.PollAsync(source.Id, "advisories", SourceCadence.Effective(_options.PollInterval, source.PollMinutes),
                        _clock, async ct =>
                        {
                            var result = await PollSourceAsync(source, ct);
                            results.Add(result);
                            return result.ErrorType;
                        }, cancellationToken);
            }

            return new AdvisoryPollResult(results);
        }
        finally
        {
            _singleFlight.Release();
        }
    }

    private async Task<AdvisorySourcePollResult> PollSourceAsync(
        AdvisorySource source,
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
                    "Advisory source {SourceId} returned an empty 2xx body; treating it as zero advisories.",
                    source.Id);
                return new AdvisorySourcePollResult(source.Id, 0, 0, 0, 0, true, null);
            }

            var parseResult = AdvisoryParser.Parse(body, source, fetchedUtc);
            var inserted = await _store.InsertAsync(parseResult.Items, cancellationToken).ConfigureAwait(false);
            var duplicates = parseResult.Items.Count - inserted;

            _logger.LogInformation(
                "Advisory source {SourceId} polled: parsed {Parsed}, inserted {Inserted}, duplicates {Duplicates}, skipped {Skipped}.",
                source.Id,
                parseResult.Items.Count,
                inserted,
                duplicates,
                parseResult.Skipped);

            return new AdvisorySourcePollResult(
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
                "Advisory source {SourceId} failed with {ExceptionType}.",
                source.Id,
                exception.GetType().Name);

            return new AdvisorySourcePollResult(source.Id, 0, 0, 0, 0, false, exception.GetType().Name);
        }
    }
}
