namespace WorldAlerts.Api.Delivery.Slack;

/// <summary>Only sends for the explicit, enabled, private configuration reference.</summary>
public sealed class StoredSlackSender(SlackSettingsStore store, SlackWebhookSender sender)
{
    public async Task<bool> SendAsync(string configId, string text, CancellationToken ct = default)
    {
        if (configId != SlackSettingsStore.ConfigId) return false;
        var settings = await store.ReadAsync(ct);
        if (settings is not { Enabled: true }) return false;
        await sender.SendAsync(text, new SlackOptions { WebhookUrl = settings.WebhookUrl }, ct);
        return true;
    }
}
