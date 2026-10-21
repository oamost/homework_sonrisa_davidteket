using WorldAlerts.Api.Dedupe;

namespace WorldAlerts.Api.Tests;

/// <summary>Pure material-revision and notification-suppression rules for task 13.</summary>
public sealed class EventRevisionPolicyTests
{
    [Fact]
    public void First_observation_is_new()
    {
        var record = DedupeTestSupport.News();
        var result = EventRevisionPolicy.Evaluate(null, record, DedupeTestSupport.Now);

        Assert.Equal(EventRevisionKind.New, result.State.LastRevisionKind);
        Assert.Equal(1, result.State.RevisionCount);
        Assert.Equal(DedupeTestSupport.Now, result.State.FirstSeenUtc);
        Assert.Equal(DedupeTestSupport.Now, result.State.LastSeenUtc);
        Assert.Empty(result.ChangedFields);
        Assert.Null(result.State.PreviousFingerprint);
    }

    [Fact]
    public void Unchanged_repeat_is_unchanged_and_does_not_increment_the_revision()
    {
        var record = DedupeTestSupport.News();
        var first = EventRevisionPolicy.Evaluate(null, record, DedupeTestSupport.Now);
        var second = EventRevisionPolicy.Evaluate(
            first.State,
            record,
            DedupeTestSupport.Now.AddMinutes(5));

        Assert.Equal(EventRevisionKind.Unchanged, second.State.LastRevisionKind);
        Assert.Equal(1, second.State.RevisionCount);
        Assert.Equal(first.State.FirstSeenUtc, second.State.FirstSeenUtc);
        Assert.Equal(DedupeTestSupport.Now.AddMinutes(5), second.State.LastSeenUtc);
        Assert.Equal(first.State.Fingerprint, second.State.Fingerprint);
        Assert.Equal(first.State.Fingerprint, second.State.PreviousFingerprint);
        Assert.Empty(second.ChangedFields);
    }

    [Fact]
    public void Changed_title_is_a_material_revision()
    {
        var first = EventRevisionPolicy.Evaluate(null, DedupeTestSupport.News(), DedupeTestSupport.Now);
        var changed = DedupeTestSupport.News(title: "A corrected headline about a regional flood");
        var second = EventRevisionPolicy.Evaluate(
            first.State,
            changed,
            DedupeTestSupport.Now.AddMinutes(1));

        Assert.Equal(EventRevisionKind.MaterialRevision, second.State.LastRevisionKind);
        Assert.Equal(2, second.State.RevisionCount);
        Assert.Equal(new[] { EventRevisionField.Title }, second.ChangedFields);
        Assert.NotEqual(first.State.Fingerprint, second.State.Fingerprint);
        Assert.Equal(first.State.Fingerprint, second.State.PreviousFingerprint);
    }

    [Fact]
    public void Changed_severity_is_a_material_revision()
    {
        var first = EventRevisionPolicy.Evaluate(
            null,
            DedupeTestSupport.Disaster(severity: 4.5),
            DedupeTestSupport.Now);
        var second = EventRevisionPolicy.Evaluate(
            first.State,
            DedupeTestSupport.Disaster(severity: 5.1),
            DedupeTestSupport.Now.AddMinutes(1));

        Assert.Equal(EventRevisionKind.MaterialRevision, second.State.LastRevisionKind);
        Assert.Contains(EventRevisionField.Severity, second.ChangedFields);
    }

    [Fact]
    public void Changed_alert_level_is_a_material_revision()
    {
        var first = EventRevisionPolicy.Evaluate(
            null,
            DedupeTestSupport.Disaster(alertLevel: "Green"),
            DedupeTestSupport.Now);
        var second = EventRevisionPolicy.Evaluate(
            first.State,
            DedupeTestSupport.Disaster(alertLevel: "Red"),
            DedupeTestSupport.Now.AddMinutes(1));

        Assert.Equal(EventRevisionKind.MaterialRevision, second.State.LastRevisionKind);
        Assert.Contains(EventRevisionField.Alert, second.ChangedFields);
    }

    [Fact]
    public void Changed_price_is_a_material_revision()
    {
        var first = EventRevisionPolicy.Evaluate(
            null,
            DedupeTestSupport.Market(value: "100.0"),
            DedupeTestSupport.Now);
        var second = EventRevisionPolicy.Evaluate(
            first.State,
            DedupeTestSupport.Market(value: "101.0"),
            DedupeTestSupport.Now.AddMinutes(1));

        Assert.Equal(EventRevisionKind.MaterialRevision, second.State.LastRevisionKind);
        Assert.Contains(EventRevisionField.Price, second.ChangedFields);
    }

    [Fact]
    public void Removed_severity_is_a_material_correction()
    {
        var first = EventRevisionPolicy.Evaluate(
            null,
            DedupeTestSupport.Disaster(severity: 4.5),
            DedupeTestSupport.Now);
        var second = EventRevisionPolicy.Evaluate(
            first.State,
            DedupeTestSupport.Disaster(severity: null),
            DedupeTestSupport.Now.AddMinutes(1));

        Assert.Equal(EventRevisionKind.MaterialRevision, second.State.LastRevisionKind);
        Assert.Contains(EventRevisionField.Severity, second.ChangedFields);
    }

    [Fact]
    public void Notification_policy_allows_unseen_and_materially_changed_content()
    {
        Assert.True(NotificationPolicy.ShouldNotify(null, "fingerprint-a"));
        Assert.True(NotificationPolicy.ShouldNotify("fingerprint-a", "fingerprint-b"));
        Assert.False(NotificationPolicy.ShouldNotify("fingerprint-a", "fingerprint-a"));
    }
}
