using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace WorldAlerts.Api.Admin;

public static class AdminExtensions
{
    private const string Scheme = "WorldAlerts.Admin";

    public static IServiceCollection AddAdminWall(this IServiceCollection services)
    {
        services.AddAuthentication(Scheme).AddCookie(Scheme, options =>
        {
            options.Cookie.Name = "__Host-WorldAlertsAdmin";
            options.Cookie.Path = "/";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.ExpireTimeSpan = TimeSpan.FromHours(4);
            options.SlidingExpiration = false;
            options.Events.OnRedirectToLogin = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api")) context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                else context.Response.Redirect("/admin/login");
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
        services.AddAuthorization();
        services.AddAntiforgery(options =>
        {
            options.Cookie.Name = "__Host-WorldAlertsCsrf";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.FormFieldName = "csrf";
        });
        services.TryAddSingleton<IAdminOperationsReader, UnavailableAdminOperationsReader>();
        return services;
    }

    public static IEndpointRouteBuilder MapAdminWall(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/login", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var token = antiforgery.GetAndStoreTokens(context).RequestToken!;
            return Html(context, $"<h1>Admin login</h1><form action='/admin/login' method='post'><label>Password <input type='password' name='password' autocomplete='current-password' required></label><input type='hidden' name='csrf' value=\"{WebUtility.HtmlEncode(token)}\"><button type='submit'>Sign in</button></form>");
        });

        app.MapPost("/admin/login", async (HttpContext context, IAntiforgery antiforgery, IConfiguration configuration) =>
        {
            if (!await ValidCsrfAsync(context, antiforgery)) return Results.BadRequest();
            var form = await context.Request.ReadFormAsync();
            var hash = configuration["WorldAlerts:Admin:PasswordHash"];
            if (!AdminPassword.Verify(form["password"], hash)) return Results.Unauthorized();
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "local-admin")], Scheme);
            await context.SignInAsync(Scheme, new ClaimsPrincipal(identity));
            return Results.Redirect("/admin");
        });

        app.MapPost("/admin/logout", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await ValidCsrfAsync(context, antiforgery)) return Results.BadRequest();
            await context.SignOutAsync(Scheme);
            return Results.Redirect("/admin/login");
        }).RequireAuthorization();

        app.MapGet("/api/admin/status", async (HttpContext context, IAdminOperationsReader reader, CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(await reader.ReadAsync(cancellationToken));
        }).RequireAuthorization();

        app.MapGet("/admin", async (HttpContext context, IAntiforgery antiforgery, IAdminOperationsReader reader, CancellationToken cancellationToken) =>
        {
            var snapshot = await reader.ReadAsync(cancellationToken);
            var token = antiforgery.GetAndStoreTokens(context).RequestToken!;
            static string Escape(string? text) => WebUtility.HtmlEncode(text ?? "unavailable");
            var sources = string.Join("", snapshot.Sources.Select(source =>
                $"<tr><td>{Escape(source.SourceId)}</td><td>{Escape(source.Health)}</td><td>{Escape(source.PollStatus)}</td><td>{Escape(source.LastPollUtc?.ToString("u"))}</td></tr>"));
            var delivery = snapshot.Delivery;
            return Html(context, $"<h1>Operations</h1><h2>Source health and polling</h2><table><thead><tr><th>Source</th><th>Health</th><th>Poll status</th><th>Last poll UTC</th></tr></thead><tbody>{sources}</tbody></table><h2>Delivery queue and failed sends</h2><p>Status: {Escape(delivery.Status)}; queued: {Escape(delivery.Queued?.ToString())}; failed: {Escape(delivery.Failed?.ToString())}</p><form action='/admin/logout' method='post'><input type='hidden' name='csrf' value=\"{Escape(token)}\"><button type='submit'>Sign out</button></form>");
        }).RequireAuthorization();
        return app;
    }

    private static async Task<bool> ValidCsrfAsync(HttpContext context, IAntiforgery antiforgery)
    {
        try { return await antiforgery.IsRequestValidAsync(context); }
        catch (AntiforgeryValidationException) { return false; }
    }

    private static IResult Html(HttpContext context, string body)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'";
        return Results.Content("<!doctype html><html lang='en'><head><meta charset='utf-8'><title>World Alerts admin</title><meta name='viewport' content='width=device-width,initial-scale=1'></head><body>" + body + "</body></html>", "text/html; charset=utf-8");
    }
}
