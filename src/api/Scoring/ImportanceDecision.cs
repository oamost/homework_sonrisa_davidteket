namespace WorldAlerts.Api.Scoring;

/// <summary>
/// Result of one deterministic importance-rule evaluation.
/// </summary>
/// <remarks>
/// The <see cref="Score"/> is a bounded deterministic metadata-strength ranking,
/// not a probability or calibrated confidence. <see cref="IsImportant"/> is governed
/// exclusively by the approved rule trigger, not a score cutoff. The numeric thresholds used by the
/// scorers are the acceptance fixtures approved in
/// <c>docs/decisions/2026-10-21-task-01-acceptance-boundaries.md</c>; they are
/// not universal weights and may be revised by the human without changing the
/// underlying rule.
/// </remarks>
/// <param name="IsImportant">Whether an approved rule marked the input important.</param>
/// <param name="RuleId">Stable identifier of the rule that produced the decision.</param>
/// <param name="Factors">Human-readable reasons supporting the decision.</param>
/// <param name="Score">Deterministic finite metadata-strength heuristic in [0,1].</param>
public sealed record ImportanceDecision(
    bool IsImportant,
    string RuleId,
    IReadOnlyList<string> Factors,
    double Score)
{
    /// <summary>Creates an important decision with an explicit bounded strength.</summary>
    public static ImportanceDecision Important(string ruleId, double strength, params string[] factors)
        => new(true, ruleId, factors, Bound(strength));

    /// <summary>Creates a non-matching decision with no measured strength.</summary>
    public static ImportanceDecision NotImportant(string ruleId, params string[] factors)
        => new(false, ruleId, factors, 0.0);

    /// <summary>Creates a non-matching decision with measured but sub-threshold strength.</summary>
    public static ImportanceDecision NotImportantWithStrength(string ruleId, double strength, params string[] factors)
        => new(false, ruleId, factors, Bound(strength));

    private static double Bound(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
}
