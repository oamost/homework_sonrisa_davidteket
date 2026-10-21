using System.Security.Cryptography;
using Microsoft.AspNetCore.Antiforgery;

namespace WorldAlerts.Api.Delivery.Slack;

public sealed record SlackTestRequest(bool Consent);

public static class SlackEndpoints
{
    /// <summary>Map admin-only settings and explicit test send; host must configure auth and antiforgery.</summary>
    public static IEndpointRouteBuilder MapSlackSettings(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/slack", async (HttpContext context, IAntiforgery antiforgery,
            SlackSettingsStore store, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            try
            {
                var status = SlackSettingsStore.Redact(await store.ReadAsync(ct));
                var token = antiforgery.GetAndStoreTokens(context).RequestToken;
                return Results.Ok(new { status.Configured, status.Enabled, status.ConfigId, status.Target, csrfToken = token });
            }
            catch (Exception ex) when (PrivateStorageFailure(ex)) { return StorageUnavailable(); }
        }).RequireAuthorization();

        app.MapPut("/api/admin/slack", async (HttpContext context, IAntiforgery antiforgery,
            SlackSettingsUpdate? request, SlackSettingsStore store, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!await ValidCsrfAsync(context, antiforgery)) return InvalidToken();
            if (request is null) return Results.BadRequest(new { error = "Slack settings are required." });
            try
            {
                var (settings, error) = SlackSettingsStore.Validate(request, await store.ReadAsync(ct));
                if (settings is null) return Results.BadRequest(new { error });
                await store.SaveAsync(settings, ct);
                return Results.Ok(SlackSettingsStore.Redact(settings));
            }
            catch (Exception ex) when (PrivateStorageFailure(ex)) { return StorageUnavailable(); }
        }).RequireAuthorization();

        app.MapPost("/api/admin/slack/test", async (HttpContext context, IAntiforgery antiforgery,
            SlackTestRequest? request, SlackSettingsStore store, SlackWebhookSender sender, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!await ValidCsrfAsync(context, antiforgery)) return InvalidToken();
            if (request?.Consent != true)
                return Results.BadRequest(new { error = "Explicit consent is required to send a Slack test to the configured webhook channel." });
            SlackSettings? settings;
            try { settings = await store.ReadAsync(ct); }
            catch (Exception ex) when (PrivateStorageFailure(ex)) { return StorageUnavailable(); }
            if (settings is not { Enabled: true })
                return Results.Conflict(new { error = "Save and explicitly enable a Slack webhook before sending a test." });
            try
            {
                await sender.SendAsync("World Alerts test requested by the local administrator. This is not an event alert.",
                    new SlackOptions { WebhookUrl = settings.WebhookUrl }, ct);
                return Results.Ok(new { message = "Slack accepted the test message. Check the configured webhook channel." });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (SlackDeliveryException ex)
            {
                var reason = ex.StatusCode is null ? "timeout or connection failure" : $"HTTP {(int)ex.StatusCode.Value}";
                return Results.Problem($"Slack refused the test message ({reason}). No automatic retry was attempted.", statusCode: 502);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            { return Results.Problem("Stored Slack settings are invalid; update the webhook before testing.", statusCode: 503); }
        }).RequireAuthorization();
        return app;
    }

    private static bool PrivateStorageFailure(Exception ex) => ex is IOException or CryptographicException or UnauthorizedAccessException or System.Text.Json.JsonException;
    private static IResult StorageUnavailable() => Results.Problem("Private Slack settings are unavailable. Check the persistent data volume and keys.", statusCode: 503);
    private static IResult InvalidToken() => Results.BadRequest(new { error = "Security token missing or expired. Reload and try again." });
    private static async Task<bool> ValidCsrfAsync(HttpContext context, IAntiforgery antiforgery)
    {
        try { return await antiforgery.IsRequestValidAsync(context); }
        catch (AntiforgeryValidationException) { return false; }
    }
}
