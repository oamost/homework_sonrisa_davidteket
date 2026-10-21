using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace WorldAlerts.Api.News;

/// <summary>
/// SQLite-backed news storage. Rows are unique on (SourceId, NativeId), so
/// re-polling the same story is idempotent across polls, restarts and process
/// lifetimes.
/// </summary>
public sealed class NewsStore
{
    public const int DefaultLimit = 50;
    public const int MinLimit = 1;
    public const int MaxLimit = 200;

    private readonly string _path;
    private readonly string _connectionString;

    public NewsStore(string path)
    {
        _path = Path.GetFullPath(path);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 5,
        }.ToString();
    }

    /// <summary>Creates the news schema. Throws when the database is unusable.</summary>
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
            CREATE TABLE IF NOT EXISTS NewsItems (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SourceId TEXT NOT NULL,
                SourceName TEXT NOT NULL,
                NativeId TEXT NOT NULL,
                Title TEXT NOT NULL,
                Link TEXT NOT NULL,
                PublishedUtc TEXT NULL,
                FirstSeenUtc TEXT NOT NULL,
                Family TEXT NOT NULL,
                CategoriesJson TEXT NOT NULL,
                Provenance TEXT NOT NULL,
                CONSTRAINT UQ_NewsItems_Source_Native UNIQUE (SourceId, NativeId)
            );
            """;
        command.ExecuteNonQuery();
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
            Console.Error.WriteLine($"SQLite news store initialization failed: {exception.GetType().Name}");
        }
    }

    /// <summary>
    /// Inserts stories, ignoring rows already present. Returns the number of rows
    /// actually inserted; duplicates are reported by the caller as parsed minus inserted.
    /// </summary>
    public async Task<int> InsertAsync(
        IReadOnlyCollection<NewsItem> items,
        CancellationToken cancellationToken = default)
    {
        if (items.Count == 0)
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
            INSERT INTO NewsItems (
                SourceId, SourceName, NativeId, Title, Link, PublishedUtc,
                FirstSeenUtc, Family, CategoriesJson, Provenance)
            VALUES (
                $sourceId, $sourceName, $nativeId, $title, $link, $publishedUtc,
                $firstSeenUtc, $family, $categoriesJson, $provenance)
            ON CONFLICT (SourceId, NativeId) DO NOTHING;
            """;

        var sourceId = command.Parameters.Add("$sourceId", SqliteType.Text);
        var sourceName = command.Parameters.Add("$sourceName", SqliteType.Text);
        var nativeId = command.Parameters.Add("$nativeId", SqliteType.Text);
        var title = command.Parameters.Add("$title", SqliteType.Text);
        var link = command.Parameters.Add("$link", SqliteType.Text);
        var publishedUtc = command.Parameters.Add("$publishedUtc", SqliteType.Text);
        var firstSeenUtc = command.Parameters.Add("$firstSeenUtc", SqliteType.Text);
        var family = command.Parameters.Add("$family", SqliteType.Text);
        var categoriesJson = command.Parameters.Add("$categoriesJson", SqliteType.Text);
        var provenance = command.Parameters.Add("$provenance", SqliteType.Text);

        var inserted = 0;
        foreach (var item in items)
        {
            sourceId.Value = item.SourceId;
            sourceName.Value = item.SourceName;
            nativeId.Value = item.NativeId;
            title.Value = item.Title;
            link.Value = item.Link;
            publishedUtc.Value = item.PublishedUtc is { } published
                ? FormatTimestamp(published)
                : DBNull.Value;
            firstSeenUtc.Value = FormatTimestamp(item.FirstSeenUtc);
            family.Value = item.Family;
            categoriesJson.Value = JsonSerializer.Serialize(item.Categories);
            provenance.Value = item.Provenance;

            inserted += await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        transaction.Commit();
        return inserted;
    }

    /// <summary>
    /// Returns stories newest-first by publication time when present, otherwise
    /// by first-seen time. The limit is clamped to 1..200.
    /// </summary>
    public async Task<IReadOnlyList<NewsItem>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        var clamped = Math.Clamp(limit, MinLimit, MaxLimit);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT SourceId, SourceName, NativeId, Title, Link, PublishedUtc,
                   FirstSeenUtc, Family, CategoriesJson, Provenance
            FROM NewsItems
            ORDER BY COALESCE(PublishedUtc, FirstSeenUtc) DESC, Id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", clamped);

        var items = new List<NewsItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new NewsItem(
                SourceId: reader.GetString(0),
                SourceName: reader.GetString(1),
                NativeId: reader.GetString(2),
                Title: reader.GetString(3),
                Link: reader.GetString(4),
                PublishedUtc: reader.IsDBNull(5) ? null : ParseTimestamp(reader.GetString(5)),
                FirstSeenUtc: ParseTimestamp(reader.GetString(6)),
                Family: reader.GetString(7),
                Categories: JsonSerializer.Deserialize<string[]>(reader.GetString(8)) ?? Array.Empty<string>(),
                Provenance: reader.GetString(9)));
        }

        return items;
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
