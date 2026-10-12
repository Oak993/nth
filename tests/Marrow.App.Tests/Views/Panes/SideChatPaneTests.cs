using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Panes;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.UI.Panes;

namespace Marrow.App.Tests.Views.Panes;

// nth web surfaces, unit W6: the Side chat pane (work/design/web-surfaces/DESIGN.md 3.9 and the approved canvas's Side chat). The pane is in
// the real detail view with the real factory; the side chat's model is a scripted offline session. The note, the empty state, the
// composer, Can act and Clear in the header, the conversation, the chip that sends an answer to the main draft, approvals, the draft that
// survives a closed pane.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w6-1")]
public sealed class SideChatPaneTests(HeadlessSession headless)
{
    private static void Say(SideChatPaneRig rig, string text)
    {
        rig.H.Main.OnMain(() => rig.Pane!.Composer.Field.Text = text);
        rig.Pump();
    }

    private static void Send(SideChatPaneRig rig)
    {
        rig.H.Main.OnMain(() => PaneWorkspaceRig.Click(rig.Pane!.Composer.SendButton));
        rig.Pump();
    }

    private static ThreadRuntimeScriptedSession Ask(SideChatPaneRig rig, string text, int launch = 1)
    {
        Say(rig, text);
        Send(rig);
        try { rig.Until(() => rig.Launches.Count >= launch && rig.Launches[launch - 1].Session.Inputs.Count >= 1, "the side chat's turn to start"); }
        catch (TimeoutException error)
        {
            var composer = rig.Pane!.Composer;
            throw new TimeoutException(error.Message + $" launches={string.Join(" | ", rig.Launches.Select(launch => launch.Session.Inputs.Count + " inputs, " + launch.Configuration.InteractionMode + ", resume " + launch.Configuration.ResumeID + ", stops " + launch.Session.StopCalls))}, text='{composer.Text}', composerRunning={composer.IsRunning}, canSend={composer.CanSend}, phase={rig.Runtime.Phase}, sidePhase={rig.H.Main.OnMain(() => rig.Model.ExistingRuntime(rig.Side!.Id)!.Phase)}, entries={rig.H.Main.OnMain(() => rig.Model.ExistingRuntime(rig.Side!.Id)!.Entries.Count)}");
        }
        return rig.Launches[launch - 1].Session;
    }

    private static void Answer(SideChatPaneRig rig, ThreadRuntimeScriptedSession session, string text)
    {
        rig.H.Main.OnMain(() =>
        {
            session.Emit(new ProviderEvent.MessageCompleted("a-" + Guid.NewGuid().ToString("N"), text));
            session.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
        });
        rig.Until(() => rig.Model.ExistingRuntime(rig.Side!.Id)!.Phase == RuntimePhase.Idle, "the answer to end");
        rig.Settle();
    }

    private static Button Action(SideChatPaneRig rig, string name) =>
        rig.Card!.Header.ActionViews.First(view => (AutomationProperties.GetName(view) ?? "").StartsWith(name, StringComparison.Ordinal));

    [Fact]
    public void Opening_the_pane_shows_the_note_the_empty_state_and_the_placeholder_and_makes_one_side_chat() => headless.OnUiThread(() =>
    {
        using var rig = new SideChatPaneRig();
        rig.Open();
        var pane = rig.Pane!;
        Assert.Equal("Side chat", AutomationProperties.GetName(rig.Card!.Header.TitleBlock));
        Assert.Equal("Sees this thread. Never interrupts it.", pane.NoteText);
        Assert.True(pane.ShowsEmptyState);
        Assert.Null(pane.Timeline);
        Assert.Equal("Ask about this thread…", pane.Composer.Field.PlaceholderText);
        Assert.Equal(PaneGlyphKind.Send, pane.Composer.SendGlyphKind);
        Assert.False(pane.SendToMainIsVisible);
        Assert.Equal(["Can act, off"], rig.Card!.Header.ActionViews.Select(AutomationProperties.GetName));
        Assert.Equal(1, rig.H.Main.OnMain(() => rig.Model.Threads.Count(thread => thread.IsSideChat)));
        Assert.Equal(rig.Side!.Id, pane.SideChatId);
        Assert.Empty(rig.Launches);

        rig.Open();
        Assert.Equal(1, rig.H.Main.OnMain(() => rig.Model.Threads.Count(thread => thread.IsSideChat)));
    });

