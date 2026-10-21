namespace WorldAlerts.Api.Advisories;

/// <summary>Per-source outcome of one advisory poll. <see cref="ErrorType"/> is null on success.</summary>
public sealed record AdvisorySourcePollResult(
    string SourceId,
    int Parsed,
    int Inserted,
    int Duplicates,
    int Skipped,
    bool EmptyBody,
    string? ErrorType);

/// <summary>Outcome of polling every configured advisory source once.</summary>
public sealed record AdvisoryPollResult(IReadOnlyList<AdvisorySourcePollResult> Sources)
{
    public static AdvisoryPollResult Empty { get; } = new(Array.Empty<AdvisorySourcePollResult>());
}
