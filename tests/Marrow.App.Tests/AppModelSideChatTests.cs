using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Services.Providers;
using Marrow.Services.Store;

namespace Marrow.App.Tests;

// PORT-ONLY: web surfaces W6 (DESIGN.md 3.9 and 14 W6): the side chat is a thread of its own under the main thread, listed nowhere, read-only
// until the person turns Can act on, saved with the library, and gone with the main thread. The runtime carries its own provider session
// (see SideChatTurnTests for what a turn is sent with).
[Trait("Decision", "ED-NEW-web-w6-1")]
public sealed class AppModelSideChatTests
{
    [Fact]
    public void The_side_chat_is_made_once_from_the_main_thread_and_is_read_only()
    {
        using var rig = new SideChatRig(configure: thread => thread with { Model = "fast-model", Effort = "high", Branch = "feature/x", WorktreePath = "C:/work/x", RuntimeMode = RuntimeMode.FullAccess });
        Assert.Null(rig.Side);
        var made = rig.MakeSide();
        Assert.Equal(made.Id, rig.MakeSide().Id);
        Assert.Equal(made.Id, rig.Side!.Id);
        Assert.True(made.IsSideChat);
        Assert.True(made.IsInPanel);
        Assert.Equal(rig.ParentId, made.ParentThreadID);
        Assert.Equal(AppModel.SideChatTitle, made.Title);
        Assert.True(made.HasCustomTitle);
        Assert.Equal((ProviderKind.Claude, "fast-model", "high"), (made.Provider, made.Model, made.Effort));
        Assert.Equal(("feature/x", "C:/work/x"), (made.Branch, made.WorktreePath));
        Assert.Equal(InteractionMode.Plan, made.InteractionMode);
        Assert.Equal(RuntimeMode.Supervised, made.RuntimeMode);
        Assert.False(made.HydraEnabled);
        Assert.False(rig.H.Main.OnMain(() => rig.App.SideChatCanAct(rig.ParentId)));
    }

    [Fact]
    public void A_thread_that_cannot_have_a_side_chat_gets_none()
    {
        using var rig = new SideChatRig();
        Assert.Null(rig.H.Main.OnMain(() => rig.App.EnsureSideChat(Guid.NewGuid())));
        var side = rig.MakeSide();
        Assert.Null(rig.H.Main.OnMain(() => rig.App.EnsureSideChat(side.Id)));
        Assert.Null(rig.H.Main.OnMain(() => rig.App.SideChat(side.Id)));
    }

    [Fact]
    public void The_side_chat_is_listed_nowhere_and_is_not_the_subagent()
    {
        using var rig = new SideChatRig();
        var side = rig.MakeSide();
        rig.H.Main.OnMain(() =>
        {
            Assert.Empty(rig.App.Helpers(rig.ParentId));
            Assert.Null(rig.App.PanelSubagent(rig.ParentId));
            Assert.Null(rig.App.Subagent(rig.ParentId));
            Assert.Empty(rig.App.PanelHeads(rig.ParentId));
            Assert.DoesNotContain(rig.App.SidebarThreads, thread => thread.Id == side.Id);
            Assert.Contains(rig.App.Threads, thread => thread.Id == side.Id);
        });
    }

    [Fact]
    public void Can_act_gives_the_main_threads_run_mode_and_off_takes_it_back()
    {
        using var rig = new SideChatRig(configure: thread => thread with { RuntimeMode = RuntimeMode.FullAccess });
        rig.H.Main.OnMain(() => rig.App.SetSideChatCanAct(rig.ParentId, true));
        Assert.True(rig.H.Main.OnMain(() => rig.App.SideChatCanAct(rig.ParentId)));
        Assert.Equal((InteractionMode.Build, RuntimeMode.FullAccess), (rig.Side!.InteractionMode, rig.Side.RuntimeMode));

        rig.H.Main.OnMain(() => rig.App.SetSideChatCanAct(rig.ParentId, false));
        Assert.False(rig.H.Main.OnMain(() => rig.App.SideChatCanAct(rig.ParentId)));
        Assert.Equal((InteractionMode.Plan, RuntimeMode.Supervised), (rig.Side!.InteractionMode, rig.Side.RuntimeMode));
    }

