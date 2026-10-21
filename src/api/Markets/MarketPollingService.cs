using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WorldAlerts.Api.Markets;

/// <summary>
/// Polls market sources once at startup, then waits <see cref="MarketOptions.PollInterval"/>
/// between sequential polls. Because each cycle awaits the previous one, polls
/// never overlap. A failed cycle is logged without stopping the host.
/// </summary>
public sealed class MarketPollingService : BackgroundService
{
    private readonly MarketIngestionService _ingestion;
    private readonly MarketOptions _options;
    private readonly ILogger<MarketPollingService> _logger;

    public MarketPollingService(
        MarketIngestionService ingestion,
        MarketOptions options,
        ILogger<MarketPollingService> logger)
    {
        _ingestion = ingestion;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.PollingEnabled)
        {
            _logger.LogInformation("Market polling is disabled; no outbound market requests will be made.");
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
                _logger.LogError(exception, "Unexpected failure during a market polling cycle.");
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
