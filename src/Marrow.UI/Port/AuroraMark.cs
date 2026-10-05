// PORT-ONLY: OD-0112's locked D Aurora, ported from design-src/app-icon/src/icons-a-j/D-aurora.js
// and iconkit.js. No loading-control or boot-stage integration belongs to this renderer.
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Marrow.UI.Port;

/// <summary>
/// Deterministic Canvas 2D drawing in icon units. Own one instance per render thread and dispose it there.
/// Reuses paths, paints, gradient buffers and picture recorders. Skia's C ABI avoids managed wrappers
/// for changing gradients, pictures and filters. Native drawing objects still allocate in Skia and are unreferenced
/// as soon as their consumer takes ownership. The zero-allocation contract measures managed bytes, as OrbRendererTests do.
/// </summary>
public sealed class AuroraMark : IDisposable
{
    public const int LaunchDurationMs = 1200;
    public const int IdlePeriodMs = 7200;

    // These are Skia-generated programs, not custom SkSL. The App warm-up registry can invoke Warmup
    // on its own GPU canvas, before showing Aurora, without Marrow.UI depending on Marrow.App.
    public static IReadOnlyList<string> WarmupPrograms { get; } = Array.AsReadOnly(new[]
    {
        "aurora-small-gradient-src-over", "aurora-night-gradient-blur-src-over",
        "aurora-ray-gradient-blur-plus", "aurora-foot-gradient-blur-plus",
        "aurora-ridge-gradient-src-over", "aurora-glass-gradient-blur-src-over",
        "aurora-star-solid-src-over", "aurora-outline-solid-src-over",
    });

    private readonly record struct Ray(double U, double H, double A, double W, double K);
    private readonly record struct Star(double X, double Y, double A, double Phase);
    private readonly record struct Box(double X, double Y, double W, double H, double R);
    private static readonly Ray[] Rays = MakeRays();
    private static readonly Star[] Stars = MakeStars();
    private readonly SKPaint paint = new() { IsAntialias = true, StrokeMiter = 10 };
    private readonly SKPaint layerPaint = new() { IsAntialias = true };
    private readonly SKPath path = new();
    private readonly SKPoint[] points = new SKPoint[2];
    private readonly uint[] colors = new uint[13];
    private readonly float[] stops = new float[13];
    private readonly nint blurRecorder = Native.sk_picture_recorder_new();
    private readonly nint filterRecorder = Native.sk_picture_recorder_new();
    private SKRect blurBounds;
    private SKMatrix blurTransform;
    private SKBlendMode blurBlend;
    private double blurAlpha;
    private float blurSigma;
    private SKSizeI deviceSize;
    private bool disposed;

