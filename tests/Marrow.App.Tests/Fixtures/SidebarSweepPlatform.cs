using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

// Recording only: SidebarView.swift:1933-1978 observes both render results before launching the row ghost.
public sealed class SidebarSweepPlatform(int? failAt = null) : IGeniePlatform
{
    public bool AccessibilityDisplayShouldReduceMotion => false;
    public double? MainScreenBackingScaleFactor => 2;
    public List<(Control Content, double Scale)> Renders { get; } = [];
    public IImage? RenderImage(Control content, double scale)
    {
        Renders.Add((content, scale));
        if (Renders.Count == failAt) return null;
        var face = ((ThemeVariantScope)content).Child!;
        return new FaceSample(face.Width, face.Height);
    }
}
