using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace WorldAlerts.Api.Delivery.Slack;

public sealed record SlackSettings(string WebhookUrl, bool Enabled)
{
    public string? ChannelLabel { get; init; }
}
public sealed record SlackSettingsUpdate(string? WebhookUrl, bool Enabled)
{
    public string? ChannelLabel { get; init; }
}
public sealed record SlackSettingsStatus(bool Configured, bool Enabled, string ConfigId, string Target)
{
    public string? ChannelLabel { get; init; }
}

/// <summary>Protected on-disk configuration; share the host's persistent DataProtection key ring.</summary>
public sealed class SlackSettingsStore(string databasePath, IDataProtectionProvider protection)
{
    public const string ConfigId = "default";
    public const string ProtectionPurpose = "WorldAlerts.SlackSettings.v1";
    private readonly string _path = Path.GetFullPath(databasePath) + ".slack.protected";
    private readonly IDataProtector _protector = protection.CreateProtector(ProtectionPurpose);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<SlackSettings?> ReadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!File.Exists(_path)) return null;
            var ciphertext = await File.ReadAllTextAsync(_path, ct);
            return JsonSerializer.Deserialize<SlackSettings>(_protector.Unprotect(ciphertext));
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(SlackSettings settings, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temp, _protector.Protect(JsonSerializer.Serialize(settings)), ct);
                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                    File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                File.Move(temp, _path, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        finally { _gate.Release(); }
    }

    public static SlackSettingsStatus Redact(SlackSettings? settings) => settings is null
        ? new(false, false, ConfigId, SlackWebhookRedactor.NotConfigured)
        : new SlackSettingsStatus(true, settings.Enabled, ConfigId, "Slack incoming webhook configured")
        { ChannelLabel = settings.ChannelLabel };

    public static (SlackSettings? Settings, string? Error) Validate(SlackSettingsUpdate update, SlackSettings? previous)
    {
        var url = string.IsNullOrWhiteSpace(update.WebhookUrl) ? previous?.WebhookUrl ?? "" : update.WebhookUrl.Trim();
        var label = update.ChannelLabel?.Trim() ?? previous?.ChannelLabel;
        if (label is { Length: > 80 } || label?.Contains("://", StringComparison.Ordinal) == true)
            return (null, "Enter a short non-secret Slack channel label.");
        var errors = new SlackOptions { WebhookUrl = url }.Validate();
        return errors.Count == 0 ? (new SlackSettings(url, update.Enabled) { ChannelLabel = label }, null) : (null, string.Join(" ", errors));
    }
}
