// PORT-ONLY: OD-0136 amendment 2, step 9 "Send button" (ED-NEW-nth-motion), with the owner's answer: "the stop and arrow inside
// the circle needs to be enlarged". The 32 px disc (was 28): a soft vertical gradient --stop to --sbot, a 1 px inner top rim
// (--prim), a hairline edge (--edge) and a small shadow (0 2 6 -2 --pshadow). Its glyph, in --on, is drawn in a 20 px box (the
// page's 16 scaled 1.25 for the owner's "enlarge", the page's own paths, so the optical centring is the page's). Send: the arrow
// flies up and out (clipped by the circle) and the stop square pops in; while working the square breathes; stop: the square
// shrinks and the arrow drops back. Press: scale 0.9. Nothing else moves (no ring, no glow, no shine on the disc).
using Avalonia;
using SkiaSharp;

namespace Marrow.UI.Port.Nth;

public sealed record NthSendState(bool Running, double ChangedAt, bool Pressed, double PressedAt, double Dim);

/// <summary>The send button's face (step 9). The site's button around it handles the click.</summary>
public sealed class NthSendFace : NthMotionControl
{
    /// <summary>The disc's diameter (step 9: 32, was 28).</summary>
    public const double Size = 32;

    /// <summary>The glyph's box: the page's 16 px enlarged 1.25 times at the owner's request.</summary>
    public const double GlyphBox = 20;

    private bool running, pressed;
    private double changedAt = double.NegativeInfinity, pressedAt = double.NegativeInfinity, dim = 1;

    public NthSendFace() { Width = Size; Height = Size; }

    /// <summary>Working (the stop square) or not (the arrow). A change animates once the face is shown.</summary>
    public bool IsRunning
    {
        get => running;
        set { if (value == running) return; running = value; changedAt = VisualRoot is null ? double.NegativeInfinity : Now; Send(); }
    }

    /// <summary>Held down: the face scales to 0.9.</summary>
    public bool IsPressed
    {
        get => pressed;
        set { if (value == pressed) return; pressed = value; pressedAt = Now; Send(); }
    }

    /// <summary>How much of itself the face keeps (a disabled button: 0.5).</summary>
    public double Dim { get => dim; set { if (value == dim) return; dim = value; Send(); } }

    protected override object? State => new NthSendState(running, changedAt, pressed, pressedAt, dim);

    protected override NthPainter CreatePainter() => new NthSendPainter();
}

