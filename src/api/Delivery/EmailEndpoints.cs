using System.Net.Mail;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;

namespace WorldAlerts.Api.Delivery;

public sealed record EmailTestRequest(string? Destination);

public static class EmailEndpoints
{
    public static IEndpointRouteBuilder MapEmailSettings(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/email", (HttpContext context, IAntiforgery antiforgery, EmailSettingsStore store, CancellationToken ct) =>
            StatusAsync(context, antiforgery, store, ct)).RequireAuthorization();

        app.MapPut("/api/admin/email", async (HttpContext context, IAntiforgery antiforgery,
            EmailSettingsUpdate? request, EmailSettingsStore store, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!await antiforgery.IsRequestValidAsync(context)) return Results.BadRequest(new { error = "Security token missing or expired. Reload and try again." });
            if (request is null) return Results.BadRequest(new { error = "SMTP settings are required." });
            try
            {
                var (settings, error) = EmailSettingsStore.Validate(request, await store.ReadAsync(ct));
                if (settings is null) return Results.BadRequest(new { error });
                await store.SaveAsync(settings, ct);
                return Results.Ok(EmailSettingsStore.Redact(settings));
            }
            catch (Exception exception) when (exception is IOException or System.Security.Cryptography.CryptographicException)
            {
                return Results.Problem("Private SMTP settings could not be saved or decrypted. Check the persistent data volume and keys.", statusCode: 503);
            }
        }).RequireAuthorization();

        app.MapPost("/api/admin/email/test", async (HttpContext context, IAntiforgery antiforgery,
            EmailTestRequest? request, EmailSettingsStore store, ISmtpTransport transport, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!await antiforgery.IsRequestValidAsync(context)) return Results.BadRequest(new { error = "Security token missing or expired. Reload and try again." });
            if (request?.Destination is null || !MailAddress.TryCreate(request.Destination, out var recipient)
                || recipient.Address != request.Destination.Trim())
                return Results.BadRequest(new { error = "Enter a valid destination email address." });
            EmailSettings? settings;
            try { settings = await store.ReadAsync(ct); }
            catch (Exception exception) when (exception is IOException or System.Security.Cryptography.CryptographicException)
            { return Results.Problem("Private SMTP settings are unavailable. Check the persistent data volume and keys.", statusCode: 503); }
            if (settings is null || !settings.Enabled)
                return Results.Conflict(new { error = "Save and explicitly enable SMTP before sending a test email." });
            try
            {
                var options = EmailSettingsStore.ToOptions(settings);
                using var message = new MailMessage(options.FromAddress, recipient.Address,
                    "World Alerts test email", "SMTP test requested by the local administrator. This is not an event alert.");
                await transport.SendAsync(message, options, ct);
                return Results.Ok(new { message = "SMTP accepted the test email. Inbox delivery is not guaranteed." });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception) { return Results.Problem("SMTP did not accept the test email. Check host, STARTTLS, credentials, sender and recipient.", statusCode: 502); }
        }).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> StatusAsync(HttpContext context, IAntiforgery antiforgery,
        EmailSettingsStore store, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            var status = EmailSettingsStore.Redact(await store.ReadAsync(ct));
            var token = antiforgery.GetAndStoreTokens(context).RequestToken;
            return Results.Ok(new { status.Configured, status.Enabled, status.Host, status.Port,
                status.TlsMode, status.FromAddress, status.RecipientAddress, status.Username,
                status.HasCredentials, csrfToken = token });
        }
        catch (Exception exception) when (exception is IOException or System.Security.Cryptography.CryptographicException)
        { return Results.Problem("Private SMTP settings are unavailable. Check the persistent data volume and keys.", statusCode: 503); }
    }
}
