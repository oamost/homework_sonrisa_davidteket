using WorldAlerts.Api.Scoring;

namespace WorldAlerts.Api.Tests;

/// <summary>
/// Reproduces the approved task-01 news fixtures and their boundary values.
/// These are acceptance examples, not universal weights. Eligibility (the
/// 24-hour window) is deliberately not evaluated here: the scorer answers only
/// the D2 importance question and receives <c>nowUtc</c> from its caller.
/// </summary>
public sealed class NewsImportanceScorerTests
{
    private readonly NewsImportanceScorer _scorer = new();

    [Fact]
    public void N1_TierOneBreakingEarthquake_IsImportant()
    {
        var decision = _scorer.Score(ScoringFixtures.N1().Item, ScoringFixtures.Now);

        Assert.True(decision.IsImportant);
        Assert.Equal(NewsImportanceRuleIds.TierOneUrgencyKeyword, decision.RuleId);
        Assert.Equal(1.0, decision.Score);
        Assert.Contains(decision.Factors, factor => factor.Contains("breaking", StringComparison.Ordinal));
    }

    [Fact]
    public void N2_TwoTierOneSourcesWithinSixHours_IsImportant()
    {
        var fixture = ScoringFixtures.N2();
        var decision = _scorer.Score(fixture.Item, ScoringFixtures.Now, fixture.CorroboratingSources);

        Assert.True(decision.IsImportant);
        Assert.Equal(NewsImportanceRuleIds.TierOneCorroboration, decision.RuleId);
        Assert.Equal(1.0, decision.Score);
        Assert.Contains(decision.Factors, factor => factor.Contains("2 independent tier-1 sources", StringComparison.Ordinal));
    }

    [Fact]
    public void N3_SingleSpecialistWithoutKeyword_IsNotImportant()
    {
        var decision = _scorer.Score(ScoringFixtures.N3().Item, ScoringFixtures.Now);

        Assert.False(decision.IsImportant);
        Assert.Equal(NewsImportanceRuleIds.NoApprovedTrigger, decision.RuleId);
        Assert.Equal(0.0, decision.Score);
    }

    [Fact]
    public void N4_TwoTierOneSourcesOutsideSixHours_IsNotImportant()
    {
        var fixture = ScoringFixtures.N4();
        var decision = _scorer.Score(fixture.Item, ScoringFixtures.Now, fixture.CorroboratingSources);

        Assert.False(decision.IsImportant);
        Assert.Equal(NewsImportanceRuleIds.NoApprovedTrigger, decision.RuleId);
        Assert.Contains(decision.Factors, factor => factor.Contains("outside the 6h window", StringComparison.Ordinal));
    }

    [Fact]
    public void N5_SpecialistConflictKeyword_IsNotImportant()
    {
        var decision = _scorer.Score(ScoringFixtures.N5().Item, ScoringFixtures.Now);

        Assert.False(decision.IsImportant);
        Assert.Contains(decision.Factors, factor => factor.Contains("not tier-1", StringComparison.Ordinal));
        Assert.Contains(decision.Factors, factor => factor.Contains("conflict", StringComparison.Ordinal));
    }

    [Fact]
    public void N6_TierOneWithoutKeyword_IsNotImportant()
    {
        var decision = _scorer.Score(ScoringFixtures.N6().Item, ScoringFixtures.Now);

        Assert.False(decision.IsImportant);
        Assert.Equal(NewsImportanceRuleIds.NoApprovedTrigger, decision.RuleId);
    }

    [Fact]
    public void ExactlySixHoursApart_IsWithinTheWindow()
    {
        var fixture = ScoringFixtures.ExactlySixHours();
        var decision = _scorer.Score(fixture.Item, ScoringFixtures.Now, fixture.CorroboratingSources);

        Assert.True(decision.IsImportant);
        Assert.Equal(NewsImportanceRuleIds.TierOneCorroboration, decision.RuleId);
    }

    [Fact]
    public void JustOverSixHoursApart_IsOutsideTheWindow()
    {
        var fixture = ScoringFixtures.JustOverSixHours();
        var decision = _scorer.Score(fixture.Item, ScoringFixtures.Now, fixture.CorroboratingSources);

        Assert.False(decision.IsImportant);
        Assert.Equal(NewsImportanceRuleIds.NoApprovedTrigger, decision.RuleId);
    }

