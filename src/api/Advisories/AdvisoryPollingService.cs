using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WorldAlerts.Api.Advisories;

/// <summary>
/// Polls advisory sources once at startup, then waits
/// <see cref="AdvisoryOptions.PollInterval"/> between sequential polls. A failed
/// cycle is logged without stopping the host.
/// </summary>
public sealed class AdvisoryPollingService : BackgroundService
{
    private readonly AdvisoryIngestionService _ingestion;
    private readonly AdvisoryOptions _options;
    private readonly ILogger<AdvisoryPollingService> _logger;

    public AdvisoryPollingService(
        AdvisoryIngestionService ingestion,
        AdvisoryOptions options,
        ILogger<AdvisoryPollingService> logger)
    {
        _ingestion = ingestion;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.PollingEnabled)
        {
            _logger.LogInformation("Advisory polling is disabled; no outbound advisory requests will be made.");
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
                _logger.LogError(exception, "Unexpected failure during an advisory polling cycle.");
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
