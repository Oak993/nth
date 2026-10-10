// PORT-ONLY: the voice preparation pill's ring, moved onto the nth motion pack step 4 (OD-0136 amendment 2, ED-NEW-nth-motion): the nth ring at 18 (the pack's sizes are 14, 18 and 20; the old locked qring was
// 16), a track under a mark arc from 12 o'clock, clockwise, on the pack's spring. Real progress only.
using Avalonia;
using Avalonia.Controls;
using Marrow.App.Port.Updates;
using Marrow.UI.Port.Nth;

namespace Marrow.App.Port.Voice;

internal sealed class VoicePreparationRing : Decorator
{
    internal const double Size = 18;

    private readonly NthRing ring = new() { Width = Size, Height = Size };

    internal VoicePreparationRing()
    {
        IsHitTestVisible = false;
        Child = ring;
    }

    /// <summary>The primitive (tests and pictures).</summary>
    internal NthRing Ring => ring;

    /// <summary>The arc as drawn now; set it to spring to a new fraction (a new download starts from nothing: the arc never runs backwards).</summary>
    internal double Fraction
    {
        get => ring.Drawn();
        set
        {
            var clamped = Math.Clamp(value, 0, 1);
            if (clamped < ring.Value) ring.Jump(clamped); else ring.Value = clamped;
        }
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);
}
