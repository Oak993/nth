using Avalonia;
using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Xunit;

namespace Marrow.App.Tests.Port.Voice;

// VT-11, V-T76 (voice-SPEC V-7.5 "later words follow their new positions with the same ease (FLIP, ED, so a wrap never jumps)"): where a word is drawn follows the place the layout gives it
// along the design's ez curve over .3 s, from where it was drawn, whatever moved it (a revision's new width, a word that wrapped, the text after the insertion). Frame by frame against the curve.
public sealed class DictationFlipTests
{
    private const double Frame = 1.0 / 60;

    private static ContinuousInstant T(double seconds) => new((long)Math.Round(seconds * TimeSpan.TicksPerSecond));

    private static void Near(Point expected, Point actual, string what) =>
        // a frame's instant is a whole number of 100 ns ticks and the curve is steep at its start: 1e-4 px is what a tick can move it
        Assert.True(Math.Abs(expected.X - actual.X) < 1e-4 && Math.Abs(expected.Y - actual.Y) < 1e-4, $"{what}: {actual} against the curve's {expected}");

    [Fact]
    [Trait("Decision", "ED-292")]
    public void The_first_place_is_where_the_word_is_drawn_with_no_motion()
    {
        var flip = new DictationFlip();
        flip.Place(new Point(10, 4), T(0), still: false);
        Assert.Equal(new Point(10, 4), flip.Displayed(T(0)));
        Assert.False(flip.IsMoving(T(0)));
    }

    [Fact]
    [Trait("Decision", "ED-292")]
    public void A_word_whose_place_changes_glides_there_on_ez_over_0_3_s_frame_by_frame()
    {
        var flip = new DictationFlip();
        flip.Place(new Point(10, 4), T(0), still: false);
        flip.Place(new Point(50, 24), T(1), still: false); // wrapped: down a line and to the right
        Near(new Point(10, 4), flip.Displayed(T(1)), "at the change");
        for (var k = 0; 1 + k * Frame <= 1.4; k++)
        {
            var t = 1 + k * Frame;
            var e = CssCurves.At(CssCurves.Ez, t - 1, 0.3);
            Near(new Point(10 + 40 * e, 4 + 20 * e), flip.Displayed(T(t)), $"at {t - 1:F3} s");
        }
        Assert.Equal(new Point(50, 24), flip.Displayed(T(1.3)));
        Assert.False(flip.IsMoving(T(1.3)));
        Assert.True(flip.IsMoving(T(1.29)));
    }

    [Fact]
    [Trait("Decision", "ED-292")]
    public void A_place_changed_in_flight_goes_on_from_where_the_word_is_drawn()
    {
        var flip = new DictationFlip();
        flip.Place(new Point(0, 0), T(0), still: false);
        flip.Place(new Point(100, 0), T(1), still: false);
        var mid = flip.Displayed(T(1.1));
        Near(new Point(100 * CssCurves.At(CssCurves.Ez, 0.1, 0.3), 0), mid, "mid-flight");
        flip.Place(new Point(60, 30), T(1.1), still: false);
        Near(mid, flip.Displayed(T(1.1)), "at the second change: no jump");
        for (var k = 1; 1.1 + k * Frame <= 1.5; k++)
        {
            var t = 1.1 + k * Frame;
            var e = CssCurves.At(CssCurves.Ez, t - 1.1, 0.3);
            Near(new Point(mid.X + (60 - mid.X) * e, mid.Y + (30 - mid.Y) * e), flip.Displayed(T(t)), $"at {t - 1.1:F3} s");
        }
    }

    [Fact]
    [Trait("Decision", "ED-292")]
    public void A_change_of_less_than_a_hundredth_of_a_pixel_is_not_a_move()
    {
        var flip = new DictationFlip();
        flip.Place(new Point(10, 4), T(0), still: false);
        flip.Place(new Point(10.004, 4), T(1), still: false);
        Assert.False(flip.IsMoving(T(1)));
        Assert.Equal(new Point(10, 4), flip.Displayed(T(1.1)));
    }

    [Fact]
    [Trait("Decision", "ED-292")]
    public void Under_reduce_motion_the_word_is_where_the_layout_puts_it_at_once()
    {
        var flip = new DictationFlip();
        flip.Place(new Point(10, 4), T(0), still: true);
        flip.Place(new Point(50, 24), T(1), still: true);
        Assert.Equal(new Point(50, 24), flip.Displayed(T(1)));
        Assert.False(flip.IsMoving(T(1)));
    }
}
