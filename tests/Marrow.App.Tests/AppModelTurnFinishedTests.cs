using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Xunit;

namespace Marrow.App.Tests;

// App/AppModel.swift:1173-1216 turnFinished: the four gates that decide whether anything is heard or seen when a turn ends
// (UI-RUNTIME-015), and the notification body and title it builds. The toast centre and the chime's sound output are
// recorders (AppModelNotifyRig), so nothing is posted and no audio device is opened.
[Collection(FinishChimeCollection.Name)]
public sealed class AppModelTurnFinishedTests
{
    private const string Title = "Fix the parser";

    private static ChatThread Chat(Project project) => AppModelSamples.Thread(project) with { Title = Title };

    private static ChatThread NativeHead(Project project, Guid lead) => AppModelSamples.Thread(project) with
    {
        Title = "Head",
        ParentThreadID = lead,
        IsInPanel = true,
        Hydra = new HydraHeadInfo(0, "task", HydraHeadInfo.Kind.Native, HydraHeadInfo.Origin.Delegated),
    };

    [Theory]
    [Trait("Req", "UI-RUNTIME-015")] // AppModel.swift:1203-1209
    [InlineData(TurnStatus.Completed, "Finished.")]
    [InlineData(TurnStatus.Interrupted, "Stopped.")]
    [InlineData(TurnStatus.Failed, "Stopped with an error.")]
    [InlineData(TurnStatus.Running, "")]
    public void The_notification_body_names_how_the_turn_ended_and_its_title_is_the_threads_title(TurnStatus status, string body)
    {
        using var rig = new AppModelNotifyRig(project => [Chat(project)]);
        rig.Configure(chime: false, notify: true);
        var id = rig.Threads[0].Id;

        rig.Finish(id, status);

        var request = Assert.Single(rig.Center.Requests);
        Assert.Equal(Title, request.Title);
        Assert.Equal(body, request.Body);
        Assert.Equal(SwiftUUID.UuidString(id), request.Identifier);
        Assert.Equal(SwiftUUID.UuidString(id), request.UserInfo["threadID"]);
        Assert.Equal(status, rig.Thread(id).LastStatus);
    }

    [Theory]
    [Trait("Req", "UI-RUNTIME-015")] // AppModel.swift:1199-1201, :1210-1215
    [InlineData(TurnStatus.Completed, true, 1, false)]
    [InlineData(TurnStatus.Failed, true, 1, false)]
    [InlineData(TurnStatus.Interrupted, true, 0, true)]
    [InlineData(TurnStatus.Completed, false, 0, true)]
    [InlineData(TurnStatus.Interrupted, false, 0, true)]
    public void The_chime_plays_unless_the_turn_was_stopped_and_a_chime_that_played_leaves_the_banner_silent(
        TurnStatus status, bool chimeOn, int plays, bool bannerSound)
    {
        using var rig = new AppModelNotifyRig(project => [Chat(project)]);
        rig.Configure(chime: chimeOn, notify: true);

        rig.Finish(rig.Threads[0].Id, status);

        Assert.Equal(plays, rig.Sound.Starts);
        Assert.Equal(bannerSound, Assert.Single(rig.Center.Requests).Sound);
    }

    [Fact]
    [Trait("Req", "UI-RUNTIME-015")] // AppModel.swift:1176-1177, :1189-1190, :1199-1201
    public void A_visible_thread_in_the_active_app_chimes_but_shows_no_banner_and_is_left_read()
    {
        using var rig = new AppModelNotifyRig(project => [Chat(project) with { HasUnread = true }], active: true);
        rig.Configure(chime: true, notify: true);
        var id = rig.Threads[0].Id;
        rig.Harness.Main.OnMain(() => rig.Model.SelectedThreadID = id);

        rig.Finish(id, TurnStatus.Completed);

        Assert.Equal(1, rig.Sound.Starts);
        Assert.Empty(rig.Center.Requests);
        Assert.False(rig.Thread(id).HasUnread);
        Assert.Equal(0, rig.DockBadge);
    }

