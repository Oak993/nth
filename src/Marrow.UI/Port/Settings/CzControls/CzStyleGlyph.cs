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
    private ContinuousInstant? nextStep;
    private WindowPresence? presence;
    private double angle;
    private bool waiting;
    private bool inViewport = true;
    public CzStyleGlyph(string style)
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        if (style is not ("beam" or "aura" or "off") && Marrow.Core.Models.ComposerLights.ShaderLightCatalog.Find(style) is null) throw new ArgumentOutOfRangeException(nameof(style));
        Style = style; Height = 56; ClipToBounds = true; frame = Tick;
        EffectiveViewportChanged += (_, e) => { inViewport = e.EffectiveViewport.Intersects(new Rect(Bounds.Size)); Request(); };
        AttachedToVisualTree += (_, _) =>
        {
            began = SwiftClock.Now; nextStep = null;
            presence = WindowPresence.For(this);
            if (presence is not null) presence.Changed += Request;
            Request();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (presence is not null) presence.Changed -= Request;
            presence = null;
            if (waiting) source?.CancelFrame(frame);
            waiting = false; nextStep = null;
        };
        PropertyChanged += (_, e) => { if (e.Property.Name is "IsVisible" or "AccessibilityReduceMotion") Request(); };
    }
    private bool VisibleOnScreen()
    {
        if (!inViewport || !IsEffectivelyVisible || presence?.Seen == false || TopLevel.GetTopLevel(this) is not { } top || Bounds.Width <= 0) return false;
        var at = this.TranslatePoint(default, top);
        return at.HasValue && new Rect(at.Value, Bounds.Size).Intersects(new Rect(top.Bounds.Size));
    }
    private void Request()
    {
        if (Period == 0 || !FrameClock.IsBound || EnvironmentValues.GetAccessibilityReduceMotion(this) || !VisibleOnScreen())
        {
            if (waiting) source?.CancelFrame(frame);
            waiting = false; nextStep = null;
            return;
        }
        if (waiting) return;
        source = FrameClock.Source.Current; waiting = true;
        // ED-NEW-smooth2-6: the same elapsed-time pose at the decoration's 60 Hz cap, with a parked draw-loop request.
        if (source is IWakeFrameSource loops)
        {
            // One refresh of lead preserves the due frame, including 144 Hz's two/three-refresh slots.
            // WakeFor's conservative 1.5-refresh lead asks on every refresh at 144 Hz.
            var wake = nextStep is { } due && loops.WakePeriod is { } period && period > TimeSpan.Zero
                ? due - period : SwiftClock.Now;
            loops.RequestFrameAt(wake, frame);
        }
        else source.RequestFrame(frame);
    }
    private void Tick(TimeSpan _)
    {
        waiting = false;
        if (!VisibleOnScreen() || EnvironmentValues.GetAccessibilityReduceMotion(this)) return;
        var now = SwiftClock.Now;
        // ED-117/ED-232: anchored slots, with the existing 1 ms completion slack. Counting from the
        // last callback instead dropped alternate frames near 60 Hz and a fifth refresh at 240 Hz.
        if (nextStep is { } due && now + TimeSpan.FromMilliseconds(1) < due) { Request(); return; }
        var interval = TimeSpan.FromSeconds(1.0 / 60);
        var slot = nextStep ?? now;
        var missed = Math.Max(0, (now - slot).Ticks / interval.Ticks);
        nextStep = slot + TimeSpan.FromTicks((missed + 1) * interval.Ticks);
        angle = ((now - began).TotalSeconds / Period * 360) % 360; InvalidateVisual(); Request();
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
