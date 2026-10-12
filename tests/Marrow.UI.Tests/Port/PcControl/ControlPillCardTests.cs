using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.PcControl;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port.PcControl;

// PORT-ONLY: Tactus pill v2, the card in the pill (OD-0145, ED-NEW-pcctl-26; DESIGN.md 10.4 "Morph"). The pill widens to
// the card's 440 DIP and grows by the body's height in one motion; the body is revealed by a clip that follows the
// body's edges, never faded, and holds its place under the anchor while the clip moves, rising its last 6 DIP; a fold
// shrinks it back and the body goes once the collapse is over; Esc on the card is Later.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-pcctl-26")]
public sealed class ControlPillCardTests(HeadlessSession headless)
{
    private static readonly PillApp Excel = new("excel", "Excel", "Filling row 12 of 40");
    private static readonly PillAsk Ask = new("a1", PillAskKind.Approval, "Outlook", "Quarterly report", "GPT-6.1 Sol") { Head = "Head 2" };
    private static readonly PillApprovalBody Body = new("a1", 0, "Outlook", "C:\\office\\outlook.exe", Risky: false, UseAsked: true, AllowAlways: false, Who: "Head 2");

    private static PillView View(bool card) => PillStates.Compute(new PillFacts { Apps = [Excel], Asks = [Ask], Card = card }, 10);

    [Fact]
    public void The_card_opens_to_its_width_and_height_revealed_by_its_clip_and_folds_back() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var animator = new RecordingPillAnimator();
        var clock = new ManualTimeProvider();
        var pill = new ControlPill(PillDriverKind.RenderThread, animator, clock);
        var window = new Window { Width = 900, Height = 600, Content = pill };
        window.Show();
        try
        {
            pill.Show(View(card: false));
            pill.Place(new PillVector(450, 8), 0.5);
            pill.Arrive();
            clock.Advance(TimeSpan.FromSeconds(2));
            Dispatcher.UIThread.RunJobs();
            pill.Settled();
            var before = pill.Motion.Final.Width;
            animator.Clear();

            pill.ShowCard(Body);
            var open = View(card: true);
            Assert.Equal("Quarterly report · GPT-6.1 Sol · Head 2", open.Line);
            pill.Show(open);

            Assert.Equal(PillMorph.CardWidth, pill.Motion.Final.Width, 9);
            Assert.True(pill.Card.BodyHeight > 0);
            Assert.Equal(PillLayout.Height + pill.Card.BodyHeight, pill.Motion.Final.Height, 9);
            Assert.True(pill.CardClip!.IsVisible);
            var reveal = animator.Last(pill.CardClip, "Resize")!;
            Assert.Equal(before, reveal.X!.Values[0], 6);
            Assert.Equal(PillMorph.CardWidth, reveal.X.Last, 6);
            Assert.Equal(0, reveal.Y!.Values[0], 6);
            Assert.Equal(pill.Card.BodyHeight, reveal.Y.Last, 6);
            // the clip's edge moves with the body's; the card holds its place and rises its last 6 DIP
            var edge = animator.Last(pill.CardClip, "Translate")!;
            var held = animator.Last(pill.Card, "Translate")!;
            Assert.Equal(-edge.X!.Values[0], held.X!.Values[0], 6);
            Assert.Equal(((PillMorph.CardWidth - before) / 2), edge.X.Values[0], 6);
            Assert.Equal(PillMorph.BodyRise, held.Y!.Values[0], 6);
            Assert.Equal(0, held.Y.Last, 9);
            Assert.Null(animator.Last(pill.Card, "Fade"));
            Assert.Null(animator.Last(pill.CardClip, "Fade"));

            // open, then folded: the clip closes over the body from where it is
            clock.Advance(TimeSpan.FromSeconds(PillMotion.LongestSeconds));
            Dispatcher.UIThread.RunJobs();
            pill.Show(View(card: false));
            Assert.Equal(PillLayout.Height, pill.Motion.Final.Height, 9);
            Assert.Null(pill.Card.Current);
            var fold = animator.Last(pill.CardClip, "Resize")!;
            Assert.True(fold.Y!.Values[0] > 0);
            Assert.Equal(0, fold.Y.Last, 6);
            Assert.True(pill.CardClip.IsVisible);
            clock.Advance(TimeSpan.FromSeconds(PillMotion.LongestSeconds));
            Dispatcher.UIThread.RunJobs();
            Assert.False(pill.CardClip.IsVisible);
            Assert.Empty(pill.Card.Children);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void A_taller_body_grows_the_open_card_and_Esc_is_Later() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var animator = new RecordingPillAnimator();
        var clock = new ManualTimeProvider();
        var pill = new ControlPill(PillDriverKind.RenderThread, animator, clock);
        var window = new Window { Width = 900, Height = 600, Content = pill };
        window.Show();
        try
        {
            var commands = new List<PillCommand>();
            pill.Command += commands.Add;
            pill.Show(View(card: false));
            pill.Place(new PillVector(450, 8), 0.5);
            pill.Arrive();
            pill.ShowCard(Body);
            pill.Show(View(card: true));
            var shorter = pill.Motion.Final.Height;

            // "Reply instead" opens the reply field: the card takes the new height at once
            Assert.Null(pill.Card.Current!.Act(ApprovalCardBody.ReplyId));
            Assert.True(pill.Motion.Final.Height > shorter);
            Assert.Equal(PillLayout.Height + pill.Card.BodyHeight, pill.Motion.Final.Height, 9);

            pill.Card.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, Source = pill.Card });
            Assert.Equal(new[] { new PillCommand(PillCommandKind.Later) }, commands);
        }
        finally
        {
            window.Close();
        }
    });
}
