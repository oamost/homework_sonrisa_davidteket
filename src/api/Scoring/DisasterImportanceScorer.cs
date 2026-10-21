using System.Globalization;
using WorldAlerts.Api.Disasters;

namespace WorldAlerts.Api.Scoring;

/// <summary>
/// Deterministic disaster importance scorer for the approved acceptance fixtures.
/// </summary>
/// <remarks>
/// <para>
/// The approved examples in
/// <c>docs/decisions/2026-10-21-task-01-acceptance-boundaries.md</c> are:
/// GDACS Orange/Red is always important; a Green/unspecified earthquake at
/// magnitude 6.0 or above is important; any tsunami flag is important; an IFRC
/// event with severity 2 or above is important. Those numbers are acceptance
/// fixtures, not universal weights.
/// </para>
/// <para>
/// Missing fields are reported explicitly instead of being invented. The rule is
/// time-independent, so no clock is read and no <c>nowUtc</c> is required; 24-hour
/// eligibility is a separate concern.
/// </para>
/// <para>
/// When more than one approved rule matches, the decision reports the first rule
/// in this fixed precedence order: GDACS Orange/Red, tsunami flag, earthquake
/// magnitude, IFRC severity. Every matched rule still contributes a factor.
/// </para>
/// </remarks>
public sealed class DisasterImportanceScorer
{
    /// <summary>The approved earthquake magnitude fixture (inclusive).</summary>
    public const double EarthquakeMagnitudeThreshold = 6.0;

    /// <summary>The approved IFRC severity fixture (inclusive).</summary>
    public const double IfrcSeverityThreshold = 2.0;

    private static readonly HashSet<string> EarthquakeTypeTokens = new(StringComparer.Ordinal)
    {
        "earthquake",
        "earthquakes",
        "eq",
        "quake",
        "quakes",
    };

    /// <summary>Scores one normalized disaster event against the approved rules.</summary>
    /// <param name="disaster">Normalized disaster event; only metadata is read.</param>
    /// <returns>
    /// A binary decision with a stable rule id and human-readable factors. The
    /// score is 1.0 when important and 0.0 otherwise.
    /// </returns>
    public ImportanceDecision Score(DisasterEvent disaster)
    {
        ArgumentNullException.ThrowIfNull(disaster);

        var factors = new List<string>();
        var matchedRules = new List<string>();

        var isGdacs = MatchesSourceFamily(disaster.SourceId, "gdacs");
        var isIfrc = MatchesSourceFamily(disaster.SourceId, "ifrc")
            || IsIfrcSeverityUnit(disaster.SeverityUnit);
        var isEarthquake = IsEarthquakeType(disaster.EventType);
        var alert = ScoringText.Normalize(disaster.AlertLevel);

        EvaluateGdacsAlert(disaster, isGdacs, alert, matchedRules, factors);
        EvaluateTsunami(disaster, matchedRules, factors);
        EvaluateEarthquakeMagnitude(
            disaster,
            isGdacs,
            isIfrc,
            isEarthquake,
            alert,
            matchedRules,
            factors);
        EvaluateIfrcSeverity(disaster, isIfrc, matchedRules, factors);

        if (!isEarthquake && !isIfrc && disaster.Severity is null)
        {
            factors.Add("Severity is missing; no approved rule uses severity for this event.");
        }

        var ruleId = matchedRules.Count == 0
            ? DisasterImportanceRuleIds.NoApprovedTrigger
            : matchedRules[0];

        return matchedRules.Count > 0
            ? ImportanceDecision.Important(ruleId, factors.ToArray())
            : ImportanceDecision.NotImportant(ruleId, factors.ToArray());
    }

    private static void EvaluateGdacsAlert(
        DisasterEvent disaster,
        bool isGdacs,
        string alert,
        List<string> matchedRules,
        List<string> factors)
    {
        if (!isGdacs)
        {
            factors.Add(alert.Length == 0
                ? "Alert level is missing; the GDACS Orange/Red rule requires a GDACS source."
                : $"Source is not GDACS, so alert level '{disaster.AlertLevel}' is not used by the GDACS rule.");
            return;
        }

        if (alert is "orange" or "red")
        {
            matchedRules.Add(DisasterImportanceRuleIds.GdacsOrangeRed);
            factors.Add($"GDACS alert level '{disaster.AlertLevel}' is Orange/Red.");
        }
        else if (alert.Length == 0)
        {
            factors.Add("GDACS alert level is missing, so the Orange/Red rule cannot apply.");
        }
        else
        {
            factors.Add($"GDACS alert level '{disaster.AlertLevel}' is not Orange/Red.");
        }
    }

    private static void EvaluateTsunami(
        DisasterEvent disaster,
        List<string> matchedRules,
        List<string> factors)
    {
        if (disaster.Tsunami is true)
        {
            matchedRules.Add(DisasterImportanceRuleIds.TsunamiFlag);
            factors.Add("Tsunami flag is set.");
        }
        else if (disaster.Tsunami is null)
        {
            factors.Add("Tsunami flag is missing.");
        }
        else
        {
            factors.Add("Tsunami flag is not set.");
        }
    }

