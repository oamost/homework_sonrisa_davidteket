using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace WorldAlerts.Api.Tests;

public sealed class FrontendHostingTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Hosted_app_serves_dashboard_and_spa_routes_without_a_second_dev_server()
    {
        var webroot = Path.Combine(_temp.Path, "wwwroot");
        Directory.CreateDirectory(webroot);
        await File.WriteAllTextAsync(Path.Combine(webroot, "index.html"), "<!doctype html><title>World Alerts UI</title>");
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseWebRoot(webroot)
            .UseSetting("WorldAlerts:DatabasePath", _temp.File("frontend.sqlite"))
            .UseSetting("WorldAlerts:News:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Markets:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Disasters:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Advisories:PollingEnabled", "false"));
        using var client = factory.CreateClient();
        Assert.Equal(webroot, factory.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath);
        Assert.Contains("World Alerts UI", await client.GetStringAsync("/"));
        Assert.Contains("World Alerts UI", await client.GetStringAsync("/subscriptions"));
        Assert.Contains("World Alerts UI", await client.GetStringAsync("/admin"));
        Assert.Contains("ok", (await client.GetStringAsync("/api/health")).ToLowerInvariant());
    }
}
