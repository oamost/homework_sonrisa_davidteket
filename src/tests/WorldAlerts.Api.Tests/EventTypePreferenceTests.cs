using WorldAlerts.Api.Subscriptions;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WorldAlerts.Api.Tests;

public sealed class EventTypePreferenceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private string Path => _temp.File("preferences.sqlite");
    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task New_local_profile_selects_every_approved_type()
    {
        var store = new EventTypePreferenceStore(Path);
        store.Initialize();
        var state = await store.GetAsync(Subscription.DefaultUserId);
        Assert.False(state.IsCustomized);
        Assert.Equal(24, state.SelectedTypes.Count);
        Assert.Contains("world-news", state.SelectedTypes);
        Assert.Contains("economy.financial", state.SelectedTypes);
    }

    [Fact]
    public async Task Empty_selection_survives_reopening_and_is_not_replaced_by_defaults()
    {
        var store = new EventTypePreferenceStore(Path);
        store.Initialize();
        await store.SaveAsync(Subscription.DefaultUserId, []);
        var reopened = new EventTypePreferenceStore(Path);
        reopened.Initialize();
        var state = await reopened.GetAsync(Subscription.DefaultUserId);
        Assert.True(state.IsCustomized);
        Assert.Empty(state.SelectedTypes);
    }

    [Fact]
    public async Task Saving_changed_selection_is_atomic_and_rejects_unknown_or_duplicate_codes()
    {
        var store = new EventTypePreferenceStore(Path);
        store.Initialize();
        var selected = (await store.GetAsync(Subscription.DefaultUserId)).SelectedTypes
            .Where(type => type != "world-news").ToArray();
        await store.SaveAsync(Subscription.DefaultUserId, selected);
        Assert.Equal(23, (await store.GetAsync(Subscription.DefaultUserId)).SelectedTypes.Count);
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(Subscription.DefaultUserId, ["unknown"]));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(Subscription.DefaultUserId, ["conflict", "conflict"]));
        Assert.DoesNotContain("world-news", (await store.GetAsync(Subscription.DefaultUserId)).SelectedTypes);
    }

    [Fact]
    public async Task Public_settings_endpoint_requires_antiforgery_on_save_and_remembers_empty_selection()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting("WorldAlerts:DatabasePath", Path)
            .UseSetting("WorldAlerts:News:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Markets:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Disasters:PollingEnabled", "false")
            .UseSetting("WorldAlerts:Advisories:PollingEnabled", "false"));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost") });
        using var initialResponse = await client.GetAsync("/api/alert-settings/types");
        Assert.True(initialResponse.IsSuccessStatusCode, await initialResponse.Content.ReadAsStringAsync());
        var initial = await initialResponse.Content.ReadFromJsonAsync<EventTypePreferenceResponse>();
        Assert.NotNull(initial);
        Assert.Equal(24, initial.SelectedTypes.Length);
        using var forbidden = await client.PutAsJsonAsync("/api/alert-settings/types", new { selectedTypes = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.BadRequest, forbidden.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/alert-settings/types")
        { Content = JsonContent.Create(new { selectedTypes = Array.Empty<string>() }) };
        request.Headers.Add("X-CSRF-TOKEN", initial.CsrfToken);
        using var saved = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var after = await client.GetFromJsonAsync<EventTypePreferenceResponse>("/api/alert-settings/types");
        Assert.True(after!.IsCustomized);
        Assert.Empty(after.SelectedTypes);
    }

    private sealed record EventTypePreferenceResponse(bool IsCustomized, string[] SelectedTypes, string CsrfToken);
}
