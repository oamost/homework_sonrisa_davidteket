using System.Globalization;
using WorldAlerts.Api.Events;

namespace WorldAlerts.Api.Dedupe;

/// <summary>How an event identity was derived.</summary>
public enum EventIdentityBasis
{
    /// <summary>The provider supplied a native item id; this is the strongest identity.</summary>
    NativeId,

    /// <summary>No native id was supplied; the identity uses source, normalized link and published time.</summary>
    NormalizedLink,

    /// <summary>No native id or usable link was supplied; the identity uses source, normalized title and published time.</summary>
    NormalizedTitle,

    /// <summary>Market sample identified by provider instrument, interval and observation time.</summary>
    MarketObservation,
}

/// <summary>
/// Stable identity of one provider event. <paramref name="Key"/> is the persisted
/// deduplication key and includes the event family, so the same source/native id
/// in two families is never treated as one event.
/// </summary>
/// <param name="Key">Canonical identity key.</param>
/// <param name="Family">Event family the identity belongs to.</param>
/// <param name="SourceId">Configured source identifier.</param>
/// <param name="NativeId">Provider-native item id when present.</param>
/// <param name="Basis">Which rule produced the key.</param>
/// <param name="NormalizedLink">Normalized provenance URL when the fallback used it.</param>
/// <param name="TimestampUtc">Published time used by the fallback, else the fetch time.</param>
public sealed record EventIdentity(
    string Key,
    EventFamily Family,
    string SourceId,
    string? NativeId,
    EventIdentityBasis Basis,
    string? NormalizedLink,
    DateTimeOffset TimestampUtc);

/// <summary>
/// Resolves the conservative identity of an <see cref="EventRecord"/>.
/// <para>
/// Rule 1: same family + same source + same native id is the same event. This is
/// the only rule that merges records without a timestamp or link check.
/// </para>
/// <para>
/// Rule 2 (no native id): same family + source + normalized link + published time
/// is the same event. When publication time is missing the first-seen/fetch time
/// is used, because inventing a publication time is worse than a slightly weaker
/// fallback. If the link is empty, the normalized title replaces the link. The
/// fallback deliberately includes the source, link/title and time so unrelated
/// items are never merged merely because they share a source.
/// </para>
/// </summary>
public static class EventIdentityResolver
{
    private const char Separator = '\u001F';

    /// <summary>Resolves the identity key for one normalized record.</summary>
    public static EventIdentity Resolve(EventRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var sourceId = (record.SourceId ?? string.Empty).Trim();
        var family = record.Family.ToString();
        var nativeId = string.IsNullOrWhiteSpace(record.SourceItemId)
            ? null
            : record.SourceItemId.Trim();

        if (record.Family == EventFamily.Market)
        {
            var instrument = RequiredMetadata(record, "instrument");
            var interval = RequiredMetadata(record, "interval");
            if (record.EventTimeUtc is not { } observationUtc)
            {
                throw new ArgumentException("Market identity requires a provider observation time.", nameof(record));
            }

            return new EventIdentity(
                string.Join(Separator, family, sourceId, "m", instrument, interval,
                    observationUtc.UtcTicks.ToString(CultureInfo.InvariantCulture)),
                record.Family, sourceId, null, EventIdentityBasis.MarketObservation, null, observationUtc);
        }

        if (nativeId is not null)
        {
            var key = string.Join(Separator, family, sourceId, "n", nativeId);
            return new EventIdentity(
                key,
                record.Family,
                sourceId,
                nativeId,
                EventIdentityBasis.NativeId,
                null,
                record.PublishedTimeUtc ?? record.FetchedUtc);
        }

        var timestamp = record.PublishedTimeUtc ?? record.FetchedUtc;
        var ticks = timestamp.UtcTicks.ToString(CultureInfo.InvariantCulture);
        var normalizedLink = DedupeUrl.Normalize(record.ProvenanceUrl);

        if (normalizedLink.Length > 0)
        {
            var key = string.Join(Separator, family, sourceId, "l", normalizedLink, ticks);
            return new EventIdentity(
                key,
                record.Family,
                sourceId,
                null,
                EventIdentityBasis.NormalizedLink,
                normalizedLink,
                timestamp);
        }

        var title = DedupeText.NormalizeTitle(TitleOf(record));
        var titleKey = string.Join(Separator, family, sourceId, "t", title, ticks);
        return new EventIdentity(
            titleKey,
            record.Family,
            sourceId,
            null,
            EventIdentityBasis.NormalizedTitle,
            null,
            timestamp);
    }

    private static string? TitleOf(EventRecord record) =>
        record.Metadata is not null
        && record.Metadata.TryGetValue("title", out var title)
            ? title
            : null;

    private static string RequiredMetadata(EventRecord record, string name) =>
        record.Metadata.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new ArgumentException($"Market identity requires {name}.", nameof(record));
}
