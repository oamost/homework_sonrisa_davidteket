using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Antiforgery;
using WorldAlerts.Api.Admin;
using WorldAlerts.Api.Advisories;
using WorldAlerts.Api.Collection;
using WorldAlerts.Api.Disasters;
using WorldAlerts.Api.Delivery;
using WorldAlerts.Api.Delivery.Slack;
using WorldAlerts.Api.Delivery.Webhooks;
using WorldAlerts.Api.Dedupe;
using WorldAlerts.Api.Markets;
using WorldAlerts.Api.News;
using WorldAlerts.Api.Queue;
using WorldAlerts.Api.Selection;
using WorldAlerts.Api.Subscriptions;

var builder = WebApplication.CreateBuilder(args);

var configuredPath = builder.Configuration["WorldAlerts:DatabasePath"];
var databasePath = Path.GetFullPath(string.IsNullOrWhiteSpace(configuredPath)
    ? Path.Combine(builder.Environment.ContentRootPath, "data", "worldalerts.sqlite")
    : configuredPath);

var database = new SqliteDatabase(databasePath);
database.TryInitialize();
builder.Services.AddSingleton(database);

// Subscriptions share the same configured SQLite file and are initialized
// best-effort so a broken database cannot abort host startup.
var subscriptionStore = new SubscriptionStore(databasePath);
subscriptionStore.TryInitialize();
builder.Services.AddSingleton(subscriptionStore);
var preferenceStore = new EventTypePreferenceStore(databasePath);
try { preferenceStore.Initialize(); }
catch (Exception exception) when (exception is SqliteException or IOException)
{ Console.Error.WriteLine($"SQLite event-type preferences unavailable: {exception.GetType().Name}"); }
builder.Services.AddSingleton(preferenceStore);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AlertSelectionService>();
builder.Services.AddSingleton<ImportantNewsService>();
var collectionStore = new CollectionStateStore(databasePath);
collectionStore.TryInitialize();
builder.Services.AddSingleton(collectionStore);
var queueStore = new NotificationQueueStore(databasePath);
try { queueStore.Initialize(); }
catch (Exception exception) when (exception is SqliteException or IOException)
{
    Console.Error.WriteLine($"SQLite notification queue initialization failed: {exception.GetType().Name}");
}
builder.Services.AddSingleton(queueStore);
builder.Services.AddSingleton(sp => new TestEventService(databasePath,
    sp.GetRequiredService<EventTypePreferenceStore>(), sp.GetRequiredService<SubscriptionStore>(),
    sp.GetRequiredService<EmailSettingsStore>(), sp.GetRequiredService<SlackSettingsStore>(),
    sp.GetRequiredService<NotificationQueueStore>(), sp.GetRequiredService<TimeProvider>(),
    sp.GetRequiredService<WebhookSettingsStore>()));
var dedupeStore = new DedupeStore(databasePath);
dedupeStore.TryInitialize();
builder.Services.AddSingleton(dedupeStore);
builder.Services.AddSingleton(sp => new EventDedupeService(dedupeStore,
    () => sp.GetRequiredService<TimeProvider>().GetUtcNow()));
