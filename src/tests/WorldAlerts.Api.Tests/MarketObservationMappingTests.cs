using WorldAlerts.Api.Markets;
using WorldAlerts.Api.Scoring;

namespace WorldAlerts.Api.Tests;

/// <summary>
/// Verifies the source-kind and interval mapping used to turn stored market
/// observations into the three approved scoring families. The fixtures are
/// offline and contain no provider calls.
/// </summary>
public sealed class MarketObservationMappingTests
{
    [Theory]
    [InlineData(MarketParser.BinanceTicker, MarketFamily.Crypto)]
    [InlineData(MarketParser.BinanceKlines, MarketFamily.Crypto)]
    [InlineData(MarketParser.Frankfurter, MarketFamily.Fx)]
    [InlineData(MarketParser.Cboe, MarketFamily.Index)]
    [InlineData(MarketParser.Twse, MarketFamily.Index)]
    [InlineData(MarketParser.Moex, MarketFamily.Index)]
    public void FamilyForSourceKind_MapsEverySanctionedKind(string kind, MarketFamily expected)
    {
        Assert.Equal(expected, MarketObservationMapping.FamilyForSourceKind(kind));
    }

    [Theory]
    [InlineData(" BINANCE-TICKER ", MarketFamily.Crypto)]
    [InlineData("Frankfurter", MarketFamily.Fx)]
    [InlineData("TWSE", MarketFamily.Index)]
    public void FamilyForSourceKind_IsCaseInsensitiveAndTrims(string kind, MarketFamily expected)
    {
        Assert.Equal(expected, MarketObservationMapping.FamilyForSourceKind(kind));
    }

    [Fact]
    public void FamilyForSourceKind_UnknownOrMissingKind_ReturnsNull()
    {
        Assert.Null(MarketObservationMapping.FamilyForSourceKind(null));
        Assert.Null(MarketObservationMapping.FamilyForSourceKind(string.Empty));
        Assert.Null(MarketObservationMapping.FamilyForSourceKind("   "));
        Assert.Null(MarketObservationMapping.FamilyForSourceKind("unknown-provider"));
    }

    [Theory]
    [InlineData("24h", 24)]
    [InlineData("1h", 1)]
    [InlineData("1d", 24)]
    [InlineData("daily", 24)]
    [InlineData("2D", 48)]
    public void TryParseInterval_MapsKnownLabels(string label, int expectedHours)
    {
        Assert.True(MarketObservationMapping.TryParseInterval(label, out var value));
        Assert.Equal(TimeSpan.FromHours(expectedHours), value);
    }

