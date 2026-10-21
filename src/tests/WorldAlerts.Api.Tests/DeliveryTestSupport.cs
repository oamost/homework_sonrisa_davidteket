using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Logging;
using WorldAlerts.Api.Delivery;

namespace WorldAlerts.Api.Tests;

/// <summary>Shared fakes for delivery tests: no sockets and no real logging.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = new();

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
        => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        var message = formatter(state, exception);
        Messages.Add(exception is null ? message : $"{message} {exception}");
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}

internal sealed record SentSmtpMessage(
    string? From,
    IReadOnlyList<string> To,
    string Subject,
    string Body,
    bool IsBodyHtml,
    Encoding? BodyEncoding);

/// <summary>Fake SMTP transport that records messages instead of opening a socket.</summary>
internal sealed class RecordingSmtpTransport : ISmtpTransport
{
    public List<SentSmtpMessage> Sent { get; } = new();

    public Exception? Failure { get; set; }

    public Task SendAsync(MailMessage message, SmtpOptions options, CancellationToken cancellationToken = default)
    {
        if (Failure is not null)
        {
            return Task.FromException(Failure);
        }

        Sent.Add(new SentSmtpMessage(
            message.From?.Address,
            message.To.Select(recipient => recipient.Address).ToArray(),
            message.Subject,
            message.Body,
            message.IsBodyHtml,
            message.BodyEncoding));
        return Task.CompletedTask;
    }
}

/// <summary>Fake HTTP handler that records the request and returns a scripted response.</summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        _respond = respond;
    }

    public Uri? LastRequestUri { get; private set; }

    public string? LastBody { get; private set; }

    public string? LastContentType { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequestUri = request.RequestUri;
        LastContentType = request.Content?.Headers.ContentType?.MediaType;
        LastBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        return await _respond(request, cancellationToken);
    }

    public static HttpResponseMessage Status(HttpStatusCode statusCode) => new(statusCode);
}
