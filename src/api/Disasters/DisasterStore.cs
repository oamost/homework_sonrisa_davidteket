using System.Globalization;
using Microsoft.Data.Sqlite;

namespace WorldAlerts.Api.Disasters;

/// <summary>
/// SQLite-backed disaster event storage. Rows are unique on
/// (SourceId, NativeId), so re-polling the same event is idempotent across
/// polls, restarts and process lifetimes.
/// </summary>
public sealed class DisasterStore
{
    public const int DefaultLimit = 50;
    public const int MinLimit = 1;
    public const int MaxLimit = 200;

    private readonly string _path;
    private readonly string _connectionString;

    public DisasterStore(string path)
    {
        _path = Path.GetFullPath(path);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 5,
        }.ToString();
    }

    /// <summary>Creates the disaster schema. Throws when the database is unusable.</summary>
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
            CREATE TABLE IF NOT EXISTS DisasterEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SourceId TEXT NOT NULL,
                NativeId TEXT NOT NULL,
                EventType TEXT NOT NULL,
                Title TEXT NOT NULL,
                EventTimeUtc TEXT NULL,
                PublishedUtc TEXT NULL,
                UpdatedUtc TEXT NULL,
                Latitude REAL NULL,
                Longitude REAL NULL,
                Country TEXT NULL,
                Iso3 TEXT NULL,
                Severity REAL NULL,
                SeverityUnit TEXT NULL,
                AlertLevel TEXT NULL,
                AlertScore REAL NULL,
                Tsunami INTEGER NULL,
                Significance INTEGER NULL,
                Provenance TEXT NOT NULL,
                FetchedUtc TEXT NOT NULL,
                Certainty TEXT NULL,
                EffectiveUtc TEXT NULL,
                ExpiresUtc TEXT NULL,
                DepthKm REAL NULL,
                CONSTRAINT UQ_DisasterEvents_Source_Native UNIQUE (SourceId, NativeId)
            );
            """;
        command.ExecuteNonQuery();

        // Existing databases predate the optional source-coverage fields.
        EnsureColumn(connection, "Certainty", "TEXT NULL");
        EnsureColumn(connection, "EffectiveUtc", "TEXT NULL");
        EnsureColumn(connection, "ExpiresUtc", "TEXT NULL");
        EnsureColumn(connection, "DepthKm", "REAL NULL");

        using var indexCommand = connection.CreateCommand();
        indexCommand.CommandText =
            """
            CREATE INDEX IF NOT EXISTS IX_DisasterEvents_EventTime
                ON DisasterEvents (EventTimeUtc DESC);
            """;
        indexCommand.ExecuteNonQuery();
    }

    /// <summary>
    /// Best-effort initialization for host startup: a broken database is logged
    /// and health checks report unavailability instead of failing startup.
    /// </summary>
    public void TryInitialize()
    {
        try
        {
            Initialize();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"SQLite disaster store initialization failed: {exception.GetType().Name}");
        }
    }

    /// <summary>
    /// Inserts events, ignoring rows already present. Returns the number of rows
    /// actually inserted; duplicates are reported by the caller as parsed minus
    /// inserted.
    /// </summary>
    public async Task<int> InsertAsync(
        IReadOnlyCollection<DisasterEvent> events,
        CancellationToken cancellationToken = default)
    {
        if (events.Count == 0)
        {
            return 0;
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO DisasterEvents (
                SourceId, NativeId, EventType, Title, EventTimeUtc, PublishedUtc,
                UpdatedUtc, Latitude, Longitude, Country, Iso3, Severity,
                SeverityUnit, AlertLevel, AlertScore, Tsunami, Significance,
                Provenance, FetchedUtc, Certainty, EffectiveUtc, ExpiresUtc, DepthKm)
            VALUES (
                $sourceId, $nativeId, $eventType, $title, $eventTimeUtc, $publishedUtc,
                $updatedUtc, $latitude, $longitude, $country, $iso3, $severity,
                $severityUnit, $alertLevel, $alertScore, $tsunami, $significance,
                $provenance, $fetchedUtc, $certainty, $effectiveUtc, $expiresUtc, $depthKm)
            ON CONFLICT (SourceId, NativeId) DO NOTHING;
            """;

        var sourceId = command.Parameters.Add("$sourceId", SqliteType.Text);
        var nativeId = command.Parameters.Add("$nativeId", SqliteType.Text);
        var eventType = command.Parameters.Add("$eventType", SqliteType.Text);
        var title = command.Parameters.Add("$title", SqliteType.Text);
        var eventTimeUtc = command.Parameters.Add("$eventTimeUtc", SqliteType.Text);
        var publishedUtc = command.Parameters.Add("$publishedUtc", SqliteType.Text);
        var updatedUtc = command.Parameters.Add("$updatedUtc", SqliteType.Text);
        var latitude = command.Parameters.Add("$latitude", SqliteType.Real);
        var longitude = command.Parameters.Add("$longitude", SqliteType.Real);
        var country = command.Parameters.Add("$country", SqliteType.Text);
        var iso3 = command.Parameters.Add("$iso3", SqliteType.Text);
        var severity = command.Parameters.Add("$severity", SqliteType.Real);
        var severityUnit = command.Parameters.Add("$severityUnit", SqliteType.Text);
        var alertLevel = command.Parameters.Add("$alertLevel", SqliteType.Text);
        var alertScore = command.Parameters.Add("$alertScore", SqliteType.Real);
        var tsunami = command.Parameters.Add("$tsunami", SqliteType.Integer);
        var significance = command.Parameters.Add("$significance", SqliteType.Integer);
        var provenance = command.Parameters.Add("$provenance", SqliteType.Text);
        var fetchedUtc = command.Parameters.Add("$fetchedUtc", SqliteType.Text);
        var certainty = command.Parameters.Add("$certainty", SqliteType.Text);
        var effectiveUtc = command.Parameters.Add("$effectiveUtc", SqliteType.Text);
        var expiresUtc = command.Parameters.Add("$expiresUtc", SqliteType.Text);
        var depthKm = command.Parameters.Add("$depthKm", SqliteType.Real);

        var inserted = 0;
        foreach (var item in events)
        {
            sourceId.Value = item.SourceId;
            nativeId.Value = item.NativeId;
            eventType.Value = item.EventType;
            title.Value = item.Title;
            eventTimeUtc.Value = TimestampOrNull(item.EventTimeUtc);
            publishedUtc.Value = TimestampOrNull(item.PublishedUtc);
            updatedUtc.Value = TimestampOrNull(item.UpdatedUtc);
            latitude.Value = item.Latitude ?? (object)DBNull.Value;
            longitude.Value = item.Longitude ?? (object)DBNull.Value;
            country.Value = item.Country ?? (object)DBNull.Value;
            iso3.Value = item.Iso3 ?? (object)DBNull.Value;
            severity.Value = item.Severity ?? (object)DBNull.Value;
            severityUnit.Value = item.SeverityUnit ?? (object)DBNull.Value;
            alertLevel.Value = item.AlertLevel ?? (object)DBNull.Value;
            alertScore.Value = item.AlertScore ?? (object)DBNull.Value;
            tsunami.Value = item.Tsunami is { } tsunamiFlag ? (tsunamiFlag ? 1 : 0) : DBNull.Value;
            significance.Value = item.Significance ?? (object)DBNull.Value;
            provenance.Value = item.Provenance;
            fetchedUtc.Value = FormatTimestamp(item.FetchedUtc);
            certainty.Value = item.Certainty ?? (object)DBNull.Value;
            effectiveUtc.Value = TimestampOrNull(item.EffectiveUtc);
            expiresUtc.Value = TimestampOrNull(item.ExpiresUtc);
            depthKm.Value = item.DepthKm ?? (object)DBNull.Value;

            inserted += await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        transaction.Commit();
        return inserted;
    }

    /// <summary>
    /// Returns events newest-first by event time. Events without an event time
    /// stay at the end with explicit nulls. The limit is clamped to 1..200.
    /// </summary>
    public Task<IReadOnlyList<DisasterEvent>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken = default) =>
        GetRecentAsync(limit, sourceId: null, cancellationToken: cancellationToken);

    /// <summary>
    /// Returns events newest-first by event time, optionally limited to one exact
    /// source id (case-insensitive). Events without an event time stay at the end.
    /// The limit is clamped to 1..200.
    /// </summary>
    public async Task<IReadOnlyList<DisasterEvent>> GetRecentAsync(
        int limit,
        string? sourceId,
        CancellationToken cancellationToken = default) =>
        await ReadAsync(Math.Clamp(limit, MinLimit, MaxLimit), sourceId, cancellationToken);

    /// <summary>Reads all persisted events without the public endpoint's display cap.</summary>
    public Task<IReadOnlyList<DisasterEvent>> GetForSelectionAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(-1, null, cancellationToken);

    private async Task<IReadOnlyList<DisasterEvent>> ReadAsync(int limit, string? sourceId, CancellationToken cancellationToken)
    {
        var normalizedSource = string.IsNullOrWhiteSpace(sourceId) ? null : sourceId.Trim();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT SourceId, NativeId, EventType, Title, EventTimeUtc, PublishedUtc,
                   UpdatedUtc, Latitude, Longitude, Country, Iso3, Severity,
                   SeverityUnit, AlertLevel, AlertScore, Tsunami, Significance,
                   Provenance, FetchedUtc, Certainty, EffectiveUtc, ExpiresUtc, DepthKm
            FROM DisasterEvents
            WHERE ($sourceId IS NULL OR SourceId COLLATE NOCASE = $sourceId)
            ORDER BY (EventTimeUtc IS NULL), EventTimeUtc DESC, Id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$sourceId", normalizedSource ?? (object)DBNull.Value);

        var events = new List<DisasterEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            events.Add(new DisasterEvent(
                SourceId: reader.GetString(0),
                NativeId: reader.GetString(1),
                EventType: reader.GetString(2),
                Title: reader.GetString(3),
                EventTimeUtc: NullableTimestamp(reader, 4),
                PublishedUtc: NullableTimestamp(reader, 5),
                UpdatedUtc: NullableTimestamp(reader, 6),
                Latitude: NullableDouble(reader, 7),
                Longitude: NullableDouble(reader, 8),
                Country: NullableString(reader, 9),
                Iso3: NullableString(reader, 10),
                Severity: NullableDouble(reader, 11),
                SeverityUnit: NullableString(reader, 12),
                AlertLevel: NullableString(reader, 13),
                AlertScore: NullableDouble(reader, 14),
                Tsunami: reader.IsDBNull(15) ? null : reader.GetInt64(15) != 0,
                Significance: reader.IsDBNull(16) ? null : reader.GetInt64(16),
                Provenance: reader.GetString(17),
                FetchedUtc: ParseTimestamp(reader.GetString(18)),
                Certainty: NullableString(reader, 19),
                EffectiveUtc: NullableTimestamp(reader, 20),
                ExpiresUtc: NullableTimestamp(reader, 21),
                DepthKm: NullableDouble(reader, 22)));
        }

        return events;
    }

    private static void EnsureColumn(SqliteConnection connection, string name, string definition)
    {
        using var info = connection.CreateCommand();
        info.CommandText = "PRAGMA table_info(DisasterEvents);";
        using var reader = info.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), name, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        reader.Close();

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE DisasterEvents ADD COLUMN {name} {definition};";
        alter.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static object TimestampOrNull(DateTimeOffset? value) =>
        value is { } timestamp ? FormatTimestamp(timestamp) : DBNull.Value;

    private static DateTimeOffset? NullableTimestamp(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ParseTimestamp(reader.GetString(ordinal));

    private static double? NullableDouble(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);

    private static string? NullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
