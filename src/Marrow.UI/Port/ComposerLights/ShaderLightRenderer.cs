// PORT-ONLY, owner-ruled addition (OD-0139 Composer Light v2; ED-NEW-lightv2-1): draws a shader light's frame with its SkSL
// program (ShaderLightShaders), one draw of the band around the composer (the composer inflated by the light's reach), with
// Screen over what is behind it: the outer light lands on the window around the composer, the inner light inside the
// composer's glass, the rim on its edge (the page's outer, inner and rim). Per frame only the uniforms change.
// Text and light (ED-NEW-lightv2-5): given the window's surface, the light reads the pixels under its band as they are when it
// draws (the chat text is drawn before the composer: ChatView's timeline, then its composer host) and composes with them itself,
// drawn with Src: the text kept on top of the light with its halo, the light over the text, or the text inverted against it
// (the v4 page's three modes). Only the pixels Avalonia redraws in this frame (the canvas's clip) are read and written, so what
// it reads is always this frame's. Without a surface (or under an opacity layer, where the surface is not what the canvas
// draws into) it draws with Screen as before, and the light behind the chat text still dims.
// The readability guard (ED-NEW-lightv2-8) dims the light near the chat text by as much as keeps the app's chat text colour at
// 4.5:1 against the window there (read from the window's pixels; with none, across the chat text block against the window's
// colour).
// With no GPU (ED-251: the light is a still) the software raster shades a band once (0.2 to 1.6 s for the largest at 2x) and
// keeps its pixels (DrawStill): a redraw of the window under the band copies them, with Screen.
using Marrow.Core.Models.ComposerLights;
using SkiaSharp;

namespace Marrow.UI.Port.ComposerLights;

/// <summary>Draws <see cref="ShaderLightFrame"/>s.</summary>
public sealed class ShaderLightRenderer : IDisposable
{
    private static readonly float[] Core = Linear(ShaderLightCatalog.Core);
    private readonly Dictionary<string, (SKRuntimeEffectUniforms Uniforms, SKRuntimeEffectChildren Children)> programs = [];
    private readonly SKPaint paint = new() { IsAntialias = false, BlendMode = SKBlendMode.Screen };
    private readonly SKShader nothing = SKShader.CreateColor(SKColors.Transparent);
    private readonly float[] v4 = new float[4];

    /// <summary>The chat text block above the composer (the v4 page's textRect): 16 px inside the composer's sides, ending 44 px above its top.</summary>
    public const float TextInset = 16, TextGap = 44;
    private readonly float[] v2 = new float[2];
    private readonly SKPaint copy = new() { IsAntialias = false, BlendMode = SKBlendMode.Screen };
    private SKImage? still;
    private string? stillId;
    private float[] stillKey = [], key = new float[64];
    private bool disposed;

    /// <summary>How many times the software raster has shaded a still band (tests).</summary>
    internal int StillShades { get; private set; }

    /// <summary>False: the pages' composition alone, without the readability guard (ED-NEW-lightv2-8; tests).</summary>
    internal bool Guard { get; set; } = true;

    // the chat text the guard keeps readable: labelColor (85 %) over the window (#1E1E20; white), and with no window to read, the window
    private static readonly float[] DarkGlyph = [.85f + .15f * 30 / 255, .85f + .15f * 30 / 255, .85f + .15f * 32 / 255];
    private static readonly float[] DarkGround = [30 / 255f, 30 / 255f, 32 / 255f];
    private static readonly float[] LightGlyph = [.15f, .15f, .15f];
    private static readonly float[] LightGround = [1f, 1f, 1f];

    private static float[] Linear(string hex) { var (r, g, b) = ShaderLightCatalog.Linear(hex); return [(float)r, (float)g, (float)b]; }

    /// <summary>The band a frame shades, in the composer's coordinates (its top left at the origin).</summary>
    public static SKRect Band(ShaderLightFrame f)
    {
        var reach = (float)f.Reach;
        return new SKRect(-reach, -reach, (float)f.W + reach, (float)f.H + reach);
    }

