// PORT-ONLY CZ-1: live 56px style card glyphs, DESIGN gen.py:391-397 (CZ-5.1/3).
using Avalonia;
using Avalonia.Controls;
using Marrow.Core.Port;
namespace Marrow.UI.Port.Settings;

public sealed class CzStyleGlyph : Control
{
    public string Style { get; }
    public double Period => Style == "beam" ? 3.4 : Style == "aura" ? 9 : Style == "off" ? 0 : 12; // OD-0150: a shader light's glyph turns slowly
    private readonly Action<TimeSpan> frame;
    private IFrameSource? source;
    private ContinuousInstant began;
    private double angle;
    private bool waiting;
    private bool inViewport = true;
    public CzStyleGlyph(string style)
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        if (style is not ("beam" or "aura" or "off") && Marrow.Core.Models.ComposerLights.ShaderLightCatalog.Find(style) is null) throw new ArgumentOutOfRangeException(nameof(style));
        Style = style; Height = 56; ClipToBounds = true; frame = Tick;
        EffectiveViewportChanged += (_, e) => { inViewport = e.EffectiveViewport.Intersects(new Rect(Bounds.Size)); if (inViewport) Request(); };
        AttachedToVisualTree += (_, _) => { began = SwiftClock.Now; Request(); };
        DetachedFromVisualTree += (_, _) => { if (waiting) source?.CancelFrame(frame); waiting = false; };
        PropertyChanged += (_, e) => { if (e.Property.Name is "IsVisible" or "AccessibilityReduceMotion") Request(); };
    }
    private bool VisibleOnScreen()
    {
        if (!inViewport || !IsEffectivelyVisible || TopLevel.GetTopLevel(this) is not { } top || Bounds.Width <= 0) return false;
        var at = this.TranslatePoint(default, top);
        return at.HasValue && new Rect(at.Value, Bounds.Size).Intersects(new Rect(top.Bounds.Size));
    }
    private void Request()
    {
        if (waiting || Period == 0 || !FrameClock.IsBound || EnvironmentValues.GetAccessibilityReduceMotion(this) || !VisibleOnScreen()) return;
        source = FrameClock.Source.Current; waiting = true; source.RequestFrame(frame);
    }
    private void Tick(TimeSpan _)
    {
        waiting = false;
        if (!VisibleOnScreen() || EnvironmentValues.GetAccessibilityReduceMotion(this)) return;
        angle = ((SwiftClock.Now - began).TotalSeconds / Period * 360) % 360; InvalidateVisual(); Request();
    }
    protected override Size ArrangeOverride(Size finalSize) { Request(); return finalSize; }
    private readonly Stack<CzStyleGlyphDraw> spare = new();
    private void GiveBack(CzStyleGlyphDraw operation) { lock (spare) spare.Push(operation); }

    /// <summary>This frame's drawing operation: one Avalonia has finished with, made the next frame's (CZ-T44: no new object per frame once warm).</summary>
    internal CzStyleGlyphDraw NextOperation()
    {
        CzStyleGlyphDraw? operation = null;
        lock (spare) if (spare.Count > 0) operation = spare.Pop();
        operation ??= new CzStyleGlyphDraw(GiveBack);
        operation.Reuse(new Rect(Bounds.Size), Style, angle, CzTheme.Dark(this));
        return operation;
    }

    public override void Render(Avalonia.Media.DrawingContext context) => context.Custom(NextOperation());
}
