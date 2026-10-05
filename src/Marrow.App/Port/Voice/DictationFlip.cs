// PORT-ONLY, owner-ruled addition (OD-0091, OD-0076; first-release voice typing, VT-11, voice-SPEC V-7.5): FLIP for the words after a change.
using Avalonia;
using Marrow.Core.Port;

namespace Marrow.App.Port.Voice;

/// <summary>
/// Where one word is drawn: the place the layout gives it, reached from where it was drawn along the design's ez over .3 s (V-7.5: later words follow their new positions with the
/// same ease, so a wrap never jumps). The first place is taken as it is; a later place that differs by a hundredth of a pixel or more starts a move from the place drawn now, and a
/// place that changes in flight goes on from where the word is.
/// </summary>
internal sealed class DictationFlip
{
    private const double Tiny = 0.01;

    private bool placed;
    private Point target;
    private DictationTween x;
    private DictationTween y;

    public Point Target => target;

    public void Place(Point place, ContinuousInstant now, bool still)
    {
        if (!placed)
        {
            placed = true;
            target = place;
            x = new DictationTween(place.X);
            y = new DictationTween(place.Y);
            return;
        }
        if (Math.Abs(place.X - target.X) < Tiny && Math.Abs(place.Y - target.Y) < Tiny) return;
        target = place;
        x.Go(place.X, now, DictationMotion.Flip, still);
        y.Go(place.Y, now, DictationMotion.Flip, still);
    }

    /// <summary>The place the word is drawn at <paramref name="now"/>.</summary>
    public Point Displayed(ContinuousInstant now) => new(x.Value(now), y.Value(now));

    public bool IsMoving(ContinuousInstant now) => x.Running(now) || y.Running(now);
}
