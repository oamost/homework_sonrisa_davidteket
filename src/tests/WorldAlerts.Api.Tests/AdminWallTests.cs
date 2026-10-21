using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorldAlerts.Api.Admin;

namespace WorldAlerts.Api.Tests;

public sealed class AdminWallTests : IDisposable
{
    private const string Password = "local-test-password";
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

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
    public async Task Login_requires_csrf_rejects_wrong_password_and_production_without_configured_hash()
    {
        using var factory = CreateFactory(AdminPassword.Hash(Password));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        var noToken = await client.PostAsync("/admin/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["password"] = Password }));
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        var token = await LoginTokenAsync(client);
        var wrong = await PostAsync(client, "/admin/login", token, "incorrect");
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/status")).StatusCode);

        using var disabledFactory = CreateFactory(null, environment: "Production");
        using var disabledClient = disabledFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        var disabledToken = await LoginTokenAsync(disabledClient);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(disabledClient, "/admin/login", disabledToken, Password)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(disabledClient, "/admin/login", disabledToken, "admin")).StatusCode);
    }

    [Fact]
    public async Task Development_loopback_accepts_default_admin_only_without_configured_hash()
    {
        using var factory = CreateFactory(null, environment: "Development");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        var token = await LoginTokenAsync(client);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(client, "/admin/login", token, "wrong")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(client, "/admin/login", token, "admin")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/status")).StatusCode);
    }

    [Fact]
    public async Task Configured_hash_overrides_default_admin_in_development()
    {
        using var factory = CreateFactory(AdminPassword.Hash(Password), environment: "Development");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        var token = await LoginTokenAsync(client);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(client, "/admin/login", token, "admin")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(client, "/admin/login", token, Password)).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Development_default_rejects_nonloopback_and_missing_remote_addresses(bool missingRemoteAddress)
    {
        using var factory = CreateFactory(null, environment: "Development",
            remoteAddress: IPAddress.Parse("192.0.2.10"), missingRemoteAddress: missingRemoteAddress);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost"),
        });
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await PostAsync(client, "/admin/login", await LoginTokenAsync(client), "admin")).StatusCode);
    }

    [Fact]
    public async Task Authorized_snapshot_redacts_secrets_and_logout_requires_csrf_then_revokes_session()
    {
        using var factory = CreateFactory(AdminPassword.Hash(Password));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        var token = await LoginTokenAsync(client);
        var login = await PostAsync(client, "/admin/login", token, Password);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), value =>
            value.Contains("HttpOnly", StringComparison.OrdinalIgnoreCase)
            && value.Contains("Secure", StringComparison.OrdinalIgnoreCase)
            && value.Contains("SameSite=Strict", StringComparison.OrdinalIgnoreCase));

        var status = await client.GetAsync("/api/admin/status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        var json = await status.Content.ReadAsStringAsync();
        using var snapshot = JsonDocument.Parse(json);
        var delivery = snapshot.RootElement.GetProperty("delivery");
        Assert.Equal("available", delivery.GetProperty("status").GetString());
        Assert.True(delivery.GetProperty("queued").GetInt32() >= 0);
        Assert.True(delivery.GetProperty("failed").GetInt32() >= 0);
        Assert.Equal(JsonValueKind.Array, snapshot.RootElement.GetProperty("sources").ValueKind);
        Assert.DoesNotContain(Password, json);
        Assert.DoesNotContain("private-webhook-secret", json);
        var page = await client.GetStringAsync("/admin");
        Assert.Contains("Delivery queue and failed sends", page);
        Assert.DoesNotContain("private-webhook-secret", page);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/admin/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/status")).StatusCode);
        var logoutToken = ParseToken(page);
        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(client, "/admin/logout", logoutToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/status")).StatusCode);
    }

    [Fact]
    public async Task Explicit_unavailable_reader_preserves_unavailable_not_zero_when_runtime_reader_is_absent()
    {
        using var factory = CreateFactory(AdminPassword.Hash(Password), services =>
        {
            services.RemoveAll<IAdminOperationsReader>();
            var readerType = typeof(AdminPassword).Assembly.GetType("WorldAlerts.Api.Admin.UnavailableAdminOperationsReader", throwOnError: true)!;
            services.AddSingleton((IAdminOperationsReader)Activator.CreateInstance(readerType, nonPublic: true)!);
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost"),
        });
        Assert.Equal(HttpStatusCode.Redirect,
            (await PostAsync(client, "/admin/login", await LoginTokenAsync(client), Password)).StatusCode);
        var response = await client.GetAsync("/api/admin/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var snapshot = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var delivery = snapshot.RootElement.GetProperty("delivery");
        Assert.Equal("unavailable", delivery.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, delivery.GetProperty("queued").ValueKind);
        Assert.Equal(JsonValueKind.Null, delivery.GetProperty("failed").ValueKind);
        Assert.Contains("unavailable", await client.GetStringAsync("/admin"));
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

    private WebApplicationFactory<Program> CreateFactory(string? hash, Action<IServiceCollection>? configure = null, string? environment = null,
        IPAddress? remoteAddress = null, bool missingRemoteAddress = false) =>
        new AdminApiFactory(hash, _temp.File($"admin-{Guid.NewGuid():N}.sqlite"), configure, environment,
            missingRemoteAddress ? null : remoteAddress ?? IPAddress.Loopback);

    private static async Task<string> LoginTokenAsync(HttpClient client) => ParseToken(await client.GetStringAsync("/admin/login"));

    [Theory]
    [InlineData("<input type='hidden' name='csrf' value=\"token-one\">", "token-one")]
    [InlineData("<input value='token-two' name=\"csrf\" type=\"hidden\">", "token-two")]
    public void Token_parser_accepts_single_or_double_quoted_attributes_in_either_order(string html, string expected) =>
        Assert.Equal(expected, ParseToken(html));

    private static string ParseToken(string html)
    {
        var inputTags = System.Text.RegularExpressions.Regex.Matches(html, @"<input\b[^>]*>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (System.Text.RegularExpressions.Match tag in inputTags)
        {
            var attributes = System.Text.RegularExpressions.Regex.Matches(tag.Value,
                "(?<key>[a-zA-Z][a-zA-Z0-9-]*)\\s*=\\s*(?:'(?<value>[^']*)'|\\\"(?<value>[^\\\"]*)\\\")");
            var values = attributes.Cast<System.Text.RegularExpressions.Match>()
                .ToDictionary(attribute => attribute.Groups["key"].Value,
                    attribute => attribute.Groups["value"].Value, StringComparer.OrdinalIgnoreCase);
            if (values.TryGetValue("name", out var name) && name == "csrf"
                && values.TryGetValue("value", out var value) && !string.IsNullOrEmpty(value))
                return System.Net.WebUtility.HtmlDecode(value);
        }
        Assert.Fail("Admin page must render a CSRF token in its form");
        return string.Empty;
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

    private sealed class AdminApiFactory(string? hash, string databasePath, Action<IServiceCollection>? configure, string? environment,
        IPAddress? remoteAddress) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("WorldAlerts:Admin:PasswordHash", hash ?? "");
            if (environment is not null) builder.UseEnvironment(environment);
            builder.UseSetting("WorldAlerts:DatabasePath", databasePath);
            builder.UseSetting("WorldAlerts:News:PollingEnabled", "false");
            builder.UseSetting("WorldAlerts:Markets:PollingEnabled", "false");
            builder.UseSetting("WorldAlerts:Disasters:PollingEnabled", "false");
            builder.UseSetting("WorldAlerts:Advisories:PollingEnabled", "false");
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IStartupFilter>(new TestRemoteAddressFilter(remoteAddress));
                configure?.Invoke(services);
            });
        }
    }

    private sealed class TestRemoteAddressFilter(IPAddress? remoteAddress) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = remoteAddress;
                return nextMiddleware();
            });
            next(app);
        };
    }
}
