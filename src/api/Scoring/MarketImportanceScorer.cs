using System.Collections.ObjectModel;
using System.Globalization;

namespace WorldAlerts.Api.Scoring;

/// <summary>Market family used to select the approved acceptance fixture.</summary>
public enum MarketFamily
{
    /// <summary>Crypto markets, approved fixture 5% over a 24-hour interval.</summary>
    Crypto,

    /// <summary>Foreign exchange, approved fixture 1% over a daily interval.</summary>
    Fx,

    /// <summary>Equity indices, approved fixture 2% over a daily interval.</summary>
    Index,
}

/// <summary>One configured percentage threshold and the fixture interval it belongs to.</summary>
/// <param name="Family">Market family.</param>
/// <param name="Percent">Absolute percentage move threshold.</param>
/// <param name="Interval">Fixture interval; the approved defaults are all 24 hours.</param>
public sealed record MarketThreshold(MarketFamily Family, double Percent, TimeSpan Interval);

/// <summary>
/// Inputs for the deterministic market rule: current and baseline values, the
/// interval over which the move is measured, the provider observation time and
/// the caller's evaluation time.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="BaselineValue"/> is nullable because none of the ingested
/// observations stores a previous value. A caller must supply a baseline from
/// its own history; when it is absent the scorer returns an explicit
/// <see cref="MarketImportanceRuleIds.MissingBaseline"/> decision rather than
/// guessing one.
/// </para>
/// <para>
/// <see cref="ObservationInterval"/> is the horizon of the move, not the
/// provider's sampling cadence. A one-hour candle is a one-hour observation;
/// a caller that wants the approved 24-hour crypto fixture must supply a
/// 24-hour baseline and a 24-hour interval.
/// </para>
/// <para>
/// No clock is read by the scorer. <see cref="NowUtc"/> is the only evaluation
/// time, which keeps every decision reproducible from its input.
/// </para>
/// </remarks>
public sealed record MarketImportanceInput(
    MarketFamily Family,
    double CurrentValue,
    double? BaselineValue,
    TimeSpan ObservationInterval,
    DateTimeOffset ObservationUtc,
    DateTimeOffset NowUtc);

/// <summary>
/// Deterministic market importance scorer for the approved acceptance fixtures.
/// </summary>
/// <remarks>
/// <para>
/// The approved examples in
/// <c>docs/decisions/2026-10-21-task-01-acceptance-boundaries.md</c> are:
/// crypto 5% over 24 hours, FX 1% daily, indices 2% daily, and an observation
/// fresher than twice its interval. Those numbers are acceptance fixtures, not
/// universal weights. No clock is read inside this class; callers supply
/// <c>nowUtc</c>.
/// </para>
/// <para>
    /// The percentage move is compared without rounding. Decimal arithmetic
    /// handles representable market values at exact decimal boundaries; extreme
    /// doubles outside decimal's range use a strict floating-point comparison.
/// </para>
/// <para>
/// Non-positive current or baseline values are not silently discarded: the
/// approved rule is an absolute percentage move, so the supplied finite values
/// are used as-is and the factors say so. A zero baseline is the only value
/// that makes the percentage undefined and therefore returns
/// <see cref="MarketImportanceRuleIds.InvalidBaseline"/>.
/// </para>
/// </remarks>
public sealed class MarketImportanceScorer
{
    private readonly IReadOnlyDictionary<MarketFamily, MarketThreshold> _thresholds;

    /// <summary>Creates a scorer with the approved default thresholds.</summary>
    public MarketImportanceScorer()
        : this(null)
    {
    }

    /// <summary>Creates a scorer, optionally overriding every family threshold.</summary>
    /// <param name="thresholds">Thresholds for all three families; null uses the approved defaults.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A threshold percentage or interval is non-positive or non-finite, or a
    /// family is not defined.
    /// </exception>
    /// <exception cref="ArgumentException">A family is missing or supplied more than once.</exception>
    public MarketImportanceScorer(IEnumerable<MarketThreshold>? thresholds)
    {
        var source = thresholds ?? DefaultThresholds.Values;
        var map = new Dictionary<MarketFamily, MarketThreshold>();
        foreach (var threshold in source)
        {
            if (threshold is null)
            {
                throw new ArgumentException("Threshold entries cannot be null.", nameof(thresholds));
            }

            if (!Enum.IsDefined(threshold.Family))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(thresholds),
                    threshold.Family,
                    "The threshold family is not a defined market family.");
            }

