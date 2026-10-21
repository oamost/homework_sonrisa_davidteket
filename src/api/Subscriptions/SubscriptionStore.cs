using System.Globalization;
using Microsoft.Data.Sqlite;

namespace WorldAlerts.Api.Subscriptions;

/// <summary>
/// SQLite-backed subscription storage. It owns the <c>Subscriptions</c> table in
/// the same database file as the rest of the application, and rows are unique on
/// (UserId, EventType, Channel), so saving the same choice twice updates it in
/// place instead of duplicating it.
/// </summary>
public sealed class SubscriptionStore
{
    private readonly string _path;
    private readonly string _connectionString;
    private readonly TimeProvider _timeProvider;

    public SubscriptionStore(string databasePath, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _path = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 5,
        }.ToString();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Creates the subscription schema. Safe to call repeatedly.</summary>
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
            CREATE TABLE IF NOT EXISTS Subscriptions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId TEXT NOT NULL,
                EventType TEXT NOT NULL,
                Channel TEXT NOT NULL,
                Destination TEXT NULL,
                Enabled INTEGER NOT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                CONSTRAINT UQ_Subscriptions_User_Type_Channel UNIQUE (UserId, EventType, Channel)
            );
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Best-effort initialization for host startup: a broken database is logged
    /// instead of aborting the process, matching the news store's behaviour.
    /// </summary>
    public void TryInitialize()
    {
        try
        {
            Initialize();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"SQLite subscription store initialization failed: {exception.GetType().Name}");
        }
    }

    /// <summary>
    /// Inserts the subscription, or updates destination, enabled flag and update
    /// time when (UserId, EventType, Channel) already exists. <see cref="Subscription.Id"/>
    /// and both timestamps on the input are ignored. Returns the stored record.
    /// </summary>
    public async Task<Subscription> UpsertAsync(
        Subscription subscription,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var userId = NormalizeUserId(subscription.UserId);
        var eventType = NormalizeEventType(subscription.EventType);
        var channel = NormalizeChannel(subscription.Channel);
        var destination = NormalizeDestination(subscription.Destination);
        var now = FormatTimestamp(_timeProvider.GetUtcNow());

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                INSERT INTO Subscriptions (
                    UserId, EventType, Channel, Destination, Enabled, CreatedUtc, UpdatedUtc)
                VALUES (
                    $userId, $eventType, $channel, $destination, $enabled, $now, $now)
                ON CONFLICT (UserId, EventType, Channel) DO UPDATE SET
                    Destination = excluded.Destination,
                    Enabled = excluded.Enabled,
                    UpdatedUtc = excluded.UpdatedUtc;
                """;
            command.Parameters.AddWithValue("$userId", userId);
            command.Parameters.AddWithValue("$eventType", eventType);
            command.Parameters.AddWithValue("$channel", channel);
            command.Parameters.AddWithValue(
                "$destination",
                destination is null ? DBNull.Value : destination);
            command.Parameters.AddWithValue("$enabled", subscription.Enabled ? 1 : 0);
            command.Parameters.AddWithValue("$now", now);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var stored = await FindAsync(connection, userId, eventType, channel, cancellationToken)
            .ConfigureAwait(false);
        return stored ?? throw new InvalidOperationException(
            "Subscription upsert did not produce a stored row.");
    }

    /// <summary>Returns one user's subscriptions ordered by event type, channel and id.</summary>
    public Task<IReadOnlyList<Subscription>> ListAsync(
        string userId = Subscription.DefaultUserId,
        CancellationToken cancellationToken = default) =>
        ListAsync(userId, eventType: null, cancellationToken: cancellationToken);

    /// <summary>
    /// Returns one user's subscriptions, optionally limited to one event type,
    /// ordered by event type, channel and id.
    /// </summary>
    public async Task<IReadOnlyList<Subscription>> ListAsync(
        string userId,
        string? eventType,
        CancellationToken cancellationToken = default)
    {
        var normalizedUser = NormalizeUserId(userId);
        var normalizedType = string.IsNullOrWhiteSpace(eventType) ? null : eventType.Trim();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, UserId, EventType, Channel, Destination, Enabled, CreatedUtc, UpdatedUtc
            FROM Subscriptions
            WHERE UserId = $userId
              AND ($eventType IS NULL OR EventType = $eventType)
            ORDER BY EventType, Channel, Id;
            """;
        command.Parameters.AddWithValue("$userId", normalizedUser);
        command.Parameters.AddWithValue("$eventType", normalizedType ?? (object)DBNull.Value);

        var subscriptions = new List<Subscription>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            subscriptions.Add(Read(reader));
        }

        return subscriptions;
    }

    /// <summary>Deletes by primary key. Returns whether a row was removed.</summary>
    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Subscriptions WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <summary>
    /// Deletes by the natural key, applying the same normalization as
    /// <see cref="UpsertAsync"/>. Returns whether a row was removed.
    /// </summary>
    public async Task<bool> DeleteAsync(
        string userId,
        string eventType,
        string channel,
        CancellationToken cancellationToken = default)
    {
        var normalizedUser = NormalizeUserId(userId);
        var normalizedType = NormalizeEventType(eventType);
        var normalizedChannel = NormalizeChannel(channel);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM Subscriptions
            WHERE UserId = $userId AND EventType = $eventType AND Channel = $channel;
            """;
        command.Parameters.AddWithValue("$userId", normalizedUser);
        command.Parameters.AddWithValue("$eventType", normalizedType);
        command.Parameters.AddWithValue("$channel", normalizedChannel);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    private static async Task<Subscription?> FindAsync(
        SqliteConnection connection,
        string userId,
        string eventType,
        string channel,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, UserId, EventType, Channel, Destination, Enabled, CreatedUtc, UpdatedUtc
            FROM Subscriptions
            WHERE UserId = $userId AND EventType = $eventType AND Channel = $channel;
            """;
        command.Parameters.AddWithValue("$userId", userId);
        command.Parameters.AddWithValue("$eventType", eventType);
        command.Parameters.AddWithValue("$channel", channel);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    private static Subscription Read(SqliteDataReader reader) =>
        new()
        {
            Id = reader.GetInt64(0),
            UserId = reader.GetString(1),
            EventType = reader.GetString(2),
            Channel = reader.GetString(3),
            Destination = reader.IsDBNull(4) ? null : reader.GetString(4),
            Enabled = reader.GetBoolean(5),
            CreatedUtc = ParseTimestamp(reader.GetString(6)),
            UpdatedUtc = ParseTimestamp(reader.GetString(7)),
        };

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static string NormalizeUserId(string? userId) =>
        string.IsNullOrWhiteSpace(userId) ? Subscription.DefaultUserId : userId.Trim();

    /// <summary>
    /// Event-type codes are trimmed but keep their casing: the approved taxonomy
    /// (task 01) has not fixed casing rules, so this slice stores codes verbatim.
    /// </summary>
    private static string NormalizeEventType(string? eventType)
    {
        var trimmed = eventType?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException(
                "Subscription event type must not be empty.", nameof(eventType));
        }

        return trimmed;
    }

    private static string NormalizeChannel(string? channel)
    {
        var normalized = channel?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized))
        {
            throw new ArgumentException(
                "Subscription channel must not be empty.", nameof(channel));
        }

        return normalized;
    }

    private static string? NormalizeDestination(string? destination)
    {
        var trimmed = destination?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
