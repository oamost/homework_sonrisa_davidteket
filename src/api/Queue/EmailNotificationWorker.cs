using System.Net.Mail;
using WorldAlerts.Api.Delivery;

namespace WorldAlerts.Api.Queue;

/// <summary>Checks private opt-in on every send, so disabling delivery takes effect even for already-queued jobs.</summary>
public sealed class EmailNotificationTransport(EmailSettingsStore settingsStore, ISmtpTransport transport) : INotificationTransport
{
    public async Task SendAsync(NotificationJob job, CancellationToken ct = default)
    {
        if (job.Channel != "email") throw new InvalidOperationException("Channel unavailable.");
        var settings = await settingsStore.ReadAsync(ct);
        if (settings is null || !settings.Enabled) throw new InvalidOperationException("Email delivery disabled.");
        var options = EmailSettingsStore.ToOptions(settings);
        if (options.Validate().Count > 0) throw new InvalidOperationException("Invalid SMTP settings.");
        using var message = new MailMessage(options.FromAddress, job.Destination,
            SmtpEmailSender.NormalizeSubject(job.Subject), job.Body);
        await transport.SendAsync(message, options, ct);
    }
}

/// <summary>Selection and dispatch run at startup (once configured), then every minute without overlap.</summary>
public sealed class EmailNotificationWorker(AlertPipeline pipeline, NotificationDispatcher dispatcher,
    EmailSettingsStore settings, TimeProvider clock, ILogger<EmailNotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var firstConfiguredRun = true;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var active = await settings.ReadAsync(stoppingToken);
                if (active?.Enabled == true)
                {
                    await pipeline.EnqueueSelectedAsync(stoppingToken);
                    await dispatcher.DispatchAsync(clock.GetUtcNow(), startupBurst: firstConfiguredRun,
                        cancellationToken: stoppingToken, emailOnly: true);
                    firstConfiguredRun = false;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                // Store/SMTP exception text may contain passwords, hostnames or destinations.
                logger.LogError("Email notification cycle failed with {ErrorType}.", exception.GetType().Name);
            }
            try { await Task.Delay(TimeSpan.FromMinutes(1), clock, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
