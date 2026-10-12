using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests;

// PORT-ONLY: web surfaces W6 (DESIGN.md 3.9, 12 "Side chat turn start: no effect on the main run's timing (separate runtime)", 13.2 "Side
// chat seeding: snapshot content and limits; never writes to the parent"). Turns run through the real StartTurn on a scripted session: what
// a turn is sent with, the fresh session each turn takes, that the main run is not touched, and what a read-only side chat refuses.
[Trait("Decision", "ED-NEW-web-w6-1")]
public sealed class SideChatTurnTests
{
    private static ApprovalRequest Request(string id, ApprovalRequest.Kind kind) => new(id, kind, "Run npm test",
        new ValueList<ApprovalRequest.Option>([
            new ApprovalRequest.Option("allow", "Allow", ApprovalRequest.Option.Role.Approve),
            new ApprovalRequest.Option("deny", "Deny", ApprovalRequest.Option.Role.Decline),
        ]));

    [Fact]
    public void A_turn_is_sent_with_the_rules_the_snapshot_of_the_main_thread_and_the_question_last()
    {
        using var rig = new SideChatRig(configure: thread => thread with { Model = "fast-model", Effort = "low" });
        rig.MainTurn("Fix the hero section", "I edited Hero.css and am waiting for the preview.");
        var mainInputs = rig.Main.Session.Inputs.Count;

        Assert.True(rig.Ask("What did it just do?"));
        var session = rig.Launch(1);
        var input = Assert.Single(session.Inputs);
        Assert.StartsWith(SideChatPrompt.Marker + "\n", input.Text, StringComparison.Ordinal);
        Assert.Contains("Main thread: ", input.Text, StringComparison.Ordinal);
        Assert.Contains("Fix the hero section", input.Text, StringComparison.Ordinal);
        Assert.Contains("I edited Hero.css and am waiting for the preview.", input.Text, StringComparison.Ordinal);
        Assert.Contains("Main run: idle, waiting for the person's next message.", input.Text, StringComparison.Ordinal);
        Assert.Contains("(snapshot 1)", input.Text, StringComparison.Ordinal);
        Assert.EndsWith("The user's question:\nWhat did it just do?", input.Text, StringComparison.Ordinal);
        Assert.Equal((InteractionMode.Plan, RuntimeMode.Supervised, "fast-model", "low"), (input.InteractionMode, input.RuntimeMode, input.Model, input.Effort));
        Assert.Contains("read-only tools", input.Text, StringComparison.Ordinal);
        Assert.Equal(mainInputs, rig.Main.Session.Inputs.Count); // the main run was sent nothing
    }

    [Fact]
    public void The_person_sees_their_own_words_in_the_side_chat_not_the_seed()
    {
        using var rig = new SideChatRig();
        Assert.True(rig.Ask("Is anything failing?"));
        rig.Answer(rig.Launch(1), "One 404.");
        var texts = rig.SideRuntime.Entries.Select(entry => entry.Item.TimelineItemContent).ToList();
        Assert.Equal(["Is anything failing?"], texts.OfType<TimelineItem.Content.User>().Select(user => user.Value.Text));
        Assert.Equal(["One 404."], texts.OfType<TimelineItem.Content.Assistant>().Select(reply => reply.Value.Text));
    }