    [Theory]
    [InlineData("quote")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0h")]
    [InlineData("-1h")]
    [InlineData("h")]
    [InlineData("1m")]
    public void TryParseInterval_RejectsLabelsWithoutAPositiveFixedDuration(string label)
    {
        Assert.False(MarketObservationMapping.TryParseInterval(label, out var value));
        Assert.Equal(TimeSpan.Zero, value);
    }

    [Fact]
    public void ApprovedIntervalFor_EveryFamily_IsTwentyFourHours()
    {
        Assert.All(
            Enum.GetValues<MarketFamily>(),
            family => Assert.Equal(
                TimeSpan.FromHours(24),
                MarketObservationMapping.ApprovedIntervalFor(family)));
    }

    [Fact]
    public void Create_FromBinanceTicker_WithSuppliedTwentyFourHourBaseline_MeetsApprovedCryptoThreshold()
    {
        var observation = Observation(MarketParser.BinanceTicker, "24h", 106.2);

        var input = MarketObservationMapping.Create(
            MarketFamily.Crypto, observation, 100.0, ScoringFixtures.Now);

        Assert.Equal(MarketFamily.Crypto, input.Family);
        Assert.Equal(observation.Value, input.CurrentValue);
        Assert.Equal(observation.ObservationUtc, input.ObservationUtc);
        Assert.Equal(100.0, input.BaselineValue);
        Assert.Equal(TimeSpan.FromHours(24), input.ObservationInterval);
        Assert.Equal(ScoringFixtures.Now, input.NowUtc);

        var decision = new MarketImportanceScorer().Score(input);

        Assert.True(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.MoveThreshold, decision.RuleId);
    }

    [Fact]
    public void Create_FromKlinesOneHour_IsUnscorableAgainstTwentyFourHourThreshold()
    {
        var observation = Observation(MarketParser.BinanceKlines, "1h", 110.0);

        var input = MarketObservationMapping.Create(
            MarketFamily.Crypto, observation, 100.0, ScoringFixtures.Now);

        Assert.Equal(TimeSpan.FromHours(1), input.ObservationInterval);

        var decision = new MarketImportanceScorer().Score(input);

        Assert.False(decision.IsImportant);
        Assert.Equal(0.0, decision.Score);
        Assert.Equal(MarketImportanceRuleIds.InvalidInterval, decision.RuleId);
        Assert.Contains(
            decision.Factors,
            factor => factor.Contains("1h", StringComparison.OrdinalIgnoreCase)
                && factor.Contains("24h", StringComparison.OrdinalIgnoreCase)
                && factor.Contains("baseline", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Create_FromTwseEndOfDay_UsesDailyInterval()
    {
        var observation = Observation(MarketParser.Twse, "1d", 1024.0);

        var input = MarketObservationMapping.Create(
            MarketFamily.Index, observation, 1000.0, ScoringFixtures.Now);

        Assert.Equal(TimeSpan.FromHours(24), input.ObservationInterval);

        var decision = new MarketImportanceScorer().Score(input);

        Assert.True(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.MoveThreshold, decision.RuleId);
    }

    [Fact]
    public void Create_FromQuoteWithoutExplicitHorizon_ProducesInvalidIntervalDecision()
    {
        var observation = Observation(MarketParser.Cboe, "quote", 1024.0);

        var input = MarketObservationMapping.Create(
            MarketFamily.Index, observation, 1000.0, ScoringFixtures.Now);

        Assert.Equal(TimeSpan.Zero, input.ObservationInterval);

        var decision = new MarketImportanceScorer().Score(input);

        Assert.False(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.InvalidInterval, decision.RuleId);
    }

    [Fact]
    public void Create_FromQuoteWithExplicitDailyHorizon_IsScoreable()
    {
        var observation = Observation(MarketParser.Moex, "quote", 1024.0);

        var input = MarketObservationMapping.Create(
            MarketFamily.Index,
            observation,
            1000.0,
            ScoringFixtures.Now,
            TimeSpan.FromHours(24));

        Assert.Equal(TimeSpan.FromHours(24), input.ObservationInterval);

        var decision = new MarketImportanceScorer().Score(input);

        Assert.True(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.MoveThreshold, decision.RuleId);
    }

    [Fact]
    public void Create_MissingBaseline_ProducesExplicitMissingBaselineDecision()
    {
        var observation = Observation(MarketParser.Frankfurter, "1d", 1.013);

        var input = MarketObservationMapping.Create(
            MarketFamily.Fx, observation, null, ScoringFixtures.Now);

        Assert.Equal(TimeSpan.FromHours(24), input.ObservationInterval);

        var decision = new MarketImportanceScorer().Score(input);

        Assert.False(decision.IsImportant);
        Assert.Equal(MarketImportanceRuleIds.MissingBaseline, decision.RuleId);
        Assert.Contains(decision.Factors, factor => factor.Contains("baseline", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Create_NullObservation_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            MarketObservationMapping.Create(MarketFamily.Crypto, null!, 100.0, ScoringFixtures.Now));
    }

    private static MarketObservation Observation(
        string sourceId,
        string interval,
        double value,
        DateTimeOffset? observationUtc = null)
    {
        var observed = observationUtc ?? ScoringFixtures.Now.AddHours(-1);
        return new MarketObservation(
            SourceId: sourceId,
            Venue: "Test Venue",
            Region: "Test Region",
            Instrument: "TEST",
            QuoteUnit: "units",
            Value: value,
            ObservationUtc: observed,
            FetchedUtc: observed.AddMinutes(1),
            Delayed: false,
            DelaySemantics: "test semantics",
            Interval: interval,
            Provenance: "https://example.test/market");
    }
}
