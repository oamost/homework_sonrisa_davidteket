namespace WorldAlerts.Api.Scoring;

/// <summary>
/// Result of one deterministic importance-rule evaluation.
/// </summary>
/// <remarks>
/// The <see cref="Score"/> is a binary indicator of the approved rule outcome:
/// 1.0 when <see cref="IsImportant"/> is true and 0.0 otherwise. It is not an
/// editorial weight, probability or ranking. The numeric thresholds used by the
/// scorers are the acceptance fixtures approved in
/// <c>docs/decisions/2026-10-21-task-01-acceptance-boundaries.md</c>; they are
/// not universal weights and may be revised by the human without changing the
/// underlying rule.
/// </remarks>
/// <param name="IsImportant">Whether an approved rule marked the input important.</param>
/// <param name="RuleId">Stable identifier of the rule that produced the decision.</param>
/// <param name="Factors">Human-readable reasons supporting the decision.</param>
/// <param name="Score">Deterministic 0 or 1 rule outcome, never an editorial weight.</param>
public sealed record ImportanceDecision(
    bool IsImportant,
    string RuleId,
    IReadOnlyList<string> Factors,
    double Score)
{
    /// <summary>Creates an important decision with a score of 1.0.</summary>
    public static ImportanceDecision Important(string ruleId, params string[] factors)
        => new(true, ruleId, factors, 1.0);

    /// <summary>Creates a not-important decision with a score of 0.0.</summary>
    public static ImportanceDecision NotImportant(string ruleId, params string[] factors)
        => new(false, ruleId, factors, 0.0);
}
