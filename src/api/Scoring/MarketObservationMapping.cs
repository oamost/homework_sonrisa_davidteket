using System.Globalization;
using WorldAlerts.Api.Markets;

namespace WorldAlerts.Api.Scoring;

/// <summary>
/// Maps configured market source kinds and interval labels to the three
/// approved scoring families, and builds a <see cref="MarketImportanceInput"/>
/// from a stored <see cref="MarketObservation"/>.
/// </summary>
/// <remarks>
/// <para>
/// The stored observation does not carry the configured source kind, so a
/// family cannot be inferred from an observation alone. Callers that know the
/// configured <c>MarketSource.Kind</c> use
/// <see cref="FamilyForSourceKind"/>; instrument names and venues are not
/// guessed. The six sanctioned kinds map to the three approved families:
/// <c>binance-ticker</c> and <c>binance-klines</c> to crypto,
/// <c>frankfurter</c> to FX, and <c>cboe</c>, <c>twse</c> and <c>moex</c> to
/// indices.
/// </para>
/// <para>
/// The interval label is the provider sampling cadence. A <c>quote</c> label
/// has no fixed duration, so it produces <see cref="TimeSpan.Zero"/> unless the
/// caller supplies an explicit move horizon. The scorer then reports
/// <see cref="MarketImportanceRuleIds.InvalidInterval"/> instead of guessing a
/// daily horizon. Likewise, a one-hour candle is passed through as a one-hour
/// move; a caller that wants the approved 24-hour crypto fixture must supply a
/// 24-hour baseline and interval explicitly.
/// </para>
/// </remarks>
public static class MarketObservationMapping
{
    /// <summary>The approved fixture interval for every family, in hours.</summary>
    private const int ApprovedIntervalHours = 24;

    /// <summary>
    /// Maps a configured market source kind to an approved scoring family.
    /// Returns null for an unknown or missing kind; it does not guess from the
    /// instrument or venue.
    /// </summary>
    /// <param name="sourceKind">Configured <c>MarketSource.Kind</c> value.</param>
    public static MarketFamily? FamilyForSourceKind(string? sourceKind)
    {
        if (string.IsNullOrWhiteSpace(sourceKind))
        {
            return null;
        }

        return sourceKind.Trim().ToLowerInvariant() switch
        {
            MarketParser.BinanceTicker or MarketParser.BinanceKlines => MarketFamily.Crypto,
            MarketParser.Frankfurter => MarketFamily.Fx,
            MarketParser.Cboe or MarketParser.Twse or MarketParser.Moex => MarketFamily.Index,
            _ => null,
        };
    }

    /// <summary>
    /// Parses a configured interval label into a positive duration. Supports
    /// <c>Nh</c>, <c>Nd</c>, <c>daily</c> and the configured <c>24h</c>,
    /// <c>1h</c> and <c>1d</c> labels. A <c>quote</c> label has no fixed
    /// duration and returns false.
    /// </summary>
    /// <param name="interval">Configured <c>MarketSource.Interval</c> value.</param>
    /// <param name="value">Parsed duration when the method returns true.</param>
    public static bool TryParseInterval(string? interval, out TimeSpan value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(interval))
        {
            return false;
        }

        var trimmed = interval.Trim();
        if (string.Equals(trimmed, "quote", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(trimmed, "daily", StringComparison.OrdinalIgnoreCase))
        {
            value = TimeSpan.FromDays(1);
            return true;
        }

        if (trimmed.Length < 2)
        {
            return false;
        }

        var unit = trimmed[^1];
        if (unit is not ('h' or 'H' or 'd' or 'D'))
        {
            return false;
        }

        if (!int.TryParse(
                trimmed.AsSpan(0, trimmed.Length - 1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var count)
            || count <= 0)
        {
            return false;
        }

        try
        {
            value = unit is 'h' or 'H'
                ? TimeSpan.FromHours(count)
                : TimeSpan.FromDays(count);
            return true;
        }
        catch (OverflowException)
        {
            value = default;
            return false;
        }
    }

    /// <summary>
    /// Returns the approved fixture interval for a family. All three approved
    /// fixtures are 24 hours; this helper exists so callers do not hard-code
    /// the number separately.
    /// </summary>
    /// <param name="family">Market family.</param>
    /// <exception cref="ArgumentOutOfRangeException">The family is not defined.</exception>
    public static TimeSpan ApprovedIntervalFor(MarketFamily family)
        => family switch
        {
            MarketFamily.Crypto or MarketFamily.Fx or MarketFamily.Index
                => TimeSpan.FromHours(ApprovedIntervalHours),
            _ => throw new ArgumentOutOfRangeException(nameof(family), family, "Unknown market family."),
        };

    /// <summary>
    /// Builds a scoring input from one stored observation and a caller-supplied
    /// baseline. The observation supplies the current value and observation
    /// time; it never supplies a baseline or an evaluation time.
    /// </summary>
    /// <param name="family">Approved family selected by the caller.</param>
    /// <param name="observation">Stored observation.</param>
    /// <param name="baselineValue">Previous value for the move horizon, or null when unavailable.</param>
    /// <param name="nowUtc">Caller's evaluation time; no clock is read here.</param>
    /// <param name="observationInterval">
    /// Explicit move horizon. When null, the observation's interval label is
    /// parsed; a <c>quote</c> label becomes <see cref="TimeSpan.Zero"/> so the
    /// scorer reports the missing horizon explicitly.
    /// </param>
    public static MarketImportanceInput Create(
        MarketFamily family,
        MarketObservation observation,
        double? baselineValue,
        DateTimeOffset nowUtc,
        TimeSpan? observationInterval = null)
    {
        ArgumentNullException.ThrowIfNull(observation);

        var interval = observationInterval
            ?? (TryParseInterval(observation.Interval, out var parsed) ? parsed : TimeSpan.Zero);

        return new MarketImportanceInput(
            family,
            observation.Value,
            baselineValue,
            interval,
            observation.ObservationUtc,
            nowUtc);
    }
}
