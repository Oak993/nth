using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.UI.Port;
using Marrow.UI.Port.PcControl;

namespace Marrow.App.Tests.Port.PcControl;

// PORT-ONLY: Tactus pill v2, the card on the bands (OD-0145, ED-NEW-pcctl-26; DESIGN.md 10.3, 10.4 "Queue", "Resolution",
// "Morph", 10.5). A new ask expands the card once the pill is still (never while it moves, is hovered or a button is
// held); Later disarms that until the queue empties; a click always expands, on the ask that was open; the pager slides
// to the next ask; when the open ask ends the one now in its place shows and the card folds when none waits; a card that
// would open with a button under the pointer moves sideways with the pill and goes back when it folds; drafts keep their
// text and selection per ask; answers and drafts come out of the host; a refused keyboard points at the chat.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-pcctl-26")]
public sealed class TactusPillHostCardTests(HeadlessSession headless)
{
    private static readonly PillApp Excel = new("excel", "Excel", "Filling row 12 of 40");
    private static readonly PillAsk First = new("a1", PillAskKind.Approval, "Outlook", "Quarterly report", "GPT-6.1 Sol");
    private static readonly PillAsk Second = new("a2", PillAskKind.ShowWindow, "Excel", "Quarterly report", "GPT-6.1 Sol");
    private static readonly PillAsk Question = new("q1", PillAskKind.Question, "Excel", "Quarterly report", "GPT-6.1 Sol");

    private static PillCardBody? Body(string id) => id switch
    {
        "a1" => new PillApprovalBody("a1", 0, "Outlook", "c:\\office\\outlook.exe", Risky: false, UseAsked: true, AllowAlways: true),
        "a2" => new PillShowBody("a2", 0, "Excel", null),
        "q1" => new PillQuestionBody("q1", 0, "GPT-6.1 Sol", "Which sheet?", ["Q1"]),
        _ => null,
    };