    [Fact]
    public void Every_turn_starts_in_a_fresh_session_that_carries_the_side_chat_so_far_and_the_state_of_that_moment()
    {
        using var rig = new SideChatRig();
        rig.MainTurn("First job", "First job done.");
        Assert.True(rig.Ask("What is done?"));
        var first = rig.Launch(1);
        rig.Answer(first, "The first job.");

        rig.MainTurn("Second job", "Second job done.");
        Assert.True(rig.Ask("And now?"));
        var second = rig.Launch(2);

        Assert.Equal(2, rig.Launches.Count);
        Assert.NotSame(first, second);
        Assert.True(first.StopCalls >= 1, "the first turn's session was let go");
        Assert.Null(rig.Launches[1].Configuration.ResumeID);
        var text = second.Inputs[0].Text;
        Assert.Contains("(snapshot 2)", text, StringComparison.Ordinal);
        Assert.Contains("Second job done.", text, StringComparison.Ordinal);
        Assert.Contains("<side_chat_so_far>\nUser: What is done?\nYou: The first job.\n", text, StringComparison.Ordinal);
        Assert.EndsWith("The user's question:\nAnd now?", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_main_run_is_not_blocked_waited_for_or_sent_anything_by_the_side_chat()
    {
        using var rig = new SideChatRig();
        rig.Main.Session.SendScript = _ => new TaskCompletionSource().Task;
        _ = rig.Main.StartTurn("A long job");
        rig.Until(() => rig.Main.Session.Inputs.Count >= 1);
        Assert.Equal(RuntimePhase.Running, rig.Main.Runtime.Phase);

        Assert.True(rig.Ask("How far along is it?"));
        var session = rig.Launch(1);
        Assert.Contains("Main run: working", session.Inputs[0].Text, StringComparison.Ordinal);
        Assert.Contains("A long job", session.Inputs[0].Text, StringComparison.Ordinal);
        rig.Answer(session, "It has just started.");

        Assert.Equal(RuntimePhase.Running, rig.Main.Runtime.Phase);
        Assert.Single(rig.Main.Session.Inputs);
        Assert.Equal(0, rig.Main.Session.InterruptCalls);
        Assert.Equal(0, rig.Main.Session.StopCalls);
    }

    [Fact]
    public void The_side_chat_writes_nothing_to_the_main_thread()
    {
        using var rig = new SideChatRig();
        rig.MainTurn("Do the thing", "Done.");
        var thread = rig.MainThread;
        var entries = rig.H.Main.OnMain(() => rig.Main.Runtime.Entries.Select(entry => entry.Item).ToList());
        var turns = rig.H.Main.OnMain(() => rig.Main.Runtime.Turns.ToList());
        var draft = rig.H.Main.OnMain(() => rig.Main.Runtime.Draft);
        var workspace = rig.H.Main.OnMain(() => rig.Main.Runtime.Workspace);

        Assert.True(rig.Ask("Summarise it"));
        rig.Answer(rig.Launch(1), "It did the thing.");
        Assert.True(rig.Ask("Again"));
        rig.Answer(rig.Launch(2), "Yes.");

        Assert.Equal(thread, rig.MainThread);
        Assert.Equal(entries, rig.H.Main.OnMain(() => rig.Main.Runtime.Entries.Select(entry => entry.Item).ToList()));
        Assert.Equal(turns, rig.H.Main.OnMain(() => rig.Main.Runtime.Turns.ToList()));
        Assert.Equal(draft, rig.H.Main.OnMain(() => rig.Main.Runtime.Draft));
        Assert.Equal(workspace, rig.H.Main.OnMain(() => rig.Main.Runtime.Workspace));
    }

    [Fact]
    public void A_question_while_an_answer_is_running_is_not_taken_and_Stop_ends_only_the_side_chat()
    {
        using var rig = new SideChatRig(script: session =>
        {
            session.SendScript = _ => new TaskCompletionSource().Task;
            session.InterruptScript = () =>
            {
                session.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Interrupted, null));
                return Task.CompletedTask;
            };
        });
        rig.Main.Session.SendScript = _ => new TaskCompletionSource().Task;
        _ = rig.Main.StartTurn("Main work");
        rig.Until(() => rig.Main.Session.Inputs.Count >= 1);

        Assert.True(rig.Ask("First"));
        var session = rig.Launch(1);
        Assert.False(rig.Ask("Second, too soon"));

        rig.H.Main.OnMain(() => rig.App.StopSideChat(rig.ParentId));
        rig.Until(() => rig.SideRuntime.Phase == RuntimePhase.Idle);
        Assert.Equal(1, session.InterruptCalls);
        Assert.Equal(RuntimePhase.Running, rig.Main.Runtime.Phase);
        Assert.Equal(0, rig.Main.Session.InterruptCalls);
    }

    [Fact]
    public void A_side_chat_message_that_looks_like_a_command_is_a_question()
    {
        using var rig = new SideChatRig();
        Assert.True(rig.Ask("/plan"));
        var session = rig.Launch(1);
        Assert.EndsWith("The user's question:\n/plan", session.Inputs[0].Text, StringComparison.Ordinal);
        Assert.Null(session.Inputs[0].Command);
        Assert.Equal(InteractionMode.Plan, rig.Side!.InteractionMode);
        Assert.Equal(InteractionMode.Build, rig.MainThread.InteractionMode);
    }

