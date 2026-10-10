// PORT-ONLY: OD-0136 amendment 2, step 11 "Connecting to Codex" (ED-NEW-nth-motion). A dashed --mark line, 1.6 px, dash 2.5 /
// gap 4.5, round caps, flowing 14 px/s toward Codex (left to right), its ends ramping 0 to 1 over the first and last 18 % (one
// linear shader). Connected: the gaps close in 0.35 s (.4,0,.2,1) and the line is solid. Calm: the dashes stand still.
// The dashes are built into one reused path each frame (no dash path effect is made per frame).
using Avalonia;
using SkiaSharp;

namespace Marrow.UI.Port.Nth;

public sealed record NthLinkState(double? ConnectedAt);

public sealed class NthLink : NthMotionControl
{
    private double? connectedAt;

    public NthLink() => Height = 16;

    public bool IsConnected
    {
        get => connectedAt is not null;
        set { if (value == IsConnected) return; connectedAt = value ? Now : null; Send(); }
    }

    protected override object? State => new NthLinkState(connectedAt);

    protected override NthPainter CreatePainter() => new NthLinkPainter();
}

public sealed class NthLinkPainter : NthSkiaPainter
{
    private readonly SKPaint paint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeWidth = 1.6f };
    private readonly SKPath path = new();
    private SKShader? ramp;
    private (double W, bool Dark) rampFor = (-1, false);

    public override bool Moving(NthFrame frame, double now) =>
        frame.State is NthLinkState s && (s.ConnectedAt is not { } c ? !frame.Calm : now - c < .35);

    public override void Draw(SKCanvas canvas, NthFrame frame, double now)
    {
        if (frame.State is not NthLinkState s) return;
        double w = frame.Size.Width, h = frame.Size.Height;
        if (w <= 6) return;
        if (ramp is null || rampFor != (w, frame.Dark))
        {
            ramp?.Dispose();
            var m = frame.Palette.Mark;
            ramp = SKShader.CreateLinearGradient(new SKPoint(2, 0), new SKPoint((float)(w - 2), 0), [m.WithAlpha(0), m, m, m.WithAlpha(0)], [0, .18f, .82f, 1], SKShaderTileMode.Clamp);
            rampFor = (w, frame.Dark);
        }
        var (dash, gap, phase) = NthMotion.Link(now - frame.Started, s.ConnectedAt is { } c ? now - c : null, frame.Calm);
        var y = (float)(h / 2);
        double x0 = 3, x1 = w - 3, period = dash + gap;
        path.Rewind();
        // the page's lineDashOffset = -phase: the pattern moves right (toward Codex) by phase
        for (var start = x0 - period + phase % period; start < x1; start += period)
        {
            var a = Math.Max(x0, start);
            var b = Math.Min(x1, start + dash);
            if (b <= a) continue;
            path.MoveTo((float)a, y); path.LineTo((float)b, y);
        }
        paint.Shader = ramp;
        paint.Color = SKColors.White;
        canvas.DrawPath(path, paint);
        paint.Shader = null;
    }

    public override void Dispose()
    {
        paint.Dispose(); path.Dispose(); ramp?.Dispose();
        base.Dispose();
    }
}