    /// <param name="deviceCanvasSize">The Canvas 2D canvas.width/height equivalent. Supply this when
    /// the caller has already restricted its clip; otherwise the device clip gives the target extent.</param>
    public void Draw(SKCanvas canvas, double size, string theme, AuroraTime t, SKSizeI? deviceCanvasSize = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(canvas);
        if (!double.IsFinite(size) || size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        if (!double.IsFinite(t.LaunchProgress) || !double.IsFinite(t.IdleSeconds)) throw new ArgumentOutOfRangeException(nameof(t));
        var clip = canvas.DeviceClipBounds;
        deviceSize = deviceCanvasSize ?? new SKSizeI(Math.Max(1, clip.Right), Math.Max(1, clip.Bottom));
        if (deviceSize.Width <= 0 || deviceSize.Height <= 0) throw new ArgumentOutOfRangeException(nameof(deviceCanvasSize));
        var small = size <= 32;
        var margin = small ? 1 : Math.Floor(size * 6 + 0.5) / 100;
        var width = size - 2 * margin;
        var p = new Box(margin, margin, width, width, width * (small ? 0.24 : 0.225));
        var l = t.LaunchProgress;
        var phase = 2 * Math.PI * (((t.IdleSeconds * 1000 % IdlePeriodMs) + IdlePeriodMs) % IdlePeriodMs / IdlePeriodMs);
        var sway = Math.Sin(phase);
        var alpha = Out(Seg(l, 0, 0.35));
        var saved = canvas.Save();
        try
        {
            Night(canvas, size, theme, p, alpha);
            if (!small) DrawStars(canvas, size, p, Out(Seg(l, 0.1, 0.5)), phase / (2 * Math.PI), alpha);
            var reveal = l >= 1 ? 1 : InOut(Seg(l, 0.18, 0.92));
            var bright = l >= 1 ? 1 : 0.6 + 0.4 * Out(Seg(l, 0.5, 1));
            Curtain(canvas, size, p, reveal, phase, sway, bright);
            Glass(canvas, size, theme, p, alpha);
        }
        finally { canvas.RestoreToCount(saved); }
    }

    /// <summary>
    /// Draws the real program families into the supplied offscreen GPU canvas. The caller supplies the
    /// actual design size and sets its device transform: run this for every size/scale used by the loading
    /// and boot controls, since Skia selects blur kernels by device sigma. No custom SkSL is needed.
    /// A complete frame exercises all families; multiple named entries are intentionally safe to repeat.
    /// </summary>
    public void Warmup(SKCanvas canvas, string program, double size, SKSizeI? deviceCanvasSize = null)
    {
        if (!WarmupPrograms.Contains(program, StringComparer.Ordinal)) throw new ArgumentException("Unknown Aurora program.", nameof(program));
        Draw(canvas, string.Equals(program, WarmupPrograms[0], StringComparison.Ordinal) ? Math.Min(size, 32) : size, "dark", AuroraTime.Still, deviceCanvasSize);
    }

    private void Night(SKCanvas c, double size, string theme, Box p, double alpha)
    {
        // The reference backend elides the cached picture's paint/layer once alpha rounds to 255.
        var grouped = size > 32 && MathF.Round((float)alpha * 255, MidpointRounding.AwayFromZero) < 255;
        if (size > 32)
        {
            // K.layer draws the complete night picture with the launch alpha as a group.
            if (grouped) Layer(c, alpha, SKBlendMode.SrcOver);
            var shadow = BeginBlur(c, size, size * 0.03, SKBlendMode.SrcOver);
            Solid(string.Equals(theme, "light", StringComparison.Ordinal) ? Color(10, 30, 50, .32) : Color(0, 0, 0, .6));
            Squircle(p.X + p.W * .03, p.Y + p.H * .05, p.W * .94, p.H * .96, p.R);
            Native.sk_canvas_draw_path(shadow, path.Handle, paint.Handle);
            EndBlur(c);
        }
        Squircle(p.X, p.Y, p.W, p.H, p.R);
        colors[0] = 0xff040818; colors[1] = 0xff06122a; colors[2] = 0xff071a2c; colors[3] = 0xff08222c;
        stops[0] = 0; stops[1] = .5f; stops[2] = .8f; stops[3] = 1;
        Gradient(p.X, p.Y, p.X, p.Y + p.H, 4, size > 32 ? 1 : alpha);
        c.DrawPath(path, paint);
        if (grouped) c.Restore();
    }

    private void DrawStars(SKCanvas c, double size, Box p, double k, double tw, double alpha)
    {
        for (var i = 0; i < Stars.Length; i++)
        {
            var s = Stars[i];
            var a = k * s.A * (.75 + .25 * Math.Cos(2 * Math.PI * (tw + s.Phase))) * (i % 3 == 0 ? 1 : .6);
            Solid(Color(220, 235, 255, Fixed3(a * .85)), alpha);
            var x = p.X + p.W * (.06 + .88 * s.X); var y = p.Y + p.H * (.06 + s.Y);
            var r = size * (.0022 + .0022 * s.A);
            path.Rewind(); path.MoveTo((float)(x + r), (float)y);
            var fx = (float)x; var fy = (float)y; var fr = (float)r;
            var oval = new SKRect(fx - fr, fy - fr, fx + fr, fy + fr);
            path.ArcTo(oval, 0, 180, false); path.ArcTo(oval, 180, 180, false); path.Close();
            c.DrawPath(path, paint);
        }
    }

    private void Curtain(SKCanvas c, double size, Box p, double reveal, double phase, double sway, double bright)
    {
        c.Save();
        Squircle(p.X, p.Y, p.W, p.H, p.R);
        c.ClipPath(path, SKClipOperation.Intersect, true);
        if (size <= 32)
        {
            var tiny = size <= 20;
            Solid(Color(80, 255, 190, .45), reveal, stroke: tiny ? 4 : 6, round: true);
            FootPath(p, phase, sway, tiny ? -2 : -3, 16, 0, 1, 1);
            c.DrawPath(path, paint);
            Solid(0xff8affd0, reveal, stroke: tiny ? 2 : 2.5, round: true);
            FootPath(p, phase, sway, 0, 16, 0, 1, 1);
            c.DrawPath(path, paint);
            c.Restore();
            return;
        }
        var glow = BeginBlur(c, size, size * .02, SKBlendMode.Plus);
        DrawRays(glow, p, reveal, phase, sway, bright, 3, .55);
        EndBlur(c);
        DrawRays(c.Handle, p, reveal, phase, sway, bright, 1, .7);
        var foot = BeginBlur(c, size, size * .006, SKBlendMode.Plus);
        for (var i = 0; i <= 12; i++)
        {
            colors[i] = Color(170, 255, 215, Fixed3((.02 + .22 * Rays[i * 13].A * Rays[i * 13 + 1].A) * bright));
            stops[i] = i / 12f;
        }
        Gradient(p.X, 0, p.X + p.W, 0, 13, 1, SKBlendMode.Plus, size * .01, true);
        FootPath(p, phase, sway, 0, 40, .02, .96, Math.Clamp(reveal * 1.15, 0, 1));
        Native.sk_canvas_draw_path(foot, path.Handle, paint.Handle);
        EndBlur(c);

        colors[0] = 0xff04121a; colors[1] = 0xff02080d; stops[0] = 0; stops[1] = 1;
        Gradient(p.X, p.Y + p.H * .78, p.X, p.Y + p.H, 2);
        path.Rewind(); path.MoveTo((float)p.X, (float)(p.Y + p.H));
        for (var i = 0; i <= 30; i++)
        {
            var u = i / 30.0;
            path.LineTo((float)(p.X + p.W * u), (float)(p.Y + p.H * (.8 + .06 * Math.Sin(u * 6.1 + 1) + .025 * Math.Sin(u * 15.3) + .012 * Math.Sin(u * 37))));
        }
        path.LineTo((float)(p.X + p.W), (float)(p.Y + p.H)); path.Close();
        c.DrawPath(path, paint);
        Solid(Color(90, 255, 190, Fixed3(.25 * bright * reveal)), stroke: size * .003);
        c.DrawPath(path, paint);
        c.Restore();
    }

    private void DrawRays(nint c, Box p, double reveal, double phase, double sway, double bright, double wk, double ak)
    {
        foreach (var r in Rays)
        {
            var show = Math.Clamp((reveal * 1.25 - r.U * .25) * 1.6 - r.U * .6, 0, 1);
            if (show <= 0) continue;
            var x = p.X + p.W * (.02 + .96 * r.U);
            var fy = Foot(p, r.U, phase, sway);
            var flick = .8 + .2 * Math.Sin(2 * Math.PI * (r.K + phase / (2 * Math.PI)));
            var len = p.H * r.H * (.5 + .5 * show) * (.9 + .1 * flick);
            var a = r.A * show * bright * flick * ak;
            colors[0] = Color(150, 255, 200, Fixed3(a * .8));
            colors[1] = Color(60, 245, 150, Fixed3(a * .7));
            colors[2] = Color(30, 190, 160, Fixed3(a * .32));
            colors[3] = Color(170, 80, 230, Fixed3(a * .16));
            colors[4] = Color(150, 90, 255, 0);
            stops[0] = 0; stops[1] = .06f; stops[2] = .4f; stops[3] = .78f; stops[4] = 1;
            Gradient(x, fy, x, fy - len, 5, 1, SKBlendMode.Plus);
            var w = p.W * .012 * r.W * wk;
            var rayRect = SKRect.Create((float)(x - w / 2), (float)(fy - len), (float)w, (float)len);
            Native.sk_canvas_draw_rect(c, in rayRect, paint.Handle);
            var dl = p.H * (.07 + .09 * r.K);
            colors[0] = Color(90, 250, 170, Fixed3(a * .42)); colors[1] = Color(60, 200, 160, 0);
            stops[0] = 0; stops[1] = 1;
            Gradient(x, fy, x, fy + dl, 2, 1, SKBlendMode.Plus);
            var tailRect = SKRect.Create((float)(x - w / 2), (float)fy, (float)w, (float)dl);
            Native.sk_canvas_draw_rect(c, in tailRect, paint.Handle);
        }
    }

    private void Glass(SKCanvas c, double size, string theme, Box p, double alpha)
    {
        var light = string.Equals(theme, "light", StringComparison.Ordinal);
        if (size <= 32)
        {
            Solid(light ? Color(0, 20, 40, .5) : Color(255, 255, 255, .22), alpha, stroke: 1);
            Squircle(p.X + .5, p.Y + .5, p.W - 1, p.H - 1, p.R);
            c.DrawPath(path, paint);
            return;
        }
        c.Save(); Squircle(p.X, p.Y, p.W, p.H, p.R); c.ClipPath(path, SKClipOperation.Intersect, true);
        var lw = size * .018;
        var rim = BeginBlur(c, size, size * .006, SKBlendMode.SrcOver, alpha);
        colors[0] = Color(255, 255, 255, .55); colors[1] = Color(255, 255, 255, .08);
        colors[2] = Color(160, 255, 220, .05); colors[3] = Color(160, 255, 220, .3);
        stops[0] = 0; stops[1] = .4f; stops[2] = .8f; stops[3] = 1;
        Gradient(p.X, p.Y, p.X + p.W * .6, p.Y + p.H, 4, 1, stroke: lw);
        Squircle(p.X + lw * .3, p.Y + lw * .3, p.W - lw * .6, p.H - lw * .6, p.R);
        Native.sk_canvas_draw_path(rim, path.Handle, paint.Handle);
        EndBlur(c);
        colors[0] = Color(255, 255, 255, .16); colors[1] = Color(255, 255, 255, .04); colors[2] = Color(255, 255, 255, 0);
        stops[0] = 0; stops[1] = .6f; stops[2] = 1;
        Gradient(p.X, p.Y, p.X + p.W * .4, p.Y + p.H * .55, 3, alpha);
        path.Rewind(); path.MoveTo((float)p.X, (float)p.Y); path.LineTo((float)(p.X + p.W), (float)p.Y);
        path.LineTo((float)(p.X + p.W), (float)(p.Y + p.H * .12));
        path.CubicTo((float)(p.X + p.W * .6), (float)(p.Y + p.H * .3), (float)(p.X + p.W * .3), (float)(p.Y + p.H * .36), (float)p.X, (float)(p.Y + p.H * .56));
        path.Close(); c.DrawPath(path, paint); c.Restore();
        var line = Math.Max(.75, size * .003);
        Solid(light ? Color(0, 20, 40, .4) : Color(255, 255, 255, .18), alpha, stroke: line);
        Squircle(p.X + line / 2, p.Y + line / 2, p.W - line, p.H - line, p.R);
        c.DrawPath(path, paint);
    }

    private void Solid(uint color, double alpha = 1, SKBlendMode blend = SKBlendMode.SrcOver, double stroke = 0, bool round = false)
    {
        Native.sk_paint_set_shader(paint.Handle, 0);
        var c = (SKColor)color;
        paint.Color = c.WithAlpha((byte)Math.Clamp((int)MathF.Round(c.Alpha * (float)alpha, MidpointRounding.AwayFromZero), 0, 255));
        Style(blend, stroke, round);
    }

    private void Gradient(double x0, double y0, double x1, double y1, int count, double alpha = 1,
        SKBlendMode blend = SKBlendMode.SrcOver, double stroke = 0, bool round = false)
    {
        points[0] = new SKPoint((float)x0, (float)y0); points[1] = new SKPoint((float)x1, (float)y1);
        var shader = Native.sk_shader_new_linear_gradient(points, colors, stops, count, SKShaderTileMode.Clamp, 0);
        if (shader == 0) throw new InvalidOperationException("Aurora gradient creation failed.");
        Native.sk_paint_set_shader(paint.Handle, shader);
        Native.sk_shader_unref(shader);
        paint.ColorF = new SKColorF(1, 1, 1, (float)alpha);
        Style(blend, stroke, round);
    }

    private void Style(SKBlendMode blend, double stroke, bool round)
    {
        paint.BlendMode = blend;
        paint.Style = stroke > 0 ? SKPaintStyle.Stroke : SKPaintStyle.Fill;
        paint.StrokeWidth = (float)stroke;
        paint.StrokeCap = round ? SKStrokeCap.Round : SKStrokeCap.Butt;
        paint.StrokeJoin = SKStrokeJoin.Miter;
    }

    private void Layer(SKCanvas c, double alpha, SKBlendMode blend)
    {
        Native.sk_paint_set_imagefilter(layerPaint.Handle, 0);
        layerPaint.ColorF = new SKColorF(1, 1, 1, (float)alpha);
        layerPaint.BlendMode = blend;
        c.SaveLayer(layerPaint);
    }

    // K.blur's two scratch canvases are pictures in the selected Node Skia backend. Record
    // unclipped, with the exact downscaled transform and padded extent, filter that picture,
    // then apply the destination's clip, alpha and blend only when compositing it back.
    private nint BeginBlur(SKCanvas c, double size, double radius, SKBlendMode blend, double alpha = 1)
    {
        var matrix = c.TotalMatrix;
        var scale = Math.Sqrt((double)matrix.ScaleX * matrix.ScaleX + (double)matrix.SkewY * matrix.SkewY);
        var rd = radius * scale;
        var k = Math.Clamp(rd / 2.5, 1, 8);
        var width = Math.Max(1, Math.Ceiling(deviceSize.Width / k)) + 4;
        var height = Math.Max(1, Math.Ceiling(deviceSize.Height / k)) + 4;
        blurBounds = SKRect.Create((float)width, (float)height);
        blurTransform = new SKMatrix((float)k, 0, (float)(-2 * k), 0, (float)k, (float)(-2 * k), 0, 0, 1);
        blurBlend = blend; blurAlpha = alpha;
        blurSigma = (float)(Math.Round(rd / k, 2, MidpointRounding.AwayFromZero) / 2);
        var target = Native.sk_picture_recorder_begin_recording(blurRecorder, in blurBounds);
        var scratchMatrix = new SKMatrix((float)(matrix.ScaleX / k), (float)(matrix.SkewX / k), (float)(matrix.TransX / k + 2),
            (float)(matrix.SkewY / k), (float)(matrix.ScaleY / k), (float)(matrix.TransY / k + 2), 0, 0, 1);
        var matrix44 = new SKMatrix44(scratchMatrix);
        Native.sk_canvas_set_matrix(target, in matrix44);
        return target;
    }

    private void EndBlur(SKCanvas c)
    {
        var picture = Native.sk_picture_recorder_end_recording(blurRecorder);
        nint filtered = 0;
        try
        {
            var target = Native.sk_picture_recorder_begin_recording(filterRecorder, in blurBounds);
            var filter = Native.sk_imagefilter_new_blur(blurSigma, blurSigma, SKShaderTileMode.Decal, 0, 0);
            if (filter == 0) throw new InvalidOperationException("Aurora blur creation failed.");
            Native.sk_paint_set_imagefilter(layerPaint.Handle, filter);
            Native.sk_imagefilter_unref(filter);
            layerPaint.Color = SKColors.White; layerPaint.BlendMode = SKBlendMode.SrcOver;
            var identity = SKMatrix.Identity;
            Native.sk_canvas_draw_picture(target, picture, in identity, layerPaint.Handle);
            Native.sk_paint_set_imagefilter(layerPaint.Handle, 0);
            filtered = Native.sk_picture_recorder_end_recording(filterRecorder);
            layerPaint.ColorF = new SKColorF(1, 1, 1, (float)blurAlpha); layerPaint.BlendMode = blurBlend;
            c.Save();
            try
            {
                c.ResetMatrix();
                c.ClipRect(blurTransform.MapRect(blurBounds), SKClipOperation.Intersect, true);
                Native.sk_canvas_draw_picture(c.Handle, filtered, in blurTransform, layerPaint.Handle);
            }
            finally { c.Restore(); }
        }
        finally
        {
            if (filtered != 0) Native.sk_picture_unref(filtered);
            Native.sk_picture_unref(picture);
            Native.sk_paint_set_imagefilter(layerPaint.Handle, 0);
        }
    }

    private void Squircle(double x, double y, double w, double h, double r)
    {
        r = Math.Min(r, Math.Min(w / 2, h / 2));
        var ex = Math.Min(r * 1.33, w / 2); var ey = Math.Min(r * 1.33, h / 2);
        const double pull = (1 - .572) * .6;
        path.Rewind(); path.MoveTo((float)(x + ex), (float)y); path.LineTo((float)(x + w - ex), (float)y);
        path.CubicTo((float)(x + w - ex * pull), (float)y, (float)(x + w), (float)(y + ey * pull), (float)(x + w), (float)(y + ey));
        path.LineTo((float)(x + w), (float)(y + h - ey));
        path.CubicTo((float)(x + w), (float)(y + h - ey * pull), (float)(x + w - ex * pull), (float)(y + h), (float)(x + w - ex), (float)(y + h));
        path.LineTo((float)(x + ex), (float)(y + h));
        path.CubicTo((float)(x + ex * pull), (float)(y + h), (float)x, (float)(y + h - ey * pull), (float)x, (float)(y + h - ey));
        path.LineTo((float)x, (float)(y + ey));
        path.CubicTo((float)x, (float)(y + ey * pull), (float)(x + ex * pull), (float)y, (float)(x + ex), (float)y); path.Close();
    }

    private void FootPath(Box p, double phase, double sway, double dy, int n, double xOffset, double xScale, double end)
    {
        path.Rewind();
        for (var i = 0; i <= n * end; i++)
        {
            var u = i / (double)n;
            var x = (float)(p.X + p.W * (xOffset + xScale * u)); var y = (float)(Foot(p, u, phase, sway) + dy);
            if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y);
        }
    }