    [Fact]
    public void A_read_only_side_chat_declines_a_command_a_file_change_and_a_plan_and_says_so()
    {
        using var rig = new SideChatRig(script: session => session.SendScript = _ => new TaskCompletionSource().Task);
        Assert.True(rig.Ask("Check the tests"));
        var session = rig.Launch(1);
        rig.H.Main.OnMain(() =>
        {
            session.Emit(new ProviderEvent.Approval(Request("a1", ApprovalRequest.Kind.Command)));
            session.Emit(new ProviderEvent.Approval(Request("a2", ApprovalRequest.Kind.FileChange)));
            session.Emit(new ProviderEvent.Approval(Request("a3", ApprovalRequest.Kind.Plan)));
        });

        Assert.Equal([("a1", "deny"), ("a2", "deny"), ("a3", "deny")], session.ApprovalAnswers.Select(answer => (answer.Id, answer.Option)));
        Assert.Empty(rig.H.Main.OnMain(() => rig.SideRuntime.Approvals));
        var notices = rig.H.Main.OnMain(() => rig.SideRuntime.Entries.Select(entry => entry.Item.TimelineItemContent).OfType<TimelineItem.Content.Notice>().Select(notice => notice.Value.Message).ToList());
        Assert.Contains("The side chat is read-only, so it declined to run a command. Turn on Can act to let it.", notices);
        Assert.Contains("The side chat is read-only, so it declined to change a file. Turn on Can act to let it.", notices);
        Assert.Contains("The side chat is read-only, so it declined to start building a plan. Turn on Can act to let it.", notices);
    }

    [Fact]
    public void A_read_only_side_chat_leaves_a_tool_request_to_the_person_and_Can_act_leaves_every_request_to_the_person()
    {
        using var rig = new SideChatRig(script: session => session.SendScript = _ => new TaskCompletionSource().Task);
        Assert.True(rig.Ask("Look it up"));
        var read = rig.Launch(1);
        rig.H.Main.OnMain(() => read.Emit(new ProviderEvent.Approval(Request("web", ApprovalRequest.Kind.Tool))));
        Assert.Empty(read.ApprovalAnswers);
        Assert.Equal(["web"], rig.H.Main.OnMain(() => rig.SideRuntime.Approvals.Select(request => request.Id).ToList()));

        using var acting = new SideChatRig(script: session => session.SendScript = _ => new TaskCompletionSource().Task);
        acting.H.Main.OnMain(() => acting.App.SetSideChatCanAct(acting.ParentId, true));
        Assert.True(acting.Ask("Fix it"));
        var act = acting.Launch(1);
        acting.H.Main.OnMain(() => act.Emit(new ProviderEvent.Approval(Request("cmd", ApprovalRequest.Kind.Command))));
        Assert.Empty(act.ApprovalAnswers);
        Assert.Equal(["cmd"], acting.H.Main.OnMain(() => acting.SideRuntime.Approvals.Select(request => request.Id).ToList()));
    }

    [Fact]
    public void A_finished_turn_ends_quietly_the_session_is_let_go_and_the_main_thread_gains_no_unread_mark()
    {
        using var rig = new SideChatRig();
        var before = rig.MainThread;
        Assert.True(rig.Ask("Quick one"));
        var session = rig.Launch(1);
        rig.Answer(session, "Quick answer.");
        rig.Until(() => session.StopCalls >= 1);

        var side = rig.Side!;
        Assert.False(side.HasUnread);
        Assert.Equal(TurnStatus.Completed, side.LastStatus);
        Assert.Equal(before, rig.MainThread);
    }

    [Fact]
    public void A_read_only_turn_takes_no_checkpoint_and_the_side_chat_title_is_never_rewritten()
    {
        using var rig = new SideChatRig();
        Assert.True(rig.Ask("Name yourself after this long question about the stylesheet"));
        var session = rig.Launch(1);
        rig.Answer(session, "Fine.");
        Assert.Equal(AppModel.SideChatTitle, rig.Side!.Title);
        Assert.All(rig.SideRuntime.Turns, turn => Assert.Null(turn.BaseCheckpoint));
    }
}
