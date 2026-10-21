using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

var configuredPath = builder.Configuration["WorldAlerts:DatabasePath"];
var databasePath = string.IsNullOrWhiteSpace(configuredPath)
    ? Path.Combine(builder.Environment.ContentRootPath, "data", "worldalerts.sqlite")
    : configuredPath;

var database = new SqliteDatabase(Path.GetFullPath(databasePath));
database.TryInitialize();
builder.Services.AddSingleton(database);

var app = builder.Build();

app.MapGet("/", () => "World Alerts API");
app.MapGet("/api/health", async (SqliteDatabase db, CancellationToken cancellationToken) =>
    await db.CanConnectAsync(cancellationToken)
        ? Results.Ok(new HealthResponse("ok"))
        : Results.Json(new HealthResponse("unavailable"), statusCode: StatusCodes.Status503ServiceUnavailable));

app.Run();

internal sealed record HealthResponse(string Status);

/// <summary>
/// Owns the local SQLite file. Initialization failure is recorded, not thrown, so the API still starts
/// and reports itself unhealthy without disclosing the configured path.
/// </summary>
internal sealed class SqliteDatabase
{
    private readonly string _path;
    private readonly string _connectionString;

    public SqliteDatabase(string path)
    {
        _path = path;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 5,
        }.ToString();
    }

    public void TryInitialize()
    {
        try
        {
            EnsureDirectory();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS SchemaVersions (
                    Version INTEGER NOT NULL,
                    AppliedUtc TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }
        catch (Exception exception)
        {
            // Unusable database: health checks report unavailability instead of failing startup.
            Console.Error.WriteLine($"SQLite initialization failed: {exception.GetType().Name}");
        }
    }

    private void EnsureDirectory()
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            return await command.ExecuteScalarAsync(cancellationToken) is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

public partial class Program;
