using System.Globalization;
using Microsoft.Extensions.Logging;

namespace WorldAlerts.Api.Disasters;

/// <summary>
/// Fetches all configured disaster sources once and persists their events. A
/// failure in one source is logged and contained, so the remaining sources still
/// run. There are no retries, and concurrent polls are rejected.
/// </summary>
public sealed class DisasterIngestionService
{
    public const string UserAgent =
        "WorldAlertsDisasterBot/1.0 (keyless hazardous-event aggregation; contact: local operator)";

    private readonly HttpClient _httpClient;
    private readonly DisasterStore _store;
    private readonly DisasterOptions _options;
    private readonly ILogger<DisasterIngestionService> _logger;
    private readonly SemaphoreSlim _singleFlight = new(1, 1);

    public DisasterIngestionService(
        HttpClient httpClient,
        DisasterStore store,
        DisasterOptions options,
        ILogger<DisasterIngestionService> logger)
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

    public async Task<DisasterPollResult> PollOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.PollingEnabled)
        {
            return DisasterPollResult.Empty;
        }

        if (!await _singleFlight.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogWarning("A disaster poll is already running; skipping this overlapping cycle.");
            return DisasterPollResult.Empty;
        }

        try
        {
            var results = new List<DisasterSourcePollResult>(_options.Sources.Count);
            foreach (var source in _options.Sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(await PollSourceAsync(source, cancellationToken).ConfigureAwait(false));
            }

            return new DisasterPollResult(results);
        }
        finally
        {
            _singleFlight.Release();
        }
    }

    private async Task<DisasterSourcePollResult> PollSourceAsync(
        DisasterSource source,
        CancellationToken cancellationToken)
    {
        var fetchedUtc = DateTimeOffset.UtcNow;

        try
        {
            if (string.Equals(source.Kind, DisasterParser.CopernicusEms, StringComparison.Ordinal))
            {
                return await PollEmsAsync(source, fetchedUtc, cancellationToken).ConfigureAwait(false);
            }

            return await PollUrlsAsync(
                source,
                BuildRequestUrls(source),
                fetchedUtc,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Disaster source {SourceId} failed with {ExceptionType}.",
                source.Id,
                exception.GetType().Name);

            return new DisasterSourcePollResult(source.Id, 0, 0, 0, 0, false, exception.GetType().Name);
        }
    }

    private async Task<DisasterSourcePollResult> PollUrlsAsync(
        DisasterSource source,
        IReadOnlyList<string> urls,
        DateTimeOffset fetchedUtc,
        CancellationToken cancellationToken)
    {
        var bodies = new List<string>();
        var anyEmpty = false;
        var successes = 0;
        Exception? firstFailure = null;

        foreach (var url in urls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var body = await FetchBodyAsync(url, cancellationToken).ConfigureAwait(false);
                successes++;
                if (string.IsNullOrWhiteSpace(body))
                {
                    anyEmpty = true;
                    continue;
                }

                bodies.Add(body);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                firstFailure ??= exception;
                _logger.LogWarning(
                    exception,
                    "Disaster source {SourceId} request to {RequestUrl} failed.",
                    source.Id,
                    url);
            }
        }

        if (bodies.Count == 0)
        {
            if (successes == 0 && firstFailure is not null)
            {
                throw firstFailure;
            }

            _logger.LogWarning(
                "Disaster source {SourceId} returned no non-empty 2xx bodies; treating it as zero events.",
                source.Id);
            return new DisasterSourcePollResult(source.Id, 0, 0, 0, 0, anyEmpty, null);
        }

        var parseResult = DisasterParser.ParseMany(bodies, source, fetchedUtc);
        return await StoreAndReportAsync(source, parseResult, false, cancellationToken).ConfigureAwait(false);
    }

    private async Task<DisasterSourcePollResult> PollEmsAsync(
        DisasterSource source,
        DateTimeOffset fetchedUtc,
        CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(source.PageSize, 1, 500);
        var maxPages = Math.Clamp(source.MaxPages, 1, 20);
        var maxItems = Math.Clamp(source.MaxItems, 1, 2000);
        var bodies = new List<string>();
        var anyEmpty = false;
        var successes = 0;
        var parsedSoFar = 0;
        var offset = 0;
        Exception? firstFailure = null;

        for (var page = 0; page < maxPages && parsedSoFar < maxItems; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var url = AppendQuery(
                AppendQuery(source.Url, "limit", pageSize.ToString(CultureInfo.InvariantCulture)),
                "offset",
                offset.ToString(CultureInfo.InvariantCulture));

            try
            {
                var body = await FetchBodyAsync(url, cancellationToken).ConfigureAwait(false);
                successes++;
                if (string.IsNullOrWhiteSpace(body))
                {
                    anyEmpty = true;
                    break;
                }

                bodies.Add(body);
                var pageResult = DisasterParser.Parse(body, source, fetchedUtc);
                parsedSoFar += pageResult.Items.Count;
                if (pageResult.Items.Count < pageSize)
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                firstFailure ??= exception;
                _logger.LogWarning(
                    exception,
                    "Copernicus EMS page {Page} for source {SourceId} failed.",
                    page,
                    source.Id);
                break;
            }

            offset += pageSize;
        }

        if (bodies.Count == 0)
        {
            if (successes == 0 && firstFailure is not null)
            {
                throw firstFailure;
            }

            _logger.LogWarning(
                "Copernicus EMS source {SourceId} returned no non-empty 2xx pages; treating it as zero events.",
                source.Id);
            return new DisasterSourcePollResult(source.Id, 0, 0, 0, 0, anyEmpty, null);
        }

        var parseResult = DisasterParser.ParseMany(bodies, source, fetchedUtc);
        if (parseResult.Items.Count > maxItems)
        {
            parseResult = new DisasterParseResult(
                parseResult.Items.Take(maxItems).ToArray(),
                parseResult.Skipped);
        }

        return await StoreAndReportAsync(source, parseResult, false, cancellationToken).ConfigureAwait(false);
    }

    private async Task<DisasterSourcePollResult> StoreAndReportAsync(
        DisasterSource source,
        DisasterParseResult parseResult,
        bool emptyBody,
        CancellationToken cancellationToken)
    {
        var inserted = await _store.InsertAsync(parseResult.Items, cancellationToken).ConfigureAwait(false);
        var duplicates = parseResult.Items.Count - inserted;

        _logger.LogInformation(
            "Disaster source {SourceId} polled: parsed {Parsed}, inserted {Inserted}, duplicates {Duplicates}, skipped {Skipped}.",
            source.Id,
            parseResult.Items.Count,
            inserted,
            duplicates,
            parseResult.Skipped);

        return new DisasterSourcePollResult(
            source.Id,
            parseResult.Items.Count,
            inserted,
            duplicates,
            parseResult.Skipped,
            emptyBody,
            null);
    }

    private async Task<string> FetchBodyAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<string> BuildRequestUrls(DisasterSource source)
    {
        var urls = new List<string>();

        if (string.Equals(source.Kind, DisasterParser.Eonet, StringComparison.Ordinal))
        {
            foreach (var category in source.Categories ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(category))
                {
                    urls.Add(AppendQuery(source.Url, "category", category.Trim()));
                }
            }
        }
        else if (string.Equals(source.Kind, DisasterParser.Nws, StringComparison.Ordinal))
        {
            foreach (var eventName in source.Events ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(eventName))
                {
                    urls.Add(AppendQuery(source.Url, "event", eventName.Trim()));
                }
            }
        }

        if (urls.Count == 0)
        {
            urls.Add(source.Url);
        }

        return urls;
    }

    private static string AppendQuery(string url, string key, string value)
    {
        var separator = url.Contains('?')
            ? url.EndsWith('?') || url.EndsWith('&') ? string.Empty : "&"
            : "?";
        return $"{url}{separator}{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}";
    }
}
