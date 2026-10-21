using WorldAlerts.Api.Events;

namespace WorldAlerts.Api.CatchUp;

/// <summary>
/// Result of one catch-up selection pass: eligible, deduplicated candidates in
/// deterministic oldest-first order plus explicit coverage gaps.
/// </summary>
/// <param name="Candidates">Eligible candidates, oldest eligible first.</param>
/// <param name="CoverageGaps">Per-family/source coverage gaps, returned as data.</param>
/// <param name="EvaluatedAtUtc">Instant the pass was evaluated at.</param>
/// <param name="WindowStartUtc">Inclusive start of the catch-up window.</param>
public sealed record CatchUpSelection(
    IReadOnlyList<CatchUpCandidate> Candidates,
    IReadOnlyList<CatchUpCoverageGap> CoverageGaps,
    DateTimeOffset EvaluatedAtUtc,
    DateTimeOffset WindowStartUtc);
