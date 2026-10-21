using System.Globalization;
using Microsoft.Data.Sqlite;

namespace WorldAlerts.Api.Advisories;

/// <summary>
/// SQLite-backed cyber-advisory storage. Rows are unique on
/// (SourceId, CveId), so re-polling the same catalogue is idempotent.
/// </summary>
public sealed class AdvisoryStore
{
    public const int DefaultLimit = 50;
    public const int MinLimit = 1;
    public const int MaxLimit = 200;

    private readonly string _path;
    private readonly string _connectionString;

    public AdvisoryStore(string path)
    {
        _path = Path.GetFullPath(path);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 5,
        }.ToString();
    }

    /// <summary>Creates the advisory schema. Throws when the database is unusable.</summary>
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
            CREATE TABLE IF NOT EXISTS Advisories (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SourceId TEXT NOT NULL,
                CveId TEXT NOT NULL,
                Vendor TEXT NOT NULL,
                Product TEXT NOT NULL,
                Name TEXT NOT NULL,
                DateAdded TEXT NULL,
                DueDate TEXT NULL,
                KnownRansomware INTEGER NOT NULL,
                Provenance TEXT NOT NULL,
                FetchedUtc TEXT NOT NULL,
                CONSTRAINT UQ_Advisories_Source_Cve UNIQUE (SourceId, CveId)
            );
            """;
        command.ExecuteNonQuery();

        using var indexCommand = connection.CreateCommand();
        indexCommand.CommandText =
            """
            CREATE INDEX IF NOT EXISTS IX_Advisories_DateAdded
                ON Advisories (DateAdded DESC);
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
            Console.Error.WriteLine($"SQLite advisory store initialization failed: {exception.GetType().Name}");
        }
    }

    /// <summary>
    /// Inserts advisories, ignoring rows already present. Returns the number of
    /// rows actually inserted; duplicates are reported by the caller as parsed
    /// minus inserted.
    /// </summary>
    public async Task<int> InsertAsync(
        IReadOnlyCollection<Advisory> advisories,
        CancellationToken cancellationToken = default)
    {
        if (advisories.Count == 0)
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
            INSERT INTO Advisories (
                SourceId, CveId, Vendor, Product, Name, DateAdded, DueDate,
                KnownRansomware, Provenance, FetchedUtc)
            VALUES (
                $sourceId, $cveId, $vendor, $product, $name, $dateAdded, $dueDate,
                $knownRansomware, $provenance, $fetchedUtc)
            ON CONFLICT (SourceId, CveId) DO NOTHING;
            """;

        var sourceId = command.Parameters.Add("$sourceId", SqliteType.Text);
        var cveId = command.Parameters.Add("$cveId", SqliteType.Text);
        var vendor = command.Parameters.Add("$vendor", SqliteType.Text);
        var product = command.Parameters.Add("$product", SqliteType.Text);
        var name = command.Parameters.Add("$name", SqliteType.Text);
        var dateAdded = command.Parameters.Add("$dateAdded", SqliteType.Text);
        var dueDate = command.Parameters.Add("$dueDate", SqliteType.Text);
        var knownRansomware = command.Parameters.Add("$knownRansomware", SqliteType.Integer);
        var provenance = command.Parameters.Add("$provenance", SqliteType.Text);
        var fetchedUtc = command.Parameters.Add("$fetchedUtc", SqliteType.Text);

        var inserted = 0;
        foreach (var item in advisories)
        {
            sourceId.Value = item.SourceId;
            cveId.Value = item.CveId;
            vendor.Value = item.Vendor;
            product.Value = item.Product;
            name.Value = item.Name;
            dateAdded.Value = DateOrNull(item.DateAdded);
            dueDate.Value = DateOrNull(item.DueDate);
            knownRansomware.Value = item.KnownRansomware ? 1 : 0;
            provenance.Value = item.Provenance;
            fetchedUtc.Value = FormatTimestamp(item.FetchedUtc);

            inserted += await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        transaction.Commit();
        return inserted;
    }

    /// <summary>
    /// Returns advisories newest-first by date added, optionally limited to one
    /// exact source id (case-insensitive). The limit is clamped to 1..200.
    /// </summary>
    public async Task<IReadOnlyList<Advisory>> GetRecentAsync(
        int limit,
        string? sourceId = null,
        CancellationToken cancellationToken = default)
    {
        var clamped = Math.Clamp(limit, MinLimit, MaxLimit);
        var normalizedSource = string.IsNullOrWhiteSpace(sourceId) ? null : sourceId.Trim();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT SourceId, CveId, Vendor, Product, Name, DateAdded, DueDate,
                   KnownRansomware, Provenance, FetchedUtc
            FROM Advisories
            WHERE ($sourceId IS NULL OR SourceId COLLATE NOCASE = $sourceId)
            ORDER BY (DateAdded IS NULL), DateAdded DESC, Id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", clamped);
        command.Parameters.AddWithValue("$sourceId", normalizedSource ?? (object)DBNull.Value);

        var advisories = new List<Advisory>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            advisories.Add(new Advisory(
                SourceId: reader.GetString(0),
                CveId: reader.GetString(1),
                Vendor: reader.GetString(2),
                Product: reader.GetString(3),
                Name: reader.GetString(4),
                DateAdded: NullableDate(reader, 5),
                DueDate: NullableDate(reader, 6),
                KnownRansomware: reader.GetInt64(7) != 0,
                Provenance: reader.GetString(8),
                FetchedUtc: ParseTimestamp(reader.GetString(9))));
        }

        return advisories;
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static object DateOrNull(DateOnly? value) =>
        value is { } date ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DBNull.Value;

    private static DateOnly? NullableDate(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : DateOnly.ParseExact(
                reader.GetString(ordinal),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None);

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
