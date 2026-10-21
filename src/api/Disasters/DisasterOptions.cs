namespace WorldAlerts.Api.Disasters;

/// <summary>
/// Configuration for keyless disaster collection, bound from the
/// <c>WorldAlerts:Disasters</c> configuration section.
/// </summary>
public sealed class DisasterOptions
{
    public const int DefaultPollMinutes = 10;
    public const int DefaultRequestTimeoutSeconds = 20;

    public bool PollingEnabled { get; set; } = true;

    public int PollMinutes { get; set; } = DefaultPollMinutes;

    public int RequestTimeoutSeconds { get; set; } = DefaultRequestTimeoutSeconds;

    public List<DisasterSource> Sources { get; set; } = new();

    public TimeSpan PollInterval => TimeSpan.FromMinutes(Math.Max(1, PollMinutes));

    public TimeSpan RequestTimeout => TimeSpan.FromSeconds(Math.Max(1, RequestTimeoutSeconds));
}

/// <summary>One configured disaster source. All sources are keyless public URLs.</summary>
public sealed class DisasterSource
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Parser selector: gdacs-rss, eonet, usgs, ifrc, nws, dwd-warnings,
    /// copernicus-ems or emsc.
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    /// <summary>EONET category ids queried and merged in one poll.</summary>
    public List<string> Categories { get; set; } = new();

    /// <summary>NWS alert event names or DWD heat/frost event allowlist.</summary>
    public List<string> Events { get; set; } = new();

    /// <summary>Scope or attribution note appended to provenance, never invented data.</summary>
    public string Semantics { get; set; } = string.Empty;

    /// <summary>Copernicus EMS page size. Ignored by other source kinds.</summary>
    public int PageSize { get; set; } = 100;

    /// <summary>Copernicus EMS maximum pages per poll. Ignored by other source kinds.</summary>
    public int MaxPages { get; set; } = 3;

    /// <summary>Copernicus EMS maximum unique activations per poll.</summary>
    public int MaxItems { get; set; } = 300;

    /// <summary>
    /// Optional, approval-dependent archive-filter window in hours.
    /// Null or nonpositive values preserve all valid historical rows.
    /// </summary>
    public int? RecencyWindowHours { get; set; }

    /// <summary>
    /// Optional EONET volcano-specific archive-filter window in hours.
    /// Falls back to RecencyWindowHours when not set. Never sets 24-hour
    /// delivery eligibility. Ignored by other source kinds.
    /// </summary>
    public int? VolcanoRecencyWindowHours { get; set; }

    /// <summary>
    /// Optional source ordering expression. IFRC GO uses
    /// <c>-created_at</c> (newest first) when this is empty, so archive rows
    /// cannot crowd recent events out of the response.
    /// </summary>
    public string Ordering { get; set; } = string.Empty;
}
