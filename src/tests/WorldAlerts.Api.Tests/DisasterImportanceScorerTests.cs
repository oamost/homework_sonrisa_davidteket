using WorldAlerts.Api.Scoring;

namespace WorldAlerts.Api.Tests;

/// <summary>
/// Reproduces the approved task-01 disaster fixtures and hardens the boundary,
/// missing-field, source-matching and combined-rule cases. These are acceptance
/// examples, not universal weights.
/// </summary>
public sealed class DisasterImportanceScorerTests
{
    private readonly DisasterImportanceScorer _scorer = new();

    [Fact]
    public void DS1_GdacsOrangeWithoutMagnitude_IsImportant()
    {
        var decision = _scorer.Score(ScoringFixtures.DS1());

        Assert.True(decision.IsImportant);
        Assert.Equal(DisasterImportanceRuleIds.GdacsOrangeRed, decision.RuleId);
        Assert.Equal(0.80, decision.Score, 6);
        Assert.Contains(decision.Factors, factor => factor.Contains("Orange/Red", StringComparison.Ordinal));
        Assert.Contains(decision.Factors, factor => factor.Contains("magnitude is missing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DS2_GreenEarthquakeAboveSix_IsImportant()
    {
        var decision = _scorer.Score(ScoringFixtures.DS2());

        Assert.True(decision.IsImportant);
        Assert.Equal(DisasterImportanceRuleIds.EarthquakeMagnitude, decision.RuleId);
        Assert.InRange(decision.Score, 0.70, 0.95);
        Assert.True(double.IsFinite(decision.Score));
    }

    [Fact]
    public void DS3_GreenEarthquakeBelowSix_IsNotImportant()
    {
        var decision = _scorer.Score(ScoringFixtures.DS3());

        Assert.False(decision.IsImportant);
        Assert.Equal(DisasterImportanceRuleIds.NoApprovedTrigger, decision.RuleId);
        Assert.Equal(0.0, decision.Score);
    }

    [Fact]
    public void DS4_TsunamiFlagOnGreenFlood_IsImportant()
    {
        var decision = _scorer.Score(ScoringFixtures.DS4());

        Assert.True(decision.IsImportant);
        Assert.Equal(DisasterImportanceRuleIds.TsunamiFlag, decision.RuleId);
    }

    [Fact]
    public void DS5_IfrcSeverityTwo_IsImportant()
    {
        var decision = _scorer.Score(ScoringFixtures.DS5());

        Assert.True(decision.IsImportant);
        Assert.Equal(DisasterImportanceRuleIds.IfrcSeverity, decision.RuleId);
    }

    [Fact]
    public void DS6_IfrcSeverityOne_IsNotImportant()
    {
        var decision = _scorer.Score(ScoringFixtures.DS6());

        Assert.False(decision.IsImportant);
        Assert.Equal(DisasterImportanceRuleIds.NoApprovedTrigger, decision.RuleId);
    }

    [Fact]
    public void DS7_RedAlertWithMissingMagnitudeAndEventTime_IsImportant()
    {
        var decision = _scorer.Score(ScoringFixtures.DS7());

        Assert.True(decision.IsImportant);
        Assert.Equal(DisasterImportanceRuleIds.GdacsOrangeRed, decision.RuleId);
        Assert.Contains(decision.Factors, factor => factor.Contains("magnitude is missing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExactlySixPointZeroMagnitude_IsImportant()
    {
        var disaster = ScoringFixtures.Disaster(
            "gdacs", "earthquake", alertLevel: "Green", severity: 6.0, severityUnit: "M");

        var decision = _scorer.Score(disaster);

        Assert.True(decision.IsImportant);
        Assert.Equal(DisasterImportanceRuleIds.EarthquakeMagnitude, decision.RuleId);
    }

    [Fact]
    public void JustBelowSixPointZeroMagnitude_IsNotImportant()
    {
        var disaster = ScoringFixtures.Disaster(
            "gdacs", "earthquake", alertLevel: "Green", severity: 5.999, severityUnit: "M");

        var decision = _scorer.Score(disaster);

        Assert.False(decision.IsImportant);
        Assert.Equal(DisasterImportanceRuleIds.NoApprovedTrigger, decision.RuleId);
    }

    [Fact]
    public void MissingSeverity_IsReportedExplicitly()
    {
        var disaster = ScoringFixtures.Disaster(
            "gdacs", "earthquake", alertLevel: "Green");

        var decision = _scorer.Score(disaster);

        Assert.False(decision.IsImportant);
        Assert.Contains(decision.Factors, factor => factor.Contains("magnitude is missing", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(decision.Factors, factor => factor.Contains("tsunami flag is missing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MissingIfrcSeverity_IsReportedExplicitly()
    {
        var disaster = ScoringFixtures.Disaster(
            "ifrc-go", "epidemic", severityUnit: "IFRC severity level");

        var decision = _scorer.Score(disaster);

        Assert.False(decision.IsImportant);
        Assert.Contains(decision.Factors, factor => factor.Contains("IFRC severity is missing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MissingAlertLevelAndFields_IsNotImportantWithExplicitFactors()
    {
        var disaster = ScoringFixtures.Disaster("gdacs", "earthquake");

        var decision = _scorer.Score(disaster);

        Assert.False(decision.IsImportant);
        Assert.Contains(decision.Factors, factor => factor.Contains("alert level is missing", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(decision.Factors, factor => factor.Contains("magnitude is missing", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(decision.Factors, factor => factor.Contains("tsunami flag is missing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AlertLevelComparisonIsCaseInsensitive()
    {
        var disaster = ScoringFixtures.Disaster("gdacs", "flood", alertLevel: "ORANGE");

        var decision = _scorer.Score(disaster);

        Assert.True(decision.IsImportant);
        Assert.Equal(DisasterImportanceRuleIds.GdacsOrangeRed, decision.RuleId);
    }

    [Fact]
    public void GreenEarthquakeWithoutAlertLevel_StillUsesMagnitudeRule()
    {
        var disaster = ScoringFixtures.Disaster(
            "usgs", "earthquake", severity: 6.4, severityUnit: "magnitude");

        var decision = _scorer.Score(disaster);

        Assert.True(decision.IsImportant);
        Assert.Equal(DisasterImportanceRuleIds.EarthquakeMagnitude, decision.RuleId);
    }

    [Fact]
    public void NonGreenGdacsAlertLevelDoesNotUseTheMagnitudeFixture()
    {
        var disaster = ScoringFixtures.Disaster(
            "gdacs", "earthquake", alertLevel: "Yellow", severity: 6.5, severityUnit: "M");

        var decision = _scorer.Score(disaster);

        Assert.False(decision.IsImportant);
        Assert.Contains(decision.Factors, factor => factor.Contains("Green/unspecified", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OrangeAlertWinsOverLowMagnitude()
    {
        var disaster = ScoringFixtures.Disaster(
            "gdacs", "earthquake", alertLevel: "Orange", severity: 4.0, severityUnit: "M");

        var decision = _scorer.Score(disaster);

        Assert.True(decision.IsImportant);
        Assert.Equal(DisasterImportanceRuleIds.GdacsOrangeRed, decision.RuleId);
        Assert.Contains(decision.Factors, factor => factor.Contains("not Green/unspecified", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NonGdacsAlertLevelDoesNotTriggerTheGdacsRule()
    {
        var disaster = ScoringFixtures.Disaster(
            "usgs", "earthquake", alertLevel: "Orange", severity: 5.0, severityUnit: "M");

        var decision = _scorer.Score(disaster);

        Assert.False(decision.IsImportant);
        Assert.Equal(DisasterImportanceRuleIds.NoApprovedTrigger, decision.RuleId);
        Assert.Contains(decision.Factors, factor => factor.Contains("Source is not GDACS", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NonGdacsAlertLevelDoesNotSuppressTheMagnitudeRule()
    {
        var disaster = ScoringFixtures.Disaster(
            "usgs", "earthquake", alertLevel: "Yellow", severity: 6.1, severityUnit: "M");

        var decision = _scorer.Score(disaster);

        Assert.True(decision.IsImportant);
        Assert.Equal(DisasterImportanceRuleIds.EarthquakeMagnitude, decision.RuleId);
    }

    [Fact]
    public void MissingAlertOnNonGdacsSource_IsReportedExplicitly()
    {
        var disaster = ScoringFixtures.Disaster("usgs", "flood");

        var decision = _scorer.Score(disaster);

        Assert.False(decision.IsImportant);
        Assert.Contains(decision.Factors, factor => factor.Contains("Alert level is missing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MissingTsunamiFlagOnNonEarthquake_IsReportedExplicitly()
    {
        var disaster = ScoringFixtures.Disaster("gdacs", "flood", alertLevel: "Green");

        var decision = _scorer.Score(disaster);

        Assert.False(decision.IsImportant);
        Assert.Contains(decision.Factors, factor => factor.Contains("Tsunami flag is missing", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(decision.Factors, factor => factor.Contains("Severity is missing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TsunamiFalseOnGreenFlood_IsNotImportant()
    {
        var disaster = ScoringFixtures.Disaster(
            "gdacs", "flood", alertLevel: "Green", tsunami: false);

        var decision = _scorer.Score(disaster);

        Assert.False(decision.IsImportant);
        Assert.Contains(decision.Factors, factor => factor.Contains("Tsunami flag is not set", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Flood")]
    [InlineData("EPIDEMIC")]
    [InlineData("Pluvial/Flash Flood")]
    public void IfrcEventTypeCasingDoesNotAffectTheSeverityRule(string eventType)
    {
        var disaster = ScoringFixtures.Disaster(
            "ifrc-go", eventType, severity: 2, severityUnit: "IFRC severity level");

        var decision = _scorer.Score(disaster);

        Assert.True(decision.IsImportant);
        Assert.Equal(DisasterImportanceRuleIds.IfrcSeverity, decision.RuleId);
    }

    [Fact]
    public void IfrcEarthquakeTypeBelowSeverityTwo_DoesNotUseTheMagnitudeFixture()
    {
        var disaster = ScoringFixtures.Disaster(
            "ifrc-go", "Earthquake", severity: 1.5, severityUnit: "IFRC severity level");

        var decision = _scorer.Score(disaster);

        Assert.False(decision.IsImportant);
        Assert.Equal(DisasterImportanceRuleIds.NoApprovedTrigger, decision.RuleId);
        Assert.Contains(decision.Factors, factor => factor.Contains("not earthquake magnitude", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(decision.Factors, factor => factor.Contains("IFRC severity 1.5 is below", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void IfrcSeverityUnitIsDetectedCaseInsensitively()
    {
        var disaster = ScoringFixtures.Disaster(
            "humanitarian-feed", "Epidemic", severity: 2.5, severityUnit: "ifrc SEVERITY level");

        var decision = _scorer.Score(disaster);

        Assert.True(decision.IsImportant);
        Assert.Equal(DisasterImportanceRuleIds.IfrcSeverity, decision.RuleId);
    }

    [Fact]
    public void IfrcSourceIdAloneIsEnoughForTheSeverityRule()
    {
        var disaster = ScoringFixtures.Disaster(
            "ifrc-go", "Flood", severity: 2.0);

        var decision = _scorer.Score(disaster);

        Assert.True(decision.IsImportant);
        Assert.Equal(DisasterImportanceRuleIds.IfrcSeverity, decision.RuleId);
    }

    [Fact]
    public void UnrelatedSourceContainingIfrcIsNotIfrc()
    {
        var disaster = ScoringFixtures.Disaster(
            "not-ifrc", "Epidemic", severity: 3, severityUnit: "count");

        var decision = _scorer.Score(disaster);

        Assert.False(decision.IsImportant);
        Assert.Contains(decision.Factors, factor => factor.Contains("not IFRC", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UnrelatedSourceContainingGdacsIsNotGdacs()
    {
        var disaster = ScoringFixtures.Disaster(
            "not-gdacs", "flood", alertLevel: "Orange");

        var decision = _scorer.Score(disaster);

        Assert.False(decision.IsImportant);
        Assert.Contains(decision.Factors, factor => factor.Contains("Source is not GDACS", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GdacsSourceIdVariantIsRecognized()
    {
        var disaster = ScoringFixtures.Disaster(
            "gdacs-rss", "flood", alertLevel: "Red");

        var decision = _scorer.Score(disaster);

        Assert.True(decision.IsImportant);
        Assert.Equal(DisasterImportanceRuleIds.GdacsOrangeRed, decision.RuleId);
    }

    [Fact]
    public void NonGdacsAlertAndNonIfrcSeverityUnitDoNotTriggerRules()
    {
        var disaster = ScoringFixtures.Disaster(
            "nws-alerts", "Heat Advisory", alertLevel: "Extreme", severity: 3, severityUnit: "NWS severity");

        var decision = _scorer.Score(disaster);

        Assert.False(decision.IsImportant);
        Assert.Equal(DisasterImportanceRuleIds.NoApprovedTrigger, decision.RuleId);
        Assert.Contains(decision.Factors, factor => factor.Contains("not used by the GDACS rule", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(decision.Factors, factor => factor.Contains("not IFRC", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Earthquake")]
    [InlineData("earthquakes")]
    [InlineData("EQ")]
    [InlineData("quake")]
    [InlineData("Earthquake (M6.0)")]
    public void EarthquakeTypeVariantsUseTheMagnitudeRule(string eventType)
    {
        var disaster = ScoringFixtures.Disaster(
            "usgs", eventType, severity: 6.0, severityUnit: "M");

        var decision = _scorer.Score(disaster);

        Assert.True(decision.IsImportant);
        Assert.Equal(DisasterImportanceRuleIds.EarthquakeMagnitude, decision.RuleId);
    }

    [Fact]
    public void TsunamiCombinedWithGdacsOrange_ReportsTheGdacsRule()
    {
        var disaster = ScoringFixtures.Disaster(
            "gdacs", "earthquake", alertLevel: "Orange", tsunami: true);

        var decision = _scorer.Score(disaster);

        Assert.True(decision.IsImportant);
        Assert.Equal(DisasterImportanceRuleIds.GdacsOrangeRed, decision.RuleId);
        Assert.Contains(decision.Factors, factor => factor.Contains("Orange/Red", StringComparison.Ordinal));
        Assert.Contains(decision.Factors, factor => factor.Contains("Tsunami flag is set", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TsunamiCombinedWithGreenMagnitude_ReportsTheTsunamiRule()
    {
        var disaster = ScoringFixtures.Disaster(
            "gdacs", "earthquake", alertLevel: "Green", severity: 6.5, severityUnit: "M", tsunami: true);

        var decision = _scorer.Score(disaster);

        Assert.True(decision.IsImportant);
        Assert.Equal(DisasterImportanceRuleIds.TsunamiFlag, decision.RuleId);
        Assert.Contains(decision.Factors, factor => factor.Contains("Tsunami flag is set", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(decision.Factors, factor => factor.Contains("at or above 6", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TsunamiCombinedWithIfrcSeverity_ReportsTheTsunamiRule()
    {
        var disaster = ScoringFixtures.Disaster(
            "ifrc-go", "epidemic", severity: 3, severityUnit: "IFRC severity level", tsunami: true);

        var decision = _scorer.Score(disaster);

        Assert.True(decision.IsImportant);
        Assert.Equal(DisasterImportanceRuleIds.TsunamiFlag, decision.RuleId);
        Assert.Contains(decision.Factors, factor => factor.Contains("Tsunami flag is set", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(decision.Factors, factor => factor.Contains("IFRC severity 3 is at or above", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TsunamiTrueWithMissingMagnitude_IsImportantAndReportsMissingMagnitude()
    {
        var disaster = ScoringFixtures.Disaster(
            "gdacs", "earthquake", alertLevel: "Green", tsunami: true);

        var decision = _scorer.Score(disaster);

        Assert.True(decision.IsImportant);
        Assert.Equal(DisasterImportanceRuleIds.TsunamiFlag, decision.RuleId);
        Assert.Contains(decision.Factors, factor => factor.Contains("magnitude is missing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NullDisasterIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => _scorer.Score(null!));
    }
}