    [Theory]
    [Trait("Req", "UI-RUNTIME-015")] // AppModel.swift:1176-1177, :1189-1190, :1203-1215
    [InlineData(false, true)] // selected, but the app is not frontmost
    [InlineData(true, false)] // the app is frontmost, but another thread is selected
    public void A_thread_that_is_not_in_view_gets_a_banner_and_goes_unread(bool active, bool selected)
    {
        using var rig = new AppModelNotifyRig(project => [Chat(project), Chat(project)], active: active);
        rig.Configure(chime: false, notify: true);
        var id = rig.Threads[0].Id;
        rig.Harness.Main.OnMain(() => rig.Model.SelectedThreadID = selected ? id : rig.Threads[1].Id);

        rig.Finish(id, TurnStatus.Completed);

        Assert.Equal("Finished.", Assert.Single(rig.Center.Requests).Body);
        Assert.True(rig.Thread(id).HasUnread);
        Assert.Equal(1, rig.DockBadge);
    }

    [Fact]
    [Trait("Req", "UI-RUNTIME-015")] // AppModel.swift:1199-1201, :1202
    public void With_notify_when_finished_off_there_is_no_banner_but_the_chime_still_plays()
    {
        using var rig = new AppModelNotifyRig(project => [Chat(project)]);
        rig.Configure(chime: true, notify: false);
        var id = rig.Threads[0].Id;

        rig.Finish(id, TurnStatus.Completed);

        Assert.Equal(1, rig.Sound.Starts);
        Assert.Empty(rig.Center.Requests);
        Assert.True(rig.Thread(id).HasUnread);
    }

    [Theory]
    [Trait("Req", "UI-RUNTIME-015")] // AppModel.swift:1198
    [InlineData(TurnStatus.Completed)]
    [InlineData(TurnStatus.Failed)]
    [InlineData(TurnStatus.Interrupted)]
    public void A_turn_that_continues_with_a_queued_message_neither_chimes_nor_notifies(TurnStatus status)
    {
        using var rig = new AppModelNotifyRig(project => [Chat(project)]);
        rig.Configure(chime: true, notify: true);
        var id = rig.Threads[0].Id;

        rig.Finish(id, status, continues: true);

        Assert.Equal(0, rig.Sound.Starts);
        Assert.Empty(rig.Center.Requests);
        Assert.Equal(status, rig.Thread(id).LastStatus);
        Assert.True(rig.Thread(id).HasUnread);
    }

    [Fact]
    [Trait("Req", "UI-RUNTIME-015")] // AppModel.swift:1181-1185, :1195-1197
    public void A_hydra_head_neither_chimes_nor_notifies_nor_marks_itself_unread()
    {
        var lead = Guid.NewGuid();
        using var rig = new AppModelNotifyRig(project =>
            [AppModelSamples.Thread(project) with { Id = lead }, NativeHead(project, lead)]);
        rig.Configure(chime: true, notify: true);
        var head = rig.Threads[1].Id;

        rig.Finish(head, TurnStatus.Completed);

        Assert.Equal(0, rig.Sound.Starts);
        Assert.Empty(rig.Center.Requests);
        Assert.False(rig.Thread(head).HasUnread);
        Assert.Equal(TurnStatus.Completed, rig.Thread(head).LastStatus);
    }

    [Fact]
    [Trait("Req", "UI-RUNTIME-015")] // AppModel.swift:1189-1194
    public void A_turn_stopped_by_a_spent_usage_limit_that_waits_for_the_reset_makes_no_sound_and_no_banner()
    {
        using var rig = new AppModelNotifyRig(project => [Chat(project)]);
        rig.Configure(chime: true, notify: true);
        var id = rig.Threads[0].Id;
        rig.Harness.Main.OnMain(() =>
        {
            rig.Model.Settings.AutoContinueAfterLimit = true;
            rig.Model.AutoContinue.NoteLimit(id, SwiftDate.Now.AddHours(1));
        });

        rig.Finish(id, TurnStatus.Failed);

        // The wait's own banner (AutoContinue.swift:103-104) is the only one: no chime, no "Stopped with an error.".
        Assert.Equal(0, rig.Sound.Starts);
        var request = Assert.Single(rig.Center.Requests);
        Assert.StartsWith("Usage limit reached. Continuing at ", request.Body, StringComparison.Ordinal);
        Assert.True(rig.Thread(id).HasUnread);
        rig.Harness.Main.OnMain(() => rig.Model.AutoContinue.Cancel(id));
    }
}
