namespace WorldAlerts.Api.News;

/// <summary>
/// Configuration for keyless RSS news collection, bound from the
/// <c>WorldAlerts:News</c> configuration section.
/// </summary>
public sealed class NewsOptions
{
    public const int DefaultPollMinutes = 15;
    public const int DefaultRequestTimeoutSeconds = 20;

    public bool PollingEnabled { get; set; } = true;

    public int PollMinutes { get; set; } = DefaultPollMinutes;

    public int RequestTimeoutSeconds { get; set; } = DefaultRequestTimeoutSeconds;

    public List<NewsFeed> Feeds { get; set; } = new();

    public TimeSpan PollInterval => TimeSpan.FromMinutes(Math.Max(1, PollMinutes));

    public TimeSpan RequestTimeout => TimeSpan.FromSeconds(Math.Max(1, RequestTimeoutSeconds));
}

/// <summary>One configured RSS feed. All feeds are keyless public URLs.</summary>
public sealed class NewsFeed
{
    /// <summary>Optional longer per-feed cadence; never shorter than the family polling interval.</summary>
    public int? PollMinutes { get; set; }
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string Family { get; set; } = string.Empty;
}
