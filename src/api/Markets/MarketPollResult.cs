namespace WorldAlerts.Api.Markets;

/// <summary>Per-source outcome of one market poll. <see cref="ErrorType"/> is null on success.</summary>
public sealed record MarketSourcePollResult(
    string SourceId,
    int Parsed,
    int Inserted,
    int Duplicates,
    int Skipped,
    bool EmptyBody,
    string? ErrorType);

/// <summary>Outcome of polling every configured market source once.</summary>
public sealed record MarketPollResult(IReadOnlyList<MarketSourcePollResult> Sources)
{
    public static MarketPollResult Empty { get; } = new(Array.Empty<MarketSourcePollResult>());
}
