namespace WorldAlerts.Api.Disasters;

/// <summary>Per-source outcome of one disaster poll. <see cref="ErrorType"/> is null on success.</summary>
public sealed record DisasterSourcePollResult(
    string SourceId,
    int Parsed,
    int Inserted,
    int Duplicates,
    int Skipped,
    bool EmptyBody,
    string? ErrorType);

/// <summary>Outcome of polling every configured disaster source once.</summary>
public sealed record DisasterPollResult(IReadOnlyList<DisasterSourcePollResult> Sources)
{
    public static DisasterPollResult Empty { get; } = new(Array.Empty<DisasterSourcePollResult>());
}
