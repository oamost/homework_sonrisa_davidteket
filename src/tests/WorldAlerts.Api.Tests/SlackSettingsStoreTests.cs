using Microsoft.AspNetCore.DataProtection;
using WorldAlerts.Api.Delivery.Slack;

namespace WorldAlerts.Api.Tests;

public sealed class SlackSettingsStoreTests
{
    [Fact]
    public async Task Protected_settings_survive_reopening_and_never_write_plaintext()
    {
        var folder = Path.Combine(Path.GetTempPath(), "slack-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var provider = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(folder, "keys")));
            var path = Path.Combine(folder, "alerts.db");
            var first = new SlackSettingsStore(path, provider);
            var webhook = "https://hooks.slack.com/services/T/B/XXXXXXXXXXXXXXXXXXXXXXXX";
            await first.SaveAsync(new SlackSettings(webhook, true));
            var second = new SlackSettingsStore(path, provider);
            Assert.Equal(webhook, (await second.ReadAsync())!.WebhookUrl);
            Assert.DoesNotContain(webhook, File.ReadAllText(path + ".slack.protected"));
            Assert.DoesNotContain("XXXXXXXXXXXXXXXX", File.ReadAllText(path + ".slack.protected"));
            var status = SlackSettingsStore.Redact(await second.ReadAsync());
            Assert.Equal("default", status.ConfigId);
            Assert.DoesNotContain("XXXXXXXX", System.Text.Json.JsonSerializer.Serialize(status));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Update_requires_valid_slack_url_and_explicit_enable()
    {
        var (invalid, error) = SlackSettingsStore.Validate(new SlackSettingsUpdate("https://localhost/private", true), null);
        Assert.Null(invalid);
        Assert.DoesNotContain("localhost", error!);
        var (disabled, _) = SlackSettingsStore.Validate(new SlackSettingsUpdate("https://hooks.slack.com/services/T/B/XXXXXXXXXXXXXXXXXXXXXXXX", false), null);
        Assert.False(disabled!.Enabled);
        var (retained, _) = SlackSettingsStore.Validate(new SlackSettingsUpdate(null, true), disabled);
        Assert.Equal(disabled.WebhookUrl, retained!.WebhookUrl);
    }
}
