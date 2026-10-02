using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Xunit;

namespace Marrow.App.Tests;

// App/AppModel.swift:1261-1264 updateDockBadge (the count of threads that are unread, not archived and not in a panel), and
// the places that call it: turnFinished :1197, markRead :1230 and :1246, archive/unarchive/settle, delete.
// The model keeps the count the badge text is made from, and these tests read it (ED-119). The taskbar overlay built from
// that count (ED-219) is checked in Port/TaskbarBadgeTests.cs.
public sealed class AppModelDockBadgeTests
{
    [Fact]
    [Trait("Req", "PR-LAUNCH-066")] // AppModel.swift:1197, :1230, :1246, :1270-1273
    [Trait("Req", "UI-LAUNCH-004")] // AppModel.swift:1270-1273
    public void Two_unread_background_chats_count_two_and_reading_both_clears_the_count()
    {
        using var rig = new AppModelNotifyRig(project => [AppModelSamples.Thread(project), AppModelSamples.Thread(project)]);
        var first = rig.Threads[0].Id;
        var second = rig.Threads[1].Id;
        rig.Configure(chime: false, notify: false);

        Assert.Equal(0, rig.DockBadge);

        rig.Finish(first, TurnStatus.Completed);
        Assert.Equal(1, rig.DockBadge);
        rig.Finish(second, TurnStatus.Completed);
        Assert.Equal(2, rig.DockBadge);

        rig.Harness.Main.OnMain(() => rig.Model.MarkRead(first));
        Assert.Equal(1, rig.DockBadge);
        rig.Harness.Main.OnMain(() => rig.Model.MarkRead(new[] { second }));
        Assert.Equal(0, rig.DockBadge);
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-066")] // AppModel.swift:1230-1246
    [Trait("Req", "UI-LAUNCH-004")] // AppModel.swift:1230-1246
    public void Reading_several_threads_at_once_updates_the_count_once_and_ignores_threads_not_asked_for()
    {
        using var rig = new AppModelNotifyRig(project =>
            [Unread(project), Unread(project), Unread(project), AppModelSamples.Thread(project)]);
        rig.Harness.Main.OnMain(() => rig.Model.UpdateDockBadge());
        Assert.Equal(3, rig.DockBadge);

        rig.Harness.Main.OnMain(() => rig.Model.MarkRead(new[] { rig.Threads[0].Id, rig.Threads[3].Id }));

        Assert.Equal(2, rig.DockBadge);
        Assert.False(rig.Thread(rig.Threads[0].Id).HasUnread);
        Assert.True(rig.Thread(rig.Threads[1].Id).HasUnread);
        Assert.True(rig.Thread(rig.Threads[2].Id).HasUnread);
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-066")] // AppModel.swift:1270-1273
    [Trait("Req", "UI-LAUNCH-004")] // AppModel.swift:1270-1273
    public void Only_threads_that_are_unread_and_not_archived_and_not_in_a_panel_are_counted()
    {
        using var rig = new AppModelNotifyRig(project =>
        [
            Unread(project),
            Unread(project),
            Unread(project) with { IsArchived = true },
            Unread(project) with { IsInPanel = true },
            Unread(project) with { IsArchived = true, IsInPanel = true },
            AppModelSamples.Thread(project),
        ]);

        rig.Harness.Main.OnMain(() => rig.Model.UpdateDockBadge());

        Assert.Equal(2, rig.DockBadge);
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-066")] // AppModel.swift:1197, :1270-1273
    [Trait("Req", "UI-LAUNCH-004")] // AppModel.swift:1270-1273
    public void A_finished_archived_thread_or_panel_helper_is_marked_unread_but_never_counted()
    {
        using var rig = new AppModelNotifyRig(project =>
        [
            AppModelSamples.Thread(project),
            AppModelSamples.Thread(project) with { IsArchived = true },
            AppModelSamples.Thread(project) with { IsInPanel = true },
        ]);
        rig.Configure(chime: false, notify: false);

        rig.Finish(rig.Threads[1].Id, TurnStatus.Completed);
        rig.Finish(rig.Threads[2].Id, TurnStatus.Completed);

        Assert.True(rig.Thread(rig.Threads[1].Id).HasUnread);
        Assert.True(rig.Thread(rig.Threads[2].Id).HasUnread);
        Assert.Equal(0, rig.DockBadge);

        rig.Finish(rig.Threads[0].Id, TurnStatus.Completed);
        Assert.Equal(1, rig.DockBadge);
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-066")] // AppModel.swift:780, :789, :812, :955, :1270-1273
    [Trait("Req", "UI-LAUNCH-004")] // AppModel.swift:780, :789, :812, :955, :1270-1273
    public void Archiving_unarchiving_settling_and_deleting_a_thread_move_the_count()
    {
        using var rig = new AppModelNotifyRig(project => [Unread(project), Unread(project), Unread(project)]);
        var first = rig.Threads[0].Id;
        var second = rig.Threads[1].Id;
        var third = rig.Threads[2].Id;
        rig.Harness.Main.OnMain(() => rig.Model.UpdateDockBadge());
        Assert.Equal(3, rig.DockBadge);

        rig.Harness.Main.OnMain(() => rig.Model.Archive(first));
        Assert.Equal(2, rig.DockBadge);

        rig.Harness.Main.OnMain(() => rig.Model.Unarchive(first));
        Assert.Equal(3, rig.DockBadge);

        // Settling a chat reads it (AppModel.swift:803-806).
        rig.Harness.Main.OnMain(() => rig.Model.Settle(second, sounds: false));
        Assert.Equal(2, rig.DockBadge);

        rig.Harness.Main.OnMain(() => rig.Model.Delete(third));
        rig.Harness.Main.Loop.RunUntilIdle();
        Assert.Equal(1, rig.DockBadge);
    }

    private static ChatThread Unread(Project project) => AppModelSamples.Thread(project) with { HasUnread = true };
}
