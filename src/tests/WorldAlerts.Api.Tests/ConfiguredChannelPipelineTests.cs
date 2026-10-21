using Microsoft.AspNetCore.DataProtection;
using WorldAlerts.Api.Dedupe;
using WorldAlerts.Api.Delivery;
using WorldAlerts.Api.Delivery.Slack;
using WorldAlerts.Api.Delivery.Webhooks;
using WorldAlerts.Api.Disasters;
using WorldAlerts.Api.Markets;
using WorldAlerts.Api.News;
using WorldAlerts.Api.Queue;
using WorldAlerts.Api.Selection;
using WorldAlerts.Api.Subscriptions;

namespace WorldAlerts.Api.Tests;

public sealed class ConfiguredChannelPipelineTests
{
    [Fact]
    public async Task Default_types_queue_important_item_to_every_configured_channel_without_per_type_rows()
    {
        using var temp = new TempDirectory();
        var path = temp.File("configured.sqlite");
        var now = DedupeTestSupport.Now;
        var news = new NewsStore(path); news.Initialize();
        var disasters = new DisasterStore(path); disasters.Initialize();
        var markets = new MarketStore(path); markets.Initialize();
        var subscriptions = new SubscriptionStore(path); subscriptions.Initialize();
        var preferences = new EventTypePreferenceStore(path); preferences.Initialize();
        var protection = DataProtectionProvider.Create("test");
        var emails = new EmailSettingsStore(path, protection);
        await emails.SaveAsync(new EmailSettings("smtp.example.test", 587, "StartTls", "sender",
            "secret", "sender@example.test", true) { RecipientAddress = "recipient@example.test" });
        var slacks = new SlackSettingsStore(path, protection);
        await slacks.SaveAsync(new SlackSettings("https://hooks.slack.com/services/T000/B000/TEST", true));
        var webhooks = new WebhookSettingsStore(path, protection);
        await webhooks.SaveAsync(new WebhookConfiguration("ops", "Operations", "https://example.org/alerts", "POST",
            [], "{\"text\":\"{{title}}\"}", true));
        await news.InsertAsync([new NewsItem("bbc-world", "BBC World", "story-1", "Breaking conflict",
            "https://example.test/story", now.AddHours(-1), now, "world-news", [], "https://example.test/story", null)]);
        var dedupe = new DedupeStore(path); dedupe.Initialize();
        var queue = new NotificationQueueStore(path); queue.Initialize();
        var selection = new AlertSelectionService(news, markets, disasters, subscriptions,
            new MarketOptions(), new FixedClock(now));
        var pipeline = new AlertPipeline(selection, news, markets, disasters, subscriptions,
            new EventDedupeService(dedupe, () => now), queue, new FixedClock(now), preferences, emails, slacks, webhooks);
        Assert.Equal(3, await pipeline.EnqueueSelectedAsync());
        var jobs = await queue.SnapshotAsync();
        Assert.Equal(new[] { "email", "slack", "webhook" }, jobs.Select(job => job.Channel).OrderBy(x => x));
        Assert.Contains(jobs, job => job.Channel == "email" && job.Destination == "recipient@example.test");
        Assert.DoesNotContain(jobs, job => job.SubscriberId.Contains("recipient@example.test"));
        Assert.Contains(jobs, job => job.Channel == "slack" && job.Destination == "");
        Assert.Contains(jobs, job => job.Channel == "webhook" && job.Destination == "ops");
        Assert.Equal(0, await pipeline.EnqueueSelectedAsync());
        await preferences.SaveAsync(Subscription.DefaultUserId, EventTypePreferenceStore.ApprovedTypes);
        await subscriptions.UpsertAsync(new Subscription { EventType = "world-news", Channel = "email",
            Destination = "original@example.test", Enabled = true });
        await news.InsertAsync([new NewsItem("bbc-world", "BBC World", "story-2", "Breaking world alert",
            "https://example.test/story-2", now.AddMinutes(-20), now, "world-news", [], "https://example.test/story-2", null)]);
        // The newly enabled legacy recipient also catches up on the earlier eligible item.
        Assert.Equal(4, await pipeline.EnqueueSelectedAsync());
        var newJobs = (await queue.SnapshotAsync()).Where(job => job.IdentityKey.Contains("story-2")).ToArray();
        Assert.Contains(newJobs, job => job.Channel == "email" && job.Destination == "original@example.test");
        Assert.Contains(newJobs, job => job.Channel == "slack");
        Assert.Contains(newJobs, job => job.Channel == "webhook" && job.Destination == "ops");
        await preferences.SaveAsync(Subscription.DefaultUserId, []);
        await news.InsertAsync([new NewsItem("bbc-world", "BBC World", "story-3", "Breaking world alert",
            "https://example.test/story-3", now.AddMinutes(-10), now, "world-news", [], "https://example.test/story-3", null)]);
        Assert.Equal(0, await pipeline.EnqueueSelectedAsync());
        await preferences.SaveAsync(Subscription.DefaultUserId, ["world-news"]);
        await emails.SaveAsync((await emails.ReadAsync())! with { Enabled = false });
        await news.InsertAsync([new NewsItem("bbc-world", "BBC World", "story-4", "Breaking world alert",
            "https://example.test/story-4", now.AddMinutes(-5), now, "world-news", [], "https://example.test/story-4", null)]);
        await pipeline.EnqueueSelectedAsync();
        var fourth = (await queue.SnapshotAsync()).Where(job => job.IdentityKey.Contains("story-4")).ToArray();
        Assert.DoesNotContain(fourth, job => job.Channel == "email");
        Assert.Contains(fourth, job => job.Channel == "slack");
    }

