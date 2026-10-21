namespace WorldAlerts.Api.Markets;

/// <summary>
/// One normalized market observation. Observation time comes from the provider
/// (candle open time, ticker close time, reference date or quote timestamp) and
/// is always stored separately from the fetch time.
/// </summary>
public sealed record MarketObservation(
    string SourceId,
    string Venue,
    string Region,
    string Instrument,
    string QuoteUnit,
    double Value,
    DateTimeOffset ObservationUtc,
    DateTimeOffset FetchedUtc,
    bool Delayed,
    string DelaySemantics,
    string Interval,
    string Provenance);

/// <summary>Outcome of parsing one market payload.</summary>
public sealed record MarketParseResult(IReadOnlyList<MarketObservation> Items, int Skipped);
