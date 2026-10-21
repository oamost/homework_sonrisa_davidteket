using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace WorldAlerts.Api.Delivery.Webhooks;

public sealed record WebhookConfiguration(string Id, string Label, string Url, string Method,
    Dictionary<string, string> Headers, string JsonTemplate, bool Enabled)
{
    public static string? Validate(WebhookConfiguration config)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(config.Id ?? "", "^[a-z0-9-]{1,40}$")
            || string.IsNullOrWhiteSpace(config.Label) || config.Label.Length > 80 || config.Label.Contains("://"))
            return "Enter a short identifier and non-secret label.";
        if (!Uri.TryCreate(config.Url, UriKind.Absolute, out var url) || url.Scheme != "https"
            || url.UserInfo.Length > 0 || url.Fragment.Length > 0 || url.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || url.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || url.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || IPAddress.TryParse(url.Host, out var ip) && !WebhookNetwork.IsPublic(ip))
            return "Enter a public HTTPS webhook URL (not a local/private address).";
        if (config.Method is not ("POST" or "PUT")) return "Use POST or PUT for webhook delivery.";
        if (config.Headers is null || config.Headers.Count > 8 || config.Headers.Any(h =>
                h.Key is not ("Authorization" or "X-API-Key" or "X-Webhook-Token")
                || h.Value.Contains('\n') || h.Value.Contains('\r') || h.Value.Length > 1024))
            return "Only Authorization, X-API-Key and X-Webhook-Token headers are supported.";
        if (config.JsonTemplate is null || config.JsonTemplate.Length > 16_384)
            return "Webhook JSON template is too large.";
        try { using var _ = JsonDocument.Parse(config.JsonTemplate); }
        catch (JsonException) { return "Enter a valid JSON body template."; }
        return null;
    }
}

public static class WebhookNetwork
{
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.IPv6None)) return false;
        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
            return bytes[0] is not (0 or 10 or 127 or >= 224)
                && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
                && !(bytes[0] == 169 && bytes[1] == 254)
                && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                && !(bytes[0] == 192 && bytes[1] == 168)
                && !(bytes[0] == 192 && bytes[1] == 0)
                && !(bytes[0] == 198 && bytes[1] is 18 or 19);
        return bytes.Length == 16 && (bytes[0] & 0xFE) != 0xFC && bytes[0] != 0xFF
            && !(bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80)
            && !address.IsIPv6LinkLocal && !address.IsIPv6SiteLocal;
    }
}

public sealed record WebhookPublicStatus(string Id, string Label, bool Enabled, bool Configured);

public sealed class WebhookSettingsStore(string databasePath, IDataProtectionProvider protection)
{
    private readonly string _path = Path.GetFullPath(databasePath) + ".webhooks.protected";
    private readonly IDataProtector _protector = protection.CreateProtector("WorldAlerts.WebhookSettings.v1");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public static WebhookPublicStatus Redact(WebhookConfiguration config) =>
        new(config.Id, config.Label, config.Enabled, true);

    public async Task<IReadOnlyList<WebhookConfiguration>> ListAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { return await ReadUnlockedAsync(ct); }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(WebhookConfiguration configuration, CancellationToken ct = default)
    {
        var error = WebhookConfiguration.Validate(configuration);
        if (error is not null) throw new ArgumentException(error, nameof(configuration));
        await _gate.WaitAsync(ct);
        try
        {
            var existing = (await ReadUnlockedAsync(ct)).Where(item => item.Id != configuration.Id).ToList();
            if (existing.Count >= 10) throw new ArgumentException("Maximum ten configured webhooks.");
            existing.Add(configuration);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temp, _protector.Protect(JsonSerializer.Serialize(existing)), ct);
                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                    File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                File.Move(temp, _path, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        finally { _gate.Release(); }
    }

    private async Task<IReadOnlyList<WebhookConfiguration>> ReadUnlockedAsync(CancellationToken ct) =>
        !File.Exists(_path) ? [] : JsonSerializer.Deserialize<List<WebhookConfiguration>>(
            _protector.Unprotect(await File.ReadAllTextAsync(_path, ct))) ?? [];
}