    [Fact]
    public void MissingPublishedTime_CannotEstablishCorroboration()
    {
        var fixture = ScoringFixtures.MissingPublishedTime();
        var decision = _scorer.Score(fixture.Item, ScoringFixtures.Now, fixture.CorroboratingSources);

        Assert.False(decision.IsImportant);
        Assert.Contains(decision.Factors, factor => factor.Contains("publication time", StringComparison.Ordinal));
    }

    [Fact]
    public void SameSourceIdTwice_IsNotIndependentCorroboration()
    {
        var item = ScoringFixtures.News("bbc-world", "BBC World", "Routine update", "2026-10-21T08:30:00Z");
        var duplicate = new[]
        {
            ScoringFixtures.Corroboration("bbc-world", "BBC World", "2026-10-21T06:10:00Z"),
        };

        var decision = _scorer.Score(item, ScoringFixtures.Now, duplicate);

        Assert.False(decision.IsImportant);
        Assert.Contains(decision.Factors, factor => factor.Contains("Only 1 independent", StringComparison.Ordinal));
    }

    [Fact]
    public void ConfigurableTierMap_CanPromoteACustomSource()
    {
        var tiers = new NewsSourceTiers(new[]
        {
            new NewsSourceTierDefinition("trade-witness", "Trade Witness", NewsSourceTier.TierOne),
        });
        var scorer = new NewsImportanceScorer(tiers);
        var decision = scorer.Score(ScoringFixtures.N5().Item, ScoringFixtures.Now);

        Assert.True(decision.IsImportant);
        Assert.Equal(NewsImportanceRuleIds.TierOneUrgencyKeyword, decision.RuleId);
    }

    [Fact]
    public void DefaultTierMap_ClassifiesSpecialists()
    {
        Assert.False(NewsSourceTiers.Default.IsTierOne("nature", "Nature"));
        Assert.True(NewsSourceTiers.Default.IsTierOne("bbc-world", "BBC World"));
        Assert.True(NewsSourceTiers.Default.IsTierOne("aljazeera-all", "Al Jazeera English"));
        Assert.True(NewsSourceTiers.Default.IsTierOne("guardian-world", "The Guardian World"));
        Assert.True(NewsSourceTiers.Default.IsTierOne("nyt-world", "NYT World"));
    }

    [Fact]
    public void T1_OldEventButTierOneBreakingHeadline_IsImportant()
    {
        var decision = _scorer.Score(NewsScoringFixtures.T1().Item, ScoringFixtures.Now);

        Assert.True(decision.IsImportant);
        Assert.Equal(NewsImportanceRuleIds.TierOneUrgencyKeyword, decision.RuleId);
    }

    [Fact]
    public void T2_MissingProviderTimestamps_DoesNotChangeKeywordImportance()
    {
        // D3 later marks T2 ineligible because no provider timestamp exists;
        // importance itself is decided from the tier-1 headline alone.
        var decision = _scorer.Score(NewsScoringFixtures.T2().Item, ScoringFixtures.Now);

        Assert.True(decision.IsImportant);
        Assert.Equal(NewsImportanceRuleIds.TierOneUrgencyKeyword, decision.RuleId);
    }

    [Fact]
    public void T3_FuturePublication_KeywordRuleStillApplies_ButEligibilityIsSeparate()
    {
        // D3 excludes T3 from delivery because its publication time is in the
        // future. The pure importance rule has no eligibility clock; this test
        // records that separation so callers cannot mistake importance for
        // eligibility.
        var decision = _scorer.Score(NewsScoringFixtures.T3().Item, ScoringFixtures.Now);

        Assert.True(decision.IsImportant);
        Assert.Equal(NewsImportanceRuleIds.TierOneUrgencyKeyword, decision.RuleId);
    }

    [Fact]
    public void T4_OldPublishedTime_DoesNotChangeKeywordImportance()
    {
        var decision = _scorer.Score(NewsScoringFixtures.T4().Item, ScoringFixtures.Now);

        Assert.True(decision.IsImportant);
        Assert.Equal(NewsImportanceRuleIds.TierOneUrgencyKeyword, decision.RuleId);
    }

