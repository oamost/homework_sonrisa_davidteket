namespace WorldAlerts.Api.Delivery;

/// <summary>
/// Reduces a Slack webhook URL to a label that is safe to log or show in an
/// admin view. The full URL, its path token and any query string are never
/// returned.
/// </summary>
public static class SlackWebhookRedactor
{
    /// <summary>Label used when no webhook is configured.</summary>
    public const string NotConfigured = "not configured";

    /// <summary>Label used when the configured value cannot be parsed as a URL.</summary>
    public const string Unparseable = "configured (unparseable)";

    /// <summary>
    /// Returns the webhook host plus at most the last four characters of the
    /// token segment, for example <c>hooks.slack.com (…a1b2)</c>.
    /// </summary>
    public static string Describe(string? webhookUrl)
    {
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            return NotConfigured;
        }

        if (!Uri.TryCreate(webhookUrl, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
        {
            return Unparseable;
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        var lastSegment = path[(path.LastIndexOf('/') + 1)..];

        // Short segments would make the "last four characters" hint disclose
        // most of the token, so fall back to the host alone.
        return lastSegment.Length >= 8
            ? $"{uri.Host} (…{lastSegment[^4..]})"
            : uri.Host;
    }
}
