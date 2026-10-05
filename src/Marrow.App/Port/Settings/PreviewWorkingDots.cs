// PORT-ONLY CZ-3 (customization-SPEC CZ-4.3; ComposerView.swift:659-675 ComposerWorkingDotsSlot): the working dots along the preview composer's bottom two-fifths, clipped to the
// pill's shape, faded in and out over 0.4 s ease-in-out as the turn starts and ends. The real slot reads a thread's run flag; the preview's is the chrome control's Running.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.App.Views.Composer;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Port.Settings;

internal sealed class PreviewWorkingDots : Decorator
{
    private readonly Panel panel = new();
    private Control? current;
    private ComposerWorkingDots? shownDots;
    private SwiftColor tint = Chrome.Accent;

    /// <summary>Whether the dots are in (or arriving).</summary>
    internal bool Shown => current is not null;

    /// <summary>How opaque the arriving dots are now (0.4 s ease-in-out); 0 when they are not in.</summary>
    internal double Opacity01 => current?.Opacity ?? 0;

    /// <summary>CZ-5 (CZ-10.4): the colour the dots wear, the workingDots setting's for the preview maker; a running field blends to it over 0.32 s, dots that arrive later wear it from the start.</summary>
    internal SwiftColor Tint
    {
        get => tint;
        set
        {
            tint = value;
            if (shownDots is not null) shownDots.Tint = value;
        }
    }

    internal PreviewWorkingDots()
    {
        IsHitTestVisible = false;
        Child = new Border { Child = panel, ClipToBounds = true }.RoundedRectangle(22, RoundedCornerStyle.Continuous).CircularInBand(); // :671
    }

    internal void Show(bool running)
    {
        if (running && current is null)
        {
            var dots = shownDots = new ComposerWorkingDots { VerticalAlignment = VerticalAlignment.Bottom, Tint = tint }; // :667-669
            var geometry = new Grid { Children = { dots } };
            geometry.SizeChanged += (_, e) => dots.Height = e.NewSize.Height * 0.4; // :668
            current = geometry.Transition(SwiftTransition.Opacity.Animation(Animation.EaseInOut(0.4))); // :672
            SwiftTransition.Insert(panel, 0, current);
        }
        else if (!running && current is { } leaving)
        {
            SwiftTransition.Remove(panel, leaving);
            current = null;
            shownDots = null;
        }
    }
}
