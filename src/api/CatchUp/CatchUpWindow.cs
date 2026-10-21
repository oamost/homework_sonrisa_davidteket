namespace WorldAlerts.Api.CatchUp;

/// <summary>
/// Lookback window for the all-category catch-up. The default is 24 hours; callers
/// may supply a different duration for tests or future configuration without
/// changing the eligibility rule.
/// </summary>
public sealed class CatchUpWindow
{
    /// <summary>The approved 24-hour catch-up window.</summary>
    public static CatchUpWindow TwentyFourHours { get; } = new(TimeSpan.FromHours(24));

    /// <summary>Creates a window with the supplied positive duration.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The duration is not positive.</exception>
    public CatchUpWindow(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                duration,
                "The catch-up window must be positive.");
        }

        Duration = duration;
    }

    /// <summary>How far back from the evaluation instant the window reaches.</summary>
    public TimeSpan Duration { get; }

    /// <summary>Returns the inclusive start of the window for <paramref name="nowUtc"/>.</summary>
    public DateTimeOffset GetStart(DateTimeOffset nowUtc) => nowUtc - Duration;

    /// <summary>
    /// Returns true when <paramref name="timestampUtc"/> is inside the window. Both
    /// edges are inclusive, so a timestamp exactly one window old is eligible.
    /// </summary>
    public bool Contains(DateTimeOffset nowUtc, DateTimeOffset timestampUtc) =>
        timestampUtc >= GetStart(nowUtc) && timestampUtc <= nowUtc;
}