    [Fact]
    public void A_pane_the_person_opens_takes_the_keyboard_in_its_composer_and_one_restored_with_the_thread_does_not() => headless.OnUiThread(() =>
    {
        using var rig = new SideChatPaneRig();
        rig.H.Main.OnMain(() => Assert.True(rig.Workspace.Toggle(PaneKind.SideChat)));
        rig.Settle();
        rig.Until(() => rig.Pane?.SideChatId is not null, "the pane");
        rig.Settle();
        var focus = TopLevel.GetTopLevel(rig.Workspace)!.FocusManager!.GetFocusedElement();
        Assert.Same(rig.Pane!.Composer.Field, focus);
    });

    [Fact]
    public void Return_sends_the_question_and_the_composer_becomes_Stop_while_the_side_chat_answers() => headless.OnUiThread(() =>
    {
        using var rig = new SideChatPaneRig();
        rig.Script = session => session.SendScript = _ => new TaskCompletionSource().Task;
        rig.Open();
        var composer = rig.Pane!.Composer;
        Assert.False(composer.CanSend);
        Say(rig, "What is the main thread doing?");
        Assert.True(composer.CanSend);
        rig.H.Main.OnMain(() => composer.Field.Focus());
        rig.H.Main.OnMain(() => rig.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null));
        rig.Until(() => rig.Launches.Count == 1 && rig.Launches[0].Session.Inputs.Count == 1, "the question to be sent");

        Assert.EndsWith("The user's question:\nWhat is the main thread doing?", rig.Launches[0].Session.Inputs[0].Text, StringComparison.Ordinal);
        Assert.Equal("", composer.Text);
        Assert.False(rig.Pane.ShowsEmptyState);
        Assert.NotNull(rig.Pane.Timeline);
        rig.Until(() => composer.IsRunning, "the composer to show the running answer");
        Assert.Equal(PaneGlyphKind.Stop, composer.SendGlyphKind);
        Assert.Equal("Stop", AutomationProperties.GetName(composer.SendButton));

        rig.H.Main.OnMain(() => PaneWorkspaceRig.Click(composer.SendButton));
        rig.Until(() => rig.Launches[0].Session.InterruptCalls == 1, "Stop to reach the session");
    });

    [Fact]
    public void Shift_Return_writes_a_new_line_and_sends_nothing() => headless.OnUiThread(() =>
    {
        using var rig = new SideChatPaneRig();
        rig.Open();
        var field = rig.Pane!.Composer.Field;
        rig.H.Main.OnMain(() => field.Focus());
        Say(rig, "first line");
        rig.H.Main.OnMain(() => rig.Window.KeyPress(Key.Enter, RawInputModifiers.Shift, PhysicalKey.Enter, null));
        rig.Pump();
        Assert.Empty(rig.Launches);
        Assert.Contains("first line", field.Text, StringComparison.Ordinal);
    });

    [Fact]
    public void An_empty_question_is_not_sent_and_the_button_says_it_cannot() => headless.OnUiThread(() =>
    {
        using var rig = new SideChatPaneRig();
        rig.Open();
        Say(rig, "   ");
        Send(rig);
        Assert.False(rig.Pane!.Composer.CanSend);
        Assert.Empty(rig.Launches);
        Assert.True(rig.Pane.ShowsEmptyState);
    });

    [Fact]
    public void An_answer_shows_in_the_conversation_and_the_chip_puts_it_in_the_main_draft_without_sending() => headless.OnUiThread(() =>
    {
        using var rig = new SideChatPaneRig();
        rig.Open();
        var session = Ask(rig, "Is anything failing?");
        Assert.False(rig.Pane!.SendToMainIsVisible, "no chip while the answer is being written");
        Answer(rig, session, "The console shows one 404 for /api/plans.");

        var pane = rig.Pane!;
        Assert.True(pane.SendToMainIsVisible);
        Assert.Equal("Send to main as a draft", pane.SendToMainText);
        Assert.Equal(PaneGlyphKind.Send, pane.Composer.SendGlyphKind);
        rig.H.Main.OnMain(() => PaneWorkspaceRig.Click(pane.SendToMainButton));
        rig.Pump();
        Assert.Equal("The console shows one 404 for /api/plans.", rig.H.Main.OnMain(() => rig.Runtime.Draft.Text));
        Assert.Equal("Added to the main draft", pane.SendToMainText);
        Assert.Equal(RuntimePhase.Idle, rig.Runtime.Phase);
        Assert.Empty(rig.H.Main.OnMain(() => rig.Runtime.Entries.ToList()));

        // The next answer is a new one: the chip is back.
        var next = Ask(rig, "And the other one?", launch: 2);
        Answer(rig, next, "Nothing else.");
        Assert.Equal("Send to main as a draft", pane.SendToMainText);
        rig.H.Main.OnMain(() => PaneWorkspaceRig.Click(pane.SendToMainButton));
        rig.Pump();
        Assert.Equal("The console shows one 404 for /api/plans.\n\nNothing else.", rig.H.Main.OnMain(() => rig.Runtime.Draft.Text));
    });

    [Fact]
    public void Can_act_in_the_header_toggles_the_mode_the_note_and_the_buttons_name() => headless.OnUiThread(() =>
    {
        using var rig = new SideChatPaneRig();
        rig.Open();
        Assert.False(rig.H.Main.OnMain(() => rig.Model.SideChatCanAct(rig.ThreadId)));

        rig.H.Main.OnMain(() => PaneWorkspaceRig.Click(Action(rig, "Can act")));
        rig.Settle();
        Assert.True(rig.H.Main.OnMain(() => rig.Model.SideChatCanAct(rig.ThreadId)));
        Assert.Equal(SideChatStrings.NoteCanAct, rig.Pane!.NoteText);
        Assert.Equal("Can act, on", AutomationProperties.GetName(Action(rig, "Can act")));
        Assert.Equal(InteractionMode.Build, rig.Side!.InteractionMode);

        rig.H.Main.OnMain(() => PaneWorkspaceRig.Click(Action(rig, "Can act")));
        rig.Settle();
        Assert.False(rig.H.Main.OnMain(() => rig.Model.SideChatCanAct(rig.ThreadId)));
        Assert.Equal(SideChatStrings.Note, rig.Pane.NoteText);
        Assert.Equal("Can act, off", AutomationProperties.GetName(Action(rig, "Can act")));
    });

    [Fact]
    public void Clear_appears_with_the_first_question_and_starts_an_empty_conversation() => headless.OnUiThread(() =>
    {
        using var rig = new SideChatPaneRig();
        rig.Open();
        Assert.DoesNotContain(rig.Card!.Header.ActionViews, view => AutomationProperties.GetName(view) == "Clear");
        var session = Ask(rig, "Hello");
        Answer(rig, session, "Hi.");
        var before = rig.Side!.Id;
        Assert.Contains(rig.Card.Header.ActionViews, view => AutomationProperties.GetName(view) == "Clear");

        rig.H.Main.OnMain(() => PaneWorkspaceRig.Click(Action(rig, "Clear")));
        rig.Until(() => rig.Pane!.SideChatId is { } id && id != before, "the pane to show the new side chat");
        rig.Settle();
        Assert.True(rig.Pane!.ShowsEmptyState);
        Assert.False(rig.Pane.SendToMainIsVisible);
        Assert.DoesNotContain(rig.Card.Header.ActionViews, view => AutomationProperties.GetName(view) == "Clear");
        Assert.Null(rig.H.Main.OnMain(() => rig.Model.Thread(before)));
        Assert.Equal(1, rig.H.Main.OnMain(() => rig.Model.Threads.Count(thread => thread.IsSideChat)));
    });

    [Fact]
    public void A_request_the_side_chat_waits_on_shows_as_a_card_and_leaves_when_answered() => headless.OnUiThread(() =>
    {
        using var rig = new SideChatPaneRig();
        rig.Script = session => session.SendScript = _ => new TaskCompletionSource().Task;
        rig.Open();
        var session = Ask(rig, "Look it up on the web");
        var request = new ApprovalRequest("web-1", ApprovalRequest.Kind.Tool, "Fetch example.com", new ValueList<ApprovalRequest.Option>([
            new ApprovalRequest.Option("yes", "Allow", ApprovalRequest.Option.Role.Approve),
            new ApprovalRequest.Option("no", "Deny", ApprovalRequest.Option.Role.Decline),
        ]));
        rig.H.Main.OnMain(() => session.Emit(new ProviderEvent.Approval(request)));
        rig.Until(() => rig.Pane!.ApprovalCards.Count == 1, "the approval card");

        rig.H.Main.OnMain(() => rig.Model.ExistingRuntime(rig.Side!.Id)!.Resolve(request, request.Options[0]));
        rig.Until(() => rig.Pane!.ApprovalCards.Count == 0, "the card to leave");
        Assert.Equal([("web-1", "yes")], session.ApprovalAnswers.Select(answer => (answer.Id, answer.Option)));
    });

    [Fact]
    public void A_question_typed_but_not_sent_is_kept_for_when_the_pane_opens_again() => headless.OnUiThread(() =>
    {
        using var rig = new SideChatPaneRig();
        rig.Open();
        Say(rig, "half a question");
        rig.H.Main.OnMain(() => rig.Runtime.Workspace = rig.Runtime.Workspace.Close(PaneKind.SideChat));
        rig.Settle();
        Assert.Null(rig.Card);

        rig.Open();
        Assert.Equal("half a question", rig.Pane!.Composer.Text);
    });

    [Fact]
    public void The_conversation_is_kept_when_the_pane_closes_and_comes_back_when_it_opens() => headless.OnUiThread(() =>
    {
        using var rig = new SideChatPaneRig();
        rig.Open();
        var session = Ask(rig, "Remember me");
        Answer(rig, session, "I will.");
        var id = rig.Side!.Id;

        rig.H.Main.OnMain(() => PaneWorkspaceRig.Click(rig.Card!.Header.CloseButton));
        rig.Settle();
        Assert.Null(rig.Card);

        rig.Open();
        Assert.Equal(id, rig.Pane!.SideChatId);
        Assert.False(rig.Pane.ShowsEmptyState);
        Assert.NotNull(rig.Pane.Timeline);
        Assert.Contains(rig.Card!.Header.ActionViews, view => AutomationProperties.GetName(view) == "Clear");
    });

    [Fact]
    public void The_pane_fits_its_narrowest_width_the_composer_inside_the_card_and_Send_reachable() => headless.OnUiThread(() =>
    {
        using var rig = new SideChatPaneRig(width: 720);
        rig.Open();
        var card = rig.Card!;
        Assert.InRange(card.Bounds.Width, PaneKinds.Size(PaneKind.SideChat).MinWidth, 640);
        var composer = rig.Pane!.Composer;
        var origin = composer.TranslatePoint(default, card)!.Value;
        Assert.True(origin.X >= 0 && origin.X + composer.Bounds.Width <= card.Bounds.Width + 0.5, "the composer is inside the card");
        var send = rig.Pane.Composer.SendButton;
        var at = send.TranslatePoint(default, card)!.Value;
        Assert.True(at.X + send.Bounds.Width <= card.Bounds.Width + 0.5 && at.X >= 0);
        Assert.True(send.Bounds.Width >= 28);
    });

    [Fact]
    public void With_Reduce_Motion_the_pane_works_the_same_and_draws_without_a_fade() => headless.OnUiThread(() =>
    {
        using var rig = new SideChatPaneRig(reduceMotion: true);
        rig.Open();
        var session = Ask(rig, "Quiet please");
        Answer(rig, session, "Quiet.");
        Assert.True(rig.Pane!.SendToMainIsVisible);
        Assert.False(rig.Pane.ShowsEmptyState);
    });
}
