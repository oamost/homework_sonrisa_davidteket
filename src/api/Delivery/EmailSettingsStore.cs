using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace WorldAlerts.Api.Delivery;

public sealed record EmailSettings(string Host, int Port, string TlsMode, string Username,
    string Password, string FromAddress, bool Enabled);

public sealed record EmailSettingsUpdate(string? Host, int Port, string? TlsMode, string? Username,
    string? Password, string? FromAddress, bool Enabled);

public sealed record EmailSettingsStatus(bool Configured, bool Enabled, string? Host, int? Port,
    string? TlsMode, string? FromAddress, bool HasCredentials);

/// <summary>Encrypted private configuration. Keys and ciphertext live beside the persistent database, not in source control.</summary>
public sealed class EmailSettingsStore(string databasePath, IDataProtectionProvider protection)
{
    private readonly string _path = Path.GetFullPath(databasePath) + ".email.protected";
    private readonly IDataProtector _protector = protection.CreateProtector("WorldAlerts.EmailSettings.v1");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<EmailSettings?> ReadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!File.Exists(_path)) return null;
            var ciphertext = await File.ReadAllTextAsync(_path, ct);
            return JsonSerializer.Deserialize<EmailSettings>(_protector.Unprotect(ciphertext));
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(EmailSettings settings, CancellationToken ct = default)
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

    public static EmailSettingsStatus Redact(EmailSettings? settings) => settings is null
        ? new(false, false, null, null, null, null, false)
        : new(true, settings.Enabled, settings.Host, settings.Port, settings.TlsMode,
            settings.FromAddress, !string.IsNullOrWhiteSpace(settings.Username));

    public static SmtpOptions ToOptions(EmailSettings settings) => new()
    {
        Host = settings.Host, Port = settings.Port,
        TlsMode = Enum.Parse<SmtpTlsMode>(settings.TlsMode),
        Username = settings.Username, Password = settings.Password,
        FromAddress = settings.FromAddress,
    };

    public static (EmailSettings? Settings, string? Error) Validate(EmailSettingsUpdate request, EmailSettings? previous)
    {
        if (!Enum.TryParse<SmtpTlsMode>(request.TlsMode, true, out var mode)
            || !Enum.IsDefined(mode) || mode != SmtpTlsMode.StartTls)
            return (null, "STARTTLS is required for SMTP configuration.");
        var settings = new EmailSettings(request.Host?.Trim() ?? "", request.Port, mode.ToString(),
            request.Username?.Trim() ?? "", string.IsNullOrEmpty(request.Password) ? previous?.Password ?? "" : request.Password,
            request.FromAddress?.Trim() ?? "", request.Enabled);
        var errors = ToOptions(settings).Validate();
        return errors.Count == 0 ? (settings, null) : (null, string.Join(" ", errors));
    }
}
