using System.Collections.Concurrent;

namespace WorldAlerts.Api.Collection;

/// <summary>
/// Result of a single-flight poll attempt for one source.
/// </summary>
/// <param name="Acquired">
/// True when this attempt won the right to poll the source; the caller must
/// release it with <see cref="SourcePollGuard.Complete"/>.
/// </param>
/// <param name="Reason">Explanatory message when the attempt was rejected.</param>
public sealed record SourcePollAttempt(bool Acquired, string? Reason)
{
    /// <summary>A granted attempt.</summary>
    public static SourcePollAttempt Granted { get; } = new(true, null);

    /// <summary>A rejected attempt because the source is already being polled.</summary>
    public static SourcePollAttempt AlreadyPolling(string sourceId) =>
        new(false, $"A poll for source '{sourceId}' is already in progress; duplicate polls are not allowed.");
}

/// <summary>
/// In-process single-flight guard: at most one concurrent poll per source.
/// A second concurrent attempt for the same source is rejected with a clear
/// result instead of running. Program.cs wiring is deferred to a later
/// integration task.
/// </summary>
public sealed class SourcePollGuard : IDisposable
{
    private readonly ConcurrentDictionary<string, byte> _inFlight = new();

    /// <summary>
    /// Attempts to begin a poll for a source. Returns a granted result exactly
    /// once per in-flight poll; concurrent duplicates are rejected.
    /// </summary>
    public SourcePollAttempt TryBegin(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            throw new ArgumentException("A source identifier is required.", nameof(sourceId));
        }

        return _inFlight.TryAdd(sourceId, 0)
            ? SourcePollAttempt.Granted
            : SourcePollAttempt.AlreadyPolling(sourceId);
    }

    /// <summary>
    /// Releases the source after its poll completes. Returns false when no
    /// in-flight poll is registered for the source.
    /// </summary>
    public bool Complete(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            throw new ArgumentException("A source identifier is required.", nameof(sourceId));
        }

        return _inFlight.TryRemove(sourceId, out _);
    }

    /// <summary>Returns whether an in-flight poll is registered for the source.</summary>
    public bool IsPolling(string sourceId) => sourceId is not null && _inFlight.ContainsKey(sourceId);

    /// <summary>Drops all in-flight registrations.</summary>
    public void Dispose() => _inFlight.Clear();
}
