using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace WorldAlerts.Api.Tests;

/// <summary>
/// Integration tests for the task02 foundation contract: the API may report
/// healthy only while its configured SQLite database is genuinely usable, and
/// a failing health check must not disclose configuration details.
/// </summary>
public sealed class FoundationTests : IDisposable
{
    private const string DatabasePathSetting = "WorldAlerts:DatabasePath";
    private const string PollingEnabledSetting = "WorldAlerts:News:PollingEnabled";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"world-alerts-tests-{Guid.NewGuid():N}");

    public FoundationTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup of a test-owned temporary directory.
        }
    }

    [Fact]
    public async Task Health_returns_200_when_the_configured_sqlite_database_is_usable()
    {
        var databasePath = Path.Combine(_root, "events.sqlite");
        CreateDatabase(databasePath);

        using var factory = CreateApi(databasePath);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(File.Exists(databasePath), "The configured SQLite file must exist.");
        Assert.Equal(1L, ReadSeededRow(databasePath));
    }

    [Fact]
    public async Task Health_returns_failure_when_the_configured_database_path_cannot_be_opened()
    {
        // The parent "directory" is a regular file, so SQLite can never open
        // this path regardless of whether the app tries to create directories.
        var blocker = Path.Combine(_root, "blocked");
        await File.WriteAllTextAsync(blocker, "not a directory");
        var databasePath = Path.Combine(blocker, "events.sqlite");

        using var factory = CreateApi(databasePath);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.True(
            (int)response.StatusCode >= 400,
            $"Expected an error status for an unopenable database, got {(int)response.StatusCode}.");
    }

    [Fact]
    public async Task Health_failure_does_not_disclose_the_configured_database_path()
    {
        var blocker = Path.Combine(_root, "blocked");
        await File.WriteAllTextAsync(blocker, "not a directory");
        var databasePath = Path.Combine(blocker, "events.sqlite");

        using var factory = CreateApi(databasePath);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");
        var body = await response.Content.ReadAsStringAsync();
        var headers = string.Join(
            "\n",
            response.Headers.Select(header => $"{header.Key}: {string.Join(", ", header.Value)}")
                .Concat(response.Content.Headers.Select(header => $"{header.Key}: {string.Join(", ", header.Value)}")));

        Assert.False(response.IsSuccessStatusCode, "An unopenable database must not be reported as healthy.");
        Assert.DoesNotContain(databasePath, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Data Source", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(databasePath, headers, StringComparison.OrdinalIgnoreCase);
    }

    private static WebApplicationFactory<Program> CreateApi(string databasePath) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(
            builder => builder
                .UseSetting(DatabasePathSetting, databasePath)
                .UseSetting(PollingEnabledSetting, "false")
                .UseSetting("WorldAlerts:Markets:PollingEnabled", "false")
                .UseSetting("WorldAlerts:Disasters:PollingEnabled", "false")
                .UseSetting("WorldAlerts:Advisories:PollingEnabled", "false"));

    private static void CreateDatabase(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadWriteCreate");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "CREATE TABLE health_probe (id INTEGER PRIMARY KEY, marker TEXT NOT NULL);" +
            "INSERT INTO health_probe (marker) VALUES ('seeded');";
        command.ExecuteNonQuery();
    }

    private static long ReadSeededRow(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM health_probe WHERE marker = 'seeded';";
        return (long)command.ExecuteScalar()!;
    }
}
