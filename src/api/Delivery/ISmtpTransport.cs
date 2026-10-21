using System.Net.Mail;

namespace WorldAlerts.Api.Delivery;

/// <summary>
/// Transport boundary for SMTP delivery. The production implementation uses
/// <see cref="System.Net.Mail.SmtpClient"/>; tests substitute a fake so no
/// socket is opened.
/// </summary>
/// <remarks>
/// This abstraction only sends a single already-built message. Retry, pacing
/// and persistence are owned by task 19 and are deliberately absent here.
/// </remarks>
public interface ISmtpTransport
{
    /// <summary>Sends <paramref name="message"/> using <paramref name="options"/>.</summary>
    Task SendAsync(MailMessage message, SmtpOptions options, CancellationToken cancellationToken = default);
}
