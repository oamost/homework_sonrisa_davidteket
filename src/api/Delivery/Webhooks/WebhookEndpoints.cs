using Microsoft.AspNetCore.Antiforgery;

namespace WorldAlerts.Api.Delivery.Webhooks;

public sealed record WebhookTestRequest(string? Id, bool Consent);

public static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapWebhookSettings(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/webhooks", async (HttpContext context, IAntiforgery antiforgery,
            WebhookSettingsStore store, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { channels = (await store.ListAsync(ct)).Select(WebhookSettingsStore.Redact),
                csrfToken = antiforgery.GetAndStoreTokens(context).RequestToken });
        }).RequireAuthorization();

        app.MapPut("/api/admin/webhooks", async (HttpContext context, IAntiforgery antiforgery,
            WebhookConfiguration? request, WebhookSettingsStore store, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!await antiforgery.IsRequestValidAsync(context)) return Results.BadRequest(new { error = "Security token missing or expired." });
            if (request is null) return Results.BadRequest(new { error = "Webhook settings are required." });
            var previous = (await store.ListAsync(ct)).FirstOrDefault(item => item.Id == request.Id);
            var configuration = previous is null ? request : request with
            {
                Url = string.IsNullOrWhiteSpace(request.Url) ? previous.Url : request.Url,
                Headers = request.Headers is { Count: 0 } ? previous.Headers : request.Headers,
            };
            var error = WebhookConfiguration.Validate(configuration);
            if (error is not null) return Results.BadRequest(new { error });
            try { await store.SaveAsync(configuration, ct); }
            catch (ArgumentException) { return Results.BadRequest(new { error = "Too many configured webhook channels." }); }
            return Results.Ok(WebhookSettingsStore.Redact(configuration));
        }).RequireAuthorization();

        app.MapPost("/api/admin/webhooks/test", async (HttpContext context, IAntiforgery antiforgery,
            WebhookTestRequest? request, WebhookSettingsStore store, WebhookSender sender, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!await antiforgery.IsRequestValidAsync(context)) return Results.BadRequest(new { error = "Security token missing or expired." });
            if (request?.Consent != true) return Results.BadRequest(new { error = "Explicit consent is required." });
            var config = (await store.ListAsync(ct)).FirstOrDefault(item => item.Id == request.Id);
            if (config is not { Enabled: true }) return Results.Conflict(new { error = "Configure a webhook before testing." });
            try
            {
                await sender.SendAsync(config, "TEST — not real news", "Explicit local webhook test request.", ct);
                return Results.Ok(new { message = "Webhook accepted the test request. Verify delivery at the destination." });
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            { return Results.Problem("Webhook refused or could not accept the test request.", statusCode: 502); }
        }).RequireAuthorization();
        return app;
    }
}