            if (!double.IsFinite(threshold.Percent) || threshold.Percent <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(thresholds),
                    "Percentage thresholds must be finite and positive.");
            }

            if (threshold.Interval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(thresholds),
                    "Threshold intervals must be positive.");
            }

            if (!map.TryAdd(threshold.Family, threshold))
            {
                throw new ArgumentException(
                    $"A threshold for {threshold.Family} was supplied more than once.",
                    nameof(thresholds));
            }
        }

        foreach (var family in Enum.GetValues<MarketFamily>())
        {
            if (!map.ContainsKey(family))
            {
                throw new ArgumentException($"A threshold is required for {family}.", nameof(thresholds));
            }
        }

        _thresholds = new ReadOnlyDictionary<MarketFamily, MarketThreshold>(map);
    }

    /// <summary>The approved default thresholds.</summary>
    public static IReadOnlyDictionary<MarketFamily, MarketThreshold> DefaultThresholds { get; } =
        new ReadOnlyDictionary<MarketFamily, MarketThreshold>(
            new Dictionary<MarketFamily, MarketThreshold>
            {
                [MarketFamily.Crypto] = new(MarketFamily.Crypto, 5.0, TimeSpan.FromHours(24)),
                [MarketFamily.Fx] = new(MarketFamily.Fx, 1.0, TimeSpan.FromHours(24)),
                [MarketFamily.Index] = new(MarketFamily.Index, 2.0, TimeSpan.FromHours(24)),
            });

    /// <summary>Scores one market observation against the approved rules.</summary>
    /// <param name="input">Current value, baseline, horizon, observation time and evaluation time.</param>
    /// <returns>
    /// An <see cref="ImportanceDecision"/> whose score is exactly 1.0 or 0.0 and
    /// whose factors explain every missing or invalid metadata field.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The input family has no configured threshold.</exception>
    public ImportanceDecision Score(MarketImportanceInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!_thresholds.TryGetValue(input.Family, out var threshold))
        {
            throw new ArgumentOutOfRangeException(
                nameof(input),
                input.Family,
                "No threshold is configured for the market family.");
        }

        // A non-finite current value cannot participate in a percentage move.
        // There is no dedicated shared rule id for this case yet; the explicit
        // factor carries the detail and the decision is conservatively false.
        if (!double.IsFinite(input.CurrentValue))
        {
            return ImportanceDecision.NotImportant(
                MarketImportanceRuleIds.BelowThreshold,
                "Current value is not a finite number, so the absolute move is undefined and cannot meet the threshold.");
        }

        if (input.BaselineValue is null)
        {
            var reasons = new List<string>
            {
                "No previous/baseline value was supplied, so the absolute move cannot be calculated.",
            };
            if (input.ObservationInterval > TimeSpan.Zero && input.ObservationInterval != threshold.Interval)
            {
                reasons.Add($"The supplied {FormatHours(input.ObservationInterval)} move interval also differs from "
                    + $"the approved {FormatHours(threshold.Interval)} {FamilyLabel(input.Family)} horizon; "
                    + "a matching baseline and interval are required.");
            }

            return ImportanceDecision.NotImportant(
                MarketImportanceRuleIds.MissingBaseline,
                reasons.ToArray());
        }

        var baseline = input.BaselineValue.Value;
        if (!double.IsFinite(baseline))
        {
            return ImportanceDecision.NotImportant(
                MarketImportanceRuleIds.InvalidBaseline,
                "The baseline value is not a finite number, so a percentage move is undefined.");
        }

        if (baseline == 0)
        {
            return ImportanceDecision.NotImportant(
                MarketImportanceRuleIds.InvalidBaseline,
                "The baseline value is zero, so a percentage move is undefined.");
        }

        if (input.ObservationInterval <= TimeSpan.Zero)
        {
            return ImportanceDecision.NotImportant(
                MarketImportanceRuleIds.InvalidInterval,
                "The observation interval must be positive to evaluate freshness.");
        }

        if (input.ObservationInterval != threshold.Interval)
        {
            return ImportanceDecision.NotImportant(
                MarketImportanceRuleIds.InvalidInterval,
                $"Observation interval {FormatHours(input.ObservationInterval)} does not match the approved "
                + $"{FormatHours(threshold.Interval)} {FamilyLabel(input.Family)} move horizon; a matching baseline "
                + "and interval are needed before this threshold can be applied.");
        }

        if (input.ObservationUtc == default || input.NowUtc == default)
        {
            return ImportanceDecision.NotImportant(
                MarketImportanceRuleIds.StaleObservation,
                "Observation or evaluation timestamp is missing; freshness cannot be established.");
        }

        // Keep the raw value for the comparison. Rounding here would let a
        // value such as 4.999999% masquerade as the approved 5% fixture.
        var movePercent = Math.Abs(input.CurrentValue - baseline) / Math.Abs(baseline) * 100.0;

        var factors = new List<string>
        {
            $"Absolute move is {FormatNumber(movePercent)}% over {FormatHours(input.ObservationInterval)} "
            + $"(configured {FormatNumber(threshold.Percent)}% threshold for {FamilyLabel(input.Family)}).",
        };

        if (input.CurrentValue == 0)
        {
            factors.Add("Current value is zero; the absolute move is computed from the supplied values.");
        }
        else if (input.CurrentValue < 0)
        {
            factors.Add("Current value is negative; the absolute move is computed from the supplied values.");
        }

        if (baseline < 0)
        {
            factors.Add("Baseline is negative; its absolute value is used as the percentage denominator.");
        }

        var age = input.NowUtc - input.ObservationUtc;
        if (age < TimeSpan.Zero)
        {
            factors.Add("Observation time is in the future relative to the evaluation time; freshness cannot be established.");
            return ImportanceDecision.NotImportant(MarketImportanceRuleIds.StaleObservation, factors.ToArray());
        }

        // Compare in hours rather than multiplying the TimeSpan, which can
        // overflow for an extreme interval. "Fresher than twice" is strict:
        // an age exactly equal to twice the interval is stale.
        var freshnessLimitHours = input.ObservationInterval.TotalHours * 2.0;
        if (age.TotalHours >= freshnessLimitHours)
        {
            factors.Add(
                $"Observation is stale: age {FormatHours(age.TotalHours)} is not newer than twice its "
                + $"{FormatHours(input.ObservationInterval)} interval ({FormatHours(freshnessLimitHours)}).");
            return ImportanceDecision.NotImportant(MarketImportanceRuleIds.StaleObservation, factors.ToArray());
        }

        factors.Add(
            $"Observation is fresh: age {FormatHours(age.TotalHours)} is newer than twice its "
            + $"{FormatHours(input.ObservationInterval)} interval ({FormatHours(freshnessLimitHours)}).");

        if (MeetsThreshold(input.CurrentValue, baseline, threshold.Percent))
        {
            factors.Add($"Move meets the configured {FormatNumber(threshold.Percent)}% threshold.");
            var ratio = double.IsFinite(movePercent) ? movePercent / threshold.Percent : double.PositiveInfinity;
            var excess = double.IsFinite(ratio) ? Math.Clamp((ratio - 1) / (ratio + 1), 0, 1) : 1;
            var freshness = Math.Clamp(1 - age.TotalHours / freshnessLimitHours, 0, 1);
            var strength = 0.60 + 0.30 * excess + 0.05 * freshness;
            factors.Add("Strength heuristic: 0.60 at approved move threshold, up to 0.30 for relative excess "
                + "(ratio−1)/(ratio+1), up to 0.05 for remaining freshness; eligibility uses raw values.");
            return ImportanceDecision.Important(MarketImportanceRuleIds.MoveThreshold, strength, factors.ToArray());
        }

        factors.Add($"Move is below the configured {FormatNumber(threshold.Percent)}% threshold.");
        var belowRatio = double.IsFinite(movePercent) ? Math.Clamp(movePercent / threshold.Percent, 0, 1) : 0;
        factors.Add("Strength heuristic: below-threshold move scaled to at most 0.45; not an importance trigger.");
        return ImportanceDecision.NotImportantWithStrength(MarketImportanceRuleIds.BelowThreshold,
            0.45 * belowRatio, factors.ToArray());
    }

    private static bool MeetsThreshold(double current, double baseline, double thresholdPercent)
    {
        try
        {
            // A direct double-to-decimal cast rounds to roughly 15 significant
            // digits, which can erase a real below-threshold difference.
            // Round-trip text retains all digits needed to distinguish doubles.
            if (!decimal.TryParse(current.ToString("R", CultureInfo.InvariantCulture),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalCurrent)
                || !decimal.TryParse(baseline.ToString("R", CultureInfo.InvariantCulture),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalBaseline)
                || !decimal.TryParse(thresholdPercent.ToString("R", CultureInfo.InvariantCulture),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalThreshold))
            {
                return Math.Abs(current / baseline - 1.0) * 100.0 >= thresholdPercent;
            }
            // Decimal conversions can round extremely small doubles to zero;
            // use floating-point arithmetic for values outside decimal precision.
            if (decimalBaseline != 0 && (current == 0 || decimalCurrent != 0))
            {
                var difference = Math.Abs(decimalCurrent - decimalBaseline);
                return difference >= Math.Abs(decimalBaseline) * decimalThreshold / 100m;
            }
        }
        catch (OverflowException)
        {
            // Large finite doubles cannot be represented by decimal.
        }

        return Math.Abs(current / baseline - 1.0) * 100.0 >= thresholdPercent;
    }

    private static string FamilyLabel(MarketFamily family)
        => family switch
        {
            MarketFamily.Crypto => "crypto",
            MarketFamily.Fx => "FX",
            MarketFamily.Index => "indices",
            _ => family.ToString(),
        };

    private static string FormatNumber(double value)
        => value.ToString("0.############", CultureInfo.InvariantCulture);

    private static string FormatHours(TimeSpan value)
        => FormatHours(value.TotalHours);

    private static string FormatHours(double value)
        => FormatNumber(value) + "h";
}
