using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Options;

namespace WorldAlerts.Api.Delivery;

/// <summary>
/// Builds a plain-text email and hands it to <see cref="ISmtpTransport"/>.
/// </summary>
/// <remarks>
/// <para>
/// This class owns message construction only. It does not queue, pace or retry
/// sends: retry and queue policy belong to task 19, while wiring subscriptions
/// and admin/UI configuration belongs to the task 20/21 integration work.
/// </para>
/// <para>
/// The configured password is passed to the transport through
/// <see cref="SmtpOptions"/> and is never logged, placed in an exception
/// message or written to the message itself.
/// </para>
/// </remarks>
public sealed class SmtpEmailSender
{
    /// <summary>Maximum subject length kept by the sender.</summary>
    public const int MaxSubjectLength = 200;

    private const string FallbackSubject = "(no subject)";

    private readonly ISmtpTransport _transport;
    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(
        ISmtpTransport transport,
        IOptions<SmtpOptions> options,
        ILogger<SmtpEmailSender> logger)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _transport = transport;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Builds a plain-text message for one recipient and sends it.
    /// </summary>
    /// <exception cref="ArgumentException">The recipient address is not valid.</exception>
    /// <exception cref="InvalidOperationException">SMTP options are incomplete or invalid.</exception>
    public async Task SendAsync(
        string toAddress,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        var errors = _options.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "SMTP delivery is not configured correctly: " + string.Join(" ", errors));
        }

        if (string.IsNullOrWhiteSpace(toAddress) || !MailAddress.TryCreate(toAddress, out _))
        {
            throw new ArgumentException("Recipient email address is not valid.", nameof(toAddress));
        }

        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromAddress),
            Subject = NormalizeSubject(subject),
            Body = body ?? string.Empty,
            IsBodyHtml = false,
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8,
        };
        message.To.Add(toAddress);

        await SendAsync(message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sends an already-built message through the configured transport.</summary>
    /// <exception cref="InvalidOperationException">SMTP options are incomplete or invalid.</exception>
    public async Task SendAsync(MailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var errors = _options.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "SMTP delivery is not configured correctly: " + string.Join(" ", errors));
        }

        try
        {
            await _transport.SendAsync(message, _options, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Log only the error type: transport messages can embed addresses or
            // server diagnostics, and secrets must never reach the log.
            _logger.LogError(
                "SMTP delivery to {RecipientCount} recipient(s) via {Host}:{Port} failed with {ErrorType}.",
                message.To.Count,
                _options.Host,
                _options.Port,
                exception.GetType().Name);
            throw;
        }

        _logger.LogInformation(
            "SMTP delivery to {RecipientCount} recipient(s) via {Host}:{Port} succeeded.",
            message.To.Count,
            _options.Host,
            _options.Port);
    }

    /// <summary>
    /// Removes header-breaking characters from a subject and bounds its length.
    /// </summary>
    public static string NormalizeSubject(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return FallbackSubject;
        }

        var flattened = subject.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

        if (flattened.Length == 0)
        {
            return FallbackSubject;
        }

        return flattened.Length <= MaxSubjectLength
            ? flattened
            : flattened[..MaxSubjectLength].TrimEnd();
    }
}
