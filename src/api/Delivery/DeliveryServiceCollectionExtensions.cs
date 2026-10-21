namespace WorldAlerts.Api.Delivery;

/// <summary>
/// Registers email and Slack delivery infrastructure without starting any
/// background work.
/// </summary>
/// <remarks>
/// <para>
/// Call <see cref="AddDeliveryInfrastructure"/> from the composition root when
/// delivery is integrated; this slice deliberately does not call it from
/// <c>Program.cs</c> and does not register hosted services.
/// </para>
/// <para>
/// Options bind from <see cref="SmtpOptions.SectionName"/> and
/// <see cref="SlackOptions.SectionName"/>. Validation is not run at startup
/// because an unconfigured channel must not stop the host; senders validate on
/// use and fail with secret-free messages.
/// </para>
/// <para>
/// Retry, queueing and pacing policy belong to task 19. Subscription-driven
/// sends, the admin view and UI configuration belong to the task 20/21
/// integration work and are not part of this registration.
/// </para>
/// </remarks>
public static class DeliveryServiceCollectionExtensions
{
    /// <summary>
    /// Registers SMTP and Slack options plus their senders. Idempotent
    /// registrations are not attempted; call this once from the composition
    /// root.
    /// </summary>
    public static IServiceCollection AddDeliveryInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<SmtpOptions>()
            .Bind(configuration.GetSection(SmtpOptions.SectionName));
        services.AddOptions<SlackOptions>()
            .Bind(configuration.GetSection(SlackOptions.SectionName));

        services.AddSingleton<ISmtpTransport, SmtpClientTransport>();
        services.AddSingleton<SmtpEmailSender>();

        // The sender enforces the configured per-send timeout itself, so the
        // shared HttpClient must not impose a second, different deadline.
        services.AddHttpClient<ISlackSender, SlackWebhookSender>(
            client => client.Timeout = Timeout.InfiniteTimeSpan);

        return services;
    }
}
