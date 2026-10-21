using System.Net.Mail;
using WorldAlerts.Api.Delivery;
using WorldAlerts.Api.Delivery.Slack;
using WorldAlerts.Api.Delivery.Webhooks;

namespace WorldAlerts.Api.Queue;

/// <summary>Checks private opt-in on every send, so disabling delivery takes effect even for already-queued jobs.</summary>
public sealed class EmailNotificationTransport(EmailSettingsStore settingsStore, ISmtpTransport transport) : IChannelNotificationTransport
{
    public string Channel => "email";
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

/// <summary>Selection and all-channel dispatch run at startup and every minute without overlap.</summary>
public sealed class EmailNotificationWorker(AlertPipeline pipeline, NotificationDispatcher dispatcher,
    EmailSettingsStore settings, SlackSettingsStore slackSettings, WebhookSettingsStore webhookSettings, TimeProvider clock,
    ILogger<EmailNotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var firstConfiguredRun = true;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var active = await settings.ReadAsync(stoppingToken);
                var slack = await slackSettings.ReadAsync(stoppingToken);
                var webhooks = await webhookSettings.ListAsync(stoppingToken);
                if (active?.Enabled == true || slack?.Enabled == true || webhooks.Any(config => config.Enabled))
                {
                    await pipeline.EnqueueSelectedAsync(stoppingToken);
                    await dispatcher.DispatchAsync(clock.GetUtcNow(), startupBurst: firstConfiguredRun,
                        cancellationToken: stoppingToken);
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