public sealed class NthSendPainter : NthSkiaPainter
{
    private readonly SKPaint paint = new() { IsAntialias = true };
    private readonly SKPaint layer = new();
    private readonly SKPath arrow = SKPath.ParseSvgPathData("M8 13V3.6M3.9 7.6L8 3.5l4.1 4.1");
    private readonly SKPath disc = new(), lowered = new();
    private readonly SKMaskFilter shadowBlur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3);
    private SKShader? fill;
    private (double Size, bool Dark) fillFor = (-1, false);

    /// <summary>The arrow's stroke in the 20 px box (the page's 1.9 in 16, kept a little lighter than a straight 1.25 scale).</summary>
    public const float ArrowStroke = 2.2f;

    public override bool Moving(NthFrame frame, double now) =>
        frame.State is NthSendState s && (s.Running && !frame.Calm || now - s.ChangedAt < .7 || now - s.PressedAt < .2);

    public override Rect Bounds(NthFrame frame) => new Rect(frame.Size).Inflate(new Thickness(4, 4, 4, 8));

    public override void Draw(SKCanvas canvas, NthFrame frame, double now)
    {
        if (frame.State is not NthSendState s) return;
        var size = Math.Min(frame.Size.Width, frame.Size.Height);
        if (size <= 0) return;
        var palette = frame.Palette;
        float cx = (float)(frame.Size.Width / 2), cy = (float)(frame.Size.Height / 2), r = (float)(size / 2);
        var scale = (float)NthMotion.Press(now - s.PressedAt, s.Pressed);
        canvas.Save();
        canvas.Translate(cx, cy);
        canvas.Scale(scale);
        var saved = -1;
        if (s.Dim < 1) { layer.Color = SKColors.White.WithAlpha((byte)Math.Round(255 * s.Dim)); saved = canvas.SaveLayer(layer); }
        // shadow: 0 2px 6px -2px
        paint.Shader = null; paint.Style = SKPaintStyle.Fill; paint.MaskFilter = shadowBlur; paint.Color = palette.Shadow;
        canvas.DrawCircle(0, 2, r - 2, paint);
        paint.MaskFilter = null;
        if (fill is null || fillFor != (size, frame.Dark))
        {
            fill?.Dispose();
            fill = SKShader.CreateLinearGradient(new SKPoint(0, -r), new SKPoint(0, r), [palette.Stop, palette.StopBottom], null, SKShaderTileMode.Clamp);
            fillFor = (size, frame.Dark);
        }
        paint.Shader = fill; paint.Color = SKColors.White;
        canvas.DrawCircle(0, 0, r, paint);
        paint.Shader = null;
        disc.Rewind(); disc.AddCircle(0, 0, r);
        lowered.Rewind(); lowered.AddCircle(0, 1, r);
        canvas.Save();
        canvas.ClipPath(disc, SKClipOperation.Intersect, true);
        // inset 0 1px 0 --prim: the disc less itself moved 1 px down, a crescent 1 px deep at the top
        canvas.Save();
        canvas.ClipPath(lowered, SKClipOperation.Difference, true);
        paint.Style = SKPaintStyle.Fill; paint.Color = palette.Rim;
        canvas.DrawCircle(0, 0, r, paint);
        canvas.Restore();
        // inset 0 0 0 .5px --edge
        paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = 1; paint.Color = palette.Edge;
        canvas.DrawCircle(0, 0, r, paint);
        DrawGlyphs(canvas, s, palette.On, frame.Calm, now, frame.Started);
        canvas.Restore();
        if (saved >= 0) canvas.RestoreToCount(saved);
        canvas.Restore();
    }

    private void DrawGlyphs(SKCanvas canvas, NthSendState s, SKColor on, bool calm, double now, double started)
    {
        // a face shown already working breathes from when it was shown, its square already in
        var shownWorking = double.IsNegativeInfinity(s.ChangedAt);
        var since = shownWorking ? now - started + .62 : now - s.ChangedAt;
        var k = (float)(NthSendFace.GlyphBox / 16);
        // the arrow: still, launching (running) or dropping back (stopped)
        var (ay, aa) = s.Running ? NthMotion.ArrowLaunch(since, calm) : shownWorking ? (0, 1) : NthMotion.ArrowDrop(since, calm);
        if (aa > 0)
        {
            canvas.Save();
            canvas.Translate(0, (float)ay);
            canvas.Scale(k);
            canvas.Translate(-8, -8);
            paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = ArrowStroke / k; paint.StrokeCap = SKStrokeCap.Round; paint.StrokeJoin = SKStrokeJoin.Round;
            paint.Color = on.WithAlpha((byte)Math.Round(255 * NthEase.Clamp01(aa)));
            canvas.DrawPath(arrow, paint);
            canvas.Restore();
        }
        // the stop square: popping in and breathing (running) or shrinking away (stopped)
        var (sc, sa) = s.Running ? NthMotion.StopIn(since, calm) : shownWorking ? (1, 0) : NthMotion.StopOut(since, calm);
        if (sa <= 0) return;
        var half = 3.5f * k * (float)sc;
        paint.Style = SKPaintStyle.Fill;
        paint.Color = on.WithAlpha((byte)Math.Round(255 * NthEase.Clamp01(sa)));
        var corner = 1.8f * k * (float)sc;
        canvas.DrawRoundRect(new SKRect(-half, -half, half, half), corner, corner, paint);
    }

    public override void Dispose()
    {
        paint.Dispose(); layer.Dispose(); arrow.Dispose(); disc.Dispose(); lowered.Dispose(); shadowBlur.Dispose(); fill?.Dispose();
        base.Dispose();
    }
}
