namespace WorldAlerts.Api.Delivery;

/// <summary>
/// Configuration for Slack incoming-webhook delivery, bound from the
/// <c>WorldAlerts:Delivery:Slack</c> configuration section.
/// </summary>
/// <remarks>
/// <para>
/// The webhook URL is a secret. Supply it through configuration or an
/// environment variable (for example
/// <c>WorldAlerts__Delivery__Slack__WebhookUrl</c>), never through source
/// control. Validation errors and the safe label from
/// <see cref="Describe"/> never contain the full URL or its token.
/// </para>
/// <para>
/// This slice only posts a message. Retry, queueing and pacing belong to
/// task 19, and subscription integration plus admin/UI configuration belong to
/// the task 20/21 integration work.
/// </para>
/// </remarks>
public sealed class SlackOptions
{
    /// <summary>Configuration section that binds this type.</summary>
    public const string SectionName = "WorldAlerts:Delivery:Slack";

    public const int DefaultTimeoutSeconds = 10;
    public const int MaxTimeoutSeconds = 600;

    /// <summary>Authorized incoming-webhook URL. Treated as a secret.</summary>
    public string WebhookUrl { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

    /// <summary>
    /// Optional channel override (for example <c>#alerts</c>). When empty the
    /// webhook's own default channel is used.
    /// </summary>
    public string? Channel { get; set; }

    /// <summary>Per-send timeout, clamped to a usable range.</summary>
    public TimeSpan Timeout => TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds, 1, MaxTimeoutSeconds));

    /// <summary>True when a webhook URL is present.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(WebhookUrl);

    /// <summary>Safe label for logs; never the full webhook URL.</summary>
    public string Describe() => SlackWebhookRedactor.Describe(WebhookUrl);

    /// <summary>
    /// Returns human-readable configuration errors. Messages never echo the
    /// webhook URL or any part of its token.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(WebhookUrl))
        {
            errors.Add("Slack webhook URL is required.");
        }
        else if (!Uri.TryCreate(WebhookUrl, UriKind.Absolute, out var uri))
        {
            errors.Add("Slack webhook URL must be an absolute HTTPS URL.");
        }
        else
        {
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Slack webhook URL must be an absolute HTTPS URL.");
            }

            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                errors.Add("Slack webhook URL must not embed credentials.");
            }
        }

        if (TimeoutSeconds is < 1 or > MaxTimeoutSeconds)
        {
            errors.Add($"Slack timeout must be between 1 and {MaxTimeoutSeconds} seconds.");
        }

        if (!string.IsNullOrWhiteSpace(Channel)
            && Channel.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            errors.Add("Slack channel override must be a channel name such as #alerts.");
        }

        return errors;
    }
}
