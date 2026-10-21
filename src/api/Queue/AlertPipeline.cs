using WorldAlerts.Api.Dedupe;
using WorldAlerts.Api.Disasters;
using WorldAlerts.Api.Delivery;
using WorldAlerts.Api.Delivery.Slack;
using WorldAlerts.Api.Delivery.Webhooks;
using WorldAlerts.Api.Events;
using WorldAlerts.Api.Markets;
using WorldAlerts.Api.News;
using WorldAlerts.Api.Selection;
using WorldAlerts.Api.Subscriptions;

namespace WorldAlerts.Api.Queue;

/// <summary>Bridges the existing, read-only scoring/eligibility selector to durable notification jobs.</summary>
public sealed class AlertPipeline(AlertSelectionService selection, NewsStore news, MarketStore markets,
    DisasterStore disasters, SubscriptionStore subscriptions, EventDedupeService dedupe,
    NotificationQueueStore queue, TimeProvider clock, EventTypePreferenceStore? preferences = null,
    EmailSettingsStore? emailSettings = null, SlackSettingsStore? slackSettings = null,
    WebhookSettingsStore? webhooks = null)
{
    public async Task<int> EnqueueSelectedAsync(CancellationToken ct = default)
    {
        var preview = await selection.SelectAsync(ct);
        var legacy = await subscriptions.ListAsync(Subscription.DefaultUserId, ct);
        var preference = preferences is null ? null : await preferences.GetAsync(Subscription.DefaultUserId, ct);
        // A legacy installation keeps its previous per-type targets until its owner saves
        // the new shared selection. Never silently subscribe a pre-existing recipient.
        var useConfigured = preference is not null && (preference.IsCustomized || legacy.Count == 0);
        var email = emailSettings is not null ? await emailSettings.ReadAsync(ct) : null;
        var slack = slackSettings is not null ? await slackSettings.ReadAsync(ct) : null;
        var configuredChannels = new List<(string Channel, string Destination)>();
        if (useConfigured && email is { Enabled: true } && !string.IsNullOrWhiteSpace(email.RecipientAddress))
            configuredChannels.Add(("email", email.RecipientAddress));
        if (useConfigured && slack is { Enabled: true }) configuredChannels.Add(("slack", string.Empty));
        if (useConfigured && webhooks is not null)
            foreach (var configured in (await webhooks.ListAsync(ct)).Where(config => config.Enabled))
                configuredChannels.Add(("webhook", configured.Id));
        var selected = configuredChannels.Count > 0
            ? (await selection.PreviewPublicAsync(ct)).Events.Where(e => e.Eligibility.IsEligible && e.Importance.IsImportant)
                .Select(e => preview.Events.FirstOrDefault(legacyItem => Key(legacyItem) == Key(e)) is { } matching
                    ? e with { Matches = matching.Matches } : e).ToArray()
            : preview.Events.Where(e => e.Selected).ToArray();
        if (selected.Length == 0) return 0;
        var now = clock.GetUtcNow();
        var records = (await news.GetForSelectionAsync(ct)).Select(n => EventRecordMappers.FromNews(n, now))
            .Concat((await disasters.GetForSelectionAsync(ct)).Select(d => EventRecordMappers.FromDisaster(d, now)))
            .Concat((await markets.GetForSelectionAsync(ct)).Select(m => EventRecordMappers.FromMarket(m, now)))
            .ToArray();
        var byKey = records.GroupBy(Key).ToDictionary(group => group.Key, group => new Queue<EventRecord>(group));
        var subscriberMap = legacy
            .ToDictionary(s => s.Id);
        var count = 0;
        foreach (var item in selected)
        {
            if (preference is { IsCustomized: true }
                && !item.EventTypes.Any(preference.SelectedTypes.Contains)) continue;
            if (!byKey.TryGetValue(Key(item), out var candidates) || candidates.Count == 0)
                continue; // Never manufacture an identity from an incomplete preview.
            var record = candidates.Dequeue();
            var revision = await dedupe.UpsertAsync(record, ct);
            foreach (var match in item.Matches)
            {
                if (!subscriberMap.TryGetValue(match.SubscriptionId, out var subscriber)
                    || !subscriber.Enabled || subscriber.Channel != match.Channel
                    || subscriber.Channel == "email" && emailSettings is not null && email?.Enabled != true
                    || subscriber.Channel == "slack" && slackSettings is not null && slack?.Enabled != true
                    || !string.Equals(subscriber.EventType, match.EventType, StringComparison.OrdinalIgnoreCase))
                    continue;
                var subscriberId = $"{subscriber.UserId}:{subscriber.Id}";
                if (!await dedupe.ShouldNotifyAsync(revision, subscriberId, subscriber.Channel, ct)) continue;
                // Slack webhook stays in protected application configuration, never in subscription destination.
                // Email must have a selected recipient; do not silently use the SMTP sender address.
                if (subscriber.Channel == "email" && string.IsNullOrWhiteSpace(subscriber.Destination)) continue;
                // Slack jobs stay pending for the separately configured Slack dispatcher;
                // the email worker claims email only and can never send these jobs.
                if (subscriber.Channel is not ("email" or "slack")) continue;
                if (await queue.EnqueueAsync(revision.CanonicalKey, revision.Fingerprint,
                    subscriberId, subscriber.Channel,
                    subscriber.Channel == "email" ? subscriber.Destination! : string.Empty,
                    item.Title, $"{item.Summary}\n{item.Link ?? item.ProvenanceUrl}", now, ct)) count++;
            }
            if (preference is null || !item.EventTypes.Any(preference.SelectedTypes.Contains)) continue;
            foreach (var target in configuredChannels)
            {
                // Existing legacy rows, including paused ones, take precedence for that channel/type.
                if (legacy.Any(s => s.Channel == target.Channel && item.EventTypes.Contains(s.EventType))) continue;
                var subscriberId = $"{Subscription.DefaultUserId}:configured:{target.Channel}" +
                    (target.Channel == "webhook" ? $":{target.Destination}" : "");
                if (!await dedupe.ShouldNotifyAsync(revision, subscriberId, target.Channel, ct)) continue;
                if (await queue.EnqueueAsync(revision.CanonicalKey, revision.Fingerprint,
                    subscriberId, target.Channel, target.Destination,
                    item.Title, $"{item.Summary}\n{item.Link ?? item.ProvenanceUrl}", now, ct)) count++;
            }
        }
        return count;
    }

    private static string Key(EventRecord record) => string.Join('\u001F', record.Family,
        record.SourceId, record.SourceItemId, record.ProvenanceUrl,
        record.EventTimeUtc?.UtcTicks, record.PublishedTimeUtc?.UtcTicks);

    private static string Key(AlertSelectionEvent item) => string.Join('\u001F', item.Family,
        item.SourceId, item.SourceItemId, item.ProvenanceUrl,
        item.EventTimeUtc?.UtcTicks, item.PublishedTimeUtc?.UtcTicks);
}
