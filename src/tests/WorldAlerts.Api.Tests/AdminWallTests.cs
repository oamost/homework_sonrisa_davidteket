using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using WorldAlerts.Api.Admin;

namespace WorldAlerts.Api.Tests;

public sealed class AdminWallTests
{
    private const string Password = "local-test-password";

    [Fact]
    public void Password_hash_accepts_only_matching_password_and_rejects_unconfigured_value()
    {
        var hash = AdminPassword.Hash(Password);
        Assert.True(AdminPassword.Verify(Password, hash));
        Assert.False(AdminPassword.Verify("incorrect", hash));
        Assert.False(AdminPassword.Verify(Password, null));
        Assert.False(AdminPassword.Verify(Password, "bad-format"));
    }

    [Fact]
    public async Task Operational_api_requires_authentication_and_login_page_does_not_leak_secrets()
    {
        using var factory = CreateFactory(AdminPassword.Hash(Password));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/status")).StatusCode);
        var wall = await client.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.Redirect, wall.StatusCode);
        Assert.Equal("/admin/login", wall.Headers.Location?.ToString());
        var login = await client.GetStringAsync("/admin/login");
        Assert.DoesNotContain(Password, login);
        Assert.DoesNotContain("private-webhook-secret", login);
    }

    [Fact]
    public async Task Login_requires_csrf_rejects_wrong_password_and_unconfigured_hash()
    {
        using var factory = CreateFactory(AdminPassword.Hash(Password));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        var noToken = await client.PostAsync("/admin/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["password"] = Password }));
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        var token = await LoginTokenAsync(client);
        var wrong = await PostAsync(client, "/admin/login", token, "incorrect");
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/status")).StatusCode);

        using var disabledFactory = CreateFactory(null);
        using var disabledClient = disabledFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        var disabledToken = await LoginTokenAsync(disabledClient);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(disabledClient, "/admin/login", disabledToken, Password)).StatusCode);
    }

    [Fact]
    public async Task Authorized_snapshot_redacts_secrets_and_logout_requires_csrf_then_revokes_session()
    {
        using var factory = CreateFactory(AdminPassword.Hash(Password));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        var token = await LoginTokenAsync(client);
        var login = await PostAsync(client, "/admin/login", token, Password);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), value => value.Contains("HttpOnly") && value.Contains("Secure") && value.Contains("SameSite=Strict"));

        var status = await client.GetAsync("/api/admin/status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        var json = await status.Content.ReadAsStringAsync();
        Assert.Contains("unavailable", json);
        Assert.DoesNotContain(Password, json);
        Assert.DoesNotContain("private-webhook-secret", json);
        var page = await client.GetStringAsync("/admin");
        Assert.Contains("unavailable", page);
        Assert.DoesNotContain("private-webhook-secret", page);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/admin/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/status")).StatusCode);
        var logoutToken = ParseToken(page);
        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(client, "/admin/logout", logoutToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/status")).StatusCode);
    }

    [Fact]
    public async Task Registered_operational_snapshot_exposes_only_safe_projection()
    {
        using var factory = CreateFactory(AdminPassword.Hash(Password), services => services.AddSingleton<IAdminOperationsReader>(
            new StubReader()));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(client, "/admin/login", await LoginTokenAsync(client), Password)).StatusCode);
        var body = await client.GetStringAsync("/api/admin/status");
        using var json = JsonDocument.Parse(body);
        Assert.Equal("poll failed", json.RootElement.GetProperty("sources")[0].GetProperty("pollStatus").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("delivery").GetProperty("queued").GetInt32());
        Assert.DoesNotContain("private-webhook-secret", body);
        var html = await client.GetStringAsync("/admin");
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>", html);
    }

    private static WebApplicationFactory<Program> CreateFactory(string? hash, Action<IServiceCollection>? configure = null) =>
        new AdminApiFactory(hash, configure);

    private static async Task<string> LoginTokenAsync(HttpClient client) => ParseToken(await client.GetStringAsync("/admin/login"));

    private static string ParseToken(string html)
    {
        var match = System.Text.RegularExpressions.Regex.Match(html, "name=\"csrf\" value=\"([^\"]+)\"");
        Assert.True(match.Success, "Admin page must render a CSRF token in its form");
        return System.Net.WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string csrf, string? password = null) =>
        client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["csrf"] = csrf,
            ["password"] = password ?? string.Empty,
        }));

    private sealed class StubReader : IAdminOperationsReader
    {
        public Task<AdminOperationsSnapshot> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(
            new AdminOperationsSnapshot(
                [new AdminSourceSnapshot("<script> news", "unavailable", "poll failed", null)],
                new AdminDeliverySnapshot("available", 2, 1)));
    }

    private sealed class AdminApiFactory(string? hash, Action<IServiceCollection>? configure) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("WorldAlerts:Admin:PasswordHash", hash ?? "");
            builder.UseSetting("WorldAlerts:News:PollingEnabled", "false");
            builder.UseSetting("WorldAlerts:Markets:PollingEnabled", "false");
            builder.UseSetting("WorldAlerts:Disasters:PollingEnabled", "false");
            builder.UseSetting("WorldAlerts:Advisories:PollingEnabled", "false");
            if (configure is not null) builder.ConfigureServices(configure);
        }
    }
}
