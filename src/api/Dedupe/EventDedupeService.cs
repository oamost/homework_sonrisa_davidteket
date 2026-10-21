using WorldAlerts.Api.Events;

namespace WorldAlerts.Api.Dedupe;

/// <summary>
/// Orchestrates identity resolution, revision tracking, conservative cross-source
/// news reconciliation and delivered-fingerprint suppression. The clock is
/// injected so polling tests and replay can be deterministic; this type starts no
/// timers and sends no notifications.
/// </summary>
public sealed class EventDedupeService
{
    private readonly DedupeStore _store;
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>Creates the service over an initialized store. The default clock is UTC now.</summary>
    public EventDedupeService(DedupeStore store, Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _clock = clock ?? (static () => DateTimeOffset.UtcNow);
    }

    /// <summary>Records one observation and returns its revision decision.</summary>
    public async Task<EventRevision> UpsertAsync(
        EventRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var identity = EventIdentityResolver.Resolve(record);
        var canonicalKey = await _store
            .ResolveCanonicalKeyAsync(identity.Key, cancellationToken)
            .ConfigureAwait(false);
        var result = await _store
            .UpsertAsync(identity, record, canonicalKey, _clock(), cancellationToken)
            .ConfigureAwait(false);
        return result.Revision;
    }

    /// <summary>
    /// Returns true when the current material fingerprint has not already been
    /// delivered. Unchanged repeats are suppressed after a restart; a materially
    /// revised event is allowed through. An unseen event is always allowed.
    /// </summary>
    public Task<bool> ShouldNotifyAsync(
        EventRevision revision,
        string subscriberId,
        string channelId,
        CancellationToken cancellationToken = default)
    {
        return _store.ShouldNotifyAsync(revision, subscriberId, channelId, cancellationToken);
    }

    /// <summary>Acknowledges a specific observed revision for one recipient and channel.</summary>
    public Task<bool> MarkDeliveredAsync(
        EventRevision revision,
        string subscriberId,
        string channelId,
        CancellationToken cancellationToken = default)
    {
        return _store.MarkDeliveredAsync(revision, subscriberId, channelId, _clock(), cancellationToken);
    }

    /// <summary>Returns the persisted revision for an identity key, or null when unseen.</summary>
    public async Task<EventRevision?> GetRevisionAsync(
        string identityKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(identityKey);

        var state = await _store
            .GetStateAsync(identityKey, cancellationToken)
            .ConfigureAwait(false);
        if (state is null)
        {
            return null;
        }

        return new EventRevision(
            state.IdentityKey,
            state.CanonicalKey,
            state.LastRevisionKind,
            state.Fingerprint,
            state.PreviousFingerprint,
            Array.Empty<EventRevisionField>(),
            state.RevisionCount,
            state.FirstSeenUtc,
            state.LastSeenUtc);
    }

    /// <summary>
    /// Reconciles news records conservatively and persists aliases so later
    /// canonical-key lookups return the cluster representative. Non-news records
    /// are ignored. Returns the number of aliases written.
    /// </summary>
    public async Task<int> ReconcileNewsAsync(
        IReadOnlyCollection<EventRecord> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);

        var clusters = NewsReconciler.Reconcile(records);
        var count = 0;
        foreach (var cluster in clusters)
        {
            var existing = new List<string>();
            foreach (var key in cluster.MemberKeys)
            {
                var resolved = await _store.ResolveCanonicalKeyAsync(key, cancellationToken).ConfigureAwait(false);
                if (resolved != key || await _store.GetStateAsync(resolved, cancellationToken).ConfigureAwait(false) is not null)
                {
                    existing.Add(resolved);
                }
            }

            var canonical = existing.FirstOrDefault() ?? cluster.CanonicalKey;
            foreach (var key in cluster.MemberKeys)
            {
                await _store.SetAliasAsync(key, canonical, cancellationToken).ConfigureAwait(false);
                count++;
            }
        }

        return count;
    }
}
