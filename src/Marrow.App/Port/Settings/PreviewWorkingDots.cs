// PORT-ONLY CZ-3 (customization-SPEC CZ-4.3; ComposerView.swift:659-675 ComposerWorkingDotsSlot): the working dots along the preview composer's bottom two-fifths, clipped to the
// pill's shape, faded in and out over 0.4 s ease-in-out as the turn starts and ends. The real slot reads a thread's run flag; the preview's is the chrome control's Running.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.App.Views.Composer;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Port.Settings;

internal sealed class PreviewWorkingDots : Decorator
{
    private readonly Panel panel = new();
    private Control? current;

    /// <summary>Whether the dots are in (or arriving).</summary>
    internal bool Shown => current is not null;

    internal PreviewWorkingDots()
    {
        IsHitTestVisible = false;
        Child = new Border { Child = panel, ClipToBounds = true }.RoundedRectangle(22, RoundedCornerStyle.Continuous).CircularInBand(); // :671
    }

    internal void Show(bool running)
    {
        if (running && current is null)
        {
            var dots = new ComposerWorkingDots { VerticalAlignment = VerticalAlignment.Bottom }; // :667-669
            var geometry = new Grid { Children = { dots } };
            geometry.SizeChanged += (_, e) => dots.Height = e.NewSize.Height * 0.4; // :668
            current = geometry.Transition(SwiftTransition.Opacity.Animation(Animation.EaseInOut(0.4))); // :672
            SwiftTransition.Insert(panel, 0, current);
        }
        else if (!running && current is { } leaving)
        {
            SwiftTransition.Remove(panel, leaving);
            current = null;
        }
    }
}
