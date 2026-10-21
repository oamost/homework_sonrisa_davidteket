using Microsoft.Data.Sqlite;
using WorldAlerts.Api.Delivery;
using WorldAlerts.Api.Delivery.Slack;
using WorldAlerts.Api.Delivery.Webhooks;
using WorldAlerts.Api.Selection;
using WorldAlerts.Api.Subscriptions;

namespace WorldAlerts.Api.Queue;

/// <summary>Durable, globally bounded synthetic event. Never accepts destinations from callers.</summary>
public sealed class TestEventService(string databasePath, EventTypePreferenceStore preferences,
    SubscriptionStore subscriptions, EmailSettingsStore emailSettings, SlackSettingsStore slackSettings,
    NotificationQueueStore queue, TimeProvider clock, WebhookSettingsStore? webhookSettings = null)
{
    private readonly string _connection = new SqliteConnectionStringBuilder
    { DataSource = Path.GetFullPath(databasePath), Mode = SqliteOpenMode.ReadWriteCreate, DefaultTimeout = 5 }.ToString();

    public void Initialize()
    {
        using var db = new SqliteConnection(_connection);
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS SyntheticTestEvents (Id INTEGER PRIMARY KEY AUTOINCREMENT, CreatedUtc TEXT NOT NULL)";
        command.ExecuteNonQuery();
    }

    public async Task<(bool Limited, string Message)> EmitAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        long id;
        await using (var db = new SqliteConnection(_connection))
        {
            await db.OpenAsync(ct);
            using var transaction = db.BeginTransaction(deferred: false);
            await using var count = db.CreateCommand();
            count.Transaction = transaction;
            count.CommandText = "SELECT CreatedUtc FROM SyntheticTestEvents WHERE CreatedUtc >= $since ORDER BY Id DESC";
            count.Parameters.AddWithValue("$since", now.AddDays(-1).ToString("O"));
            var attempts = new List<DateTimeOffset>();
            await using (var reader = await count.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct)) attempts.Add(DateTimeOffset.Parse(reader.GetString(0)));
            if (attempts.Count >= 20 || attempts.Any(time => now - time < TimeSpan.FromMinutes(1)))
            {
                transaction.Commit();
                return (true, "TEST send limit reached. Try again after one minute.");
            }
            await using var insert = db.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO SyntheticTestEvents (CreatedUtc) VALUES ($now); SELECT last_insert_rowid();";
            insert.Parameters.AddWithValue("$now", now.ToString("O"));
            id = Convert.ToInt64(await insert.ExecuteScalarAsync(ct));
            transaction.Commit();
        }

        var preference = await preferences.GetAsync(Subscription.DefaultUserId, ct);
        if (!preference.SelectedTypes.Contains("world-news"))
            return (false, "TEST event created; no notification sent because World news is not selected.");

        var legacy = await subscriptions.ListAsync(Subscription.DefaultUserId, ct);
        var configuredEmail = await emailSettings.ReadAsync(ct);
        var configuredSlack = await slackSettings.ReadAsync(ct);
        var targets = new List<(string Channel, string Destination, string Subscriber)>();
        foreach (var row in legacy.Where(row => row.EventType == "world-news" && row.Enabled
            && (row.Channel == "email" && configuredEmail?.Enabled == true
                || row.Channel == "slack" && configuredSlack?.Enabled == true)))
            targets.Add((row.Channel, row.Channel == "email" ? row.Destination ?? "" : "", $"local:{row.Id}"));

        // Enabled private channel settings are explicit opt-in even when an
        // unrelated old legacy subscription exists.
        {
            if (!legacy.Any(row => row.EventType == "world-news" && row.Channel == "email")
                && configuredEmail is { Enabled: true, RecipientAddress: { Length: > 0 } } email)
                targets.Add(("email", email.RecipientAddress, "local:configured:email"));
            if (!legacy.Any(row => row.EventType == "world-news" && row.Channel == "slack")
                && configuredSlack is { Enabled: true })
                targets.Add(("slack", "", "local:configured:slack"));
            if (webhookSettings is not null)
                foreach (var webhook in (await webhookSettings.ListAsync(ct)).Where(item => item.Enabled))
                    targets.Add(("webhook", webhook.Id, $"local:configured:webhook:{webhook.Id}"));
        }

        var enqueued = 0;
        foreach (var target in targets.Where(target => target.Channel is "email" or "slack" or "webhook"
            && (target.Channel != "email" || !string.IsNullOrWhiteSpace(target.Destination))))
            if (await queue.EnqueueAsync($"synthetic-test:{id}", "test", target.Subscriber, target.Channel,
                target.Destination, "TEST — not real news", "This is a World Alerts test event, not a real news report.", now, ct))
                enqueued++;
        return (false, enqueued == 0
            ? "TEST event created; no configured channel matches the saved World news selection."
            : "TEST event queued for configured matching channels. Check delivery status and destinations.");
    }

    public async Task<IReadOnlyList<ImportantNewsItem>> RecentAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var result = new List<ImportantNewsItem>();
        await using var db = new SqliteConnection(_connection);
        await db.OpenAsync(ct);
        await using var read = db.CreateCommand();
        read.CommandText = "SELECT Id, CreatedUtc FROM SyntheticTestEvents WHERE CreatedUtc >= $since ORDER BY Id DESC LIMIT 20";
        read.Parameters.AddWithValue("$since", now.AddMinutes(-5).ToString("O"));
        await using var reader = await read.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var created = DateTimeOffset.Parse(reader.GetString(1));
            result.Add(new ImportantNewsItem("TEST — not real news", "Synthetic event requested locally to check notifications.",
                "local-test", "World Alerts internal test", reader.GetInt64(0).ToString(),
                "/subscriptions", "/subscriptions", created, "test.synthetic", ["Explicit local test request."],
                "News", ["world-news"], created, 1.0));
        }
        return result;
    }
}
