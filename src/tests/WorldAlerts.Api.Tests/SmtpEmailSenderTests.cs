using System.Text;
using Microsoft.Extensions.Options;
using WorldAlerts.Api.Delivery;

namespace WorldAlerts.Api.Tests;

/// <summary>
/// Message-building contract for SMTP delivery. A fake transport replaces the
/// network, so these tests never open a socket.
/// </summary>
public sealed class SmtpEmailSenderTests
{
    private const string Secret = "p@ssw0rd-never-log-9f3a";

    [Fact]
    public async Task SendAsync_builds_a_plain_text_message_through_the_transport()
    {
        var transport = new RecordingSmtpTransport();
        var logger = new CapturingLogger<SmtpEmailSender>();
        var sender = CreateSender(transport, logger);

        await sender.SendAsync("person@example.test", "Market alert", "SPX moved 3%.");

        var message = Assert.Single(transport.Sent);
        Assert.Equal("alerts@example.test", message.From);
        Assert.Equal(new[] { "person@example.test" }, message.To);
        Assert.Equal("Market alert", message.Subject);
        Assert.Equal("SPX moved 3%.", message.Body);
        Assert.False(message.IsBodyHtml);
        Assert.Equal(Encoding.UTF8, message.BodyEncoding);
        Assert.All(logger.Messages, entry => Assert.DoesNotContain(Secret, entry, StringComparison.Ordinal));
        Assert.All(logger.Messages, entry => Assert.DoesNotContain("SPX moved 3%.", entry, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SendAsync_flattens_header_breaks_in_the_subject()
    {
        var transport = new RecordingSmtpTransport();
        var sender = CreateSender(transport, new CapturingLogger<SmtpEmailSender>());

        await sender.SendAsync("person@example.test", "Alert\r\nBcc: attacker@example.test", "body");

        var message = Assert.Single(transport.Sent);
        Assert.DoesNotContain("\r", message.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", message.Subject, StringComparison.Ordinal);
        Assert.Contains("Bcc: attacker@example.test", message.Subject, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizeSubject_bounds_length_and_supplies_a_fallback()
    {
        var normalized = SmtpEmailSender.NormalizeSubject(new string('a', 500));

        Assert.Equal(SmtpEmailSender.MaxSubjectLength, normalized.Length);
        Assert.Equal("(no subject)", SmtpEmailSender.NormalizeSubject("  \r\n "));
        Assert.Equal("(no subject)", SmtpEmailSender.NormalizeSubject(null));
    }

    [Fact]
    public async Task SendAsync_rejects_an_invalid_recipient_without_sending()
    {
        var transport = new RecordingSmtpTransport();
        var sender = CreateSender(transport, new CapturingLogger<SmtpEmailSender>());

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => sender.SendAsync("not-an-address", "subject", "body"));

        Assert.Empty(transport.Sent);
        Assert.DoesNotContain(Secret, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_refuses_invalid_options_without_echoing_the_password()
    {
        var transport = new RecordingSmtpTransport();
        var options = SmtpOptionsTests.ValidOptions();
        options.Host = string.Empty;
        var sender = new SmtpEmailSender(transport, Options.Create(options), new CapturingLogger<SmtpEmailSender>());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sender.SendAsync("person@example.test", "subject", "body"));

        Assert.Empty(transport.Sent);
        Assert.Contains("host", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Secret, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_propagates_transport_failures_without_logging_secrets()
    {
        var transport = new RecordingSmtpTransport
        {
            Failure = new InvalidOperationException("relay rejected the message"),
        };
        var logger = new CapturingLogger<SmtpEmailSender>();
        var sender = CreateSender(transport, logger);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sender.SendAsync("person@example.test", "subject", "body"));

        Assert.NotEmpty(logger.Messages);
        Assert.All(logger.Messages, entry => Assert.DoesNotContain(Secret, entry, StringComparison.Ordinal));
        Assert.Contains(logger.Messages, entry => entry.Contains("InvalidOperationException", StringComparison.Ordinal));
    }

    private static SmtpEmailSender CreateSender(
        RecordingSmtpTransport transport,
        CapturingLogger<SmtpEmailSender> logger)
        => new(transport, Options.Create(SmtpOptionsTests.ValidOptions()), logger);
}
