using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WorldAlerts.Api.Delivery;
using WorldAlerts.Api.Delivery.Slack;

namespace WorldAlerts.Api.Tests;

public sealed class SlackEndpointsTests
{
    [Fact]
    public async Task Anonymous_access_is_unauthorized_and_authenticated_mutation_needs_csrf()
    {
        await using var host = await Fixture.StartAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/admin/slack")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.PostAsJsonAsync("/api/admin/slack/test", new { consent = true })).StatusCode);
        await host.LoginAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.PutAsJsonAsync("/api/admin/slack", new { webhookUrl = "https://hooks.slack.com/services/T/B/secret", enabled = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.PostAsJsonAsync("/api/admin/slack/test", new { consent = true })).StatusCode);
        Assert.Equal(0, host.Handler.Count);
    }

    [Fact]
    public async Task Save_redacts_secret_and_explicit_test_reports_http_outcome_without_leakage()
    {
        await using var host = await Fixture.StartAsync();
        await host.LoginAsync();
        var token = await host.CsrfAsync();
        var webhook = "https://hooks.slack.com/services/T/B/XXXXXXXXXXXXXXXXXXXXXXXX";
        var invalid = new HttpRequestMessage(HttpMethod.Put, "/api/admin/slack") { Content = JsonContent.Create(new { webhookUrl = "https://localhost/private", enabled = true }) };
        invalid.Headers.Add("X-CSRF-TOKEN", token);
        var rejected = await host.Client.SendAsync(invalid);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Contains("incoming-webhook", await rejected.Content.ReadAsStringAsync());
        var save = new HttpRequestMessage(HttpMethod.Put, "/api/admin/slack") { Content = JsonContent.Create(new { webhookUrl = webhook, enabled = true, channelLabel = "#alerts" }) };
        save.Headers.Add("X-CSRF-TOKEN", token);
        var saved = await host.Client.SendAsync(save);
        Assert.True(saved.StatusCode == HttpStatusCode.OK,
            $"Expected Slack settings save to succeed, got {(int)saved.StatusCode}: {await saved.Content.ReadAsStringAsync()}");
        Assert.DoesNotContain(webhook, await saved.Content.ReadAsStringAsync());
        using var statusResponse = await host.Client.GetAsync("/api/admin/slack");
        using var statusJson = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());
        Assert.Equal("#alerts", statusJson.RootElement.GetProperty("channelLabel").GetString());
        var missingConsent = await host.TestAsync(false, token);
        Assert.Equal(HttpStatusCode.BadRequest, missingConsent.StatusCode);
        Assert.Equal(0, host.Handler.Count);
        foreach (var status in new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.TooManyRequests })
        {
            host.Handler.Status = status;
            var result = await host.TestAsync(true, token);
            Assert.Equal(status == HttpStatusCode.OK ? HttpStatusCode.OK : HttpStatusCode.BadGateway, result.StatusCode);
            Assert.DoesNotContain(webhook, await result.Content.ReadAsStringAsync());
        }
        Assert.Equal(3, host.Handler.Count);
    }

    [Fact]
    public async Task Disabled_or_missing_configuration_never_sends_a_test_message()
    {
        await using var host = await Fixture.StartAsync();
        await host.LoginAsync();
        var token = await host.CsrfAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await host.TestAsync(true, token)).StatusCode);
        var save = new HttpRequestMessage(HttpMethod.Put, "/api/admin/slack")
        {
            Content = JsonContent.Create(new { webhookUrl = "https://hooks.slack.com/services/T/B/XXXXXXXXXXXXXXXXXXXXXXXX", enabled = false }),
        };
        save.Headers.Add("X-CSRF-TOKEN", token);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(save)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await host.TestAsync(true, token)).StatusCode);
        Assert.Equal(0, host.Handler.Count);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly string _folder;
        private string _cookies = "";
        public HttpClient Client { get; }
        public CountingHandler Handler { get; }
        private Fixture(WebApplication app, string folder, CountingHandler handler)
        { _app = app; _folder = folder; Handler = handler; Client = app.GetTestClient(); }

        public static async Task<Fixture> StartAsync()
        {
            var folder = Path.Combine(Path.GetTempPath(), "slack-api-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var handler = new CountingHandler();
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options => options.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; });
            builder.Services.AddAuthorization();
            builder.Services.AddAntiforgery(o => o.HeaderName = "X-CSRF-TOKEN");
            builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(folder, "keys")));
            builder.Services.AddSingleton(sp => new SlackSettingsStore(Path.Combine(folder, "alerts.db"), sp.GetRequiredService<IDataProtectionProvider>()));
            builder.Services.AddHttpClient<SlackWebhookSender>().ConfigurePrimaryHttpMessageHandler(() => handler);
            builder.Services.Configure<SlackOptions>(_ => { });
            builder.Services.AddSingleton<StoredSlackSender>();
            var app = builder.Build();
            app.UseAuthentication(); app.UseAuthorization();
            app.MapGet("/test-login", async (HttpContext ctx) =>
            {
                await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin")], CookieAuthenticationDefaults.AuthenticationScheme)));
            });
            app.MapSlackSettings();
            await app.StartAsync();
            return new Fixture(app, folder, handler);
        }
        public async Task LoginAsync()
        {
            var response = await Client.GetAsync("/test-login");
            _cookies = response.Headers.GetValues("Set-Cookie").First().Split(';')[0];
            Client.DefaultRequestHeaders.Remove("Cookie");
            Client.DefaultRequestHeaders.Add("Cookie", _cookies);
        }
        public async Task<string> CsrfAsync()
        {
            var response = await Client.GetAsync("/api/admin/slack");
            if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
            {
                _cookies += "; " + cookies.First().Split(';')[0];
                Client.DefaultRequestHeaders.Remove("Cookie");
                Client.DefaultRequestHeaders.Add("Cookie", _cookies);
            }
            using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.GetProperty("csrfToken").GetString()!;
        }
        public Task<HttpResponseMessage> TestAsync(bool consent, string token)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/slack/test") { Content = JsonContent.Create(new { consent }) };
            request.Headers.Add("X-CSRF-TOKEN", token);
            return Client.SendAsync(request);
        }
        public async ValueTask DisposeAsync()
        { Client.Dispose(); await _app.DisposeAsync(); Directory.Delete(_folder, true); }
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Count { get; private set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Count++; return Task.FromResult(new HttpResponseMessage(Status)); }
    }
}
