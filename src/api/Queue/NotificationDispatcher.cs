using WorldAlerts.Api.Delivery;

namespace WorldAlerts.Api.Queue;

public interface INotificationTransport
{
    Task SendAsync(NotificationJob job, CancellationToken ct = default);
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
        CancellationToken cancellationToken = default, bool emailOnly = false)
    {
        var jobs = await queue.ClaimAsync(now, startupBurst ? 50 : 10, startupBurst, cancellationToken, emailOnly);
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
