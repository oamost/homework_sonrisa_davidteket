using WorldAlerts.Api.Delivery;

namespace WorldAlerts.Api.Tests;

/// <summary>
/// Validation and redaction contract for SMTP options: errors must be clear
/// without echoing the configured password.
/// </summary>
public sealed class SmtpOptionsTests
{
    private const string Secret = "p@ssw0rd-never-log-9f3a";

    [Fact]
    public void Validate_accepts_complete_options()
    {
        var errors = ValidOptions().Validate();

        Assert.Empty(errors);
        Assert.True(ValidOptions().IsValid);
    }

    [Fact]
    public void Validate_accepts_an_unauthenticated_relay()
    {
        var options = ValidOptions();
        options.Username = string.Empty;
        options.Password = string.Empty;

        Assert.Empty(options.Validate());
    }

    [Fact]
    public void Validate_requires_host_from_address_and_matching_credentials()
    {
        var options = new SmtpOptions
        {
            Host = string.Empty,
            FromAddress = string.Empty,
            Username = "mailer",
            Password = Secret,
        };

        var errors = options.Validate();

        Assert.Contains(errors, error => error.Contains("host", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("from address", StringComparison.OrdinalIgnoreCase));
        Assert.All(errors, error => Assert.DoesNotContain(Secret, error, StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_rejects_a_password_without_a_username_without_echoing_it()
    {
        var options = ValidOptions();
        options.Username = string.Empty;

        var errors = options.Validate();

        var error = Assert.Single(errors);
        Assert.Contains("username", error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Secret, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_rejects_a_host_with_a_scheme_or_whitespace()
    {
        var options = ValidOptions();
        options.Host = "smtp://mailer example.test";

        var error = Assert.Single(options.Validate());

        Assert.Contains("host name", error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Secret, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_rejects_an_invalid_from_address_without_echoing_it()
    {
        var options = ValidOptions();
        options.FromAddress = "not-an-address";

        var error = Assert.Single(options.Validate());

        Assert.Contains("from address", error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not-an-address", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_bounds_port_timeout_and_tls_mode()
    {
        var options = ValidOptions();
        options.Port = 0;
        options.TimeoutSeconds = 0;
        options.TlsMode = (SmtpTlsMode)42;

        var errors = options.Validate();

        Assert.Equal(3, errors.Count);
        Assert.All(errors, error => Assert.DoesNotContain(Secret, error, StringComparison.Ordinal));
    }

    [Fact]
    public void ToString_never_contains_the_password()
    {
        var text = ValidOptions().ToString();

        Assert.DoesNotContain(Secret, text, StringComparison.Ordinal);
        Assert.Contains("smtp.example.test", text, StringComparison.Ordinal);
        Assert.Contains("HasCredentials = True", text, StringComparison.Ordinal);
    }

    internal static SmtpOptions ValidOptions() => new()
    {
        Host = "smtp.example.test",
        Port = 587,
        TlsMode = SmtpTlsMode.StartTls,
        Username = "mailer",
        Password = Secret,
        FromAddress = "alerts@example.test",
        TimeoutSeconds = 30,
    };
}
