using WorldAlerts.Api.Delivery;
using WorldAlerts.Api.Delivery.Slack;

namespace WorldAlerts.Api.Queue;

public interface INotificationTransport
{
    Task SendAsync(NotificationJob job, CancellationToken ct = default);
}

public interface IChannelNotificationTransport : INotificationTransport
{
    string Channel { get; }
}

public sealed class RegisteredNotificationTransport(params IChannelNotificationTransport[] channels) : INotificationTransport
{
    public Task SendAsync(NotificationJob job, CancellationToken ct = default) =>
        channels.FirstOrDefault(channel => channel.Channel == job.Channel)?.SendAsync(job, ct)
        ?? throw new NotSupportedException("Unknown notification channel.");
}

public sealed class SlackNotificationTransport(SlackSettingsStore settingsStore, ISlackSender sender) : IChannelNotificationTransport
{
    public string Channel => "slack";
    public async Task SendAsync(NotificationJob job, CancellationToken ct = default)
    {
        if (job.Channel != "slack") throw new InvalidOperationException("Channel unavailable.");
        var settings = await settingsStore.ReadAsync(ct);
        if (settings is not { Enabled: true }) throw new InvalidOperationException("Slack needs setup.");
        await sender.SendAsync($"{job.Subject}\n{job.Body}", ct);
    }
}

/// <summary>Retrieves the protected webhook on each send; never reads it from a queue destination.</summary>
public sealed class ProtectedSlackSender(StoredSlackSender sender) : ISlackSender
{
    public async Task SendAsync(string text, CancellationToken ct = default)
    {
        if (!await sender.SendAsync(SlackSettingsStore.ConfigId, text, ct))
            throw new InvalidOperationException("Slack needs setup.");
    }
}

/// <summary>Only explicit configured channels are dispatched. No implicit live test sends.</summary>
public sealed class ConfiguredNotificationTransport(SmtpEmailSender smtp, ISlackSender slack) : INotificationTransport
{
    public Task SendAsync(NotificationJob job, CancellationToken ct = default) => job.Channel switch
    {
        "email" => smtp.SendAsync(job.Destination, job.Subject, job.Body, ct),
        "slack" => slack.SendAsync($"{job.Subject}\n{job.Body}", ct),
        _ => throw new NotSupportedException("Unknown notification channel."),
    };
}

public sealed class NotificationDispatcher(NotificationQueueStore queue, INotificationTransport transport)
{
    public async Task<int> DispatchAsync(DateTimeOffset now, bool startupBurst = false,
        CancellationToken cancellationToken = default, bool emailOnly = false, bool testOnly = false)
    {
        var jobs = await queue.ClaimAsync(now, startupBurst ? 50 : 10, startupBurst, cancellationToken, emailOnly, testOnly);
        foreach (var job in jobs)
        {
            try
            {
                await transport.SendAsync(job, cancellationToken);
                await queue.AcknowledgeAsync(job.Id, now, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // In-flight state is uncertain: external acceptance may have occurred.
                throw;
            }
            catch (Exception exception)
            {
                // Never persist transport exception messages: they may include credentials or destinations.
                await queue.FailAsync(job.Id, now, exception.GetType().Name, cancellationToken);
            }
        }
        return jobs.Count;
    }
}
