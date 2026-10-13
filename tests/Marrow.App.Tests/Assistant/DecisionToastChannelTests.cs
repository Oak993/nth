using System.Xml.Linq;
using Marrow.App.Assistant;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Testing;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-9 (DESIGN.md rev 5, 13.1 "The NotificationChannel abstraction ships now with one implementation, Windows"; ED-NEW-dextra-as9-4, -6): the
// Windows channel hands a notice to nth's own toast center as an AssistantDecision request, asks for the permission first as every toast does, and
// takes a toast back by its tag. A center that cannot take it down is not an error. The real center posts through the delegate's presentation gate.
[Trait("Feature", "AS-9")]
[Trait("Decision", "ED-NEW-dextra-as9-4")]
public sealed class DecisionToastChannelTests
{
    private static readonly ToastBuildIdentity Release = new("nth.App", "nth-release");

    private static DecisionNotice Notice() => new(Guid.Parse("a0000000-0000-4000-8000-000000000001"), new ValueList<Guid>([Guid.Parse("a0000000-0000-4000-8000-000000000001")]),
        "Store needs you", "Run npm test?", new ValueList<NoticeAction>([NoticeAction.AllowOnce, NoticeAction.Deny, NoticeAction.Open])) { Nonce = "n0nce" };

    [Fact]
    public async Task The_channel_is_the_windows_one_and_hands_the_notice_to_the_toast_center_as_a_decision_request()
    {
        var center = new RecordingAppToastCenter();
        var permissions = 0;
        var channel = new WindowsNotificationChannel(logo: () => @"C:\nth\mark.png", requestPermission: () => permissions++, center: () => center);
        Assert.Equal("Windows", channel.Name);
        Assert.IsAssignableFrom<NotificationChannel>(channel);
        Assert.True(await channel.Send(Notice()));
        var request = Assert.Single(center.Requests);
        Assert.Equal(ToastKind.AssistantDecision, request.Kind);
        Assert.Equal(@"C:\nth\mark.png", request.Logo);
        Assert.Equal(["Allow once", "Deny", "Open"], request.Buttons.Select(b => b.Content));
        Assert.Equal(1, permissions);
    }

    [Fact]
    public async Task The_channel_reads_the_ambient_toast_center_at_the_moment_it_sends()
    {
        var center = new RecordingAppToastCenter();
        using var binding = ToastNotifications.Center.Override(center);
        var channel = new WindowsNotificationChannel();
        Assert.True(await channel.Send(Notice()));
        await channel.Withdraw(Notice().Target);
        Assert.Single(center.Requests);
        Assert.Equal(["dextra-decision-a0000000000040008000000000000001"], center.Removed);
        Assert.Null(center.Requests[0].Logo);
    }

    [Fact]
    public async Task A_canceled_send_posts_nothing()
    {
        var center = new RecordingAppToastCenter();
        var channel = new WindowsNotificationChannel(center: () => center);
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => channel.Send(Notice(), cancel.Token));
        Assert.Empty(center.Requests);
    }

    [Fact]
    public void The_real_center_takes_a_toast_back_by_its_tag_and_the_posted_toast_is_the_decision_xml()
    {
        using var runtime = new TestRuntime();
        var platform = new RecordingToastPlatform();
        var center = new WindowsToastCenter(platform, Release);
        center.InstallDelegate(new AppDelegate());
        using var binding = ToastNotifications.Center.Override(center);
        var channel = new WindowsNotificationChannel(logo: () => Path.Combine(Path.GetTempPath(), "dextra-needs-you-dark.png"));
        runtime.Loop.Run(() => channel.Send(Notice()));
        var posted = Assert.Single(platform.Posts);
        Assert.Equal("dextra-decision-a0000000000040008000000000000001", posted.Tag);
        Assert.Equal(3, XDocument.Parse(posted.Xml).Descendants("action").Count());
        runtime.Loop.Run(() => channel.Withdraw(Notice().Target));
        Assert.Equal([("nth.App", posted.Tag)], platform.Removed);
        Assert.Empty(platform.Delivered);
    }

    [Fact]
    public void A_failure_the_platform_does_not_answer_reaches_the_caller_which_reports_it()
    {
        var platform = new RecordingToastPlatform { FailRemove = true };
        var center = new WindowsToastCenter(platform, Release);
        Assert.Throws<InvalidOperationException>(() => center.Remove("dextra-decision-x"));
        Assert.Empty(platform.Removed);
    }

    [Fact]
    public async Task A_center_that_cannot_take_a_toast_back_does_nothing()
    {
        IToastCenter disabled = DisabledToastCenter.Instance;
        await disabled.Remove("dextra-decision-x");
        var channel = new WindowsNotificationChannel(center: () => disabled);
        await channel.Withdraw(Guid.NewGuid());
    }
}
