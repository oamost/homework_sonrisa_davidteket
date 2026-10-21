using System.Globalization;
using Microsoft.Data.Sqlite;
using WorldAlerts.Api.Events;

namespace WorldAlerts.Api.Dedupe;

/// <summary>
/// SQLite persistence for event identities, material revisions and delivered
/// fingerprints. Initialization is idempotent, upserts are atomic, and the same
/// database can be reopened after a restart without re-notifying unchanged items.
/// </summary>
public sealed class DedupeStore
{
    private readonly string _path;
    private readonly string _connectionString;

    /// <summary>Creates a store for the supplied database path. The file is not opened yet.</summary>
    public DedupeStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 30,
        }.ToString();
    }

    /// <summary>Creates the schema. Safe to call repeatedly and from multiple instances.</summary>
    public void Initialize()
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS EventRevisionStates (
                IdentityKey TEXT NOT NULL PRIMARY KEY,
                CanonicalKey TEXT NOT NULL,
                Family TEXT NOT NULL,
                SourceId TEXT NOT NULL,
                NativeId TEXT NULL,
                MaterialFieldsJson TEXT NOT NULL,
                Fingerprint TEXT NOT NULL,
                PreviousFingerprint TEXT NULL,
                FirstSeenUtc TEXT NOT NULL,
                LastSeenUtc TEXT NOT NULL,
                RevisionCount INTEGER NOT NULL,
                LastRevisionKind TEXT NOT NULL,
                DeliveredFingerprint TEXT NULL,
                DeliveredUtc TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_EventRevisionStates_CanonicalKey
                ON EventRevisionStates (CanonicalKey);
            CREATE TABLE IF NOT EXISTS EventAliases (
                AliasKey TEXT NOT NULL PRIMARY KEY,
                CanonicalKey TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_EventAliases_CanonicalKey
                ON EventAliases (CanonicalKey);
            CREATE TABLE IF NOT EXISTS EventDeliveries (
                IdentityKey TEXT NOT NULL,
                SubscriberId TEXT NOT NULL,
                ChannelId TEXT NOT NULL,
                RevisionCount INTEGER NOT NULL,
                Fingerprint TEXT NOT NULL,
                DeliveredUtc TEXT NOT NULL,
                PRIMARY KEY (IdentityKey, SubscriberId, ChannelId)
            );
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>Best-effort initialization for host startup; failures are logged, not thrown.</summary>
    public void TryInitialize()
    {
        try
        {
            Initialize();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"SQLite dedupe store initialization failed: {exception.GetType().Name}");
        }
    }

    /// <summary>Returns the persisted state for an identity key, or null when unseen.</summary>
    public async Task<EventRevisionState?> GetStateAsync(
        string identityKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(identityKey);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ReadStateAsync(connection, transaction: null, identityKey, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Atomically records one observation. An immediate transaction serializes
    /// concurrent writers, so two pollers upserting the same identity produce one
    /// row and one revision rather than duplicate rows or lost counters.
    /// </summary>
    public async Task<EventUpsertResult> UpsertAsync(
        EventIdentity identity,
        EventRecord record,
        string canonicalKey,
        DateTimeOffset observedUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrEmpty(canonicalKey);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction(deferred: false);

        var previous = await ReadStateAsync(connection, transaction, identity.Key, cancellationToken)
            .ConfigureAwait(false);
        var evaluated = EventRevisionPolicy.Evaluate(previous, record, observedUtc);
        var state = evaluated.State with
        {
            IdentityKey = identity.Key,
            CanonicalKey = canonicalKey,
            Family = identity.Family,
            SourceId = identity.SourceId,
            NativeId = identity.NativeId,
        };

        if (previous is null)
        {
            await InsertAsync(connection, transaction, state, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await UpdateAsync(connection, transaction, state, cancellationToken).ConfigureAwait(false);
        }

        transaction.Commit();
        return new EventUpsertResult(state, evaluated.ChangedFields);
    }

    /// <summary>Returns whether this revision is pending for the explicit recipient and channel.</summary>
    public async Task<bool> ShouldNotifyAsync(
        EventRevision revision, string subscriberId, string channelId,
        CancellationToken cancellationToken = default)
    {
        ValidateDelivery(revision, subscriberId, channelId);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT s.RevisionCount, s.Fingerprint, d.RevisionCount, d.Fingerprint
            FROM EventRevisionStates s
            LEFT JOIN EventDeliveries d ON d.IdentityKey = s.IdentityKey
                AND d.SubscriberId = $subscriberId AND d.ChannelId = $channelId
            WHERE s.IdentityKey = $identityKey;
            """;
        command.Parameters.AddWithValue("$identityKey", revision.IdentityKey);
        command.Parameters.AddWithValue("$subscriberId", subscriberId);
        command.Parameters.AddWithValue("$channelId", channelId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            || reader.GetInt32(0) != revision.RevisionCount
            || !string.Equals(reader.GetString(1), revision.Fingerprint, StringComparison.Ordinal))
        {
            return false; // Never queue an unpersisted or superseded revision.
        }

        return reader.IsDBNull(2)
            || reader.GetInt32(2) != revision.RevisionCount
            || !string.Equals(reader.GetString(3), revision.Fingerprint, StringComparison.Ordinal);
    }

    /// <summary>Atomically acknowledges only the currently persisted revision for one recipient/channel.</summary>
    public async Task<bool> MarkDeliveredAsync(
        EventRevision revision,
        string subscriberId,
        string channelId,
        DateTimeOffset deliveredUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateDelivery(revision, subscriberId, channelId);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO EventDeliveries (IdentityKey, SubscriberId, ChannelId, RevisionCount, Fingerprint, DeliveredUtc)
            SELECT IdentityKey, $subscriberId, $channelId, RevisionCount, Fingerprint, $deliveredUtc
            FROM EventRevisionStates
            WHERE IdentityKey = $identityKey AND RevisionCount = $revisionCount AND Fingerprint = $fingerprint
            ON CONFLICT (IdentityKey, SubscriberId, ChannelId)
            DO UPDATE SET RevisionCount = excluded.RevisionCount, Fingerprint = excluded.Fingerprint,
                          DeliveredUtc = excluded.DeliveredUtc
            WHERE EventDeliveries.RevisionCount <= excluded.RevisionCount;
            """;
        command.Parameters.AddWithValue("$fingerprint", revision.Fingerprint);
        command.Parameters.AddWithValue("$revisionCount", revision.RevisionCount);
        command.Parameters.AddWithValue("$subscriberId", subscriberId);
        command.Parameters.AddWithValue("$channelId", channelId);
        command.Parameters.AddWithValue("$deliveredUtc", FormatTimestamp(deliveredUtc));
        command.Parameters.AddWithValue("$identityKey", revision.IdentityKey);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    private static void ValidateDelivery(EventRevision revision, string subscriberId, string channelId)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentException.ThrowIfNullOrWhiteSpace(revision.IdentityKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(revision.Fingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriberId);
        ArgumentException.ThrowIfNullOrWhiteSpace(channelId);
    }

    /// <summary>Stores a cross-source alias. Repeating the same alias is idempotent.</summary>
    public async Task SetAliasAsync(
        string aliasKey,
        string canonicalKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(aliasKey);
        ArgumentException.ThrowIfNullOrEmpty(canonicalKey);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO EventAliases (AliasKey, CanonicalKey)
            VALUES ($aliasKey, $canonicalKey)
            ON CONFLICT (AliasKey) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$aliasKey", aliasKey);
        command.Parameters.AddWithValue("$canonicalKey", canonicalKey);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Resolves an alias to its canonical key, returning the input when unaliased.</summary>
    public async Task<string> ResolveCanonicalKeyAsync(
        string identityKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(identityKey);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT CanonicalKey FROM EventAliases WHERE AliasKey = $aliasKey;
            """;
        command.Parameters.AddWithValue("$aliasKey", identityKey);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is string canonical && canonical.Length > 0 ? canonical : identityKey;
    }

    /// <summary>Returns the number of persisted identities. Intended for diagnostics and tests.</summary>
    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM EventRevisionStates;";
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is long count ? checked((int)count) : 0;
    }

    private static async Task<EventRevisionState?> ReadStateAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string identityKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT IdentityKey, CanonicalKey, Family, SourceId, NativeId,
                   MaterialFieldsJson, Fingerprint, PreviousFingerprint,
                   FirstSeenUtc, LastSeenUtc, RevisionCount, LastRevisionKind,
                   DeliveredFingerprint, DeliveredUtc
            FROM EventRevisionStates
            WHERE IdentityKey = $identityKey;
            """;
        command.Parameters.AddWithValue("$identityKey", identityKey);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new EventRevisionState(
            IdentityKey: reader.GetString(0),
            CanonicalKey: reader.GetString(1),
            Family: ParseFamily(reader.GetString(2)),
            SourceId: reader.GetString(3),
            NativeId: reader.IsDBNull(4) ? null : reader.GetString(4),
            MaterialFieldsJson: reader.GetString(5),
            Fingerprint: reader.GetString(6),
            PreviousFingerprint: reader.IsDBNull(7) ? null : reader.GetString(7),
            FirstSeenUtc: ParseTimestamp(reader.GetString(8)),
            LastSeenUtc: ParseTimestamp(reader.GetString(9)),
            RevisionCount: reader.GetInt32(10),
            LastRevisionKind: ParseRevisionKind(reader.GetString(11)),
            DeliveredFingerprint: reader.IsDBNull(12) ? null : reader.GetString(12),
            DeliveredUtc: reader.IsDBNull(13) ? null : ParseTimestamp(reader.GetString(13)));
    }

    private static async Task InsertAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        EventRevisionState state,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO EventRevisionStates (
                IdentityKey, CanonicalKey, Family, SourceId, NativeId,
                MaterialFieldsJson, Fingerprint, PreviousFingerprint,
                FirstSeenUtc, LastSeenUtc, RevisionCount, LastRevisionKind,
                DeliveredFingerprint, DeliveredUtc)
            VALUES (
                $identityKey, $canonicalKey, $family, $sourceId, $nativeId,
                $materialFieldsJson, $fingerprint, $previousFingerprint,
                $firstSeenUtc, $lastSeenUtc, $revisionCount, $lastRevisionKind,
                $deliveredFingerprint, $deliveredUtc);
            """;
        AddStateParameters(command, state);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpdateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        EventRevisionState state,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE EventRevisionStates
            SET CanonicalKey = $canonicalKey,
                Family = $family,
                SourceId = $sourceId,
                NativeId = $nativeId,
                MaterialFieldsJson = $materialFieldsJson,
                Fingerprint = $fingerprint,
                PreviousFingerprint = $previousFingerprint,
                LastSeenUtc = $lastSeenUtc,
                RevisionCount = $revisionCount,
                LastRevisionKind = $lastRevisionKind
            WHERE IdentityKey = $identityKey;
            """;
        AddStateParameters(command, state);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddStateParameters(SqliteCommand command, EventRevisionState state)
    {
        command.Parameters.AddWithValue("$identityKey", state.IdentityKey);
        command.Parameters.AddWithValue("$canonicalKey", state.CanonicalKey);
        command.Parameters.AddWithValue("$family", state.Family.ToString());
        command.Parameters.AddWithValue("$sourceId", state.SourceId);
        command.Parameters.AddWithValue("$nativeId", (object?)state.NativeId ?? DBNull.Value);
        command.Parameters.AddWithValue("$materialFieldsJson", state.MaterialFieldsJson);
        command.Parameters.AddWithValue("$fingerprint", state.Fingerprint);
        command.Parameters.AddWithValue("$previousFingerprint", (object?)state.PreviousFingerprint ?? DBNull.Value);
        command.Parameters.AddWithValue("$firstSeenUtc", FormatTimestamp(state.FirstSeenUtc));
        command.Parameters.AddWithValue("$lastSeenUtc", FormatTimestamp(state.LastSeenUtc));
        command.Parameters.AddWithValue("$revisionCount", state.RevisionCount);
        command.Parameters.AddWithValue("$lastRevisionKind", state.LastRevisionKind.ToString());
        command.Parameters.AddWithValue("$deliveredFingerprint", (object?)state.DeliveredFingerprint ?? DBNull.Value);
        command.Parameters.AddWithValue("$deliveredUtc", state.DeliveredUtc is { } delivered
            ? FormatTimestamp(delivered)
            : DBNull.Value);
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static EventFamily ParseFamily(string value) =>
        Enum.TryParse<EventFamily>(value, ignoreCase: false, out var family)
            ? family
            : EventFamily.Other;

    private static EventRevisionKind ParseRevisionKind(string value) =>
        Enum.TryParse<EventRevisionKind>(value, ignoreCase: false, out var kind)
            ? kind
            : EventRevisionKind.Unchanged;

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
