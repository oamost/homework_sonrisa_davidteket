using System.Globalization;
using Microsoft.Data.Sqlite;

namespace WorldAlerts.Api.Queue;

public sealed record NotificationJob(long Id, string IdentityKey, string Revision, string SubscriberId,
    string Channel, string Destination, string Subject, string Body, string State, int Attempts,
    DateTimeOffset DueUtc, string? FailureType);

/// <summary>Durable outgoing queue. An interrupted send is uncertain, never silently replayed.</summary>
public sealed class NotificationQueueStore(string databasePath)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = Path.GetFullPath(databasePath), Mode = SqliteOpenMode.ReadWriteCreate, DefaultTimeout = 30,
    }.ToString();

    public void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS NotificationJobs (
                Id INTEGER PRIMARY KEY, IdentityKey TEXT NOT NULL, Revision TEXT NOT NULL,
                SubscriberId TEXT NOT NULL, Channel TEXT NOT NULL, Destination TEXT NOT NULL,
                Subject TEXT NOT NULL, Body TEXT NOT NULL, State TEXT NOT NULL,
                Attempts INTEGER NOT NULL DEFAULT 0, DueUtc TEXT NOT NULL, FailureType TEXT NULL,
                UNIQUE (IdentityKey, Revision, SubscriberId, Channel));
            CREATE TABLE IF NOT EXISTS NotificationPacing (JobId INTEGER PRIMARY KEY, ClaimedUtc TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
        // No local acknowledgement means the external provider may already have accepted it.
        using var recover = connection.CreateCommand();
        recover.CommandText = "UPDATE NotificationJobs SET State = 'uncertain' WHERE State = 'sending';";
        recover.ExecuteNonQuery();
    }

    public async Task<bool> EnqueueAsync(string identity, string revision, string subscriber,
        string channel, string destination, string subject, string body, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO NotificationJobs
            (IdentityKey, Revision, SubscriberId, Channel, Destination, Subject, Body, State, DueUtc)
            VALUES ($identity, $revision, $subscriber, $channel, $destination, $subject, $body, 'pending', $due);
            """;
        cmd.Parameters.AddWithValue("$identity", identity);
        cmd.Parameters.AddWithValue("$revision", revision);
        cmd.Parameters.AddWithValue("$subscriber", subscriber);
        cmd.Parameters.AddWithValue("$channel", channel);
        cmd.Parameters.AddWithValue("$destination", destination);
        cmd.Parameters.AddWithValue("$subject", subject);
        cmd.Parameters.AddWithValue("$body", body);
        cmd.Parameters.AddWithValue("$due", Stamp(now));
        return await cmd.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<IReadOnlyList<NotificationJob>> ClaimAsync(DateTimeOffset now, int maximum,
        bool startupBurst = false, CancellationToken cancellationToken = default, bool emailOnly = false, bool testOnly = false)
    {
        if (maximum <= 0) return [];
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var tx = connection.BeginTransaction(deferred: false);
        var capacity = Math.Min(maximum, startupBurst ? 50 : 10);
        if (!startupBurst)
        {
            await using var count = connection.CreateCommand();
            count.Transaction = tx;
            count.CommandText = "SELECT COUNT(*) FROM NotificationPacing WHERE ClaimedUtc > $since AND ClaimedUtc <= $now";
            count.Parameters.AddWithValue("$since", Stamp(now.AddMinutes(-1)));
            count.Parameters.AddWithValue("$now", Stamp(now));
            capacity = Math.Min(capacity, Math.Max(0, 10 - Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken))));
        }
        var jobs = new List<NotificationJob>();
        if (capacity > 0)
        {
            await using var read = connection.CreateCommand();
            read.Transaction = tx;
            read.CommandText = """
                SELECT Id, IdentityKey, Revision, SubscriberId, Channel, Destination, Subject, Body,
                       State, Attempts, DueUtc, FailureType FROM NotificationJobs
                WHERE State = 'pending' AND ($emailOnly = 0 OR Channel = 'email')
                  AND ($testOnly = 0 OR IdentityKey LIKE 'synthetic-test:%')
                  AND DueUtc <= $now ORDER BY DueUtc, Id LIMIT $limit;
                """;
            read.Parameters.AddWithValue("$now", Stamp(now));
            read.Parameters.AddWithValue("$emailOnly", emailOnly ? 1 : 0);
            read.Parameters.AddWithValue("$testOnly", testOnly ? 1 : 0);
            read.Parameters.AddWithValue("$limit", capacity);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                jobs.Add(new NotificationJob(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                    reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
                    reader.GetString(7), reader.GetString(8), reader.GetInt32(9),
                    DateTimeOffset.Parse(reader.GetString(10), CultureInfo.InvariantCulture),
                    reader.IsDBNull(11) ? null : reader.GetString(11)));
            await reader.DisposeAsync();
            foreach (var job in jobs)
            {
                await using var update = connection.CreateCommand();
                update.Transaction = tx;
                update.CommandText = startupBurst
                    ? "UPDATE NotificationJobs SET State = 'sending', Attempts = Attempts + 1 WHERE Id = $id;"
                    : """
                      UPDATE NotificationJobs SET State = 'sending', Attempts = Attempts + 1 WHERE Id = $id;
                      INSERT INTO NotificationPacing (JobId, ClaimedUtc) VALUES ($id, $now)
                      ON CONFLICT(JobId) DO UPDATE SET ClaimedUtc = excluded.ClaimedUtc;
                      """;
                update.Parameters.AddWithValue("$id", job.Id);
                update.Parameters.AddWithValue("$now", Stamp(now));
                await update.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        tx.Commit();
        return jobs;
    }

    public Task AcknowledgeAsync(long id, DateTimeOffset now, CancellationToken ct = default) =>
        SetStateAsync(id, "sent", now, null, ct);

    public async Task FailAsync(long id, DateTimeOffset now, string failureType, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE NotificationJobs SET State = CASE WHEN Attempts >= 4 THEN 'failed' ELSE 'pending' END,
                DueUtc = $due, FailureType = $type WHERE Id = $id AND State = 'sending';
            """;
        await using var attempts = connection.CreateCommand();
        attempts.CommandText = "SELECT Attempts FROM NotificationJobs WHERE Id = $id";
        attempts.Parameters.AddWithValue("$id", id);
        var count = Convert.ToInt32(await attempts.ExecuteScalarAsync(ct));
        var minutes = count switch { 1 => 1, 2 => 5, _ => 15 };
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$due", Stamp(now.AddMinutes(minutes)));
        cmd.Parameters.AddWithValue("$type", failureType);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<NotificationJob>> SnapshotAsync(CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, IdentityKey, Revision, SubscriberId, Channel, Destination, Subject, Body,
                   State, Attempts, DueUtc, FailureType FROM NotificationJobs ORDER BY Id;
            """;
        var rows = new List<NotificationJob>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(new NotificationJob(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
                reader.GetString(7), reader.GetString(8), reader.GetInt32(9),
                DateTimeOffset.Parse(reader.GetString(10), CultureInfo.InvariantCulture),
                reader.IsDBNull(11) ? null : reader.GetString(11)));
        return rows;
    }

    private async Task SetStateAsync(long id, string state, DateTimeOffset now, string? failure, CancellationToken ct)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE NotificationJobs SET State = $state, DueUtc = $now, FailureType = $failure WHERE Id = $id AND State = 'sending'";
        cmd.Parameters.AddWithValue("$state", state);
        cmd.Parameters.AddWithValue("$now", Stamp(now));
        cmd.Parameters.AddWithValue("$failure", (object?)failure ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string Stamp(DateTimeOffset time) => time.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
}
