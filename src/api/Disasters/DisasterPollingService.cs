using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WorldAlerts.Api.Disasters;

/// <summary>
/// Polls disaster sources once at startup, then waits
/// <see cref="DisasterOptions.PollInterval"/> between sequential polls. Because
/// each cycle awaits the previous one, polls never overlap. A failed cycle is
/// logged without stopping the host.
/// </summary>
public sealed class DisasterPollingService : BackgroundService
{
    private readonly DisasterIngestionService _ingestion;
    private readonly DisasterOptions _options;
    private readonly ILogger<DisasterPollingService> _logger;

    public DisasterPollingService(
        DisasterIngestionService ingestion,
        DisasterOptions options,
        ILogger<DisasterPollingService> logger)
    {
        _ingestion = ingestion;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.PollingEnabled)
        {
            _logger.LogInformation("Disaster polling is disabled; no outbound disaster requests will be made.");
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
                _logger.LogError(exception, "Unexpected failure during a disaster polling cycle.");
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