    /// <summary>
    /// Draws <paramref name="f"/> with the composer's top left at the canvas's origin, at a device <paramref name="scale"/>, with
    /// an overall <paramref name="opacity"/>. Returns false when the frame has nothing to draw.
    /// </summary>
    /// <param name="surface">The surface the canvas draws into (Text and light reads what is under the band from it), or null.</param>
    /// <param name="fadeBottom">Where the window ends below (composer px): the outer light fades out over the 13 px above it.</param>
    public bool Draw(SKCanvas canvas, ShaderLightFrame f, float scale, float opacity = 1, SKSurface? surface = null, float? fadeBottom = null)
    {
        if (disposed || !(f.W > 0) || !(f.H > 0) || f.Alpha * opacity <= 0.0005 || !ShaderLightShaders.Has(f.Def.Id)) return false;
        var band = Band(f);
        using var under = surface is null ? null : Under(canvas, surface, band, out var transform) is { } image ? Backdrop(image, transform) : null;
        using var shader = Shader(f, scale, opacity, under, fadeBottom);
        paint.Shader = shader;
        paint.BlendMode = under is null ? SKBlendMode.Screen : SKBlendMode.Src;
        if (GpuWarmSites.Draw()) canvas.DrawRect(band, paint); // a warm-up site (ED-NEW-warmupsplit-1)
        paint.Shader = null;
        return true;
    }

