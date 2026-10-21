using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WorldAlerts.Api.Delivery;

namespace WorldAlerts.Api.Tests;

/// <summary>
/// Slack webhook contract. A fake <see cref="HttpMessageHandler"/> replaces the
/// network; success requires 2xx and failures must not disclose the URL.
/// </summary>
public sealed class SlackWebhookSenderTests
{
    private const string Token = "T00000000/B00000000/XXXXXXXXXXXXXXXXXXXXXXXX";
    private const string Webhook = "https://hooks.slack.com/services/" + Token;

    [Fact]
    public async Task SendAsync_posts_text_as_json_and_accepts_2xx()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(FakeHttpMessageHandler.Status(HttpStatusCode.OK)));
        var logger = new CapturingLogger<SlackWebhookSender>();
        var sender = CreateSender(handler, logger);

        await sender.SendAsync("Breaking: earthquake in Chile");

        Assert.Equal("application/json", handler.LastContentType);
        using var payload = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("Breaking: earthquake in Chile", payload.RootElement.GetProperty("text").GetString());
        Assert.False(payload.RootElement.TryGetProperty("channel", out _));
        Assert.Equal(Webhook, handler.LastRequestUri?.ToString());
        Assert.All(logger.Messages, entry => Assert.DoesNotContain(Token, entry, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SendAsync_includes_the_channel_override_when_configured()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(FakeHttpMessageHandler.Status(HttpStatusCode.NoContent)));
        var sender = CreateSender(handler, new CapturingLogger<SlackWebhookSender>(), channel: "#alerts");

        await sender.SendAsync("hello");

        using var payload = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("#alerts", payload.RootElement.GetProperty("channel").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, 400)]
    [InlineData(HttpStatusCode.TooManyRequests, 429)]
    public async Task SendAsync_surfaces_non_2xx_status_without_the_webhook_url(
        HttpStatusCode statusCode,
        int expectedCode)
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(FakeHttpMessageHandler.Status(statusCode)));
        var logger = new CapturingLogger<SlackWebhookSender>();
        var sender = CreateSender(handler, logger);

        var exception = await Assert.ThrowsAsync<SlackDeliveryException>(() => sender.SendAsync("hello"));

        Assert.Equal(statusCode, exception.StatusCode);
        Assert.Contains(expectedCode.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("https://", exception.ToString(), StringComparison.Ordinal);
        Assert.All(logger.Messages, entry => Assert.DoesNotContain(Token, entry, StringComparison.Ordinal));
        Assert.All(logger.Messages, entry => Assert.DoesNotContain("https://", entry, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SendAsync_reports_a_timeout_without_the_webhook_url()
    {
        var handler = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return FakeHttpMessageHandler.Status(HttpStatusCode.OK);
        });
        var logger = new CapturingLogger<SlackWebhookSender>();
        var sender = CreateSender(handler, logger, timeoutSeconds: 1);

        var exception = await Assert.ThrowsAsync<SlackDeliveryException>(() => sender.SendAsync("hello"));

        Assert.Null(exception.StatusCode);
        Assert.Contains("timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Token, exception.ToString(), StringComparison.Ordinal);
        Assert.All(logger.Messages, entry => Assert.DoesNotContain(Token, entry, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SendAsync_reports_an_unreachable_endpoint_without_the_webhook_url()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("connection refused to " + Webhook)));
        var logger = new CapturingLogger<SlackWebhookSender>();
        var sender = CreateSender(handler, logger);

        var exception = await Assert.ThrowsAsync<SlackDeliveryException>(() => sender.SendAsync("hello"));

        Assert.Null(exception.StatusCode);
        Assert.DoesNotContain(Token, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("https://", exception.ToString(), StringComparison.Ordinal);
        Assert.All(logger.Messages, entry => Assert.DoesNotContain(Token, entry, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SendAsync_propagates_caller_cancellation()
    {
        var handler = new FakeHttpMessageHandler((_, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(FakeHttpMessageHandler.Status(HttpStatusCode.OK));
        });
        var sender = CreateSender(handler, new CapturingLogger<SlackWebhookSender>());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sender.SendAsync("hello", cancellation.Token));
    }

    [Fact]
    public async Task SendAsync_refuses_missing_configuration_without_echoing_a_url()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(FakeHttpMessageHandler.Status(HttpStatusCode.OK)));
        var sender = new SlackWebhookSender(
            new HttpClient(handler),
            Options.Create(new SlackOptions()),
            new CapturingLogger<SlackWebhookSender>());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync("hello"));

        Assert.Contains("required", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(handler.LastRequestUri);
    }

    [Fact]
    public async Task SendAsync_rejects_empty_text()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(FakeHttpMessageHandler.Status(HttpStatusCode.OK)));
        var sender = CreateSender(handler, new CapturingLogger<SlackWebhookSender>());

        await Assert.ThrowsAsync<ArgumentException>(() => sender.SendAsync("   "));
        Assert.Null(handler.LastRequestUri);
    }

    private static SlackWebhookSender CreateSender(
        FakeHttpMessageHandler handler,
        CapturingLogger<SlackWebhookSender> logger,
        string? channel = null,
        int timeoutSeconds = 10)
        => new(
            new HttpClient(handler),
            Options.Create(new SlackOptions
            {
                WebhookUrl = Webhook,
                Channel = channel,
                TimeoutSeconds = timeoutSeconds,
            }),
            logger);
}
