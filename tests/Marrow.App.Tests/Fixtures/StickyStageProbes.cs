using Avalonia;
using Avalonia.Media;
using Avalonia.Input;
using Marrow.App.Port.Settings;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// The Customization page in a scroll viewer (CZ-6) with the two parts the later units add stood in for: a section at the end of the sticky container (CZ-5's Working dots, 300 high) and one in the rest after it (CZ-11's Speech, 900 high).
/// Both are counting probes with a fill so a hit test lands on them.
/// </summary>
internal sealed class StickyStageProbes : IDisposable
{
    internal CustomizationPageRig Rig { get; }
    internal MeasureProbe WorkingDots { get; }
    internal MeasureProbe Speech { get; }

    internal StickyStageProbes(bool reduceMotion, double height = 700)
    {
        Rig = new CustomizationPageRig(reduceMotion: reduceMotion, scroll: true, height: height);
        WorkingDots = new MeasureProbe { Height = 300, Background = Brushes.Transparent, Tag = "Working dots" };
        Speech = new MeasureProbe { Height = 900, Background = Brushes.Transparent, Tag = "Speech" };
        Rig.Do(() =>
        {
            Rig.Page.Sticky.Children.Add(WorkingDots);
            Rig.Page.Rest.Children.Clear(); // the stand-in geometry is the Speech probe's alone: the pill section (step 3a) is not part of what the sticky tests measure
            Rig.Page.Rest.Children.Add(Speech);
        });
    }

    /// <summary>The sticky container's laid-out height: H_c.</summary>
    internal double ContainerHeight => Rig.Get(() => Rig.Page.Sticky.Bounds.Height);

    /// <summary>The most the viewer can scroll.</summary>
    internal double MaxOffset => Rig.Get(() => Rig.Scroll!.Extent.Height - Rig.Scroll.Viewport.Height);

    /// <summary>Where a visual's top edge is now, in the viewer's own coordinates (a visual's transforms included).</summary>
    internal double ViewportTop(Avalonia.Visual visual) => Rig.Get(() => Top(visual));

    /// <summary><see cref="ViewportTop"/> for a caller already on the main loop.</summary>
    internal double Top(Avalonia.Visual visual) => visual.TranslatePoint(default, Rig.Scroll!)!.Value.Y;

    /// <summary>What a click at <paramref name="point"/> in the viewer would reach, after the compositor has taken the layout and the clips of this moment (a hit test reads the composition's visuals).</summary>
    internal Avalonia.Input.IInputElement? HitAt(Point point) => Rig.Get(() =>
    {
        // two ticks: the first hands this moment's layout to the compositor, the second lets its hit test see it
        for (var tick = 0; tick < 2; tick++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        return Rig.Scroll!.InputHitTest(point);
    });

    public void Dispose() => Rig.Dispose();
}
