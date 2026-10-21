using System.Globalization;
using Microsoft.Data.Sqlite;

namespace WorldAlerts.Api.Markets;

/// <summary>
/// SQLite-backed market observation storage. Rows are unique on
/// (SourceId, Instrument, Interval, ObservationUtc), so re-polling the same
/// ticker, candle, reference date or quote is idempotent across restarts.
/// </summary>
public sealed class MarketStore
{
    public const int DefaultLimit = 50;
    public const int MinLimit = 1;
    public const int MaxLimit = 200;

    private readonly string _path;
    private readonly string _connectionString;

    public MarketStore(string path)
    {
        _path = Path.GetFullPath(path);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 5,
        }.ToString();
    }

    /// <summary>Creates the market schema. Throws when the database is unusable.</summary>
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
            CREATE TABLE IF NOT EXISTS MarketObservations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SourceId TEXT NOT NULL,
                Venue TEXT NOT NULL,
                Region TEXT NOT NULL,
                Instrument TEXT NOT NULL,
                QuoteUnit TEXT NOT NULL,
                Value REAL NOT NULL,
                ObservationUtc TEXT NOT NULL,
                FetchedUtc TEXT NOT NULL,
                Delayed INTEGER NOT NULL,
                DelaySemantics TEXT NOT NULL,
                Interval TEXT NOT NULL,
                Provenance TEXT NOT NULL,
                CONSTRAINT UQ_MarketObservations_Source_Instrument_Interval_Observation
                    UNIQUE (SourceId, Instrument, Interval, ObservationUtc)
            );
            """;
        command.ExecuteNonQuery();

        using var indexCommand = connection.CreateCommand();
        indexCommand.CommandText =
            """
            CREATE INDEX IF NOT EXISTS IX_MarketObservations_Observation
                ON MarketObservations (ObservationUtc DESC);
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
            Console.Error.WriteLine($"SQLite market store initialization failed: {exception.GetType().Name}");
        }
    }

    /// <summary>
    /// Inserts observations, ignoring rows already present. Returns the number of
    /// rows actually inserted; duplicates are reported by the caller as parsed
    /// minus inserted.
    /// </summary>
    public async Task<int> InsertAsync(
        IReadOnlyCollection<MarketObservation> observations,
        CancellationToken cancellationToken = default)
    {
        if (observations.Count == 0)
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
            INSERT INTO MarketObservations (
                SourceId, Venue, Region, Instrument, QuoteUnit, Value,
                ObservationUtc, FetchedUtc, Delayed, DelaySemantics, Interval, Provenance)
            VALUES (
                $sourceId, $venue, $region, $instrument, $quoteUnit, $value,
                $observationUtc, $fetchedUtc, $delayed, $delaySemantics, $interval, $provenance)
            ON CONFLICT (SourceId, Instrument, Interval, ObservationUtc) DO NOTHING;
            """;

        var sourceId = command.Parameters.Add("$sourceId", SqliteType.Text);
        var venue = command.Parameters.Add("$venue", SqliteType.Text);
        var region = command.Parameters.Add("$region", SqliteType.Text);
        var instrument = command.Parameters.Add("$instrument", SqliteType.Text);
        var quoteUnit = command.Parameters.Add("$quoteUnit", SqliteType.Text);
        var value = command.Parameters.Add("$value", SqliteType.Real);
        var observationUtc = command.Parameters.Add("$observationUtc", SqliteType.Text);
        var fetchedUtc = command.Parameters.Add("$fetchedUtc", SqliteType.Text);
        var delayed = command.Parameters.Add("$delayed", SqliteType.Integer);
        var delaySemantics = command.Parameters.Add("$delaySemantics", SqliteType.Text);
        var interval = command.Parameters.Add("$interval", SqliteType.Text);
        var provenance = command.Parameters.Add("$provenance", SqliteType.Text);

        var inserted = 0;
        foreach (var observation in observations)
        {
            sourceId.Value = observation.SourceId;
            venue.Value = observation.Venue;
            region.Value = observation.Region;
            instrument.Value = observation.Instrument;
            quoteUnit.Value = observation.QuoteUnit;
            value.Value = observation.Value;
            observationUtc.Value = FormatTimestamp(observation.ObservationUtc);
            fetchedUtc.Value = FormatTimestamp(observation.FetchedUtc);
            delayed.Value = observation.Delayed ? 1 : 0;
            delaySemantics.Value = observation.DelaySemantics;
            interval.Value = observation.Interval;
            provenance.Value = observation.Provenance;

            inserted += await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        transaction.Commit();
        return inserted;
    }

    /// <summary>
    /// Returns observations newest-first by observation time. The limit is
    /// clamped to 1..200. All timestamps and delay markers are explicit.
    /// </summary>
    public Task<IReadOnlyList<MarketObservation>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken = default) =>
        GetRecentAsync(limit, sourceId: null, cancellationToken: cancellationToken);

    /// <summary>
    /// Returns observations newest-first by observation time, optionally limited
    /// to one exact source id (case-insensitive). The limit is clamped to 1..200.
    /// </summary>
    public async Task<IReadOnlyList<MarketObservation>> GetRecentAsync(
        int limit,
        string? sourceId,
        CancellationToken cancellationToken = default) =>
        await ReadAsync(Math.Clamp(limit, MinLimit, MaxLimit), sourceId, cancellationToken);

    /// <summary>Reads all persisted observations, including older baseline history, without a display cap.</summary>
    public Task<IReadOnlyList<MarketObservation>> GetForSelectionAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(-1, null, cancellationToken);

    private async Task<IReadOnlyList<MarketObservation>> ReadAsync(int limit, string? sourceId, CancellationToken cancellationToken)
    {
        var normalizedSource = string.IsNullOrWhiteSpace(sourceId) ? null : sourceId.Trim();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT SourceId, Venue, Region, Instrument, QuoteUnit, Value,
                   ObservationUtc, FetchedUtc, Delayed, DelaySemantics, Interval, Provenance
            FROM MarketObservations
            WHERE ($sourceId IS NULL OR SourceId COLLATE NOCASE = $sourceId)
            ORDER BY ObservationUtc DESC, Id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$sourceId", normalizedSource ?? (object)DBNull.Value);

        var observations = new List<MarketObservation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            observations.Add(new MarketObservation(
                SourceId: reader.GetString(0),
                Venue: reader.GetString(1),
                Region: reader.GetString(2),
                Instrument: reader.GetString(3),
                QuoteUnit: reader.GetString(4),
                Value: reader.GetDouble(5),
                ObservationUtc: ParseTimestamp(reader.GetString(6)),
                FetchedUtc: ParseTimestamp(reader.GetString(7)),
                Delayed: reader.GetInt64(8) != 0,
                DelaySemantics: reader.GetString(9),
                Interval: reader.GetString(10),
                Provenance: reader.GetString(11)));
        }

        return observations;
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