    private static void Pass(PcPillHostRig rig, double seconds)
    {
        rig.Clock.Advance(TimeSpan.FromSeconds(seconds));
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void A_new_ask_expands_the_card_once_still_and_the_queue_moves_on() => headless.OnUiThread(() =>
    {
        var rig = PcPillHostRig.Create();
        rig.Host.Bodies = Body;
        var changes = new List<bool>();
        rig.Host.CardChanged += changes.Add;
        rig.Host.Update(new PillFacts { Apps = [Excel], Asks = [First, Second] });
        var pill = rig.Host.Pill(PcTestMonitors.Main.Id)!;
        var mirror = rig.Host.Mirror(PcTestMonitors.Main.Id)!;
        // arriving: the expand waits for the pill to be still
        Assert.Empty(changes);
        Assert.Null(pill.Card.Current);

        Pass(rig, 2);
        Assert.Equal(new[] { true }, changes);
        Assert.Equal("a1", rig.Host.OpenAsk);
        Assert.IsType<ApprovalCardBody>(pill.Card.Current);
        Assert.IsType<ApprovalCardBody>(mirror.Card.Current);
        Assert.Equal(PillGroupKind.Card, pill.View!.Group);
        Assert.Equal("1 of 2", pill.View.Pager);
        Assert.Equal(PillMorph.CardWidth, pill.Motion.Final.Width, 9);
        Assert.Equal(PillLayout.Height + pill.Card.BodyHeight, pill.Motion.Final.Height, 9);

        pill.Raise(PillCommandKind.NextAsk);
        Assert.Equal("a2", rig.Host.OpenAsk);
        Assert.IsType<ShowWindowCardBody>(pill.Card.Current);
        Assert.IsType<ApprovalCardBody>(pill.Card.Leaving);
        Assert.Equal("2 of 2", pill.View.Pager);

        // the open ask was answered: the one in its place shows; the last one gone folds the card
        rig.Host.Update(new PillFacts { Apps = [Excel], Asks = [First] });
        Assert.Equal("a1", rig.Host.OpenAsk);
        Assert.IsType<ApprovalCardBody>(pill.Card.Current);
        rig.Host.Update(new PillFacts { Apps = [Excel] });
        Assert.Equal(new[] { true, false }, changes);
        Assert.Null(rig.Host.OpenAsk);
        Assert.Null(pill.Card.Current);
        Assert.Equal(PillLayout.Height, pill.Motion.Final.Height, 9);
    });

    [Fact]
    public void Later_disarms_the_expand_until_the_queue_empties_and_a_click_always_expands() => headless.OnUiThread(() =>
    {
        var rig = PcPillHostRig.Create();
        var revision = 0;
        rig.Host.Bodies = id => new PillShowBody(id, revision, "Excel", null);
        var changes = new List<bool>();
        rig.Host.CardChanged += changes.Add;
        rig.Host.Update(new PillFacts { Apps = [Excel], Asks = [Second] });
        var pill = rig.Host.Pill(PcTestMonitors.Main.Id)!;

        // held: a button down anywhere keeps the expand waiting
        var held = true;
        rig.Host.ButtonHeld = () => held;
        Pass(rig, 2);
        Assert.Empty(changes);
        pill.Raise(PillCommandKind.Click);
        Assert.Equal(new[] { true }, changes);

        // the same revision is not shown again; a new revision shows in place
        var shown = pill.Card.Current;
        rig.Host.Refresh();
        Assert.Same(shown, pill.Card.Current);
        revision = 1;
        rig.Host.Refresh();
        Assert.NotSame(shown, pill.Card.Current);
        Assert.Null(pill.Card.Leaving);

        pill.Raise(PillCommandKind.Later);
        Assert.Equal(new[] { true, false }, changes);
        Assert.NotEqual(PillGroupKind.Card, pill.View!.Group);
        held = false;
        rig.Host.Update(new PillFacts { Apps = [Excel], Asks = [Second, First] });
        Pass(rig, 2);
        Assert.False(rig.Host.CardOpen);

        // an empty queue re-arms it
        rig.Host.Update(new PillFacts { Apps = [Excel] });
        rig.Host.Update(new PillFacts { Apps = [Excel], Asks = [First] });
        Pass(rig, 2);
        Assert.True(rig.Host.CardOpen);
        Assert.Equal(new[] { true, false, true }, changes);
    });

    [Fact]
    public void A_card_under_the_pointer_moves_sideways_and_back_when_it_folds() => headless.OnUiThread(() =>
    {
        // a band in a real window: its mark draws on the frame clock
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var rig = PcPillHostRig.Create();
        var surface = rig.Surfaces.For(PcTestMonitors.Main.Id)!;
        var window = new Avalonia.Controls.Window { Width = 1920, Height = 600, Content = surface.Input };
        window.Show();
        try
        {
            rig.Host.Bodies = Body;
            PillVector? pointer = null;
            rig.Host.PointerAt = _ => pointer;
            rig.Host.Update(new PillFacts { Apps = [Excel], Asks = [First] });
            var pill = rig.Host.Pill(PcTestMonitors.Main.Id)!;
            Pass(rig, 2);
            var anchor = pill.Motion.Final.Anchor;
            var layout = pill.Card.Layout();
            var chat = layout.First(c => c.Id == ApprovalCardBody.ChatId).Bounds;
            pill.Raise(PillCommandKind.Later);
            Pass(rig, 2);

            // the pointer rests where "Allow for this chat" will open
            var left = anchor.X - (PillMorph.CardWidth / 2);
            pointer = new PillVector(left + chat.X + (chat.Width / 2), anchor.Y + PillLayout.Height + chat.Y + (chat.Height / 2));
            pill.Raise(PillCommandKind.Click);
            var shift = CardPlacement.Shift(left, anchor.Y + PillLayout.Height, PillMorph.CardWidth, pill.Card.Layout(), pointer, PcTestMonitors.Main.WorkDip);
            Assert.NotEqual(0, shift.Dx);
            Assert.Equal(anchor.X + shift.Dx, pill.Motion.Final.Anchor.X, 6);

            pill.Raise(PillCommandKind.Later);
            Assert.Equal(anchor.X, pill.Motion.Final.Anchor.X, 6);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void Answers_and_drafts_come_out_the_mirror_follows_and_a_draft_comes_back_with_its_selection() => headless.OnUiThread(() =>
    {
        var rig = PcPillHostRig.Create();
        rig.Host.Bodies = Body;
        var answers = new List<PillCardAnswer>();
        var drafts = new List<(string Id, string Text)>();
        rig.Host.CardAnswered += answers.Add;
        rig.Host.DraftChanged += (id, text) => drafts.Add((id, text));
        rig.Host.Update(new PillFacts { Apps = [Excel], Asks = [Question] });
        var pill = rig.Host.Pill(PcTestMonitors.Main.Id)!;
        var mirror = rig.Host.Mirror(PcTestMonitors.Main.Id)!;
        pill.Raise(PillCommandKind.Click);

        var field = pill.Card.Current!.Composer!.Field;
        field.Text = "the second one";
        field.SelectionStart = 4;
        field.SelectionEnd = 10;
        Assert.Equal(("q1", "the second one"), Assert.Single(drafts));
        Assert.Equal("the second one", mirror.Card.Draft);

        // folded and opened again: the draft, the caret and the selection are back
        pill.Raise(PillCommandKind.Later);
        pill.Raise(PillCommandKind.Click);
        var again = pill.Card.Current!.Composer!.Field;
        Assert.NotSame(field, again);
        Assert.Equal("the second one", again.Text);
        Assert.Equal(4, again.SelectionStart);
        Assert.Equal(10, again.SelectionEnd);

        // Send is free of the shield: it answers at once with the typed text
        pill.Card.Current.Composer.Send.Press();
        Assert.Equal(new PillCardAnswer("q1", 0, PillCardChoice.Send, Text: "the second one"), Assert.Single(answers));

        rig.Host.KeyboardRefused();
        Assert.IsType<PillTypeInChatBody>(pill.Card.Current!.Model);
        Assert.IsType<PillTypeInChatBody>(mirror.Card.Current!.Model);

        // the ask ended: its draft is dropped
        rig.Host.Update(new PillFacts { Apps = [Excel] });
        rig.Host.Update(new PillFacts { Apps = [Excel], Asks = [Question] });
        pill.Raise(PillCommandKind.Click);
        Assert.Equal("", pill.Card.Current!.Composer!.Field.Text ?? "");
    });
}
