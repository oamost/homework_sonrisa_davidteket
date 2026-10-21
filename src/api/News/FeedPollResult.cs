namespace WorldAlerts.Api.News;

/// <summary>Per-feed outcome of one poll. <see cref="ErrorType"/> is null on success.</summary>
public sealed record FeedPollResult(
    string FeedId,
    int Parsed,
    int Inserted,
    int Duplicates,
    int Skipped,
    string? ErrorType);

/// <summary>Outcome of polling every configured feed once.</summary>
public sealed record NewsPollResult(IReadOnlyList<FeedPollResult> Feeds)
{
    public static NewsPollResult Empty { get; } = new(Array.Empty<FeedPollResult>());
}
