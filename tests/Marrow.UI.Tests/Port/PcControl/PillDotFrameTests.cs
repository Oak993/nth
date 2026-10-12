using Avalonia;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.PcControl;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port.PcControl;

// PORT-ONLY: Tactus, the full-screen dot's frames (OD-0145, ED-NEW-pcctl-36; DESIGN.md 10.3 "Auto-dodge" row 4, canvas
// `.sdot`). Read back at 240 Hz the way the compositor plays its key frames: into the dot the text, the group and Stop leave
// first while the body holds its size; then the body closes to a 32 DIP circle round the mark, the row and the live glyph on
// their pose's places in every frame, nothing visible crossing the mark; out of it the body grows first and the group and
// Stop come in once it has. No fades. Designed ends: the dot (mark centred, glyph at the corner) and the working pill.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-pcctl-36")]
public sealed class PillDotFrameTests(HeadlessSession headless)
{
    private const double Frame = 1.0 / 240;

    // DESIGN.md 10.11: the 1 ms keys stay within 0.15 DIP of the curve
    private const double KeyError = 0.15;

    // A part narrower than this is not seen (a quarter of a pixel at 200 % scale).
    private const double Unseen = 0.5;

    private static readonly PillApp Excel = new("excel", "Excel", "Filling row 12 of 40");

    private static PillView Working() => PillStates.Compute(new PillFacts { Apps = [Excel] }, 10);

    private static PillView Dot() => PillStates.Compute(new PillFacts { Apps = [Excel], Dot = true }, 10);

    private static (ControlPill Pill, PillFrames Frames, ManualTimeProvider Clock, double T0) Make()
    {
        var animator = new RecordingPillAnimator();
        var clock = new ManualTimeProvider();
        var pill = new ControlPill(PillDriverKind.RenderThread, animator, clock);
        var frames = new PillFrames(pill, animator);
        var t0 = pill.Now;
        frames.At(t0, () =>
        {
            pill.Show(Working(), t0);
            pill.Place(new PillVector(600, 8), 0.5, t0);
            pill.Present(t0);
        });
        return (pill, frames, clock, t0);
    }

    private static void Near(double expected, double actual, double tolerance, string what) =>
        Assert.True(Math.Abs(actual - expected) <= tolerance, $"{what}: {actual} is {Math.Abs(actual - expected)} from {expected}");

    // The row's and the glyph's drawn places at t (their layout plus their translation), in the anchor's coordinates.
    private static (PillVector Row, PillVector Glyph) Drawn(ControlPill pill, PillFrames frames, double t)
    {
        var laid = frames.Laid(t);
        var row = new PillVector(laid.RowLeft, laid.RowTop) + frames.Offset(pill.Row, t);
        return (row, row + laid.Glyph + frames.Offset(pill.Glyph!, t));
    }

    // The mark's 24 DIP glyph sits 4 DIP into its 32 DIP slot, which the row puts at (0, 8).
    private static double MarkRight(PillVector row) => row.X + 4 + 24;

    // Stop's capsule as drawn: from its slot's left edge, scaled about the centre its swap set.
    private static (double Left, double Width) StopDrawn(ControlPill pill, PillFrames frames, double t)
    {
        var laid = frames.Laid(t);
        var scale = frames.Scale(pill.StopSlot, t);
        var centre = frames.On(pill.StopSlot).Last(c => c.Kind == "SetCentre").A;
        var left = laid.StopLeft + frames.Offset(pill.StopSlot, t).X;
        return (left + (centre * (1 - scale)), scale * laid.StopVisible);
    }

    [Fact]
    public void Into_the_dot_the_content_leaves_first_then_the_body_closes_round_the_mark_and_nothing_crosses_it() => headless.OnUiThread(() =>
    {
        var (pill, frames, clock, t0) = Make();
        var wide = pill.Motion.Final.Width;
        frames.At(t0, () => pill.Show(Dot(), t0));
        frames.Pass(clock, ControlPill.DotStagger, motions: true);
        var closing = pill.Now;
        frames.Pass(clock, 1.0, motions: true);
        var end = pill.Now;

        Assert.DoesNotContain(frames.All, c => c.Kind == "Fade");
        var count = (int)Math.Ceiling((end - t0) / Frame);
        for (var i = 0; i <= count; i++)
        {
            var t = t0 + (i * Frame);
            var what = "frame " + i;
            var pose = frames.Expected(t);
            // the body holds its size while the content leaves
            if (t < closing - 1e-9)
            {
                Near(wide, pose.Width, 1e-9, what + " width");
                Near(PillLayout.Height, pose.Height, 1e-9, what + " height");
            }
            // the row and the glyph on their pose's places (within the keys' error), inside the body
            var places = PillPlaces.Of(pose, pill.Parts);
            var (row, glyph) = Drawn(pill, frames, t);
            Near(places.RowLeft, row.X, KeyError, what + " row x");
            Near(places.RowTop, row.Y, KeyError, what + " row y");
            Near(places.RowLeft + places.Glyph.X, glyph.X, 2 * KeyError, what + " glyph x");
            Near(places.RowTop + places.Glyph.Y, glyph.Y, 2 * KeyError, what + " glyph y");
            Assert.True(row.X + 4 >= places.BodyLeft - KeyError && MarkRight(row) <= places.BodyRight + KeyError, what + ": the mark leaves the body");
            Assert.True(glyph.X - 3 >= places.BodyLeft - (2 * KeyError) && glyph.X + 3 <= places.BodyRight + (2 * KeyError), what + ": the glyph leaves the body");
            // nothing seen crosses the mark: Stop, while any of it shows, stays right of the mark's glyph
            var stop = StopDrawn(pill, frames, t);
            Assert.True(stop.Width < Unseen || stop.Left >= MarkRight(row) - KeyError, $"{what}: Stop ({stop.Width:F2} DIP) crosses the mark at {stop.Left:F2}");
        }

        // the designed end: a 32 DIP circle, the mark centred, the live glyph at the top right corner, nothing else shown
        var final = pill.Motion.Final;
        Assert.Equal(PillLayout.DotSize, final.Width, 9);
        Assert.Equal(PillLayout.DotSize, final.Height, 9);
        var last = PillPlaces.Of(final, pill.Parts);
        var (endRow, endGlyph) = Drawn(pill, frames, end);
        Near(last.BodyLeft + (PillLayout.DotSize / 2), endRow.X + 4 + 12, KeyError, "the mark's centre");
        Near(PillLayout.DotSize / 2, endRow.Y + 8 + 4 + 12, KeyError, "the mark's middle");
        Near(last.BodyLeft + PillLayout.DotSize - 4, endGlyph.X, 2 * KeyError, "the glyph's x");
        Near(4, endGlyph.Y, 2 * KeyError, "the glyph's y");
        Assert.Equal(PillGroupKind.None, pill.ShownGroup);
        Assert.False(pill.StopSlot.IsVisible);
        Assert.Equal("", pill.TitleSlot.Text);
        Assert.Equal("", pill.LineSlot.Text);
        Assert.True(pill.Dot.IsVisible);
    });

