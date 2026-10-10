// PORT-ONLY: first-release feature unit UE-5 (OD-0100 place D), moved onto the nth motion pack (OD-0136 amendment 2, ED-NEW-nth-motion): the two small drawings of the update pill, the run's ring (step 4) and a row's bar (step 3
// option B, 3 px as step 10 draws the sidebar bar). Both are the pack's primitives (render thread); these classes keep the pill's API.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;

namespace Marrow.App.Port.Updates;

/// <summary>
/// The run's progress ring in the pill: the nth ring at 18 pt (step 4: 2 pt stroke, track under a mark arc with a round cap that starts at 12 o'clock and grows clockwise, on the pack's spring). The arc springs to a new
/// fraction (<see cref="Set"/>); with no animation it is there at once. The pill has its own marks for the phases after the run (the check, the restart arrow), so the ring keeps no done disc of its own.
/// </summary>
internal sealed class UpdateRing : Decorator
{
    public const double Size = 18;

    private readonly NthRing ring = new() { Width = Size, Height = Size };

    public UpdateRing()
    {
        IsHitTestVisible = false;
        Child = ring;
    }

    /// <summary>The primitive (tests and pictures).</summary>
    internal NthRing Ring => ring;

    /// <summary>The fraction drawn now (0 to 1).</summary>
    public double Fraction => ring.Drawn();

    /// <summary>The fraction the ring is going to.</summary>
    public double Target => ring.Value;

    public void Set(double target, Animation? animation)
    {
        var clamped = Math.Clamp(target, 0, 1);
        if (animation is null || clamped < ring.Value) ring.Jump(clamped); // a new run starts from nothing: the arc never runs backwards
        else ring.Value = clamped;
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);
}

/// <summary>
/// A row's bar in the popover: the nth progress bar, 3 px (step 3 option B; step 10 uses the 3 px bar for the sidebar). With a value the fill springs to it and the light passes inside it; with none (a CLI's own updater reports
/// no progress and the design page says no percentage is invented) the unknown-length bar runs while the bar is <see cref="Active"/>; under reduce motion it is the still middle segment whose opacity eases.
/// </summary>
internal sealed class UpdateProgressBar : Decorator
{
    public const double TrackHeight = 3;

    private readonly NthProgressBar bar = new() { Height = TrackHeight };
    private bool active;

    public UpdateProgressBar()
    {
        IsHitTestVisible = false;
        Child = bar;
    }

    /// <summary>The primitive (tests and pictures).</summary>
    internal NthProgressBar Bar => bar;

    /// <summary>The fill's share of the track drawn now (0 while the amount is not known).</summary>
    public double Shown => Target is null ? 0 : bar.Shown;

    /// <summary>The value the bar is going to; null while the amount is not known.</summary>
    public double? Target { get; private set; }

    /// <summary>The bar is on show (its row is updating): only then does an unknown amount run. A row that is not updating takes no frames.</summary>
    public bool Active
    {
        get => active;
        set
        {
            active = value;
            bar.IsIndeterminate = Target is null && active;
        }
    }

    /// <summary>Whether the unknown-length bar is running (it asks for frames).</summary>
    internal bool IsSweeping => Target is null && active && bar.IsIndeterminate;

    public void Set(double? target, Animation? animation)
    {
        Target = target is { } t ? Math.Clamp(t, 0, 1) : null;
        if (Target is { } known)
        {
            bar.IsIndeterminate = false; // a known amount needs no unknown-length bar
            if (animation is null || known < bar.Value) bar.Jump(known);
            else bar.Value = known;
        }
        else bar.IsIndeterminate = active;
    }

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, TrackHeight);
}
