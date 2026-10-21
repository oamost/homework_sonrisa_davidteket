using System.Net.Mail;

namespace WorldAlerts.Api.Delivery;

/// <summary>How the SMTP transport protects the connection.</summary>
public enum SmtpTlsMode
{
    /// <summary>No transport encryption. Only acceptable on a trusted local network.</summary>
    None = 0,

    /// <summary>Upgrade the connection with STARTTLS before authenticating (typically port 587).</summary>
    StartTls = 1,
}

/// <summary>
/// Configuration for SMTP email delivery, bound from the
/// <c>WorldAlerts:Delivery:Smtp</c> configuration section.
/// </summary>
/// <remarks>
/// <para>
/// Secrets are supplied through configuration and can therefore come from
/// environment variables (for example <c>WorldAlerts__Delivery__Smtp__Password</c>)
/// or a secret store, so they never need to live in source control.
/// </para>
/// <para>
/// This slice only transports messages. Delivery queueing, pacing and retry
/// policy belong to task 19, and the subscription-to-delivery integration and
/// admin/UI configuration belong to the task 20/21 integration work; neither is
/// implemented here.
/// </para>
/// <para>
/// The built-in <see cref="SmtpClientTransport"/> uses
/// <see cref="System.Net.Mail.SmtpClient"/>, which supports STARTTLS but not
/// implicit TLS (SMTPS on port 465). A custom <see cref="ISmtpTransport"/> is
/// required for implicit TLS.
/// </para>
/// </remarks>
public sealed class SmtpOptions
{
    /// <summary>Configuration section that binds this type.</summary>
    public const string SectionName = "WorldAlerts:Delivery:Smtp";

    public const int DefaultPort = 587;
    public const int DefaultTimeoutSeconds = 30;
    public const int MaxTimeoutSeconds = 600;

    /// <summary>SMTP server host name, without a scheme.</summary>
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = DefaultPort;

    public SmtpTlsMode TlsMode { get; set; } = SmtpTlsMode.StartTls;

    /// <summary>SMTP user name. Leave empty for an unauthenticated relay.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// SMTP password, normally supplied through an environment variable or
    /// secret store. It is never logged and never included in validation errors.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Sender address, optionally with a display name.</summary>
    public string FromAddress { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

    /// <summary>Per-send timeout, clamped to a usable range.</summary>
    public TimeSpan Timeout => TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds, 1, MaxTimeoutSeconds));

    /// <summary>True when no validation error is present.</summary>
    public bool IsValid => Validate().Count == 0;

    /// <summary>
    /// Returns human-readable configuration errors. Messages describe what is
    /// wrong but never echo the password or any other secret value.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Host))
        {
            errors.Add("SMTP host is required.");
        }
        else if (Host.Any(char.IsWhiteSpace) || Host.Contains("://", StringComparison.Ordinal))
        {
            errors.Add("SMTP host must be a host name without a scheme or whitespace.");
        }

        if (Port is < 1 or > 65535)
        {
            errors.Add("SMTP port must be between 1 and 65535.");
        }

        if (!Enum.IsDefined(TlsMode))
        {
            errors.Add("SMTP TLS mode must be None or StartTls.");
        }

        if (string.IsNullOrWhiteSpace(FromAddress))
        {
            errors.Add("SMTP from address is required.");
        }
        else if (!MailAddress.TryCreate(FromAddress, out _))
        {
            errors.Add("SMTP from address is not a valid email address.");
        }

        var hasUsername = !string.IsNullOrWhiteSpace(Username);
        var hasPassword = !string.IsNullOrEmpty(Password);
        if (hasUsername && !hasPassword)
        {
            errors.Add("SMTP password is required when a username is configured.");
        }
        else if (hasPassword && !hasUsername)
        {
            errors.Add("SMTP username is required when a password is configured.");
        }

        if (TimeoutSeconds is < 1 or > MaxTimeoutSeconds)
        {
            errors.Add($"SMTP timeout must be between 1 and {MaxTimeoutSeconds} seconds.");
        }

        return errors;
    }

    /// <summary>Safe summary for diagnostics; never includes the password.</summary>
    public override string ToString() =>
        $"SmtpOptions {{ Host = {Host}, Port = {Port}, TlsMode = {TlsMode}, " +
        $"From = {FromAddress}, HasCredentials = {!string.IsNullOrWhiteSpace(Username)}, " +
        $"TimeoutSeconds = {TimeoutSeconds} }}";
}
