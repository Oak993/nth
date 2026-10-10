// PORT-ONLY: OD-0136 amendment 2, step 14 "Model colours loading" (ED-NEW-nth-motion). Inside the effort slider's tube while a
// model's colours load: one light sweep every 1.6 s clipped to the tube (no radial glow anywhere) and three flat --mark dots
// (r 2, 11 px apart, no gradient, no highlight) that swell to r 2.8 in turn. When the colours arrive the model's gradient fills
// the tube from the left as a rounded capsule (0.6 s, .2,.8,.2,1) and the dots fade in 0.2 s. The painter draws into any
// canvas (the effort slider draws it inside its own tube); NthTube is the stand-alone control.
using Avalonia;
using SkiaSharp;

namespace Marrow.UI.Port.Nth;

/// <summary>The tube's state: the colours (null while loading) and when they arrived.</summary>
public sealed record NthTubeState(SKColor[]? Colours, double? LoadedAt);

public sealed class NthTube : NthMotionControl
{
    private SKColor[]? colours;
    private double? loadedAt;

    public NthTube() { Height = 12; MaxWidth = 168; }

    /// <summary>The model's colours arrived: they fill the tube from the left.</summary>
    public void Load(SKColor[] modelColours) { colours = modelColours; loadedAt = Now; Send(); }

    protected override object? State => new NthTubeState(colours, loadedAt);

    protected override NthPainter CreatePainter() => new NthTubePainter();
}

public sealed class NthTubePainter : NthSkiaPainter
{
    private readonly SKPaint paint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPath tube = new();
    private SKShader? band, model;
    private bool bandDark;
    private (double W, SKColor[]? Colours) modelFor;

    public override bool Moving(NthFrame frame, double now) =>
        frame.State is not NthTubeState { LoadedAt: { } at } || now - at < .6;

    public override void Draw(SKCanvas canvas, NthFrame frame, double now)
    {
        var s = frame.State as NthTubeState;
        DrawTube(canvas, frame.Size.Width, frame.Size.Height, frame.Palette, frame.Dark, frame.Calm, now - frame.Started,
            s?.LoadedAt is { } at ? now - at : null, s?.Colours, true);
    }

    /// <summary>The loading tube of <paramref name="width"/> x <paramref name="height"/> at the origin; <paramref name="tubeFill"/> false when the host draws the tube.</summary>
    public void DrawTube(SKCanvas canvas, double width, double height, NthPalette palette, bool dark, bool calm, double seconds, double? loaded, SKColor[]? colours, bool tubeFill)
    {
        if (width <= 0 || height <= 0) return;
        var r = (float)(height / 2);
        tube.Rewind();
        tube.AddRoundRect(new SKRect(0, 0, (float)width, (float)height), r, r);
        if (tubeFill) { paint.Shader = null; paint.Color = palette.Tube; canvas.DrawPath(tube, paint); }
        var fade = loaded is { } ld ? 1 - NthEase.Clamp01(ld / .2) : 1;
        if (fade > 0 && NthMotion.TubeLight(seconds, width, calm) is { } x)
        {
            if (band is null || bandDark != dark)
            {
                band?.Dispose();
                var c = palette.SkeletonLight;
                band = SKShader.CreateLinearGradient(new SKPoint(-30, 0), new SKPoint(30, 0), [c.WithAlpha(0), c, c.WithAlpha(0)], [0, .5f, 1], SKShaderTileMode.Clamp);
                bandDark = dark;
            }
            canvas.Save();
            canvas.ClipPath(tube, SKClipOperation.Intersect, true);
            canvas.Translate((float)x, 0);
            paint.Shader = band; paint.Color = SKColors.White.WithAlpha((byte)Math.Round(255 * fade));
            canvas.DrawRect(new SKRect(-30, 0, 30, (float)height), paint);
            canvas.Restore();
        }
        if (fade > 0)
        {
            paint.Shader = null;
            var mid = Math.Round(width / 2);
            for (var i = 0; i < 3; i++)
            {
                var b = NthMotion.TubeDot(seconds, i, calm);
                paint.Color = palette.Mark.WithAlpha((byte)Math.Round(255 * fade * (.8 + .2 * b)));
                canvas.DrawCircle((float)(mid + (i - 1) * 11), (float)(height / 2), (float)(2 * (1 + .4 * b)), paint);
            }
        }
        if (loaded is not { } since || colours is not { Length: > 0 }) return;
        if (model is null || modelFor.W != width || !ReferenceEquals(modelFor.Colours, colours))
        {
            model?.Dispose();
            model = colours.Length == 1
                ? SKShader.CreateColor(colours[0])
                : SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint((float)width, 0), colours, null, SKShaderTileMode.Clamp);
            modelFor = (width, colours);
        }
        var fill = calm ? width : Math.Max(height, width * NthEase.Out.Ease(since / .6));
        paint.Shader = model;
        paint.Color = SKColors.White.WithAlpha((byte)Math.Round(255 * (calm ? NthEase.Clamp01(since / .2) : 1)));
        canvas.DrawRoundRect(new SKRect(0, 0, (float)fill, (float)height), r, r, paint);
        paint.Shader = null;
    }

    public override void Dispose()
    {
        paint.Dispose(); tube.Dispose(); band?.Dispose(); model?.Dispose();
        base.Dispose();
    }
}
