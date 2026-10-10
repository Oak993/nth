// PORT-ONLY, first-release UE-4 (OD-0100 place A, OD-0076), nth motion pack step 3 option B (OD-0136 amendment 2, ED-NEW-nth-motion). The bar under a running row: the nth progress bar, 4 px. Marrow's download has
// a percentage (Velopack reports one), so its fill springs to it; a CLI's own updater reports none, so its row shows the unknown-length bar (a line that grows from the left and whose tail catches up)
// and no invented percentage. Under Reduce Motion the unknown bar is the still middle segment whose opacity eases (the pack's calm rule). The drawing and its clock are the primitive's (render thread);
// this class keeps the row's API: Set, Active, Indeterminate, TargetFraction, Fraction, Moves.
using Avalonia;
using Avalonia.Controls;
using Marrow.UI.Port.Nth;

namespace Marrow.App.Port.Settings;

internal sealed class UpdatesProgressBar : Decorator
{
    public const double BarHeight = 4;

    private readonly NthProgressBar bar = new() { Height = BarHeight };
    private bool indeterminate = true;
    private bool active;

    public UpdatesProgressBar()
    {
        IsHitTestVisible = false;
        Child = bar;
    }

    /// <summary>The bar the row shows (tests and pictures).</summary>
    internal NthProgressBar Bar => bar;

    /// <summary>True while the bar has no amount to show and so shows the unknown-length bar.</summary>
    public bool Indeterminate => indeterminate;

    /// <summary>The fill the bar is springing to (0 to 1) while it is not indeterminate.</summary>
    public double TargetFraction => bar.Value;

    /// <summary>The fill drawn now.</summary>
    public double Fraction => bar.Shown;

    /// <summary>True while the light or the unknown-length bar travels (false when still under Reduce Motion, detached, folded shut, or full).</summary>
    public bool Moves => active && TopLevel.GetTopLevel(bar) is not null && !bar.Calm && (indeterminate || bar.Value is > 0 and < .999);

    /// <summary>Whether the bar is on show. A bar folded shut is not drawn, so it asks for no frames.</summary>
    public bool Active
    {
        get => active;
        set
        {
            if (active == value) return;
            active = value;
            bar.IsIndeterminate = indeterminate && active; // a bar that is not on show shows only its track
        }
    }

    /// <summary>Whether the bar is drawn at all: the row hides it once its fold has shut (a closed fold would otherwise keep a light passing).</summary>
    internal void SetDrawn(bool drawn) => bar.IsVisible = drawn;

    /// <summary>The amount, or null when the updater reports none.</summary>
    public void Set(double? progress)
    {
        if (progress is null)
        {
            if (indeterminate) return;
            indeterminate = true;
            bar.IsIndeterminate = active;
            return;
        }
        var clamped = Math.Clamp(progress.Value, 0, 1);
        var was = indeterminate;
        indeterminate = false;
        bar.IsIndeterminate = false;
        if (was) bar.Jump(0);
        bar.Value = clamped;
    }
}
