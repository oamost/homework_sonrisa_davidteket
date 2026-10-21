using System.Net;
using System.Net.Mail;

namespace WorldAlerts.Api.Delivery;

/// <summary>
/// Production <see cref="ISmtpTransport"/> backed by
/// <see cref="System.Net.Mail.SmtpClient"/>.
/// </summary>
/// <remarks>
/// No retry, queueing or pacing happens here; those belong to task 19. This
/// class opens one network connection per message and lets the caller observe
/// transport failures.
/// </remarks>
public sealed class SmtpClientTransport : ISmtpTransport
{
    /// <inheritdoc />
    public async Task SendAsync(MailMessage message, SmtpOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(options);

        var errors = options.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "SMTP delivery is not configured correctly: " + string.Join(" ", errors));
        }

        using var client = new SmtpClient(options.Host, options.Port)
        {
            EnableSsl = options.TlsMode == SmtpTlsMode.StartTls,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Timeout = (int)options.Timeout.TotalMilliseconds,
        };

        if (!string.IsNullOrWhiteSpace(options.Username))
        {
            client.Credentials = new NetworkCredential(options.Username, options.Password);
        }

        // SmtpClient.Timeout does not reliably bound async sends, so enforce the
        // configured deadline with a linked token.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Timeout);

        await client.SendMailAsync(message, timeout.Token).ConfigureAwait(false);
    }
}
