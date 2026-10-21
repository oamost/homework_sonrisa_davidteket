namespace WorldAlerts.Api.Delivery;

/// <summary>
/// Sends a text notification to Slack. The production implementation posts to
/// an incoming webhook; tests substitute a fake HTTP handler.
/// </summary>
/// <remarks>
/// Only a single send is defined. Retry, queueing and pacing belong to task 19,
/// and subscription integration plus admin/UI configuration belong to the
/// task 20/21 integration work.
/// </remarks>
public interface ISlackSender
{
    /// <summary>Posts <paramref name="text"/> as a plain-text Slack message.</summary>
    Task SendAsync(string text, CancellationToken cancellationToken = default);
}
