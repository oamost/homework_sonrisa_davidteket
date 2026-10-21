using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using WorldAlerts.Api.Queue;

namespace WorldAlerts.Api.Delivery.Webhooks;

/// <summary>Resolves once per attempt, pins the vetted IP at connect, and never follows redirects.</summary>
public sealed class WebhookSender(
    Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null, HttpMessageHandler? testHandler = null)
{
    public async Task SendAsync(WebhookConfiguration configuration, string title, string body, CancellationToken ct = default)
    {
        if (WebhookConfiguration.Validate(configuration) is not null) throw new InvalidOperationException("Invalid webhook configuration.");
        var url = new Uri(configuration.Url);
        var addresses = IPAddress.TryParse(url.Host, out var literal)
            ? [literal] : await (resolve ?? DNS)(url.Host, ct);
        if (addresses.Length == 0 || addresses.Any(address => !WebhookNetwork.IsPublic(address)))
            throw new InvalidOperationException("Webhook destination is not public.");
        using var productionHandler = testHandler is null ? new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectCallback = async (context, token) =>
            {
                // HttpClient still validates TLS against the original URL hostname.
                foreach (var address in addresses)
                {
                    var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), token);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (SocketException) { socket.Dispose(); }
                }
                throw new InvalidOperationException("Webhook destination could not be reached.");
            },
        } : null;
        using var client = new HttpClient(testHandler ?? productionHandler!, disposeHandler: false)
        { Timeout = TimeSpan.FromSeconds(10) };
        using var request = new HttpRequestMessage(new HttpMethod(configuration.Method), url);
        var json = configuration.JsonTemplate.Replace("{{title}}", Escape(title), StringComparison.Ordinal)
            .Replace("{{body}}", Escape(body), StringComparison.Ordinal);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        foreach (var header in configuration.Headers)
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        try
        {
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Webhook refused the notification.");
        }
        catch (HttpRequestException) { throw new InvalidOperationException("Webhook destination could not be reached."); }
    }

    private static Task<IPAddress[]> DNS(string host, CancellationToken ct) => Dns.GetHostAddressesAsync(host, ct);
    private static string Escape(string text) => JsonSerializer.Serialize(text)[1..^1];
}

public sealed class WebhookNotificationTransport(WebhookSettingsStore store, WebhookSender sender) : IChannelNotificationTransport
{
    public string Channel => "webhook";

    public async Task SendAsync(NotificationJob job, CancellationToken ct = default)
    {
        if (job.Channel != Channel) throw new InvalidOperationException("Channel unavailable.");
        var configuration = (await store.ListAsync(ct)).FirstOrDefault(config => config.Id == job.Destination);
        if (configuration is not { Enabled: true }) throw new InvalidOperationException("Webhook needs setup.");
        await sender.SendAsync(configuration, job.Subject, job.Body, ct);
    }
}