// Keep encryption keys on the same persistent private volume as the database.
var keyDirectory = new DirectoryInfo(databasePath + ".keys");
try
{
    Directory.CreateDirectory(keyDirectory.FullName);
    if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        File.SetUnixFileMode(keyDirectory.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
{
    // Preserve the foundation's unhealthy host response. Never substitute ephemeral keys:
    // settings must stay unavailable until the persistent private volume is repaired.
    Console.Error.WriteLine($"Persistent email encryption keys unavailable: {exception.GetType().Name}");
}
builder.Services.AddDataProtection().PersistKeysToFileSystem(keyDirectory)
    .SetApplicationName("WorldAlerts.Email");
builder.Services.AddSingleton(sp => new EmailSettingsStore(databasePath,
    sp.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>()));
builder.Services.AddSingleton(sp => new SlackSettingsStore(databasePath,
    sp.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>()));
builder.Services.AddHttpClient<SlackWebhookSender>();
builder.Services.AddSingleton<StoredSlackSender>();
builder.Services.AddSingleton<ISlackSender, ProtectedSlackSender>();
builder.Services.AddSingleton<SlackNotificationTransport>();
builder.Services.AddSingleton(sp => new WebhookSettingsStore(databasePath,
    sp.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>()));
builder.Services.AddSingleton<WebhookSender>();
builder.Services.AddSingleton<WebhookNotificationTransport>();
builder.Services.AddSingleton<ISmtpTransport, SmtpClientTransport>();
builder.Services.AddSingleton<IChannelNotificationTransport, EmailNotificationTransport>();
builder.Services.AddSingleton<IChannelNotificationTransport>(sp => sp.GetRequiredService<SlackNotificationTransport>());
builder.Services.AddSingleton<IChannelNotificationTransport>(sp => sp.GetRequiredService<WebhookNotificationTransport>());
builder.Services.AddSingleton<INotificationTransport>(sp => new RegisteredNotificationTransport(
    sp.GetServices<IChannelNotificationTransport>().ToArray()));
builder.Services.AddSingleton<NotificationDispatcher>();
builder.Services.AddSingleton<AlertPipeline>();
builder.Services.AddHostedService<EmailNotificationWorker>();
builder.Services.AddSingleton<IAdminOperationsReader, RuntimeAdminOperationsReader>();
builder.Services.AddAdminWall();
builder.Services.Configure<Microsoft.AspNetCore.Antiforgery.AntiforgeryOptions>(options =>
    options.HeaderName = "X-CSRF-TOKEN");

// News and health share the one configured SQLite file; there is no second default.
var newsOptions = builder.Configuration.GetSection("WorldAlerts:News").Get<NewsOptions>() ?? new NewsOptions();
var newsStore = new NewsStore(databasePath);
newsStore.TryInitialize();
builder.Services.AddSingleton(newsOptions);
builder.Services.AddSingleton(newsStore);
builder.Services.AddHttpClient<NewsIngestionService>((services, client) =>
{
    var options = services.GetRequiredService<NewsOptions>();
    client.Timeout = options.RequestTimeout;
});
builder.Services.AddHostedService<NewsPollingService>();

var marketOptions = builder.Configuration.GetSection("WorldAlerts:Markets").Get<MarketOptions>() ?? new MarketOptions();
var marketStore = new MarketStore(databasePath);
marketStore.TryInitialize();
builder.Services.AddSingleton(marketOptions);
builder.Services.AddSingleton(marketStore);
builder.Services.AddHttpClient<MarketIngestionService>((services, client) =>
{
    var options = services.GetRequiredService<MarketOptions>();
    client.Timeout = options.RequestTimeout;
});
builder.Services.AddHostedService<MarketPollingService>();

var disasterOptions = builder.Configuration.GetSection("WorldAlerts:Disasters").Get<DisasterOptions>() ?? new DisasterOptions();
var disasterStore = new DisasterStore(databasePath);
disasterStore.TryInitialize();
builder.Services.AddSingleton(disasterOptions);
builder.Services.AddSingleton(disasterStore);
builder.Services.AddHttpClient<DisasterIngestionService>((services, client) =>
{
    var options = services.GetRequiredService<DisasterOptions>();
    client.Timeout = options.RequestTimeout;
});
builder.Services.AddHostedService<DisasterPollingService>();

var advisoryOptions = builder.Configuration.GetSection("WorldAlerts:Advisories").Get<AdvisoryOptions>() ?? new AdvisoryOptions();
var advisoryStore = new AdvisoryStore(databasePath);
advisoryStore.TryInitialize();
builder.Services.AddSingleton(advisoryOptions);
builder.Services.AddSingleton(advisoryStore);
builder.Services.AddHttpClient<AdvisoryIngestionService>((services, client) =>
{
    var options = services.GetRequiredService<AdvisoryOptions>();
    client.Timeout = options.RequestTimeout;
});
builder.Services.AddHostedService<AdvisoryPollingService>();

var app = builder.Build();
try { app.Services.GetRequiredService<TestEventService>().Initialize(); }
catch (Exception exception) when (exception is SqliteException or IOException)
{ Console.Error.WriteLine($"SQLite TEST event store unavailable: {exception.GetType().Name}"); }
var frontendIndex = Path.Combine(app.Environment.WebRootPath ??
    Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "index.html");
if (File.Exists(frontendIndex))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapFallback((HttpContext context) =>
        context.Request.Path.StartsWithSegments("/api")
        || context.Request.Path.StartsWithSegments("/admin/login")
        || context.Request.Path.StartsWithSegments("/admin/logout")
            ? Results.NotFound() : Results.File(frontendIndex, "text/html"));
}
app.UseAuthentication();
app.UseAuthorization();
app.MapAdminWall(hostFrontend: File.Exists(frontendIndex));
app.MapEmailSettings();
app.MapSlackSettings();
app.MapWebhookSettings();
app.MapGet("/api/test-events/token", (HttpContext context, IAntiforgery antiforgery) =>
{
    context.Response.Headers.CacheControl = "no-store";
    return Results.Ok(new { csrfToken = antiforgery.GetAndStoreTokens(context).RequestToken });
});
app.MapPost("/api/test-events", async (HttpContext context, IAntiforgery antiforgery,
    TestEventService tests, CancellationToken ct) =>
{
    context.Response.Headers.CacheControl = "no-store";
    var origin = context.Request.Headers.Origin.ToString();
    if (!string.IsNullOrEmpty(origin) && !string.Equals(origin,
        $"{context.Request.Scheme}://{context.Request.Host}", StringComparison.OrdinalIgnoreCase))
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    var fetchSite = context.Request.Headers["Sec-Fetch-Site"].ToString();
    if (!string.IsNullOrEmpty(fetchSite) && fetchSite != "same-origin")
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (!await antiforgery.IsRequestValidAsync(context))
        return Results.BadRequest(new { message = "Security token missing or expired. Reload and try again." });
    var (limited, message) = await tests.EmitAsync(ct);
    return Results.Json(new { message }, statusCode: limited ? StatusCodes.Status429TooManyRequests : StatusCodes.Status200OK);
});

if (!File.Exists(frontendIndex)) app.MapGet("/", () => "World Alerts API");
app.MapGet("/api/health", async (SqliteDatabase db, CancellationToken cancellationToken) =>
    await db.CanConnectAsync(cancellationToken)
        ? Results.Ok(new HealthResponse("ok"))
        : Results.Json(new HealthResponse("unavailable"), statusCode: StatusCodes.Status503ServiceUnavailable));
app.MapGet("/api/alerts/selection", async (AlertSelectionService selection, CancellationToken cancellationToken) =>
    Results.Ok(await selection.SelectAsync(cancellationToken)));
app.MapGet("/api/news/important", async (ImportantNewsService importantNews, CancellationToken cancellationToken) =>
    Results.Ok(await importantNews.GetAsync(cancellationToken)));
app.MapGet("/api/news", async (NewsStore store, int? limit, CancellationToken cancellationToken) =>
{
    var clamped = Math.Clamp(limit ?? NewsStore.DefaultLimit, NewsStore.MinLimit, NewsStore.MaxLimit);
    var items = await store.GetRecentAsync(clamped, cancellationToken);
    return Results.Ok(items);
});
app.MapGet("/api/markets", async (MarketStore store, int? limit, string? source, CancellationToken cancellationToken) =>
{
    var clamped = Math.Clamp(limit ?? MarketStore.DefaultLimit, MarketStore.MinLimit, MarketStore.MaxLimit);
    var items = await store.GetRecentAsync(clamped, source, cancellationToken);
    return Results.Ok(items);
});
app.MapGet("/api/disasters", async (DisasterStore store, int? limit, string? source, CancellationToken cancellationToken) =>
{
    var clamped = Math.Clamp(limit ?? DisasterStore.DefaultLimit, DisasterStore.MinLimit, DisasterStore.MaxLimit);
    var items = await store.GetRecentAsync(clamped, source, cancellationToken);
    return Results.Ok(items);
});
app.MapGet("/api/advisories", async (AdvisoryStore store, int? limit, string? source, CancellationToken cancellationToken) =>
{
    var clamped = Math.Clamp(limit ?? AdvisoryStore.DefaultLimit, AdvisoryStore.MinLimit, AdvisoryStore.MaxLimit);
    var items = await store.GetRecentAsync(clamped, source, cancellationToken);
    return Results.Ok(items);
});
app.MapGet("/api/subscriptions", async (SubscriptionStore store, string? eventType, CancellationToken cancellationToken) =>
{
    var items = await store.ListAsync(Subscription.DefaultUserId, eventType, cancellationToken);
    return Results.Ok(items);
});
app.MapGet("/api/alert-settings/types", async (HttpContext context, IAntiforgery antiforgery,
    EventTypePreferenceStore store, CancellationToken cancellationToken) =>
{
    context.Response.Headers.CacheControl = "no-store";
    var current = await store.GetAsync(Subscription.DefaultUserId, cancellationToken);
    return Results.Ok(new { current.IsCustomized, current.SelectedTypes,
        csrfToken = antiforgery.GetAndStoreTokens(context).RequestToken });
});
app.MapPut("/api/alert-settings/types", async (HttpContext context, IAntiforgery antiforgery,
    EventTypePreferenceUpdate? update, EventTypePreferenceStore store, CancellationToken cancellationToken) =>
{
    context.Response.Headers.CacheControl = "no-store";
    if (!await antiforgery.IsRequestValidAsync(context))
        return Results.BadRequest(new { error = "Security token missing or expired. Reload and try again." });
    if (update?.SelectedTypes is null) return Results.BadRequest(new { error = "Selected types are required." });
    try { await store.SaveAsync(Subscription.DefaultUserId, update.SelectedTypes, cancellationToken); }
    catch (ArgumentException) { return Results.BadRequest(new { error = "Unknown or duplicate event type." }); }
    return Results.Ok(await store.GetAsync(Subscription.DefaultUserId, cancellationToken));
});
app.MapPost("/api/subscriptions", async (
    SubscriptionRequest? request,
    SubscriptionStore store,
    CancellationToken cancellationToken) =>
{
    if (request is null
        || string.IsNullOrWhiteSpace(request.EventType)
        || string.IsNullOrWhiteSpace(request.Channel))
    {
        return Results.BadRequest(new SubscriptionError("eventType and channel are required."));
    }

    try
    {
        var stored = await store.UpsertAsync(
            new Subscription
            {
                EventType = request.EventType,
                Channel = request.Channel,
                Destination = request.Destination,
                Enabled = request.Enabled ?? true,
            },
            cancellationToken);
        return Results.Ok(stored);
    }
    catch (ArgumentException)
    {
        // Do not echo the submitted body: it may contain an opaque destination.
        return Results.BadRequest(new SubscriptionError("eventType and channel are required."));
    }
});
app.MapDelete("/api/subscriptions/{id:long}", async (
    long id,
    SubscriptionStore store,
    CancellationToken cancellationToken) =>
    await store.DeleteAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound());

app.Run();

internal sealed record HealthResponse(string Status);

internal sealed record SubscriptionRequest(
    string? EventType,
    string? Channel,
    string? Destination,
    bool? Enabled);

internal sealed record SubscriptionError(string Error);
internal sealed record EventTypePreferenceUpdate(string[]? SelectedTypes);

/// <summary>
/// Owns the local SQLite file. Initialization failure is recorded, not thrown, so the API still starts
/// and reports itself unhealthy without disclosing the configured path.
/// </summary>
internal sealed class SqliteDatabase
{
    private readonly string _path;
    private readonly string _connectionString;

    public SqliteDatabase(string path)
    {
        _path = path;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 5,
        }.ToString();
    }

    public void TryInitialize()
    {
        try
        {
            EnsureDirectory();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS SchemaVersions (
                    Version INTEGER NOT NULL,
                    AppliedUtc TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }
        catch (Exception exception)
        {
            // Unusable database: health checks report unavailability instead of failing startup.
            Console.Error.WriteLine($"SQLite initialization failed: {exception.GetType().Name}");
        }
    }

    private void EnsureDirectory()
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            return await command.ExecuteScalarAsync(cancellationToken) is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

public partial class Program;
