// PORT-ONLY CZ-1, CZ-4 (CZ-T44): one frame of a style glyph, drawn on the render thread. A glyph keeps a few of these and takes one back when Avalonia disposes it (Avalonia disposes a drawing operation only once it will
// not be drawn again: on the UI thread before it is sent, on the render thread when the next frame's drawing replaces it), so a frame makes no new operation (the pattern of JewelKnobOperation).
using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
namespace Marrow.UI.Port.Settings;

internal sealed class CzStyleGlyphDraw(Action<CzStyleGlyphDraw> giveBack) : ICustomDrawOperation
{
    private readonly CzStyleGlyphPainter painter = new();
    private int given;
    private string style = "off";
    private double angle;
    private bool dark;

    public Rect Bounds { get; private set; }

    /// <summary>Makes a returned operation the next frame's.</summary>
    internal void Reuse(Rect bounds, string style, double angle, bool dark)
    {
        Bounds = bounds;
        this.style = style;
        this.angle = angle;
        this.dark = dark;
        Volatile.Write(ref given, 0);
    }

    public bool HitTest(Point point) => false;
    public bool Equals(ICustomDrawOperation? other) => false;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref given, 1) == 0) giveBack(this);
    }

    public void Render(ImmediateDrawingContext context)
    {
        var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (feature is null) return;
        using var lease = feature.Lease();
        painter.Draw(lease.SkCanvas, (float)Bounds.Width, style, angle, dark, (float)lease.CurrentOpacity);
    }
}
