using System.Globalization;
using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace WorldAlerts.Api.Collection;

/// <summary>
/// SQLite-backed persistence of <see cref="SourcePollState"/> rows so
/// collection progress survives restarts. Rows are unique on SourceId, so
/// upserts are idempotent. Program.cs wiring, source-specific cadence
/// values, and admin display are deferred to a later integration task.
/// </summary>
public sealed class CollectionStateStore
{
    private sealed record Existing(int ConsecutiveFailures, string? LastSuccessUtc);

    private readonly string _path;
    private readonly string _connectionString;
    private readonly SourcePollGuard _guard = new();

    /// <param name="path">
    /// Configured SQLite database file path; shares the configured database
    /// with the family stores.
    /// </param>
    public CollectionStateStore(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        _path = System.IO.Path.GetFullPath(path);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 5,
        }.ToString();
    }

    /// <summary>
    /// Creates the collection schema. Idempotent: returns false, instead of
    /// throwing, when the database file cannot be opened.
    /// </summary>
    public bool TryInitialize()
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS SourcePollState (
                    SourceId TEXT NOT NULL PRIMARY KEY,
                    Family TEXT NOT NULL,
                    LastAttemptUtc TEXT,
                    LastSuccessUtc TEXT,
                    LastError TEXT,
                    ConsecutiveFailures INTEGER NOT NULL DEFAULT 0,
                    NextDueUtc TEXT
                );
                """;
            command.ExecuteNonQuery();
            // Earlier integrated builds used SourcePollProgress. Copy into this single
            // canonical state table without replacing rows already written by this build.
            command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'SourcePollProgress';";
            if (command.ExecuteScalar() is not null)
            {
                command.CommandText = """
                    INSERT OR IGNORE INTO SourcePollState
                        (SourceId, Family, LastAttemptUtc, LastSuccessUtc, LastError, ConsecutiveFailures, NextDueUtc)
                    SELECT SourceId, Family, LastAttemptUtc, LastSuccessUtc, LastError, ConsecutiveFailures, NextDueUtc
                    FROM SourcePollProgress;
                    """;
                command.ExecuteNonQuery();
            }
            return true;
        }
        catch (DbException)
        {
            return false;
        }
        catch (System.IO.IOException)
        {
            return false;
        }
    }

    /// <summary>Strict initialization for callers that need to reject an unusable database.</summary>
    public void Initialize()
    {
        if (!TryInitialize()) throw new InvalidOperationException("Collection state could not be initialized.");
    }

    /// <summary>Read-only asynchronous projection of the canonical source state table.</summary>
    public Task<IReadOnlyList<SourcePollState>> SnapshotAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(ListAll());
    }

    public Task<bool> IsDueAsync(string sourceId, string family, TimeSpan cadence, DateTimeOffset now,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        RequireIdentifiers(sourceId, family);
        var state = TryGet(sourceId) ?? new SourcePollState { SourceId = sourceId, Family = family };
        return Task.FromResult(PollCadence.IsDue(state, now, cadence));
    }

    /// <summary>Single-flight per source, with persisted cadence and sanitized failure type.</summary>
    public async Task PollAsync(string sourceId, string family, TimeSpan cadence, TimeProvider clock,
        Func<CancellationToken, Task<string?>> poll, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(poll);
        var acquired = _guard.TryBegin(sourceId);
        if (!acquired.Acquired) return;
        try
        {
            var now = clock.GetUtcNow();
            if (!await IsDueAsync(sourceId, family, cadence, now, ct)) return;
            string? error;
            try { error = await poll(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception exception) { error = exception.GetType().Name; }
            await RecordAsync(sourceId, family, now, error is null, error, cadence, ct);
        }
        finally { _guard.Complete(sourceId); }
    }

    public Task RecordAsync(string sourceId, string family, DateTimeOffset now, bool success,
        string? errorType, TimeSpan cadence, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (cadence < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(cadence));
        if (success) RecordSuccess(sourceId, family, now, now.Add(cadence));
        else RecordFailure(sourceId, family, now, errorType ?? "UnknownFailure", now.Add(cadence));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Records a successful poll for a source: stores the attempt and success
    /// time, clears the error, and resets consecutive failures.
    /// </summary>
    public void RecordSuccess(
        string sourceId,
        string family,
        DateTimeOffset attemptUtc,
        DateTimeOffset? nextDueUtc)
    {
        RequireIdentifiers(sourceId, family);
        using var connection = OpenConnection();
        Upsert(
            connection,
            sourceId,
            family,
            attemptUtc,
            Serialize(attemptUtc),
            null,
            0,
            nextDueUtc);
    }

    /// <summary>
    /// Records a failed poll for a source: increments consecutive failures
    /// from the stored value, stores the error, and preserves the last
    /// success time.
    /// </summary>
    public void RecordFailure(
        string sourceId,
        string family,
        DateTimeOffset attemptUtc,
        string error,
        DateTimeOffset? nextDueUtc)
    {
        RequireIdentifiers(sourceId, family);
        if (string.IsNullOrWhiteSpace(error))
        {
            throw new ArgumentException("An error message is required.", nameof(error));
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var existing = ReadExisting(connection, sourceId, transaction);
        Upsert(
            connection,
            sourceId,
            family,
            attemptUtc,
            existing.LastSuccessUtc,
            error,
            existing.ConsecutiveFailures + 1,
            nextDueUtc, transaction);
        transaction.Commit();
    }

    /// <summary>Returns the stored state for a source, or null when it has never been recorded.</summary>
    public SourcePollState? TryGet(string sourceId)
    {
        RequireIdentifiers(sourceId, family: null);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT SourceId, Family, LastAttemptUtc, LastSuccessUtc, LastError, ConsecutiveFailures, NextDueUtc FROM SourcePollState WHERE SourceId = @SourceId";
        command.Parameters.AddWithValue("@SourceId", sourceId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadState(reader) : null;
    }

    /// <summary>Returns all stored source states, ordered by source id.</summary>
    public IReadOnlyList<SourcePollState> ListAll()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT SourceId, Family, LastAttemptUtc, LastSuccessUtc, LastError, ConsecutiveFailures, NextDueUtc FROM SourcePollState ORDER BY SourceId";
        using var reader = command.ExecuteReader();
        var states = new List<SourcePollState>();
        while (reader.Read())
        {
            states.Add(ReadState(reader));
        }

        return states;
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static void Upsert(
        SqliteConnection connection,
        string sourceId,
        string family,
        DateTimeOffset attemptUtc,
        string? lastSuccessUtc,
        string? lastError,
        int consecutiveFailures,
        DateTimeOffset? nextDueUtc,
        SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO SourcePollState (SourceId, Family, LastAttemptUtc, LastSuccessUtc, LastError, ConsecutiveFailures, NextDueUtc)
            VALUES (@SourceId, @Family, @LastAttemptUtc, @LastSuccessUtc, @LastError, @ConsecutiveFailures, @NextDueUtc)
            ON CONFLICT(SourceId) DO UPDATE SET
                Family = excluded.Family,
                LastAttemptUtc = excluded.LastAttemptUtc,
                LastSuccessUtc = excluded.LastSuccessUtc,
                LastError = excluded.LastError,
                ConsecutiveFailures = excluded.ConsecutiveFailures,
                NextDueUtc = excluded.NextDueUtc;
            """;
        command.Parameters.AddWithValue("@SourceId", sourceId);
        command.Parameters.AddWithValue("@Family", family);
        command.Parameters.AddWithValue("@LastAttemptUtc", Serialize(attemptUtc));
        command.Parameters.AddWithValue("@LastSuccessUtc", (object?)lastSuccessUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("@LastError", (object?)lastError ?? DBNull.Value);
        command.Parameters.AddWithValue("@ConsecutiveFailures", consecutiveFailures);
        command.Parameters.AddWithValue("@NextDueUtc", (object?)Serialize(nextDueUtc) ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static Existing ReadExisting(SqliteConnection connection, string sourceId, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT ConsecutiveFailures, LastSuccessUtc FROM SourcePollState WHERE SourceId = @SourceId";
        command.Parameters.AddWithValue("@SourceId", sourceId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return new Existing(0, null);
        }

        return new Existing(
            reader.GetInt32(0),
            reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private static SourcePollState ReadState(SqliteDataReader reader) => new()
    {
        SourceId = reader.GetString(0),
        Family = reader.GetString(1),
        LastAttemptUtc = reader.IsDBNull(2) ? null : ParseTimestamp(reader.GetString(2)),
        LastSuccessUtc = reader.IsDBNull(3) ? null : ParseTimestamp(reader.GetString(3)),
        LastError = reader.IsDBNull(4) ? null : reader.GetString(4),
        ConsecutiveFailures = reader.GetInt32(5),
        NextDueUtc = reader.IsDBNull(6) ? null : ParseTimestamp(reader.GetString(6)),
    };

    private static string? Serialize(DateTimeOffset? value) =>
        value?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static void RequireIdentifiers(string? sourceId, string? family)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            throw new ArgumentException("A source identifier is required.", nameof(sourceId));
        }

        if (family is not null && string.IsNullOrWhiteSpace(family))
        {
            throw new ArgumentException("A source family is required.", nameof(family));
        }
    }
}
