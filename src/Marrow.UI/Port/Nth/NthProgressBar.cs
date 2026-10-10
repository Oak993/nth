// PORT-ONLY: OD-0136 amendment 2, step 3 option B "Liquid silver" (ED-NEW-nth-motion). Known and unknown length, 4 px and
// 3 px. A metal fill (--ftop to --fbot, lighter on top) on a --track capsule; the light runs only inside the fill; unknown
// length grows from the left and its tail catches up, like a tide. No knob, no sparks. The drawing is exactly as tall as the
// bar and clipped to its capsule, so nothing is cut off.
using Avalonia;
using SkiaSharp;

namespace Marrow.UI.Port.Nth;

/// <summary>The bar's state: its value (a spring toward the site's value) or unknown length.</summary>
public sealed record NthBarState(NthSpring Value, bool Indeterminate);

/// <summary>Step 3 option B. Set <see cref="Value"/> (0..1) or <see cref="IsIndeterminate"/>; height 4 (or 3) px.</summary>
public sealed class NthProgressBar : NthMotionControl
{
    private NthSpring value = NthSpring.At(0);
    private bool indeterminate;

    public NthProgressBar() => Height = 4;

    /// <summary>The value the fill springs to (0..1).</summary>
    public double Value
    {
        get => value.Target;
        set
        {
            var target = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
            if (target == this.value.Target) return;
            this.value = this.value.Retarget(target, Now, Calm);
            Send();
        }
    }

    /// <summary>Show <paramref name="target"/> at once (a new download, a bar shown already part way).</summary>
    public void Jump(double target)
    {
        value = NthSpring.At(Math.Clamp(target, 0, 1));
        Send();
    }

    /// <summary>The fill as drawn now (tests).</summary>
    public double Shown => value.Value(Now);

    public bool IsIndeterminate { get => indeterminate; set { if (indeterminate == value) return; indeterminate = value; Restart(); } }

    protected override object? State => new NthBarState(value, indeterminate);

    protected override NthPainter CreatePainter() => new NthBarPainter();
}

/// <summary>Draws step 3 option B.</summary>
public sealed class NthBarPainter : NthSkiaPainter
{
    private readonly SKPaint paint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPath capsule = new();
    private SKShader? fill, light;
    private (double Height, bool Dark) fillFor = (-1, false);
    private (double Half, bool Dark) lightFor = (-1, false);

    public override bool Moving(NthFrame frame, double now)
    {
        if (frame.State is not NthBarState state) return false;
        if (state.Indeterminate) return true;
        if (!state.Value.Settled(now)) return true;
        var v = state.Value.Value(now);
        return !frame.Calm && v > 0 && v < .999; // the light keeps passing inside a partial fill
    }

    private static void Capsule(SKPath path, double x0, double x1, double h)
    {
        path.Rewind();
        var r = (float)(h / 2);
        path.AddRoundRect(new SKRect((float)x0, 0, (float)Math.Max(x1, x0 + h), (float)h), r, r);
    }

    private SKShader FillShader(double h, NthPalette palette, bool dark)
    {
        if (fill is null || fillFor != (h, dark))
        {
            fill?.Dispose();
            fill = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, (float)h), [palette.FillTop, palette.FillBottom], null, SKShaderTileMode.Clamp);
            fillFor = (h, dark);
        }
        return fill;
    }

    private SKShader LightShader(double half, NthPalette palette, bool dark)
    {
        if (light is null || lightFor != (half, dark))
        {
            light?.Dispose();
            var c = palette.FillLight;
            light = SKShader.CreateLinearGradient(new SKPoint((float)-half, 0), new SKPoint((float)half, 0),
                [c.WithAlpha(0), c, c.WithAlpha(0)], [0, .5f, 1], SKShaderTileMode.Clamp);
            lightFor = (half, dark);
        }
        return light;
    }

    public override void Draw(SKCanvas canvas, NthFrame frame, double now)
    {
        if (frame.State is not NthBarState state) return;
        double w = frame.Size.Width, h = frame.Size.Height;
        if (w <= 0 || h <= 0) return;
        var palette = frame.Palette;
        var t = now - frame.Started;
        paint.Shader = null; paint.Color = palette.Track;
        Capsule(capsule, 0, w, h);
        canvas.DrawPath(capsule, paint);
        canvas.Save();
        canvas.ClipPath(capsule, SKClipOperation.Intersect, true);
        var shader = FillShader(h, palette, frame.Dark);
        if (state.Indeterminate)
        {
            var seg = NthMotion.BarIndeterminate(t, w, h, frame.Calm);
            if (seg.Alpha > 0)
            {
                Capsule(capsule, seg.X0, seg.X1, h);
                Fill(shader, seg.Alpha);
                canvas.DrawPath(capsule, paint);
            }
        }
        else
        {
            var value = Math.Min(1, state.Value.Value(now));
            var fw = value * w;
            var alpha = NthMotion.BarFillAlpha(fw, h);
            if (alpha > 0)
            {
                // the fill starts already inside the left round end
                Capsule(capsule, -h, Math.Max(fw, .1), h);
                Fill(shader, alpha);
                canvas.DrawPath(capsule, paint);
                if (NthMotion.BarLight(t, fw, w, value, frame.Calm) is { } centre)
                {
                    var half = NthMotion.BarLightHalf(w);
                    canvas.ClipPath(capsule, SKClipOperation.Intersect, true);
                    canvas.Translate((float)centre, 0);
                    paint.Shader = LightShader(half, palette, frame.Dark);
                    paint.Color = SKColors.White.WithAlpha((byte)Math.Round(255 * alpha));
                    canvas.DrawRect(new SKRect((float)-half, 0, (float)half, (float)h), paint);
                }
            }
        }
        paint.Shader = null;
        canvas.Restore();
    }

    private void Fill(SKShader shader, double alpha)
    {
        paint.Shader = shader;
        paint.Color = SKColors.White.WithAlpha((byte)Math.Round(255 * alpha));
    }

    public override void Dispose()
    {
        paint.Dispose(); capsule.Dispose(); fill?.Dispose(); light?.Dispose();
        base.Dispose();
    }
}