    [Fact]
    public async Task Unrelated_old_subscription_does_not_silence_explicitly_enabled_email_and_slack()
    {
        using var temp = new TempDirectory();
        var path = temp.File("mixed-legacy.sqlite");
        var now = DedupeTestSupport.Now;
        var news = new NewsStore(path); news.Initialize();
        var disasters = new DisasterStore(path); disasters.Initialize();
        var markets = new MarketStore(path); markets.Initialize();
        var subscriptions = new SubscriptionStore(path); subscriptions.Initialize();
        // The disabled row is for a DIFFERENT category. It must not suppress
        // alerts for world-news in the configured private delivery channels.
        await subscriptions.UpsertAsync(new Subscription { EventType = "tech.ai", Channel = "email",
            Destination = "legacy@example.test", Enabled = false });
        var preferences = new EventTypePreferenceStore(path); preferences.Initialize();
        var protection = DataProtectionProvider.Create("test");
        var emails = new EmailSettingsStore(path, protection);
        await emails.SaveAsync(new EmailSettings("smtp.example.test", 587, "StartTls", "sender", "secret",
            "sender@example.test", true) { RecipientAddress = "recipient@example.test" });
        var slacks = new SlackSettingsStore(path, protection);
        await slacks.SaveAsync(new SlackSettings("https://hooks.slack.com/services/T/B/TEST", true));
        await news.InsertAsync([new NewsItem("bbc-world", "BBC World", "today-1", "Breaking conflict",
            "https://example.test/today", now.AddMinutes(-5), now, "world-news", [], "https://example.test/today", null)]);
        var dedupe = new DedupeStore(path); dedupe.Initialize();
        var queue = new NotificationQueueStore(path); queue.Initialize();
        var selection = new AlertSelectionService(news, markets, disasters, subscriptions,
            new MarketOptions(), new FixedClock(now));
        var pipeline = new AlertPipeline(selection, news, markets, disasters, subscriptions,
            new EventDedupeService(dedupe, () => now), queue, new FixedClock(now), preferences, emails, slacks);
        Assert.Equal(2, await pipeline.EnqueueSelectedAsync());
        var jobs = await queue.SnapshotAsync();
        Assert.Contains(jobs, job => job.Channel == "email" && job.Destination == "recipient@example.test");
        Assert.Contains(jobs, job => job.Channel == "slack");
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