    [Fact]
    public void T5_MissingEventTime_UsesPublishedTimeOnlyForImportance()
    {
        var decision = _scorer.Score(NewsScoringFixtures.T5().Item, ScoringFixtures.Now);

        Assert.True(decision.IsImportant);
        Assert.Equal(NewsImportanceRuleIds.TierOneUrgencyKeyword, decision.RuleId);
    }

    [Fact]
    public void WordBoundary_DoesNotMatchWarInsideSoftware()
    {
        var decision = _scorer.Score(NewsScoringFixtures.WordBoundaryFalsePositive().Item, ScoringFixtures.Now);

        Assert.False(decision.IsImportant);
        Assert.Contains(decision.Factors, factor => factor.Contains("no approved urgency keyword", StringComparison.Ordinal));
    }

    [Fact]
    public void WordBoundary_MatchesWarAsAWholeWord()
    {
        var decision = _scorer.Score(NewsScoringFixtures.WordBoundaryTruePositive().Item, ScoringFixtures.Now);

        Assert.True(decision.IsImportant);
        Assert.Equal(NewsImportanceRuleIds.TierOneUrgencyKeyword, decision.RuleId);
        Assert.Contains(decision.Factors, factor => factor.Contains("'war'", StringComparison.Ordinal));
    }

    [Fact]
    public void KeywordMatching_NormalizesCaseAndWhitespace()
    {
        var decision = _scorer.Score(NewsScoringFixtures.CaseAndWhitespaceKeyword().Item, ScoringFixtures.Now);

        Assert.True(decision.IsImportant);
        Assert.Equal(NewsImportanceRuleIds.TierOneUrgencyKeyword, decision.RuleId);
        Assert.Contains(decision.Factors, factor => factor.Contains("'breaking'", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingHeadline_ProducesAnExplicitFactor()
    {
        var decision = _scorer.Score(NewsScoringFixtures.MissingTitle().Item, ScoringFixtures.Now);

        Assert.False(decision.IsImportant);
        Assert.Contains(decision.Factors, factor => factor.Contains("Headline is missing", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownSource_ProducesExplicitClassificationAndMissingTimeFactors()
    {
        var decision = _scorer.Score(NewsScoringFixtures.UnknownSource().Item, ScoringFixtures.Now);

        Assert.False(decision.IsImportant);
        Assert.Contains(decision.Factors, factor => factor.Contains("(unknown source)", StringComparison.Ordinal));
        Assert.Contains(decision.Factors, factor => factor.Contains("unclassified", StringComparison.Ordinal));
        Assert.Contains(decision.Factors, factor => factor.Contains("no publication time", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateCorroboratingSourceIds_CountOnce()
    {
        var fixture = NewsScoringFixtures.DuplicateCorroboratorIds();
        var decision = _scorer.Score(fixture.Item, ScoringFixtures.Now, fixture.CorroboratingSources);

        Assert.False(decision.IsImportant);
        Assert.Contains(decision.Factors, factor => factor.Contains("Duplicate source id", StringComparison.Ordinal));
        Assert.Contains(decision.Factors, factor => factor.Contains("Only 1 independent", StringComparison.Ordinal));
    }

    [Fact]
    public void SameSourceIdInDifferentCase_IsNotIndependent()
    {
        var fixture = NewsScoringFixtures.SameSourceDifferentCase();
        var decision = _scorer.Score(fixture.Item, ScoringFixtures.Now, fixture.CorroboratingSources);

        Assert.False(decision.IsImportant);
        Assert.Contains(decision.Factors, factor => factor.Contains("Duplicate source id", StringComparison.Ordinal));
    }

    [Fact]
    public void FutureCorroboratingPublication_IsExcluded()
    {
        var fixture = NewsScoringFixtures.FutureCorroborator();
        var decision = _scorer.Score(fixture.Item, ScoringFixtures.Now, fixture.CorroboratingSources);

        Assert.False(decision.IsImportant);
        Assert.Contains(decision.Factors, factor => factor.Contains("future publication time", StringComparison.Ordinal));
    }

    [Fact]
    public void FuturePrimaryPublication_IsExcludedFromCorroboration()
    {
        var fixture = NewsScoringFixtures.FuturePrimary();
        var decision = _scorer.Score(fixture.Item, ScoringFixtures.Now, fixture.CorroboratingSources);

        Assert.False(decision.IsImportant);
        Assert.Contains(decision.Factors, factor => factor.Contains("future publication time", StringComparison.Ordinal));
        Assert.Contains(decision.Factors, factor => factor.Contains("Only 1 independent", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingCorroboratingPublicationTime_IsExcluded()
    {
        var fixture = NewsScoringFixtures.MissingCorroboratorTime();
        var decision = _scorer.Score(fixture.Item, ScoringFixtures.Now, fixture.CorroboratingSources);

        Assert.False(decision.IsImportant);
        Assert.Contains(decision.Factors, factor => factor.Contains("no publication time", StringComparison.Ordinal));
    }

    [Fact]
    public void NonTierOneCorroboration_DoesNotSatisfyTheTwoSourceRule()
    {
        var fixture = NewsScoringFixtures.NonTierOneCorroboration();
        var decision = _scorer.Score(fixture.Item, ScoringFixtures.Now, fixture.CorroboratingSources);

        Assert.False(decision.IsImportant);
        Assert.Contains(
            decision.Factors,
            factor => factor.Contains("Ignoring corroborating source 'Nature' because it is not tier-1", StringComparison.Ordinal));
    }

    [Fact]
    public void SpecialistPrimary_CanBeCorroboratedByTwoTierOneSources()
    {
        var fixture = NewsScoringFixtures.SpecialistWithTwoTierOneCorroborators();
        var decision = _scorer.Score(fixture.Item, ScoringFixtures.Now, fixture.CorroboratingSources);

        Assert.True(decision.IsImportant);
        Assert.Equal(NewsImportanceRuleIds.TierOneCorroboration, decision.RuleId);
        Assert.Contains(decision.Factors, factor => factor.Contains("2 independent tier-1 sources", StringComparison.Ordinal));
    }

    [Fact]
    public void FuturePrimary_DoesNotBlockTwoValidTierOneCorroborators()
    {
        var fixture = NewsScoringFixtures.FuturePrimaryWithTwoValidCorroborators();
        var decision = _scorer.Score(fixture.Item, ScoringFixtures.Now, fixture.CorroboratingSources);

        Assert.True(decision.IsImportant);
        Assert.Equal(NewsImportanceRuleIds.TierOneCorroboration, decision.RuleId);
    }

    [Fact]
    public void MissingPrimaryTime_DoesNotBlockTwoValidTierOneCorroborators()
    {
        var fixture = NewsScoringFixtures.MissingPrimaryTimeWithTwoValidCorroborators();
        var decision = _scorer.Score(fixture.Item, ScoringFixtures.Now, fixture.CorroboratingSources);

        Assert.True(decision.IsImportant);
        Assert.Equal(NewsImportanceRuleIds.TierOneCorroboration, decision.RuleId);
    }

    [Fact]
    public void ThreeIndependentTierOneSources_AreReportedInTheFactor()
    {
        var fixture = NewsScoringFixtures.ThreeIndependentTierOneSources();
        var decision = _scorer.Score(fixture.Item, ScoringFixtures.Now, fixture.CorroboratingSources);

        Assert.True(decision.IsImportant);
        Assert.Equal(NewsImportanceRuleIds.TierOneCorroboration, decision.RuleId);
        Assert.Contains(decision.Factors, factor => factor.Contains("3 independent tier-1 sources", StringComparison.Ordinal));
    }

    [Fact]
    public void AnyPairWithinWindow_IsEnough()
    {
        var item = ScoringFixtures.News("bbc-world", "BBC World", "Regional talks resume", "2026-10-21T08:30:00Z");
        var sources = new[]
        {
            ScoringFixtures.Corroboration("nyt-world", "NYT World", "2026-10-21T01:00:00Z"),
            ScoringFixtures.Corroboration("guardian-world", "The Guardian World", "2026-10-21T00:30:00Z"),
        };

        var decision = _scorer.Score(item, ScoringFixtures.Now, sources);

        Assert.True(decision.IsImportant);
        Assert.Equal(NewsImportanceRuleIds.TierOneCorroboration, decision.RuleId);
    }

    [Fact]
    public void NonPositiveCorroborationWindow_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new NewsImportanceScorer(corroborationWindow: TimeSpan.Zero));
    }
}
