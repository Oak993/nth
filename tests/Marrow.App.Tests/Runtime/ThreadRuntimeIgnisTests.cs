using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Port.Ignis;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Runtime;

// PORT-ONLY: Ignis (ED-NEW-ignis-runtime; PLAN unit U3). The ThreadRuntime and AppModel hooks: the hidden keep-warm ping
// turn, CacheUsage and usage-limit forwarding, the silent tidy-up. Turns run through the real StartTurn over the offline
// scripted session (Fixtures/ThreadRuntimeScriptedSession.cs); the controller is a recorder; no provider is ever reached.
[Trait("Decision", "OD-0134")]
public sealed class ThreadRuntimeIgnisTests
{
    // The finish chime is off: FinishChime caches its sound on first play, which belongs to the chime tests' collection.
    private static ThreadRuntimeTurnRig Rig() => new(ProviderKind.Claude, settings: app => app.Settings.ChimeWhenFinished = false);

    private static void WaitForSends(ThreadRuntimeTurnRig rig, int count) =>
        rig.H.Main.Loop.RunUntil(() => rig.Session.Inputs.Count >= count, AppModelHarness.Bound);

    /// <summary>One ordinary turn, completed, then the thread marked read: the chat is between tasks.</summary>
    private static void FirstTurn(ThreadRuntimeTurnRig rig)
    {
        rig.Send("Fix the parser");
        rig.Complete();
        rig.H.Main.OnMain(() => rig.App.UpdateThread(rig.InitialThread.Id, thread => thread with { HasUnread = false }));
    }

    private static IIgnisRuntimePort Port(ThreadRuntimeTurnRig rig) => rig.Runtime;

    private static bool IsNotice(TimelineEntry entry, string message) =>
        entry.Item.TimelineItemContent is TimelineItem.Content.Notice notice && notice.Value.Message == message;

