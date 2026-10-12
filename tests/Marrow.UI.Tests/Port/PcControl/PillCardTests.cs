using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.UI.Port.PcControl;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port.PcControl;

// PORT-ONLY: Tactus pill v2, the card (OD-0145, ED-NEW-pcctl-26; DESIGN.md 10.4 "Accidental-click protection", "Queue",
// "Keys"). Its answers rise from 35 % over 400 ms when a body shows; a mouse click counts only after the pointer rested on
// the button for 150 ms (else the footer says "Rest on a button for a moment, then click"); "Always allow" takes two
// clicks at least 600 ms apart; Enter never answers ("Enter does nothing here") and Esc folds the card; paging slides
// the bodies sideways, no fade.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-pcctl-26")]
public sealed class PillCardTests(HeadlessSession headless)
{
    private static readonly PillApprovalBody Approval = new("a1", 0, "Outlook", "C:\\office\\outlook.exe", Risky: false, UseAsked: true, AllowAlways: true);

    private static PillCardRig Make()
    {
        var animator = new RecordingPillAnimator();
        var later = new List<(double, Action)>();
        PillCardRig? rig = null;
        var card = new PillCard(animator, () => rig!.Now, (seconds, run) => later.Add((seconds, run)));
        var window = new Window { Width = 600, Height = 600, Content = new Canvas { Children = { card } } };
        rig = new PillCardRig(card, animator, window, [], later);
        card.Answered += rig.Answers.Add;
        window.Show();
        return rig;
    }

    [Fact]
    public void Answers_rise_from_the_shield_and_a_click_counts_only_after_a_rest() => headless.OnUiThread(() =>
    {
        var rig = Make();
        try
        {
            rig.Card.Show(Approval, 0, PillMorph.MaxCardHeight);
            var body = Assert.IsType<ApprovalCardBody>(rig.Card.Current);
            var fade = rig.Animator.Last(rig.Button(ApprovalCardBody.ChatId), "Fade")!.X!;
            Assert.Equal(CardShield.ShieldFrom, fade.Values[0], 2);
            Assert.Equal(1, fade.Last, 9);
            Assert.Null(rig.Animator.Last(rig.Button(ApprovalCardBody.SeeId), "Fade"));
            Assert.True(rig.Card.BodyHeight > 0);
            Assert.True(rig.Card.BodyHeight <= PillMorph.MaxCardHeight - PillLayout.Height);

            rig.Button(ApprovalCardBody.ChatId).Press();
            Assert.Empty(rig.Answers);

            rig.Now += 0.5;
            rig.Button(ApprovalCardBody.ChatId).Press();
            Assert.Empty(rig.Answers);
            Assert.Equal(CardShield.RestHint, body.Footer!.Shown);

            rig.RestOn(ApprovalCardBody.ChatId);
            rig.Now += 0.2;
            rig.Button(ApprovalCardBody.ChatId).Press();
            Assert.Equal(new PillCardAnswer("a1", 0, PillCardChoice.AllowChat, Use: true), Assert.Single(rig.Answers));
        }
        finally
        {
            rig.Window.Close();
        }
    });

    [Fact]
    public void Always_allow_takes_two_clicks_at_least_600_ms_apart() => headless.OnUiThread(() =>
    {
        var rig = Make();
        try
        {
            rig.Card.Show(Approval, 0, PillMorph.MaxCardHeight);
            var body = Assert.IsType<ApprovalCardBody>(rig.Card.Current);
            rig.Now += 0.5;
            rig.RestOn(ApprovalCardBody.AlwaysId);
            rig.Now += 0.2;

            rig.Button(ApprovalCardBody.AlwaysId).Press();
            Assert.True(body.IsArmed);
            Assert.Empty(rig.Answers);
            rig.Now += 0.3;
            rig.Button(ApprovalCardBody.AlwaysId).Press();
            Assert.Equal(CardShield.AgainHint, body.Footer!.Shown);
            Assert.Empty(rig.Answers);
            rig.Now += 0.4;
            rig.Button(ApprovalCardBody.AlwaysId).Press();

            Assert.Equal(PillCardChoice.AllowAlways, Assert.Single(rig.Answers).Choice);
            Assert.False(body.IsArmed);
        }
        finally
        {
            rig.Window.Close();
        }
    });

    [Fact]
    public void Enter_never_answers_and_Esc_folds_the_card() => headless.OnUiThread(() =>
    {
        var rig = Make();
        try
        {
            var commands = new List<PillCardCommand>();
            rig.Card.Command += commands.Add;
            rig.Card.Show(Approval, 0, PillMorph.MaxCardHeight);
            rig.Now += 1;

            rig.Card.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = rig.Card });
            Assert.Empty(rig.Answers);
            Assert.Equal(CardShield.EnterHint, rig.Card.Current!.Footer!.Shown);

            rig.Card.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, Source = rig.Card });
            Assert.Equal(new[] { PillCardCommand.Fold }, commands);
        }
        finally
        {
            rig.Window.Close();
        }
    });

    [Fact]
    public void The_next_ask_slides_in_from_the_right_and_the_old_body_leaves_to_the_left() => headless.OnUiThread(() =>
    {
        var rig = Make();
        try
        {
            rig.Card.Show(Approval, 0, PillMorph.MaxCardHeight);
            var first = rig.Card.Current!;
            rig.Card.Show(new PillShowBody("s1", 0, "Excel", null), 1, PillMorph.MaxCardHeight);
            var second = rig.Card.Current!;

            Assert.Same(first, rig.Card.Leaving);
            Assert.False(first.IsHitTestVisible);
            var comingIn = rig.Animator.Last(second, "Translate")!;
            Assert.Equal(PillMorph.CardWidth, comingIn.X!.Values[0], 6);
            Assert.Equal(0, comingIn.X.Last, 9);
            var goingOut = rig.Animator.Last(first, "Translate")!;
            Assert.Equal(0, goingOut.X!.Values[0], 6);
            Assert.Equal(-PillMorph.CardWidth, goingOut.X.Last, 6);
            Assert.Null(rig.Animator.Last(second, "Fade"));
            Assert.Null(rig.Animator.Last(first, "Fade"));

            foreach (var (_, run) in rig.Later.ToList()) run();
            Assert.Null(rig.Card.Leaving);
            Assert.DoesNotContain(first, rig.Card.Children);
        }
        finally
        {
            rig.Window.Close();
        }
    });

    [Fact]
    public void The_reply_field_reports_its_draft_and_a_refused_keyboard_points_at_the_chat_with_it() => headless.OnUiThread(() =>
    {
        var rig = Make();
        try
        {
            var drafts = new List<(string Id, string Text)>();
            rig.Card.DraftChanged += (id, text) => drafts.Add((id, text));
            rig.Card.Show(new PillQuestionBody("q1", 0, "GPT-6.1 Sol", "Which sheet?", []), 0, PillMorph.MaxCardHeight);

            rig.Card.Current!.Composer!.Field.Text = "the second one";
            Assert.Equal(("q1", "the second one"), drafts[^1]);
            Assert.Equal("the second one", rig.Card.Draft);

            rig.Card.KeyboardRefused();
            var pointer = Assert.IsType<ProviderPointerBody>(rig.Card.Current);
            Assert.Equal(new PillTypeInChatBody("q1", 0, "the second one"), pointer.Model);
        }
        finally
        {
            rig.Window.Close();
        }
    });
}
