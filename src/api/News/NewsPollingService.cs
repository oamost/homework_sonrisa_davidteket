using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WorldAlerts.Api.News;

/// <summary>
/// Polls all news feeds once at startup, then waits <see cref="NewsOptions.PollInterval"/>
/// between sequential polls. Because each cycle awaits the previous one, polls
/// never overlap. A failed cycle is logged without stopping the host.
/// </summary>
public sealed class NewsPollingService : BackgroundService
{
    private readonly NewsIngestionService _ingestion;
    private readonly NewsOptions _options;
    private readonly ILogger<NewsPollingService> _logger;

    public NewsPollingService(
        NewsIngestionService ingestion,
        NewsOptions options,
        ILogger<NewsPollingService> logger)
    {
        _ingestion = ingestion;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.PollingEnabled)
        {
            _logger.LogInformation("News polling is disabled; no outbound news requests will be made.");
            return;
        }

        using var timer = new PeriodicTimer(_options.PollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _ingestion.PollOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Unexpected failure during a news polling cycle.");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
