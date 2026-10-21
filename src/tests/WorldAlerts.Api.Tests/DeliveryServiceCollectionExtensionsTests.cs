using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorldAlerts.Api.Delivery;

namespace WorldAlerts.Api.Tests;

/// <summary>
/// Registration contract for delivery infrastructure. This method is not wired
/// into Program.cs in this slice; these tests only prove it can be called and
/// resolves the expected services without hosted services.
/// </summary>
public sealed class DeliveryServiceCollectionExtensionsTests
{
    private const string Secret = "p@ssw0rd-never-log-9f3a";
    private const string Webhook = "https://hooks.slack.com/services/T00000000/B00000000/XXXXXXXXXXXXXXXXXXXXXXXX";

    [Fact]
    public void AddDeliveryInfrastructure_binds_options_and_registers_senders()
    {
        var configuration = BuildConfiguration();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDeliveryInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();

        var smtp = provider.GetRequiredService<IOptions<SmtpOptions>>().Value;
        Assert.Equal("smtp.example.test", smtp.Host);
        Assert.Equal(587, smtp.Port);
        Assert.Equal(SmtpTlsMode.StartTls, smtp.TlsMode);
        Assert.Equal("alerts@example.test", smtp.FromAddress);
        Assert.Equal(Secret, smtp.Password);

        var slack = provider.GetRequiredService<IOptions<SlackOptions>>().Value;
        Assert.Equal(Webhook, slack.WebhookUrl);
        Assert.Equal("#alerts", slack.Channel);
        Assert.Equal("hooks.slack.com (…XXXX)", slack.Describe());

        Assert.NotNull(provider.GetRequiredService<ISmtpTransport>());
        Assert.NotNull(provider.GetRequiredService<SmtpEmailSender>());
        Assert.NotNull(provider.GetRequiredService<ISlackSender>());
        Assert.Empty(provider.GetServices<IHostedService>());
    }

    [Fact]
    public void AddDeliveryInfrastructure_resolves_when_channels_are_unconfigured()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDeliveryInfrastructure(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();

        Assert.NotEmpty(provider.GetRequiredService<IOptions<SmtpOptions>>().Value.Validate());
        Assert.NotEmpty(provider.GetRequiredService<IOptions<SlackOptions>>().Value.Validate());
        Assert.NotNull(provider.GetRequiredService<SmtpEmailSender>());
        Assert.NotNull(provider.GetRequiredService<ISlackSender>());
    }

    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WorldAlerts:Delivery:Smtp:Host"] = "smtp.example.test",
                ["WorldAlerts:Delivery:Smtp:Port"] = "587",
                ["WorldAlerts:Delivery:Smtp:TlsMode"] = "StartTls",
                ["WorldAlerts:Delivery:Smtp:Username"] = "mailer",
                ["WorldAlerts:Delivery:Smtp:Password"] = Secret,
                ["WorldAlerts:Delivery:Smtp:FromAddress"] = "alerts@example.test",
                ["WorldAlerts:Delivery:Smtp:TimeoutSeconds"] = "30",
                ["WorldAlerts:Delivery:Slack:WebhookUrl"] = Webhook,
                ["WorldAlerts:Delivery:Slack:Channel"] = "#alerts",
                ["WorldAlerts:Delivery:Slack:TimeoutSeconds"] = "10",
            })
            .Build();
}
