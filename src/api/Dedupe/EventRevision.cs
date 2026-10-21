using WorldAlerts.Api.Events;

namespace WorldAlerts.Api.Dedupe;

/// <summary>Kind of observation produced when an event is seen again.</summary>
public enum EventRevisionKind
{
    /// <summary>The identity was not previously persisted.</summary>
    New,

    /// <summary>The material provider fields are identical to the persisted revision.</summary>
    Unchanged,

    /// <summary>At least one material provider field changed (a correction or update).</summary>
    MaterialRevision,
}

/// <summary>
/// Result of observing one event: what changed and which revision it represents.
/// <paramref name="ChangedFields"/> is empty for <see cref="EventRevisionKind.New"/>
/// and <see cref="EventRevisionKind.Unchanged"/>.
/// </summary>
/// <param name="IdentityKey">Persisted identity key.</param>
/// <param name="CanonicalKey">Canonical key after conservative cross-source reconciliation.</param>
/// <param name="Kind">New, unchanged or materially revised.</param>
/// <param name="Fingerprint">Fingerprint of the current material fields.</param>
/// <param name="PreviousFingerprint">Fingerprint of the previous revision, when one exists.</param>
/// <param name="ChangedFields">Material fields that changed.</param>
/// <param name="RevisionCount">Number of distinct material versions, starting at 1.</param>
/// <param name="FirstSeenUtc">When the identity was first persisted.</param>
/// <param name="LastSeenUtc">When the identity was last observed.</param>
public sealed record EventRevision(
    string IdentityKey,
    string CanonicalKey,
    EventRevisionKind Kind,
    string Fingerprint,
    string? PreviousFingerprint,
    IReadOnlyList<EventRevisionField> ChangedFields,
    int RevisionCount,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc);

/// <summary>
/// Persisted state for one event identity. <paramref name="DeliveredFingerprint"/>
/// records the exact revision that was delivered, so restarting the process does
/// not re-notify an unchanged repeat but still allows a later correction.
/// </summary>
/// <param name="IdentityKey">Persisted identity key.</param>
/// <param name="CanonicalKey">Canonical key after conservative cross-source reconciliation.</param>
/// <param name="Family">Event family.</param>
/// <param name="SourceId">Configured source identifier.</param>
/// <param name="NativeId">Provider-native item id when present.</param>
/// <param name="MaterialFieldsJson">Persisted material field snapshot.</param>
/// <param name="Fingerprint">Current material fingerprint.</param>
/// <param name="PreviousFingerprint">Previous material fingerprint, when one exists.</param>
/// <param name="FirstSeenUtc">When the identity was first persisted.</param>
/// <param name="LastSeenUtc">When the identity was last observed.</param>
/// <param name="RevisionCount">Number of distinct material versions.</param>
/// <param name="LastRevisionKind">Kind of the most recent observation.</param>
/// <param name="DeliveredFingerprint">Fingerprint that was delivered, when any.</param>
/// <param name="DeliveredUtc">When the fingerprint was delivered, when any.</param>
public sealed record EventRevisionState(
    string IdentityKey,
    string CanonicalKey,
    EventFamily Family,
    string SourceId,
    string? NativeId,
    string MaterialFieldsJson,
    string Fingerprint,
    string? PreviousFingerprint,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc,
    int RevisionCount,
    EventRevisionKind LastRevisionKind,
    string? DeliveredFingerprint,
    DateTimeOffset? DeliveredUtc);

/// <summary>Outcome of an atomic upsert, including the fields that changed.</summary>
/// <param name="State">Persisted state after the upsert.</param>
/// <param name="ChangedFields">Material fields that changed during this upsert.</param>
public sealed record EventUpsertResult(
    EventRevisionState State,
    IReadOnlyList<EventRevisionField> ChangedFields)
{
    /// <summary>Projects the persisted state into the public revision view.</summary>
    public EventRevision Revision => new(
        State.IdentityKey,
        State.CanonicalKey,
        State.LastRevisionKind,
        State.Fingerprint,
        State.PreviousFingerprint,
        ChangedFields,
        State.RevisionCount,
        State.FirstSeenUtc,
        State.LastSeenUtc);
}

/// <summary>
/// Pure revision rules. Given the previous persisted state (or null) and a freshly
/// observed record, decide whether this is new, an unchanged repeat, or a material
/// revision. The clock is supplied by the caller; this type reads no ambient time.
/// </summary>
public static class EventRevisionPolicy
{
    /// <summary>Evaluates one observation against the previously persisted state.</summary>
    public static EventUpsertResult Evaluate(
        EventRevisionState? previous,
        EventRecord record,
        DateTimeOffset observedUtc)
    {
        ArgumentNullException.ThrowIfNull(record);

        var fields = EventMaterialFields.Extract(record);
        var fingerprint = EventMaterialFields.Fingerprint(fields);
        var materialFieldsJson = EventMaterialFields.Serialize(fields);

        if (previous is null)
        {
            var state = new EventRevisionState(
                IdentityKey: string.Empty,
                CanonicalKey: string.Empty,
                Family: record.Family,
                SourceId: record.SourceId,
                NativeId: record.SourceItemId,
                MaterialFieldsJson: materialFieldsJson,
                Fingerprint: fingerprint,
                PreviousFingerprint: null,
                FirstSeenUtc: observedUtc,
                LastSeenUtc: observedUtc,
                RevisionCount: 1,
                LastRevisionKind: EventRevisionKind.New,
                DeliveredFingerprint: null,
                DeliveredUtc: null);

            return new EventUpsertResult(state, Array.Empty<EventRevisionField>());
        }

        var previousFields = EventMaterialFields.Deserialize(previous.MaterialFieldsJson);
        var changed = EventMaterialFields.Diff(previousFields, fields);
        var kind = changed.Count == 0
            ? EventRevisionKind.Unchanged
            : EventRevisionKind.MaterialRevision;

        var updated = previous with
        {
            Family = record.Family,
            SourceId = record.SourceId,
            NativeId = record.SourceItemId,
            MaterialFieldsJson = materialFieldsJson,
            Fingerprint = fingerprint,
            PreviousFingerprint = previous.Fingerprint,
            LastSeenUtc = observedUtc,
            RevisionCount = kind == EventRevisionKind.MaterialRevision
                ? previous.RevisionCount + 1
                : previous.RevisionCount,
            LastRevisionKind = kind,
        };

        return new EventUpsertResult(updated, changed);
    }
}

/// <summary>
/// Pure delivery suppression rule. A record should notify when the fingerprint of
/// its current material content differs from the fingerprint that was last
/// delivered (or when nothing has been delivered yet). Consequently an unchanged
/// repeat is suppressed after a restart, while a correction that changes a
/// material field is allowed through.
/// </summary>
public static class NotificationPolicy
{
    /// <summary>Returns true when the current fingerprint has not already been delivered.</summary>
    public static bool ShouldNotify(string? deliveredFingerprint, string currentFingerprint)
    {
        ArgumentException.ThrowIfNullOrEmpty(currentFingerprint);
        return !string.Equals(deliveredFingerprint, currentFingerprint, StringComparison.Ordinal);
    }
}