    private static double Foot(Box p, double u, double phase, double sway) =>
        p.Y + p.H * (.6 - .12 * Math.Sin(Math.PI * 1.5 * u + .35) + .045 * Math.Sin(Math.PI * 3.6 * u + 1.1) + sway * .025 * Math.Sin(2 * Math.PI * u * 1.5 + phase));
    private static double Seg(double p, double a, double b) => Math.Clamp((p - a) / (b - a), 0, 1);
    private static double Out(double u) => 1 - Math.Pow(1 - u, 3);
    private static double InOut(double u) => u < .5 ? 4 * u * u * u : 1 - Math.Pow(-2 * u + 2, 3) / 2;
    private static double Fixed3(double value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);
    private static uint Color(byte r, byte g, byte b, double alpha) => (uint)new SKColor(r, g, b, (byte)Math.Clamp((int)MathF.Round((float)alpha * 255, MidpointRounding.AwayFromZero), 0, 255));

    private static double Random(ref uint s)
    {
        // IconKit.rng uses SIGNED >> 17 (not the conventional xorshift logical shift).
        unchecked { s ^= s << 13; s ^= (uint)((int)s >> 17); s ^= s << 5; }
        return s / 4294967296.0;
    }
    private static Ray[] MakeRays()
    {
        uint seed = 42; var result = new Ray[200];
        for (var i = 0; i < result.Length; i++)
        {
            var q = Random(ref seed);
            result[i] = new Ray(Random(ref seed), .14 + .34 * q * q + .06 * Random(ref seed), .2 + .8 * Random(ref seed), .35 + .8 * Random(ref seed), Random(ref seed));
        }
        Array.Sort(result, (a, b) => a.U.CompareTo(b.U)); return result;
    }
    private static Star[] MakeStars()
    {
        uint seed = 8; var result = new Star[46];
        for (var i = 0; i < result.Length; i++) result[i] = new Star(Random(ref seed), Random(ref seed) * .62, .3 + Random(ref seed) * .7, Random(ref seed));
        return result;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Native.sk_picture_recorder_delete(blurRecorder); Native.sk_picture_recorder_delete(filterRecorder);
        path.Dispose(); paint.Dispose(); layerPaint.Dispose();
    }

    private static class Native
    {
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint sk_picture_recorder_new();
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void sk_picture_recorder_delete(nint recorder);
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint sk_picture_recorder_begin_recording(nint recorder, in SKRect bounds);
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint sk_picture_recorder_end_recording(nint recorder);
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void sk_picture_unref(nint picture);
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void sk_canvas_set_matrix(nint canvas, in SKMatrix44 matrix);
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void sk_canvas_draw_picture(nint canvas, nint picture, in SKMatrix matrix, nint paint);
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void sk_canvas_draw_path(nint canvas, nint path, nint paint);
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void sk_canvas_draw_rect(nint canvas, in SKRect rect, nint paint);
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint sk_imagefilter_new_blur(float sigmaX, float sigmaY, SKShaderTileMode mode, nint input, nint crop);
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void sk_imagefilter_unref(nint filter);
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void sk_paint_set_imagefilter(nint paint, nint filter);
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint sk_shader_new_linear_gradient([In] SKPoint[] points, [In] uint[] colors, [In] float[] positions, int count, SKShaderTileMode mode, nint matrix);
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void sk_paint_set_shader(nint paint, nint shader);
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void sk_shader_unref(nint shader);
    }
}