    [Fact]
    public void A_hidden_ping_is_a_notice_turn_that_ends_silently_and_leaves_the_chat_where_it_was()
    {
        using var rig = Rig();
        FirstTurn(rig);
        var before = rig.Thread;
        Assert.True(rig.H.Main.OnMain(() => Port(rig).IsBetweenTasks));

        Assert.True(rig.H.Main.OnMain(() => Port(rig).SendHiddenPing()));
        WaitForSends(rig, 2);

        Assert.Equal(IgnisWords.PingText, rig.Session.Inputs[1].Text);
        var turn = rig.H.Main.OnMain(() => rig.Runtime.Turns[^1]);
        Assert.Equal(IgnisEvent.PingPrefix + turn.Id.ToString("D"), turn.UserItemID);
        Assert.Null(turn.BaseCheckpoint);
        var item = rig.H.Main.OnMain(() => rig.Runtime.Entries.Single(entry => entry.Id == turn.UserItemID).Item);
        Assert.True(IgnisEvent.IsPingItem(item.Id));
        Assert.Equal(IgnisWords.PingText, Assert.IsType<TimelineItem.Content.Notice>(item.TimelineItemContent).Value.Message);
        Assert.DoesNotContain(rig.H.Main.OnMain(() => rig.Runtime.Entries.ToArray()), entry =>
            entry.Item.TimelineItemContent is TimelineItem.Content.User user && user.Value.Text == IgnisWords.PingText);
        Assert.True(rig.H.Main.OnMain(() => rig.Runtime.IgnisIsPingTurn(turn.Id)));

        rig.Complete();
        var after = rig.Thread;
        Assert.False(after.HasUnread); // an ordinary turn ending off screen marks the chat unread
        Assert.Equal(TurnStatus.Completed, after.LastStatus);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt); // the sidebar order does not move
        Assert.Equal(before.Title, after.Title);
        Assert.Equal(2, rig.Session.Inputs.Count);
    }

    [Fact]
    public void An_ordinary_turn_ending_off_screen_still_marks_the_chat_unread()
    {
        using var rig = Rig();
        FirstTurn(rig);
        rig.Send("Now the lexer");
        rig.Complete();
        Assert.True(rig.Thread.HasUnread);
    }

    [Fact]
    public void A_follow_up_typed_during_a_ping_is_sent_as_an_ordinary_turn_when_the_ping_ends()
    {
        using var rig = Rig();
        FirstTurn(rig);
        rig.H.Main.OnMain(() => Port(rig).SendHiddenPing());
        WaitForSends(rig, 2);

        rig.Finish(rig.StartTurn("Now the lexer"));
        Assert.Equal("Now the lexer", rig.H.Main.OnMain(() => Assert.Single(rig.Runtime.FollowUps).Text));
        Assert.False(rig.H.Main.OnMain(() => Port(rig).IsBetweenTasks));
        Assert.False(rig.H.Main.OnMain(() => Port(rig).SendHiddenPing()));

        rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null)));
        WaitForSends(rig, 3);

        Assert.Contains("Now the lexer", rig.Session.Inputs[2].Text, StringComparison.Ordinal);
        var turn = rig.H.Main.OnMain(() => rig.Runtime.Turns[^1]);
        Assert.False(IgnisEvent.IsPingItem(turn.UserItemID));
        Assert.Contains(rig.H.Main.OnMain(() => rig.Runtime.Entries.ToArray()), entry =>
            entry.Item.TimelineItemContent is TimelineItem.Content.User user && user.Value.Text == "Now the lexer");
        rig.Complete();
        Assert.True(rig.Thread.HasUnread); // the user's own turn is heard as usual
    }

    [Fact]
    public void A_chat_with_a_queued_follow_up_or_a_running_turn_is_not_between_tasks()
    {
        using var rig = Rig();
        rig.Session.SendScript = _ => new TaskCompletionSource().Task;
        _ = rig.StartTurn("Fix the parser");
        WaitForSends(rig, 1);
        Assert.False(rig.H.Main.OnMain(() => Port(rig).IsBetweenTasks));
        Assert.False(rig.H.Main.OnMain(() => Port(rig).SendHiddenPing()));
        Assert.False(rig.H.Main.OnMain(() => Port(rig).Tidy(12)));
        Assert.Single(rig.Session.Inputs);
    }

    [Fact]
    public void Cache_usage_turn_ends_and_usage_limits_reach_the_controller()
    {
        var controller = new RecordingIgnisController();
        using var bound = IgnisSeams.Controller.Override(() => controller);
        using var rig = Rig();
        var usage = IgnisSamples.Usage(read: 40_000, write1h: 2_000);
        var resets = new DateTimeOffset(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);

        _ = rig.StartTurn("Fix the parser");
        WaitForSends(rig, 1);
        rig.H.Main.OnMain(() =>
        {
            rig.Session.Emit(new ProviderEvent.CacheUsage(usage));
            rig.Session.Emit(new ProviderEvent.UsageLimit(resets));
        });
        rig.Complete();

        var id = rig.InitialThread.Id;
        Assert.Equal([(id, usage)], controller.Observed);
        Assert.Equal([(id, (DateTimeOffset?)resets)], controller.Limits);
        Assert.Contains(id, controller.Finished);
        Assert.Same(rig.Runtime, Assert.Single(controller.Attached));
        Assert.NotNull(rig.H.Main.OnMain(() => Port(rig).LastRequestAt));
    }

    [Fact]
    public void A_paid_credits_hint_reaches_the_controller_and_the_port_knows_when_the_turn_started()
    {
        var controller = new RecordingIgnisController();
        using var bound = IgnisSeams.Controller.Override(() => controller);
        using var rig = Rig();

        _ = rig.StartTurn("Fix the parser");
        WaitForSends(rig, 1);
        var started = rig.H.Main.OnMain(() => Port(rig).TurnStartedAt);
        rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.CacheBilling(true)));
        rig.Complete();

        Assert.Equal([(rig.InitialThread.Id, true)], controller.Credits);
        Assert.NotNull(started); // ED-NEW-ignis-runtime: the gap Ignis learns ends where the turn started
    }

    [Fact]
    public void A_tidy_up_compacts_silently_and_its_compacted_notice_ends_it()
    {
        var controller = new RecordingIgnisController();
        using var bound = IgnisSeams.Controller.Override(() => controller);
        using var rig = Rig();
        FirstTurn(rig);
        var compacts = 0;
        rig.Session.CompactScript = () =>
        {
            compacts++;
            rig.Session.Emit(new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Info, Message = "Context compacted." }));
            return Task.CompletedTask;
        };
        controller.Finished.Clear();

        Assert.True(rig.H.Main.OnMain(() => Port(rig).Tidy(12)));
        rig.H.Main.RunUntilIdle();

        Assert.Equal(1, compacts);
        var entries = rig.H.Main.OnMain(() => rig.Runtime.Entries.ToArray());
        Assert.DoesNotContain(entries, entry => IsNotice(entry, "Compacting context."));
        Assert.DoesNotContain(entries, entry => IsNotice(entry, "Context compacted."));
        Assert.False(rig.H.Main.OnMain(() => ((IIgnisRuntimeTimeline)rig.Runtime).IsTidying));
        Assert.Equal([rig.InitialThread.Id], controller.Finished);
        Assert.True(rig.H.Main.OnMain(() => Port(rig).IsBetweenTasks));
    }

    [Fact]
    public void A_tidy_up_whose_provider_never_says_compacted_ends_after_the_wait()
    {
        var controller = new RecordingIgnisController();
        using var bound = IgnisSeams.Controller.Override(() => controller);
        using var rig = Rig();
        FirstTurn(rig);
        controller.Finished.Clear();

        Assert.True(rig.H.Main.OnMain(() => Port(rig).Tidy(12)));
        rig.H.Main.RunUntilIdle();
        Assert.True(rig.H.Main.OnMain(() => ((IIgnisRuntimeTimeline)rig.Runtime).IsTidying));
        Assert.False(rig.H.Main.OnMain(() => Port(rig).IsBetweenTasks));
        Assert.Empty(controller.Finished);

        rig.H.Main.Advance(ThreadRuntime.IgnisTidyNoticeWait);
        Assert.False(rig.H.Main.OnMain(() => ((IIgnisRuntimeTimeline)rig.Runtime).IsTidying));
        Assert.Equal([rig.InitialThread.Id], controller.Finished);
    }

    [Fact]
    public void The_users_own_compact_still_says_so()
    {
        using var rig = Rig();
        FirstTurn(rig);
        rig.Session.CompactScript = () =>
        {
            rig.Session.Emit(new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Info, Message = "Context compacted." }));
            return Task.CompletedTask;
        };
        rig.H.Main.OnMain(() => rig.Runtime.Compact());
        rig.H.Main.RunUntilIdle();
        var entries = rig.H.Main.OnMain(() => rig.Runtime.Entries.ToArray());
        Assert.Contains(entries, entry => IsNotice(entry, "Compacting context."));
        Assert.Contains(entries, entry => IsNotice(entry, "Context compacted."));
    }

    [Fact]
    public void An_event_line_is_a_notice_with_the_events_item_id_appended_once()
    {
        using var rig = Rig();
        FirstTurn(rig);
        var @event = new IgnisEvent("e1", rig.InitialThread.Id, IgnisEventKind.WentCold, SwiftDate.Now, 0.21m, IgnisAmountKind.Lost);
        rig.H.Main.OnMain(() =>
        {
            ((IIgnisRuntimeTimeline)rig.Runtime).AppendEvent(@event);
            ((IIgnisRuntimeTimeline)rig.Runtime).AppendEvent(@event);
        });
        var lines = rig.H.Main.OnMain(() => rig.Runtime.Entries.Where(entry => entry.Id == "ignis:event:e1").ToArray());
        var line = Assert.Single(lines);
        Assert.Equal(IgnisWords.EventLine(@event), Assert.IsType<TimelineItem.Content.Notice>(line.Item.TimelineItemContent).Value.Message);
    }
}
