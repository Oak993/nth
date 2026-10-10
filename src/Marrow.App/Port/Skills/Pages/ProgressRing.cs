// PORT-ONLY: first-release feature unit SK-15 (DL-17), moved onto the nth motion pack step 4 (OD-0136 amendment 2, ED-NEW-nth-motion). The determinate ring of a row that downloads: the nth ring, 14 points
// by default (the pack's sizes are 14, 18 and 20), a track under a mark arc that starts at 12 o'clock and runs clockwise, on the pack's spring; at 100 % it can fill into the solid disc with its tick.
using Avalonia;
using Avalonia.Controls;
using Marrow.UI.Port.Nth;

namespace Marrow.App.Port.Skills.Pages;

/// <summary>A ring at <see cref="Value"/> (0 to 1), as wide and tall as it is given (14 by default). The arc springs to a new value and never runs backwards across the ring.</summary>
internal sealed class ProgressRing : Decorator
{
    private readonly NthRing ring = new() { Width = 14, Height = 14 };

    public ProgressRing()
    {
        Width = Height = 14;
        IsHitTestVisible = false;
        Child = ring;
    }

    /// <summary>The primitive (tests and pictures).</summary>
    internal NthRing Ring => ring;

    /// <summary>The arc the ring is going to (0 to 1).</summary>
    public double Value
    {
        get => ring.Value;
        set
        {
            var clamped = Math.Clamp(double.IsFinite(value) ? value : 0, 0, 1);
            if (clamped < ring.Value) ring.Jump(clamped); // a new download starts from nothing
            else ring.Value = clamped;
        }
    }

    /// <summary>At 100 % the ring fills into the solid disc and its tick draws in (a row whose download is complete).</summary>
    public bool IsDone { get => ring.IsDone; set => ring.IsDone = value; }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WidthProperty) ring.Width = double.IsNaN(Width) ? 14 : Width;
        else if (change.Property == HeightProperty) ring.Height = double.IsNaN(Height) ? 14 : Height;
    }
}
