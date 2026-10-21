using WorldAlerts.Api.Events;

namespace WorldAlerts.Api.CatchUp;

/// <summary>Why a normalized event record is or is not eligible for catch-up.</summary>
public enum CatchUpEligibilityStatus
{
    /// <summary>The provider timestamp falls inside the window and is not in the future.</summary>
    Eligible,

    /// <summary>Neither an event time nor a publication time was supplied by the provider.</summary>
    MissingProviderTimestamps,

    /// <summary>A provider timestamp lies in the future relative to the evaluation instant.</summary>
    FutureTimestamp,

    /// <summary>The provider timestamp is older than the catch-up window.</summary>
    OutsideWindow,
}

/// <summary>Outcome of evaluating one record against the 24-hour eligibility rule.</summary>
/// <param name="Status">Eligibility decision.</param>
/// <param name="EligibilityTimestampUtc">
/// Publication time when supplied, otherwise event time. Null when no provider
/// timestamp exists or the record was excluded as future-dated.
/// </param>
/// <param name="IsLateItem">
/// True when the event occurred before the window but the provider published it
/// inside the window, so it is delivered normally as a late item.
/// </param>
public readonly record struct CatchUpEligibilityResult(
    CatchUpEligibilityStatus Status,
    DateTimeOffset? EligibilityTimestampUtc,
    bool IsLateItem)
{
    /// <summary>True when the record may become a catch-up candidate.</summary>
    public bool IsEligible => Status == CatchUpEligibilityStatus.Eligible;
}

/// <summary>
/// Applies decision D3 (24-hour eligibility): the publication time drives
/// eligibility when present, even if the event itself is older; otherwise the
/// event time is used (for example market observations). Records without any
/// provider timestamp are never eligible, and any future provider timestamp
/// excludes the record.
/// </summary>
public sealed class CatchUpEligibilityEvaluator
{
    private readonly CatchUpWindow _window;

    /// <summary>Creates an evaluator using the supplied window, or 24 hours by default.</summary>
    public CatchUpEligibilityEvaluator(CatchUpWindow? window = null)
    {
        _window = window ?? CatchUpWindow.TwentyFourHours;
    }

    /// <summary>
    /// Evaluates <paramref name="record"/> against the window ending at
    /// <paramref name="nowUtc"/>. The caller supplies the instant so results are
    /// deterministic and independent of wall-clock time.
    /// </summary>
    public CatchUpEligibilityResult Evaluate(EventRecord record, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (record.EventTimeUtc is null && record.PublishedTimeUtc is null)
        {
            return new CatchUpEligibilityResult(
                CatchUpEligibilityStatus.MissingProviderTimestamps,
                null,
                false);
        }

        if (record.EventTimeUtc > nowUtc || record.PublishedTimeUtc > nowUtc)
        {
            return new CatchUpEligibilityResult(
                CatchUpEligibilityStatus.FutureTimestamp,
                null,
                false);
        }

        var basis = record.PublishedTimeUtc ?? record.EventTimeUtc;
        if (basis is null)
        {
            // Unreachable after the missing-provider check, but keeps the flow explicit.
            return new CatchUpEligibilityResult(
                CatchUpEligibilityStatus.MissingProviderTimestamps,
                null,
                false);
        }

        if (!_window.Contains(nowUtc, basis.Value))
        {
            return new CatchUpEligibilityResult(
                CatchUpEligibilityStatus.OutsideWindow,
                basis,
                false);
        }

        var windowStart = _window.GetStart(nowUtc);
        var isLateItem = record.EventTimeUtc is { } eventTime
            && eventTime < windowStart
            && record.PublishedTimeUtc is not null;

        return new CatchUpEligibilityResult(
            CatchUpEligibilityStatus.Eligible,
            basis,
            isLateItem);
    }
}
