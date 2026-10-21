using WorldAlerts.Api.Scoring;

namespace WorldAlerts.Api.Tests;

/// <summary>
/// Reproduces the approved task-01 market fixtures and their threshold and
/// freshness boundaries. These are acceptance examples, not universal weights.
/// </summary>
public sealed class MarketImportanceScorerTests
{
    private readonly MarketImportanceScorer _scorer = new();

    [Fact]
    public void MK1_CryptoAboveFivePercentAndFresh_IsImportant()
    {
        var decision = _scorer.Score(ScoringFixtures.MK1());

        Assert.True(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.MoveThreshold, decision.RuleId);
        Assert.Equal(1.0, decision.Score);
    }

    [Fact]
    public void MK2_CryptoBelowFivePercent_IsNotImportant()
    {
        var decision = _scorer.Score(ScoringFixtures.MK2());

        Assert.False(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.BelowThreshold, decision.RuleId);
    }

    [Fact]
    public void MK3_FxAboveOnePercentAndFresh_IsImportant()
    {
        var decision = _scorer.Score(ScoringFixtures.MK3());

        Assert.True(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.MoveThreshold, decision.RuleId);
    }

    [Fact]
    public void MK4_FxBelowOnePercent_IsNotImportant()
    {
        var decision = _scorer.Score(ScoringFixtures.MK4());

        Assert.False(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.BelowThreshold, decision.RuleId);
    }

    [Fact]
    public void MK5_IndexAboveTwoPercentAndFresh_IsImportant()
    {
        var decision = _scorer.Score(ScoringFixtures.MK5());

        Assert.True(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.MoveThreshold, decision.RuleId);
    }

    [Fact]
    public void MK6_StaleObservationOverridesLargeMove_IsNotImportant()
    {
        var decision = _scorer.Score(ScoringFixtures.MK6());

        Assert.False(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.StaleObservation, decision.RuleId);
    }

    [Theory]
    [InlineData(MarketFamily.Crypto, 105.0, 100.0)]
    [InlineData(MarketFamily.Fx, 1.01, 1.00)]
    [InlineData(MarketFamily.Index, 1020.0, 1000.0)]
    public void ExactlyAtThreshold_IsImportant(MarketFamily family, double currentValue, double baselineValue)
    {
        var input = ScoringFixtures.Market(family, currentValue, baselineValue, "2026-10-21T08:00:00Z");

        var decision = _scorer.Score(input);

        Assert.True(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.MoveThreshold, decision.RuleId);
    }

    [Theory]
    [InlineData(MarketFamily.Crypto, 104.99, 100.0)]
    [InlineData(MarketFamily.Fx, 1.009, 1.00)]
    [InlineData(MarketFamily.Index, 1019.9, 1000.0)]
    public void JustBelowThreshold_IsNotImportant(MarketFamily family, double currentValue, double baselineValue)
    {
        var input = ScoringFixtures.Market(family, currentValue, baselineValue, "2026-10-21T08:00:00Z");

        var decision = _scorer.Score(input);

        Assert.False(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.BelowThreshold, decision.RuleId);
    }

    [Fact]
    public void ExactlyTwiceTheInterval_IsStale()
    {
        var input = ScoringFixtures.Market(
            MarketFamily.Crypto, 110.0, 100.0, "2026-10-19T09:00:00Z");

        var decision = _scorer.Score(input);

        Assert.False(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.StaleObservation, decision.RuleId);
    }

    [Fact]
    public void JustInsideTwiceTheInterval_IsFresh()
    {
        var input = ScoringFixtures.Market(
            MarketFamily.Crypto, 110.0, 100.0, "2026-10-19T09:01:00Z");

        var decision = _scorer.Score(input);

        Assert.True(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.MoveThreshold, decision.RuleId);
    }