    /// <summary>
    /// Draws <paramref name="f"/> as <see cref="Draw"/> does with no surface, from pixels kept from the last time the same frame
    /// was drawn at the same place on the device (with no GPU the light is a still, ED-251; Text and light then draws as with no
    /// surface: the light with Screen, dimmed behind the chat text).
    /// </summary>
    public bool DrawStill(SKCanvas canvas, ShaderLightFrame f, float scale, float opacity = 1, float? fadeBottom = null)
    {
        if (disposed || !(f.W > 0) || !(f.H > 0) || f.Alpha * opacity <= 0.0005 || !ShaderLightShaders.Has(f.Def.Id)) return false;
        var band = Band(f);
        var m = canvas.TotalMatrix;
        var device = SKRectI.Ceiling(m.MapRect(band), true);
        if (device.Width <= 0 || device.Height <= 0) return false;
        m.TransX -= device.Left;
        m.TransY -= device.Top;
        var n = Key(f, scale, opacity, fadeBottom, m, device);
        if (still is null || stillId != f.Def.Id || !key.AsSpan(0, n).SequenceEqual(stillKey))
        {
            using var raster = SKSurface.Create(new SKImageInfo(device.Width, device.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            if (raster is null) return false;
            raster.Canvas.Clear(SKColors.Transparent); // the band's rounded-out edge pixels it does not cover
            raster.Canvas.SetMatrix(m);
            using (var shader = Shader(f, scale, opacity, null, fadeBottom))
            {
                paint.Shader = shader;
                paint.BlendMode = SKBlendMode.Src;
                raster.Canvas.DrawRect(band, paint);
                paint.Shader = null;
            }
            still?.Dispose();
            still = raster.Snapshot();
            stillId = f.Def.Id;
            stillKey = key.AsSpan(0, n).ToArray();
            StillShades++;
        }
        canvas.Save();
        canvas.ResetMatrix();
        canvas.DrawImage(still, device.Left, device.Top, copy);
        canvas.Restore();
        return true;
    }

    // everything a still band's pixels follow: the frame's uniforms, the overall opacity, where the window ends, and where on
    // the device the band falls (its transform to the band's own pixels)
    private int Key(ShaderLightFrame f, float scale, float opacity, float? fadeBottom, SKMatrix m, SKRectI device)
    {
        var n = 0;
        void Add(double v) { if (n == key.Length) Array.Resize(ref key, n * 2); key[n++] = (float)v; }
        foreach (var v in new[] { f.Alpha, f.Loop, f.Energy, f.Work, f.Key, f.Caret, f.Spread, f.Intensity, f.W, f.H, f.Radius, f.Reach, f.Behind, f.Halo }) Add(v);
        foreach (var v in f.Palette) Add(v);
        foreach (var v in f.Average) Add(v);
        foreach (var v in f.Pulse) Add(v);
        foreach (var v in f.PulseAmp) Add(v);
        foreach (var v in f.Modes) Add(v);
        Add(f.Dark ? 1 : 0); Add(f.GuardLift); Add(Guard ? 1 : 0); Add(scale); Add(opacity); Add(fadeBottom ?? 1e6f);
        Add(m.ScaleX); Add(m.SkewX); Add(m.TransX); Add(m.SkewY); Add(m.ScaleY); Add(m.TransY); Add(m.Persp0); Add(m.Persp1); Add(m.Persp2);
        Add(device.Width); Add(device.Height);
        return n;
    }

    // the surface's pixels under the band, inside this frame's clip (an image of them, and the canvas's transform)
    private static SKImage? Under(SKCanvas canvas, SKSurface surface, SKRect band, out SKMatrix transform)
    {
        transform = canvas.TotalMatrix;
        var at = SKRectI.Intersect(SKRectI.Ceiling(transform.MapRect(band), true), canvas.DeviceClipBounds);
        if (at.IsEmpty) return null;
        var image = surface.Snapshot(at);
        transform.TransX -= at.Left;
        transform.TransY -= at.Top;
        return image;
    }

    // the image as a shader in the composer's coordinates (its reads clamped to it: only this frame's pixels)
    private static SKShader? Backdrop(SKImage image, SKMatrix transform)
    {
        using (image)
        {
            if (!transform.TryInvert(out var local)) return null;
            return image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, new SKSamplingOptions(SKFilterMode.Linear), local);
        }
    }

    /// <summary>The frame's shader with its uniforms bound (tests read them; the caller disposes it).</summary>
    internal SKShader Shader(ShaderLightFrame f, float scale, float opacity = 1, SKShader? under = null, float? fadeBottom = null)
    {
        var effect = ShaderLightShaders.Effect(f.Def.Id);
        if (!programs.TryGetValue(f.Def.Id, out var p)) programs[f.Def.Id] = p = (new SKRuntimeEffectUniforms(effect), new SKRuntimeEffectChildren(effect));
        Bind(p.Uniforms, f, scale, opacity, under is not null, fadeBottom);
        p.Children["uBack"] = under ?? nothing;
        var shader = effect.ToShader(p.Uniforms, p.Children);
        p.Children["uBack"] = nothing; // the frame's image goes with its shader
        return shader;
    }

    /// <summary>The page's uniforms from a frame (composer px; uAA the size of a device pixel in them).</summary>
    internal void Bind(SKRuntimeEffectUniforms u, ShaderLightFrame f, float scale, float opacity, bool back = false, float? fadeBottom = null)
    {
        float w = (float)f.W, h = (float)f.H;
        Pair(u, "uC", w / 2, h / 2);
        Pair(u, "uB", w / 2, h / 2);
        u["uR"] = (float)f.Radius;
        u["uLoop"] = (float)f.Loop;
        u["uEnergy"] = (float)f.Energy;
        u["uWork"] = (float)f.Work;
        u["uInt"] = (float)f.Intensity;
        u["uSpread"] = (float)f.Spread;
        u["uKey"] = (float)f.Key;
        u["uCaret"] = (float)f.Caret;
        u["uPal"] = f.Palette;
        u["uAvg"] = f.Average;
        u["uCore"] = Core;
        u["uAA"] = 1 / Math.Max(1, scale);
        u["uReach"] = (float)f.Reach;
        u["uOpacity"] = (float)Math.Clamp(f.Alpha * opacity, 0, 1);
        u["uPulse"] = f.Pulse;
        u["uPulseA"] = f.PulseAmp;
        u["uUseBack"] = back ? 1f : 0f;
        u["uModes"] = f.Modes;
        u["uBehind"] = (float)f.Behind;
        u["uHalo"] = (float)f.Halo;
        v4[0] = TextInset; v4[1] = -1e5f; v4[2] = w - TextInset; v4[3] = -TextGap;
        u["uText"] = v4;
        u["uTextLight"] = f.Dark ? 1f : 0f;
        u["uFade"] = fadeBottom ?? 1e6f;
        u["uGuard"] = Guard ? 1f : 0f;
        u["uGuardLift"] = (float)Math.Max(1, f.GuardLift);
        u["uGlyph"] = f.Dark ? DarkGlyph : LightGlyph;
        u["uGround"] = f.Dark ? DarkGround : LightGround;
    }

    private void Pair(SKRuntimeEffectUniforms u, string name, float x, float y)
    {
        v2[0] = x; v2[1] = y;
        u[name] = v2;
    }

    /// <summary>Releases the programs' uniforms. Call it on the thread that draws.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var (u, c) in programs.Values) { u.Dispose(); c.Dispose(); }
        programs.Clear();
        paint.Dispose();
        copy.Dispose();
        still?.Dispose();
        still = null;
        nothing.Dispose();
    }
}