    private static void EvaluateEarthquakeMagnitude(
        DisasterEvent disaster,
        bool isGdacs,
        bool isIfrc,
        bool isEarthquake,
        string alert,
        List<string> matchedRules,
        List<string> factors)
    {
        if (!isEarthquake)
        {
            factors.Add($"Event type '{disaster.EventType}' is not an earthquake.");
            return;
        }

        if (isIfrc)
        {
            factors.Add(
                "Event uses the IFRC severity scale, so its severity is evaluated as IFRC severity, "
                + "not earthquake magnitude.");
            return;
        }

        if (disaster.Severity is null)
        {
            factors.Add("Earthquake magnitude is missing.");
            return;
        }

        var magnitude = disaster.Severity.Value;
        var meetsThreshold = magnitude >= EarthquakeMagnitudeThreshold;

        // A specified non-Green GDACS alert is already handled by the Orange/Red
        // rule or is a medium alert (Yellow) that the approved fixture does not
        // promote. Non-GDACS alert levels are not GDACS alert levels, so they do
        // not suppress the magnitude rule.
        var gdacsAlertIsSpecifiedNonGreen = isGdacs && alert.Length > 0 && alert != "green";
        if (gdacsAlertIsSpecifiedNonGreen)
        {
            factors.Add(meetsThreshold
                ? $"Earthquake magnitude {FormatNumber(magnitude)} meets the "
                    + $"{FormatNumber(EarthquakeMagnitudeThreshold)} fixture, but GDACS alert level "
                    + $"'{disaster.AlertLevel}' is not Green/unspecified; the approved magnitude rule "
                    + "applies only to Green/unspecified alert levels."
                : $"Earthquake magnitude {FormatNumber(magnitude)} is below "
                    + $"{FormatNumber(EarthquakeMagnitudeThreshold)}; GDACS alert level "
                    + $"'{disaster.AlertLevel}' is not Green/unspecified.");
            return;
        }

        if (meetsThreshold)
        {
            matchedRules.Add(DisasterImportanceRuleIds.EarthquakeMagnitude);
            factors.Add(
                $"Earthquake magnitude {FormatNumber(magnitude)} is at or above "
                + $"{FormatNumber(EarthquakeMagnitudeThreshold)}.");
        }
        else
        {
            factors.Add(
                $"Earthquake magnitude {FormatNumber(magnitude)} is below "
                + $"{FormatNumber(EarthquakeMagnitudeThreshold)}.");
        }
    }

    private static void EvaluateIfrcSeverity(
        DisasterEvent disaster,
        bool isIfrc,
        List<string> matchedRules,
        List<string> factors)
    {
        if (!isIfrc)
        {
            factors.Add("Source and severity unit are not IFRC, so the IFRC severity rule does not apply.");
            return;
        }

        if (disaster.Severity is null)
        {
            factors.Add("IFRC severity is missing.");
            return;
        }

        if (disaster.Severity >= IfrcSeverityThreshold)
        {
            matchedRules.Add(DisasterImportanceRuleIds.IfrcSeverity);
            factors.Add(
                $"IFRC severity {FormatNumber(disaster.Severity.Value)} is at or above "
                + $"{FormatNumber(IfrcSeverityThreshold)}.");
        }
        else
        {
            factors.Add(
                $"IFRC severity {FormatNumber(disaster.Severity.Value)} is below "
                + $"{FormatNumber(IfrcSeverityThreshold)}.");
        }
    }

    /// <summary>
    /// Matches a configured source id family without treating an arbitrary
    /// substring as the provider. The family token must be the first normalized
    /// token, so <c>gdacs</c> and <c>gdacs-rss</c> match GDACS while
    /// <c>not-gdacs</c> and <c>my-ifrc-feed</c> do not.
    /// </summary>
    private static bool MatchesSourceFamily(string? sourceId, string family)
    {
        var normalized = ScoringText.Normalize(sourceId);
        if (normalized.Length == 0)
        {
            return false;
        }

        var separator = normalized.IndexOf(' ');
        var firstToken = separator < 0 ? normalized : normalized[..separator];
        return string.Equals(firstToken, family, StringComparison.Ordinal);
    }

    private static bool IsIfrcSeverityUnit(string? severityUnit)
    {
        var normalized = ScoringText.Normalize(severityUnit);
        return ScoringText.ContainsTokenSequence(normalized, "ifrc severity level")
            || string.Equals(normalized, "ifrc severity", StringComparison.Ordinal);
    }

    private static bool IsEarthquakeType(string? eventType)
    {
        var normalized = ScoringText.Normalize(eventType);
        if (normalized.Length == 0)
        {
            return false;
        }

        var separator = normalized.IndexOf(' ');
        var firstToken = separator < 0 ? normalized : normalized[..separator];
        return EarthquakeTypeTokens.Contains(firstToken);
    }

    private static string FormatNumber(double value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);
}
