using WorldAlerts.Api.Collection;

namespace WorldAlerts.Api.Tests;

public sealed class CollectionPollCadenceTests
{
    private static readonly DateTimeOffset Attempted = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private static SourcePollState State(DateTimeOffset? lastAttempt, DateTimeOffset? nextDue, string family = "news") =>
        new()
        {
            SourceId = "s1",
            Family = family,
            LastAttemptUtc = lastAttempt,
            NextDueUtc = nextDue,
        };

    [Fact]
    public void IsDue_NeverAttempted_IsDue()
        => Assert.True(PollCadence.IsDue(State(null, null), Attempted));

    [Fact]
    public void IsDue_JustBeforeInterval_NotDue()
        => Assert.False(PollCadence.IsDue(State(Attempted, null), Attempted.AddMinutes(14).AddSeconds(59)));

    [Fact]
    public void IsDue_ExactlyAtIntervalBoundary_IsDue()
        => Assert.True(PollCadence.IsDue(State(Attempted, null), Attempted.AddMinutes(15)));

    [Fact]
    public void IsDue_FollowsExplicitNextDue_WhenLaterThanRateCap()
    {
        var due = Attempted.AddMinutes(45);
        Assert.False(PollCadence.IsDue(State(Attempted, due), Attempted.AddMinutes(15)));
        Assert.True(PollCadence.IsDue(State(Attempted, due), due));
    }

    [Fact]
    public void IsDue_NeverFasterThanConfiguredInterval_EvenWithEarlierNextDue()
    {
        var state = State(Attempted, Attempted.AddMinutes(5));
        Assert.False(PollCadence.IsDue(state, Attempted.AddMinutes(5)));
        Assert.True(PollCadence.IsDue(state, Attempted.AddMinutes(15)));
    }

    [Fact]
    public void IsDue_UsesExplicitInterval_WhenSupplied()
    {
        var state = State(Attempted, null);
        Assert.True(PollCadence.IsDue(state, Attempted.AddSeconds(61), TimeSpan.FromSeconds(60)));
        Assert.False(PollCadence.IsDue(state, Attempted.AddSeconds(59), TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void IsDue_RejectsNegativeInterval()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => PollCadence.IsDue(State(Attempted, null), Attempted.AddDays(1), TimeSpan.FromSeconds(-1)));

    [Fact]
    public void GetInterval_KnownFamiliesUseSafeDefaults_AndFallsBackForUnknown()
    {
        Assert.Equal(TimeSpan.FromMinutes(15), PollCadence.GetInterval("news"));
        Assert.Equal(TimeSpan.FromMinutes(10), PollCadence.GetInterval("markets"));
        Assert.Equal(TimeSpan.FromMinutes(30), PollCadence.GetInterval("advisories"));
        Assert.Equal(PollCadence.DefaultPollInterval, PollCadence.GetInterval("unknown-family"));
        Assert.Equal(PollCadence.DefaultPollInterval, PollCadence.GetInterval(null));
    }

    [Fact]
    public void DueUtcAfter_AddsIntervalToAttempt()
        => Assert.Equal(Attempted.AddMinutes(15), PollCadence.DueUtcAfter(Attempted, TimeSpan.FromMinutes(15)));
}