    [Fact]
    public void Can_act_follows_the_main_threads_run_mode_at_each_send()
    {
        using var rig = new SideChatRig();
        rig.H.Main.OnMain(() => rig.App.SetSideChatCanAct(rig.ParentId, true));
        Assert.Equal(RuntimeMode.Supervised, rig.Side!.RuntimeMode);
        rig.H.Main.OnMain(() => rig.App.UpdateThread(rig.ParentId, thread => thread with { RuntimeMode = RuntimeMode.AutoAcceptEdits }));
        Assert.True(rig.Ask("make the change"));
        var session = rig.Launch(1);
        Assert.Equal((InteractionMode.Build, RuntimeMode.AutoAcceptEdits), (session.Inputs[0].InteractionMode, session.Inputs[0].RuntimeMode));
        Assert.DoesNotContain("read-only tools", session.Inputs[0].Text, StringComparison.Ordinal);
        Assert.Contains("You may act with this thread's tools", session.Inputs[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Turning_Can_act_off_stops_a_turn_that_was_acting()
    {
        using var rig = new SideChatRig(script: session => session.SendScript = _ => new TaskCompletionSource().Task);
        rig.H.Main.OnMain(() => rig.App.SetSideChatCanAct(rig.ParentId, true));
        Assert.True(rig.Ask("fix the css"));
        var session = rig.Launch(1);
        Assert.Equal(InteractionMode.Build, session.Inputs[0].InteractionMode);
        Assert.True(rig.SideRuntime.IsRunning);

        rig.H.Main.OnMain(() => rig.App.SetSideChatCanAct(rig.ParentId, false));
        rig.Until(() => session.InterruptCalls >= 1);
        Assert.Equal(InteractionMode.Plan, rig.Side!.InteractionMode);
    }

    [Fact]
    public void Closing_the_pane_ends_Can_act_and_stops_an_acting_turn_but_not_a_read_only_one()
    {
        using var acting = new SideChatRig(script: session => session.SendScript = _ => new TaskCompletionSource().Task);
        acting.H.Main.OnMain(() => acting.App.SetSideChatCanAct(acting.ParentId, true));
        Assert.True(acting.Ask("rename it"));
        var running = acting.Launch(1);
        acting.H.Main.OnMain(() => acting.Main.Runtime.Workspace = acting.Main.Runtime.Workspace.Open(PaneKind.SideChat));
        acting.H.Main.OnMain(() => acting.Main.Runtime.Workspace = acting.Main.Runtime.Workspace.Close(PaneKind.SideChat));
        acting.Until(() => running.InterruptCalls >= 1);
        Assert.False(acting.H.Main.OnMain(() => acting.App.SideChatCanAct(acting.ParentId)));

        using var reading = new SideChatRig(script: session => session.SendScript = _ => new TaskCompletionSource().Task);
        Assert.True(reading.Ask("what is it doing"));
        var quiet = reading.Launch(1);
        reading.H.Main.OnMain(() => reading.Main.Runtime.Workspace = reading.Main.Runtime.Workspace.Open(PaneKind.SideChat));
        reading.H.Main.OnMain(() => reading.Main.Runtime.Workspace = reading.Main.Runtime.Workspace.Close(PaneKind.SideChat));
        reading.H.Main.RunUntilIdle();
        Assert.Equal(0, quiet.InterruptCalls);
        Assert.True(reading.SideRuntime.IsRunning);
    }

    [Fact]
    public void Clear_deletes_the_conversation_and_a_new_empty_side_chat_takes_its_place_keeping_Can_act()
    {
        using var rig = new SideChatRig();
        rig.H.Main.OnMain(() => rig.App.SetSideChatCanAct(rig.ParentId, true));
        Assert.True(rig.Ask("first"));
        rig.Answer(rig.Launch(1), "an answer");
        var before = rig.Side!;
        Assert.NotEmpty(rig.SideRuntime.Entries);

        rig.H.Main.OnMain(() => rig.App.ClearSideChat(rig.ParentId));
        var after = rig.Side!;
        Assert.NotEqual(before.Id, after.Id);
        Assert.Empty(rig.SideRuntime.Entries);
        Assert.True(rig.H.Main.OnMain(() => rig.App.SideChatCanAct(rig.ParentId)));
        Assert.Null(rig.H.Main.OnMain(() => rig.App.Thread(before.Id)));
        Assert.Single(rig.H.Main.OnMain(() => rig.App.Threads.Where(thread => thread.IsSideChat).ToList()));
    }

    [Fact]
    public void Send_to_main_puts_the_answer_in_the_main_composer_after_what_is_typed_and_sends_nothing()
    {
        using var rig = new SideChatRig();
        var inputs = rig.Main.Session.Inputs.Count;
        Assert.True(rig.H.Main.OnMain(() => rig.App.SendSideChatToMain(rig.ParentId, "  The console shows one 404.  ")));
        Assert.Equal("The console shows one 404.", rig.Main.Runtime.Draft.Text);

        rig.H.Main.OnMain(() => rig.Main.Runtime.Draft = rig.Main.Runtime.Draft with { Text = "Please look at this:   " });
        Assert.True(rig.H.Main.OnMain(() => rig.App.SendSideChatToMain(rig.ParentId, "Second answer")));
        Assert.Equal("Please look at this:\n\nSecond answer", rig.Main.Runtime.Draft.Text);

        Assert.False(rig.H.Main.OnMain(() => rig.App.SendSideChatToMain(rig.ParentId, "   ")));
        Assert.False(rig.H.Main.OnMain(() => rig.App.SendSideChatToMain(Guid.NewGuid(), "x")));
        rig.H.Main.RunUntilIdle();
        Assert.Equal(inputs, rig.Main.Session.Inputs.Count);
        Assert.Equal(RuntimePhase.Idle, rig.Main.Runtime.Phase);
    }

    [Fact]
    public void The_side_chat_is_saved_with_the_library_and_comes_back_as_a_side_chat()
    {
        using var rig = new SideChatRig();
        Assert.True(rig.Ask("what changed?"));
        rig.Answer(rig.Launch(1), "Two files.");
        var id = rig.Side!.Id;
        rig.SideRuntime.SaveNow();
        rig.H.Main.Advance(TimeSpan.FromSeconds(6));
        DiskWriter.Shared.WaitForQueuedWrites();

        var saved = AppModelHarness.LoadedLibrary().Threads.Single(thread => thread.Id == id);
        Assert.True(saved.IsSideChat);
        Assert.True(saved.IsInPanel);
        Assert.Equal(rig.ParentId, saved.ParentThreadID);
        var document = Storage.ReadDocument(id)!.Value.Document;
        Assert.Equal(["what changed?"], document.Items.Select(item => item.TimelineItemContent).OfType<TimelineItem.Content.User>().Select(user => user.Value.Text));
        Assert.Contains(document.Items, item => item.TimelineItemContent is TimelineItem.Content.Assistant { Value.Text: "Two files." });
    }

    [Fact]
    public void Archiving_the_main_thread_stops_its_side_chat_and_deleting_it_deletes_the_side_chat()
    {
        using var rig = new SideChatRig(script: session => session.SendScript = _ => new TaskCompletionSource().Task);
        Assert.True(rig.Ask("slow question"));
        var session = rig.Launch(1);
        var id = rig.Side!.Id;
        rig.H.Main.OnMain(() => rig.App.Archive(rig.ParentId));
        rig.Until(() => session.InterruptCalls >= 1 || session.StopCalls >= 1);

        rig.H.Main.OnMain(() => rig.App.Delete(rig.ParentId));
        Assert.Null(rig.H.Main.OnMain(() => rig.App.Thread(id)));
        Assert.DoesNotContain(rig.H.Main.OnMain(() => rig.App.Threads.ToList()), thread => thread.IsSideChat);
    }
}
