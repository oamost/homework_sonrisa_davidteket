using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace WorldAlerts.Api.Subscriptions;

public sealed record EventTypePreference(bool IsCustomized, IReadOnlyList<string> SelectedTypes);

/// <summary>One local user's selection, distinct from legacy per-channel destinations.</summary>
public sealed class EventTypePreferenceStore(string databasePath)
{
    public static readonly IReadOnlyList<string> ApprovedTypes =
    [
        "world-news", "conflict", "politics", "diplomacy", "disaster.earthquake",
        "disaster.flood", "disaster.cyclone", "disaster.wildfire", "disaster.volcano",
        "disaster.drought", "disaster.tsunami", "disaster.landslide", "disaster.epidemic",
        "market.crypto", "market.fx", "market.indices", "health.humanitarian",
        "tech.cyber", "tech.ai", "tech.acquisitions", "tech.critical-infrastructure",
        "environment.energy", "science.transformative", "economy.financial",
    ];

    private readonly string _connection = new SqliteConnectionStringBuilder
    {
        DataSource = Path.GetFullPath(databasePath), Mode = SqliteOpenMode.ReadWriteCreate, DefaultTimeout = 5,
    }.ToString();

    public void Initialize()
    {
        using var db = new SqliteConnection(_connection);
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS EventTypePreferences (UserId TEXT PRIMARY KEY, SelectedTypes TEXT NOT NULL)";
        command.ExecuteNonQuery();
    }

    public async Task<EventTypePreference> GetAsync(string userId, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(_connection);
        await db.OpenAsync(ct);
        await using var command = db.CreateCommand();
        command.CommandText = "SELECT SelectedTypes FROM EventTypePreferences WHERE UserId = $user";
        command.Parameters.AddWithValue("$user", userId);
        var value = await command.ExecuteScalarAsync(ct);
        return value is string json
            ? new EventTypePreference(true, JsonSerializer.Deserialize<string[]>(json) ?? [])
            : new EventTypePreference(false, ApprovedTypes.ToArray());
    }

    public async Task SaveAsync(string userId, IReadOnlyCollection<string> types, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(types);
        var allowed = new HashSet<string>(ApprovedTypes, StringComparer.Ordinal);
        if (types.Count != types.Distinct(StringComparer.Ordinal).Count() || types.Any(type => !allowed.Contains(type)))
            throw new ArgumentException("Unknown or duplicate event type.", nameof(types));

        await using var db = new SqliteConnection(_connection);
        await db.OpenAsync(ct);
        await using var command = db.CreateCommand();
        command.CommandText = "INSERT INTO EventTypePreferences (UserId, SelectedTypes) VALUES ($user, $types) " +
            "ON CONFLICT(UserId) DO UPDATE SET SelectedTypes = excluded.SelectedTypes";
        command.Parameters.AddWithValue("$user", userId);
        command.Parameters.AddWithValue("$types", JsonSerializer.Serialize(ApprovedTypes.Where(types.Contains).ToArray()));
        await command.ExecuteNonQueryAsync(ct);
    }
}