    [Fact]
    public void MissingBaseline_IsNotImportantWithAnExplicitFactor()
    {
        var input = new MarketImportanceInput(
            MarketFamily.Crypto,
            110.0,
            null,
            TimeSpan.FromHours(24),
            ScoringFixtures.At("2026-10-21T08:00:00Z"),
            ScoringFixtures.Now);

        var decision = _scorer.Score(input);

        Assert.False(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.MissingBaseline, decision.RuleId);
        Assert.Contains(decision.Factors, factor => factor.Contains("baseline", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MissingBaselineWithOneHourInterval_ExplainsBothMissingInputs()
    {
        var input = ScoringFixtures.Market(
            MarketFamily.Crypto, 110, 100, "2026-10-21T08:00:00Z", intervalHours: 1)
            with { BaselineValue = null };

        var decision = _scorer.Score(input);

        Assert.False(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.MissingBaseline, decision.RuleId);
        Assert.Contains(decision.Factors, factor => factor.Contains("baseline", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(decision.Factors, factor => factor.Contains("1h", StringComparison.OrdinalIgnoreCase)
            && factor.Contains("24h", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ZeroBaseline_IsNotImportant()
    {
        var input = ScoringFixtures.Market(MarketFamily.Crypto, 1.0, 0.0, "2026-10-21T08:00:00Z");

        var decision = _scorer.Score(input);

        Assert.False(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.InvalidBaseline, decision.RuleId);
    }

    [Fact]
    public void NegativeMoveUsesAbsoluteValue()
    {
        var input = ScoringFixtures.Market(MarketFamily.Crypto, 93.8, 100.0, "2026-10-21T08:00:00Z");

        var decision = _scorer.Score(input);

        Assert.True(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.MoveThreshold, decision.RuleId);
    }

    [Fact]
    public void FutureObservation_IsNotTreatedAsFresh()
    {
        var input = ScoringFixtures.Market(MarketFamily.Crypto, 110.0, 100.0, "2026-10-21T10:00:00Z");

        var decision = _scorer.Score(input);

        Assert.False(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.StaleObservation, decision.RuleId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveObservationInterval_IsNotImportantWithExplicitFactor(int intervalHours)
    {
        var input = new MarketImportanceInput(
            MarketFamily.Crypto,
            110.0,
            100.0,
            TimeSpan.FromHours(intervalHours),
            ScoringFixtures.At("2026-10-21T08:00:00Z"),
            ScoringFixtures.Now);

        var decision = _scorer.Score(input);

        Assert.False(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.InvalidInterval, decision.RuleId);
        Assert.Equal(0.0, decision.Score);
        Assert.Contains(decision.Factors, factor => factor.Contains("interval", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void JustBelowFivePercentAtSixDecimalPlaces_IsNotImportant()
    {
        var input = ScoringFixtures.Market(
            MarketFamily.Crypto, 104.999999, 100.0, "2026-10-21T08:00:00Z");

        var decision = _scorer.Score(input);

        Assert.False(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.BelowThreshold, decision.RuleId);
    }

    [Fact]
    public void JustAboveFivePercentAtSixDecimalPlaces_IsImportant()
    {
        var input = ScoringFixtures.Market(
            MarketFamily.Crypto, 105.000001, 100.0, "2026-10-21T08:00:00Z");

        var decision = _scorer.Score(input);

        Assert.True(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.MoveThreshold, decision.RuleId);
    }

    [Fact]
    public void ExactOnePercentWithBinaryRoundingError_IsImportant()
    {
        // (0.101 - 0.1) / 0.1 is slightly below 1% in raw binary double
        // arithmetic; the comparison tolerance must still treat the exact
        // decimal boundary as meeting the fixture.
        var input = ScoringFixtures.Market(
            MarketFamily.Fx, 0.101, 0.1, "2026-10-21T08:00:00Z");

        var decision = _scorer.Score(input);

        Assert.True(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.MoveThreshold, decision.RuleId);
    }

    [Fact]
    public void ZeroCurrentValue_IsTreatedAsAFullDecline()
    {
        var input = ScoringFixtures.Market(
            MarketFamily.Crypto, 0.0, 100.0, "2026-10-21T08:00:00Z");

        var decision = _scorer.Score(input);

        Assert.True(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.MoveThreshold, decision.RuleId);
        Assert.Contains(decision.Factors, factor => factor.Contains("Current value is zero", StringComparison.Ordinal));
    }

    [Fact]
    public void NegativeCurrentValue_UsesItsAbsoluteMove()
    {
        var input = ScoringFixtures.Market(
            MarketFamily.Crypto, -50.0, 100.0, "2026-10-21T08:00:00Z");

        var decision = _scorer.Score(input);

        Assert.True(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.MoveThreshold, decision.RuleId);
        Assert.Contains(decision.Factors, factor => factor.Contains("Current value is negative", StringComparison.Ordinal));
    }

    [Fact]
    public void NegativeBaseline_UsesAbsoluteBaselineAsDenominator()
    {
        var input = ScoringFixtures.Market(
            MarketFamily.Crypto, 0.0, -100.0, "2026-10-21T08:00:00Z");

        var decision = _scorer.Score(input);

        Assert.True(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.MoveThreshold, decision.RuleId);
        Assert.Contains(decision.Factors, factor => factor.Contains("Baseline is negative", StringComparison.Ordinal));
    }

    [Fact]
    public void NonFiniteBaseline_IsInvalidBaselineWithExplicitFactor()
    {
        var input = new MarketImportanceInput(
            MarketFamily.Crypto,
            110.0,
            double.NaN,
            TimeSpan.FromHours(24),
            ScoringFixtures.At("2026-10-21T08:00:00Z"),
            ScoringFixtures.Now);

        var decision = _scorer.Score(input);

        Assert.False(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.InvalidBaseline, decision.RuleId);
        Assert.Contains(decision.Factors, factor => factor.Contains("finite", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NonFiniteCurrentValue_IsNotImportantWithExplicitFactor()
    {
        var input = new MarketImportanceInput(
            MarketFamily.Crypto,
            double.PositiveInfinity,
            100.0,
            TimeSpan.FromHours(24),
            ScoringFixtures.At("2026-10-21T08:00:00Z"),
            ScoringFixtures.Now);

        var decision = _scorer.Score(input);

        Assert.False(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.BelowThreshold, decision.RuleId);
        Assert.Contains(decision.Factors, factor => factor.Contains("finite", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OneHourCryptoMove_CannotUseTwentyFourHourThreshold()
    {
        var input = ScoringFixtures.Market(
            MarketFamily.Crypto, 110.0, 100.0, "2026-10-21T08:00:00Z", intervalHours: 1);

        var decision = _scorer.Score(input);

        Assert.False(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.InvalidInterval, decision.RuleId);
        Assert.Equal(0.0, decision.Score);
        Assert.Contains(
            decision.Factors,
            factor => factor.Contains("24h", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(MarketFamily.Crypto, 104.9999999999999, 100.0)]
    [InlineData(MarketFamily.Fx, 1.00999999999999, 1.0)]
    [InlineData(MarketFamily.Index, 101.9999999999999, 100.0)]
    public void RepresentableMoveBelowExactThreshold_IsNotRoundedUp(
        MarketFamily family, double current, double baseline)
    {
        var decision = _scorer.Score(ScoringFixtures.Market(
            family, current, baseline, "2026-10-21T08:00:00Z"));

        Assert.False(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.BelowThreshold, decision.RuleId);
    }

    [Fact]
    public void MissingObservationTimestamp_CannotBeConsideredFresh()
    {
        var input = ScoringFixtures.Market(MarketFamily.Crypto, 110, 100, "2026-10-21T08:00:00Z")
            with { ObservationUtc = default };

        var decision = _scorer.Score(input);

        Assert.False(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.StaleObservation, decision.RuleId);
        Assert.Contains(decision.Factors, factor => factor.Contains("timestamp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OneTickBeforeTwiceTheInterval_IsFresh()
    {
        var observation = ScoringFixtures.Now - TimeSpan.FromHours(48) + TimeSpan.FromTicks(1);
        var input = new MarketImportanceInput(
            MarketFamily.Crypto,
            110.0,
            100.0,
            TimeSpan.FromHours(24),
            observation,
            ScoringFixtures.Now);

        var decision = _scorer.Score(input);

        Assert.True(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.MoveThreshold, decision.RuleId);
    }

    [Fact]
    public void ObservationOneTickInTheFuture_IsStale()
    {
        var input = new MarketImportanceInput(
            MarketFamily.Crypto,
            110.0,
            100.0,
            TimeSpan.FromHours(24),
            ScoringFixtures.Now + TimeSpan.FromTicks(1),
            ScoringFixtures.Now);

        var decision = _scorer.Score(input);

        Assert.False(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.StaleObservation, decision.RuleId);
        Assert.Contains(decision.Factors, factor => factor.Contains("future", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CustomThresholds_AreUsed()
    {
        var scorer = new MarketImportanceScorer(new[]
        {
            new MarketThreshold(MarketFamily.Crypto, 10.0, TimeSpan.FromHours(24)),
            new MarketThreshold(MarketFamily.Fx, 1.0, TimeSpan.FromHours(24)),
            new MarketThreshold(MarketFamily.Index, 2.0, TimeSpan.FromHours(24)),
        });

        var decision = scorer.Score(ScoringFixtures.MK1());

        Assert.False(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.BelowThreshold, decision.RuleId);
    }

    [Fact]
    public void DefaultThresholds_MatchTheApprovedFixtures()
    {
        Assert.Equal(5.0, MarketImportanceScorer.DefaultThresholds[MarketFamily.Crypto].Percent);
        Assert.Equal(1.0, MarketImportanceScorer.DefaultThresholds[MarketFamily.Fx].Percent);
        Assert.Equal(2.0, MarketImportanceScorer.DefaultThresholds[MarketFamily.Index].Percent);
        Assert.All(
            MarketImportanceScorer.DefaultThresholds.Values,
            threshold => Assert.Equal(TimeSpan.FromHours(24), threshold.Interval));
    }

    [Fact]
    public void Constructor_RejectsNonFiniteThreshold()
    {
        var thresholds = new[]
        {
            new MarketThreshold(MarketFamily.Crypto, double.NaN, TimeSpan.FromHours(24)),
            new MarketThreshold(MarketFamily.Fx, 1.0, TimeSpan.FromHours(24)),
            new MarketThreshold(MarketFamily.Index, 2.0, TimeSpan.FromHours(24)),
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => new MarketImportanceScorer(thresholds));
    }

    [Fact]
    public void Constructor_RejectsDuplicateFamily()
    {
        var thresholds = new[]
        {
            new MarketThreshold(MarketFamily.Crypto, 5.0, TimeSpan.FromHours(24)),
            new MarketThreshold(MarketFamily.Crypto, 6.0, TimeSpan.FromHours(24)),
            new MarketThreshold(MarketFamily.Fx, 1.0, TimeSpan.FromHours(24)),
            new MarketThreshold(MarketFamily.Index, 2.0, TimeSpan.FromHours(24)),
        };

        Assert.Throws<ArgumentException>(() => new MarketImportanceScorer(thresholds));
    }

    [Fact]
    public void Constructor_RequiresEveryFamily()
    {
        var thresholds = new[]
        {
            new MarketThreshold(MarketFamily.Crypto, 5.0, TimeSpan.FromHours(24)),
        };

        Assert.Throws<ArgumentException>(() => new MarketImportanceScorer(thresholds));
    }
}
