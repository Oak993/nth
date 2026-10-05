using Avalonia;
using Avalonia.Media;
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

    public void Dispose() => Rig.Dispose();
}
