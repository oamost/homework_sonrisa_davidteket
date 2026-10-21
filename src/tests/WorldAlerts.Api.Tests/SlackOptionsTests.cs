using WorldAlerts.Api.Delivery;

namespace WorldAlerts.Api.Tests;

/// <summary>
/// Slack option validation and webhook redaction: errors and labels must be
/// useful without disclosing the webhook URL or its token.
/// </summary>
public sealed class SlackOptionsTests
{
    private const string Token = "T00000000/B00000000/XXXXXXXXXXXXXXXXXXXXXXXX";
    private const string Webhook = "https://hooks.slack.com/services/" + Token;

    [Fact]
    public void Validate_accepts_an_https_webhook_and_channel_override()
    {
        var options = new SlackOptions { WebhookUrl = Webhook, Channel = "#alerts", TimeoutSeconds = 10 };

        Assert.Empty(options.Validate());
        Assert.True(options.IsConfigured);
    }

    [Fact]
    public void Validate_requires_a_webhook_url()
    {
        var error = Assert.Single(new SlackOptions().Validate());

        Assert.Contains("required", error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(SlackWebhookRedactor.NotConfigured, new SlackOptions().Describe());
    }

    [Fact]
    public void Validate_rejects_non_https_urls_without_echoing_them()
    {
        foreach (var url in new[]
        {
            "http://hooks.slack.com/services/" + Token,
            "hooks.slack.com/services/" + Token,
            "ftp://hooks.slack.com/services/" + Token,
        })
        {
            var options = new SlackOptions { WebhookUrl = url };

            var error = Assert.Single(options.Validate());

            Assert.Contains("HTTPS", error, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hooks.slack.com", error, StringComparison.Ordinal);
            Assert.DoesNotContain(Token, error, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Validate_rejects_embedded_credentials_without_echoing_them()
    {
        var options = new SlackOptions { WebhookUrl = "https://user:pass@hooks.slack.com/services/" + Token };

        var error = Assert.Single(options.Validate());

        Assert.Contains("credentials", error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("user:pass", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://internal.example/services/T/B/XXXXXXXXXXXXXXXXXXXXXXXX")]
    [InlineData("https://hooks.slack.com.evil.test/services/T/B/XXXXXXXXXXXXXXXXXXXXXXXX")]
    [InlineData("https://hooks.slack.com:8443/services/T/B/XXXXXXXXXXXXXXXXXXXXXXXX")]
    [InlineData("https://hooks.slack.com/other/T/B/XXXXXXXXXXXXXXXXXXXXXXXX")]
    [InlineData("https://hooks.slack.com/services/T/B/XXXXXXXXXXXXXXXXXXXXXXXX?redirect=1")]
    [InlineData("https://hooks.slack.com/services/T/B/XXXXXXXXXXXXXXXXXXXXXXXX#fragment")]
    public void Validate_rejects_non_slack_destinations_without_echoing_the_url(string url)
    {
        var errors = new SlackOptions { WebhookUrl = url }.Validate();
        Assert.NotEmpty(errors);
        Assert.All(errors, error => Assert.DoesNotContain(url, error));
    }

    [Fact]
    public void Validate_bounds_timeout_and_rejects_a_channel_with_whitespace()
    {
        var options = new SlackOptions { WebhookUrl = Webhook, TimeoutSeconds = 0, Channel = "#alerts\nbcc" };

        var errors = options.Validate();

        Assert.Equal(2, errors.Count);
        Assert.All(errors, error => Assert.DoesNotContain(Token, error, StringComparison.Ordinal));
    }

    [Fact]
    public void Describe_returns_the_host_and_last_four_characters_only()
    {
        var label = new SlackOptions { WebhookUrl = Webhook }.Describe();

        Assert.Equal("hooks.slack.com (…XXXX)", label);
        Assert.DoesNotContain("T00000000", label, StringComparison.Ordinal);
        Assert.DoesNotContain("B00000000", label, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_does_not_disclose_short_tokens()
    {
        var label = SlackWebhookRedactor.Describe("https://hooks.slack.com/services/short");

        Assert.Equal("hooks.slack.com", label);
    }

    [Fact]
    public void Describe_handles_missing_and_unparseable_values()
    {
        Assert.Equal(SlackWebhookRedactor.NotConfigured, SlackWebhookRedactor.Describe(null));
        Assert.Equal(SlackWebhookRedactor.NotConfigured, SlackWebhookRedactor.Describe("  "));
        Assert.Equal(SlackWebhookRedactor.Unparseable, SlackWebhookRedactor.Describe("not a url"));
    }
}
