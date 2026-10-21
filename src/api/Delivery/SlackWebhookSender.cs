using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace WorldAlerts.Api.Delivery;

/// <summary>
/// Posts <c>{"text": "..."}</c> to a Slack incoming webhook.
/// </summary>
/// <remarks>
/// <para>
/// A send succeeds only for a 2xx response. Non-2xx responses, timeouts and
/// transport failures surface as <see cref="SlackDeliveryException"/> with the
/// status or timeout reason and a redacted target label; the webhook URL and its
/// token are never included in exception text or logs.
/// </para>
/// <para>
/// This class performs exactly one HTTP attempt. Retry and queue policy belong
/// to task 19; subscription wiring and admin/UI configuration belong to the
/// task 20/21 integration work.
/// </para>
/// </remarks>
public sealed class SlackWebhookSender : ISlackSender
{
    private const string RedactedFailureMessage =
        "Slack webhook delivery failed because the endpoint could not be reached.";

    private readonly HttpClient _httpClient;
    private readonly SlackOptions _options;
    private readonly ILogger<SlackWebhookSender> _logger;

    public SlackWebhookSender(
        HttpClient httpClient,
        IOptions<SlackOptions> options,
        ILogger<SlackWebhookSender> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">The message text is empty.</exception>
    /// <exception cref="InvalidOperationException">Slack options are incomplete or invalid.</exception>
    /// <exception cref="SlackDeliveryException">The post did not succeed.</exception>
    public async Task SendAsync(string text, CancellationToken cancellationToken = default)
        => await SendAsync(text, _options, cancellationToken);

    /// <summary>Send with privately loaded settings, without sharing a mutable options instance.</summary>
    public async Task SendAsync(string text, SlackOptions options, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Slack message text is required.", nameof(text));
        }

        var errors = options.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Slack delivery is not configured correctly: " + string.Join(" ", errors));
        }

        var payload = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["text"] = text,
        };
        if (!string.IsNullOrWhiteSpace(options.Channel))
        {
            payload["channel"] = options.Channel;
        }

        var target = options.Describe();

        using var request = new HttpRequestMessage(HttpMethod.Post, options.WebhookUrl)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"),
        };

        // The options timeout is enforced per send so a shared HttpClient can
        // never silently use a different deadline.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Timeout);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                "Slack delivery to {Target} timed out after {TimeoutSeconds} seconds.",
                target,
                 options.Timeout.TotalSeconds);
            throw new SlackDeliveryException(
                 $"Slack webhook delivery to {target} timed out after {options.Timeout.TotalSeconds:0} seconds.",
                safeTarget: target);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(
                "Slack delivery to {Target} failed with {ErrorType}.",
                target,
                exception.GetType().Name);
            throw new SlackDeliveryException(RedactedFailureMessage, safeTarget: target);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Slack delivery to {Target} failed with HTTP {StatusCode}.",
                    target,
                    (int)response.StatusCode);
                throw new SlackDeliveryException(
                    $"Slack webhook delivery to {target} failed with HTTP {(int)response.StatusCode} ({response.StatusCode}).",
                    response.StatusCode,
                    target);
            }
        }

        _logger.LogInformation("Slack delivery to {Target} succeeded.", target);
    }
}
