using System.Net;

namespace WorldAlerts.Api.Delivery;

/// <summary>
/// Raised when a Slack webhook post fails. The message carries the HTTP status
/// (or timeout) but never the webhook URL or its token.
/// </summary>
public sealed class SlackDeliveryException : Exception
{
    public SlackDeliveryException(
        string message,
        HttpStatusCode? statusCode = null,
        string? safeTarget = null)
        : base(message)
    {
        StatusCode = statusCode;
        SafeTarget = safeTarget ?? SlackWebhookRedactor.NotConfigured;
    }

    /// <summary>HTTP status returned by Slack, when one was received.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>Redacted target label suitable for logs and the admin view.</summary>
    public string SafeTarget { get; }
}