    [Fact]
    public void Out_of_the_dot_the_body_grows_first_and_the_group_and_Stop_come_in_once_it_has() => headless.OnUiThread(() =>
    {
        var (pill, frames, clock, t0) = Make();
        frames.At(t0, () => pill.Show(Dot(), t0));
        frames.Pass(clock, ControlPill.DotStagger, motions: true);
        frames.Pass(clock, 1.0, motions: true);
        var grow = pill.Now;
        frames.At(grow, () => pill.Show(Working(), grow));
        var run = pill.GroupOf(PillGroupKind.Run)!;
        // the body grows at once; the group and Stop wait
        Assert.True(pill.Motion.Final.Width > PillLayout.MinWidth - 1e-9);
        Assert.False(run.IsVisible);
        Assert.False(pill.StopSlot.IsVisible);
        frames.Pass(clock, ControlPill.DotStagger, motions: true);
        var enter = pill.Now;
        Assert.True(run.IsVisible);
        Assert.True(pill.StopSlot.IsVisible);
        frames.Pass(clock, 1.0, motions: true);
        var end = pill.Now;

        Assert.DoesNotContain(frames.All, c => c.Kind == "Fade");
        var count = (int)Math.Ceiling((end - grow) / Frame);
        for (var i = 0; i <= count; i++)
        {
            var t = grow + (i * Frame);
            var what = "frame " + i;
            var pose = frames.Expected(t);
            var places = PillPlaces.Of(pose, pill.Parts);
            var (row, glyph) = Drawn(pill, frames, t);
            Near(places.RowLeft, row.X, KeyError, what + " row x");
            Near(places.RowTop, row.Y, KeyError, what + " row y");
            Near(places.RowLeft + places.Glyph.X, glyph.X, 2 * KeyError, what + " glyph x");
            Assert.True(MarkRight(row) <= places.BodyRight + KeyError, what + ": the mark leaves the body");
            // Stop only comes in once the body has grown past it, and never over the mark
            var stop = StopDrawn(pill, frames, t);
            if (t < enter - 1e-9) Assert.True(stop.Width < Unseen, $"{what}: Stop shows before the body has grown");
            else Assert.True(stop.Width < Unseen || stop.Left >= MarkRight(row) - KeyError, $"{what}: Stop crosses the mark");
        }

        // the designed end: the working pill as it was
        var working = new ControlPill(PillDriverKind.RenderThread, new RecordingPillAnimator(), clock);
        working.Show(Working(), working.Now);
        Assert.Equal(working.Motion.Final.Width, pill.Motion.Final.Width, 9);
        Assert.Equal(PillLayout.Height, pill.Motion.Final.Height, 9);
        Assert.Equal(PillPlaces.GlyphHome, PillPlaces.Of(pill.Motion.Final, pill.Parts).Glyph);
        Assert.Equal(Working().Line, pill.LineSlot.Text);
        Assert.Equal(PillGroupKind.Run, pill.ShownGroup);
        Near(1, frames.Scale(pill.StopSlot, end), 1e-9, "Stop's scale");
    });

    [Fact]
    public void The_dot_view_shows_the_mark_and_the_live_glyph_only_and_a_card_wins_over_it()
    {
        var dot = Dot();
        Assert.True(dot.Dot);
        Assert.Equal("", dot.Title);
        Assert.Equal("", dot.Line);
        Assert.Equal(PillGroupKind.None, dot.Group);
        Assert.False(dot.ShowsStop);
        Assert.Equal(0, dot.Badge);
        Assert.False(dot.HintShown);
        Assert.False(dot.RowsShown);
        Assert.Equal(PillGlyph.Live, dot.Glyph);
        Assert.Equal(Working().Announcement, dot.Announcement);
        var ask = new PillAsk("a1", PillAskKind.Approval, "Outlook", "Quarterly report", "GPT-6.1 Sol");
        var card = PillStates.Compute(new PillFacts { Apps = [Excel], Asks = [ask], Card = true, Dot = true }, 10);
        Assert.False(card.Dot);
        Assert.Equal(PillGroupKind.Card, card.Group);
    }
}
