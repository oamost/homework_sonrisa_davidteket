namespace WorldAlerts.Api.Markets;

/// <summary>
/// Configuration for keyless market collection, bound from the
/// <c>WorldAlerts:Markets</c> configuration section.
/// </summary>
public sealed class MarketOptions
{
    public const int DefaultPollMinutes = 10;
    public const int DefaultRequestTimeoutSeconds = 20;

    public bool PollingEnabled { get; set; } = true;

    public int PollMinutes { get; set; } = DefaultPollMinutes;

    public int RequestTimeoutSeconds { get; set; } = DefaultRequestTimeoutSeconds;

    public List<MarketSource> Sources { get; set; } = new();

    public TimeSpan PollInterval => TimeSpan.FromMinutes(Math.Max(1, PollMinutes));

    public TimeSpan RequestTimeout => TimeSpan.FromSeconds(Math.Max(1, RequestTimeoutSeconds));
}

/// <summary>
/// One configured market source. Single-venue feeds carry their venue/region and
/// delay semantics so the stored observation never implies more freshness than
/// the provider supplies.
/// </summary>
public sealed class MarketSource
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Parser selector: binance-ticker, binance-klines, frankfurter, cboe, twse or moex.</summary>
    public string Kind { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string Venue { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;

    public string Instrument { get; set; } = string.Empty;

    public string QuoteUnit { get; set; } = string.Empty;

    public string Symbol { get; set; } = string.Empty;

    public string Interval { get; set; } = string.Empty;

    public string FromCurrency { get; set; } = string.Empty;

    public string ToCurrency { get; set; } = string.Empty;

    /// <summary>
    /// Optional allowlist for sources that return a full venue listing (TWSE).
    /// An empty list means no instruments are stored, never the full listing.
    /// </summary>
    public List<string> Instruments { get; set; } = new();

    public bool Delayed { get; set; }

    public string DelaySemantics { get; set; } = string.Empty;
}
