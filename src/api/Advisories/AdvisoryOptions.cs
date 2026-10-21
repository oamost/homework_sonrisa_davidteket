namespace WorldAlerts.Api.Advisories;

/// <summary>
/// Configuration for keyless cyber-advisory collection, bound from the
/// <c>WorldAlerts:Advisories</c> configuration section.
/// </summary>
public sealed class AdvisoryOptions
{
    public const int DefaultPollMinutes = 60;
    public const int DefaultRequestTimeoutSeconds = 20;

    public bool PollingEnabled { get; set; } = true;

    public int PollMinutes { get; set; } = DefaultPollMinutes;

    public int RequestTimeoutSeconds { get; set; } = DefaultRequestTimeoutSeconds;

    public List<AdvisorySource> Sources { get; set; } = new();

    public TimeSpan PollInterval => TimeSpan.FromMinutes(Math.Max(1, PollMinutes));

    public TimeSpan RequestTimeout => TimeSpan.FromSeconds(Math.Max(1, RequestTimeoutSeconds));
}

/// <summary>One configured cyber-advisory source. All sources are keyless public URLs.</summary>
public sealed class AdvisorySource
{
    /// <summary>Optional longer per-source cadence; never shorter than the family polling interval.</summary>
    public int? PollMinutes { get; set; }
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;
}
