using SkiaSharp;

namespace Marrow.UI.Theme.Wallpapers;

/// <summary>One window's wallpaper: phase clock, eased uniforms and a size-specific static bake.</summary>
public sealed class WallpaperRenderer : IDisposable
{
    private WallpaperDefinition? wall;
    private SKRuntimeEffect? effect;
    private SKRuntimeEffectUniforms? uniforms;
    private SKImage? cache, outgoing;
    private SKSurface? surface;
    private double fade = 1;
    private bool initialized;
    private readonly SKPaint paint = new();
    private readonly float[] resolution = new float[2], optionUniform = new float[2];
    private (int W, int H, float Dpr, GRContext? Gpu) size;
    private float[] palette = new float[18], fromPalette = new float[18], targetPalette = new float[18];
    private double dark, fromDark, targetDark, transition = 1;
    private readonly double[] options = new double[2], fromOptions = new double[2], targetOptions = new double[2];
    private double speed = 1, targetSpeed = 1;
    private bool dirty = true;
    public bool Visible { get; set; } = true;
    public bool ReduceMotion { get; set; }
    public double Phase { get; private set; }
    public int Renders { get; private set; }
    public bool NeedsFrames => Visible && (fade < 1 || (wall is not null && (transition < 1 || (wall.Kind == WallpaperKind.Animated && !ReduceMotion))));

    public WallpaperRenderer(WallpaperSelection selection, bool dark = true) => Set(selection, dark);

    public void Set(WallpaperSelection selection, bool dark)
    {
        selection = WallpaperLibraryStore.Normalize(selection);
        if (selection.Kind == WallpaperKind.None)
        {
            if (wall is null) return;
            FreezeOutgoing(); Release(); wall = null; transition = 1; fade = 0; return;
        }
        var next = WallpaperCatalogue.Get(selection.Kind, selection.Id!);
        var changed = wall != next;
        if (changed)
        {
            FreezeOutgoing(); Release(); fade = initialized ? 0 : 1; initialized = true; wall = next; effect = WallpaperCatalogue.Compile(next); uniforms = new(effect); Phase = 0;
        }
        var pack = next.Packs.Single(p => p.Id == selection.Pack);
        var pal = WallpaperPalette.For(pack, dark ? 1 : 0);
        var opt = next.Options.Select(o => selection.Options![o.Id]).ToArray();
        targetSpeed = selection.Speed;
        if (changed)
        {
            palette = pal; targetPalette = (float[])pal.Clone(); this.dark = targetDark = dark ? 1 : 0;
            for (var i = 0; i < 2; i++) options[i] = targetOptions[i] = opt[i];
            speed = targetSpeed; transition = 1; dirty = true;
        }
        else if (!targetPalette.SequenceEqual(pal) || targetDark != (dark ? 1 : 0) || !targetOptions.SequenceEqual(opt))
        {
            palette.CopyTo(fromPalette, 0); targetPalette = pal; fromDark = this.dark; targetDark = dark ? 1 : 0;
            options.CopyTo(fromOptions, 0); opt.CopyTo(targetOptions, 0); transition = 0;
        }
    }

    public void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (!Visible) return;
        if (fade < 1)
        {
            fade = Math.Min(1, fade + seconds / .55);
            if (fade == 1) { outgoing?.Dispose(); outgoing = null; }
        }
        if (wall is null) return;
        if (transition < 1)
        {
            transition = Math.Min(1, transition + seconds / .55);
            var k = transition * transition * (3 - 2 * transition);
            for (var i = 0; i < 18; i++) palette[i] = (float)(fromPalette[i] + (targetPalette[i] - fromPalette[i]) * k);
            dark = fromDark + (targetDark - fromDark) * k;
            for (var i = 0; i < 2; i++) options[i] = fromOptions[i] + (targetOptions[i] - fromOptions[i]) * k;
            dirty = true;
        }
        if (wall.Kind != WallpaperKind.Animated || ReduceMotion) return;
        // Integral of the 0.18 s speed follower: changing the target does not change the phase or current speed.
        var decay = Math.Exp(-seconds / .18);
        var travel = targetSpeed * seconds + (speed - targetSpeed) * .18 * (1 - decay);
        speed = targetSpeed + (speed - targetSpeed) * decay;
        Phase = (Phase + travel) % wall.Period;
        if (seconds > 0) dirty = true;
    }

    /// <summary>A still preview/proof at raw shader time, including T; independent of the wrapped playback clock.</summary>
    public void DrawFrameAt(SKCanvas canvas, int width, int height, double loopTime)
    {
        if (!double.IsFinite(loopTime)) throw new ArgumentOutOfRangeException(nameof(loopTime));
        if (wall is not null) DrawShader(canvas, width, height, 1, loopTime);
    }

    public void Draw(SKCanvas canvas, int width, int height, float dpr = 1, GRContext? gpu = null)
    {
        if (width <= 0 || height <= 0) return;
        var nextSize = (width, height, dpr, gpu);
        if (size != nextSize) { size = nextSize; dirty = true; surface?.Dispose(); surface = null; }
        if (wall is not null && (cache is null || dirty))
        {
            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            surface ??= (gpu is null ? null : SKSurface.Create(gpu, false, info)) ?? SKSurface.Create(info);
            // Drop the previous snapshot before writing, so Skia need not copy it on write.
            cache?.Dispose(); cache = null;
            surface.Canvas.Clear(SKColors.Transparent);
            DrawShader(surface.Canvas, width, height, dpr, Phase);
            cache = surface.Snapshot(); dirty = false; Renders++;
        }
        Composite(canvas, width, height);
    }

    private void DrawShader(SKCanvas canvas, int width, int height, float dpr, double time)
    {
        if (wall is null || effect is null || uniforms is null) return;
        resolution[0] = width; resolution[1] = height; uniforms["u_res"] = resolution;
        uniforms["u_pal"] = palette; uniforms["u_dark"] = (float)dark;
        if (wall.Kind == WallpaperKind.Static)
        {
            uniforms["u_dpr"] = dpr; uniforms["u_grain"] = (float)options[0]; uniforms["u_opt"] = (float)options[1];
        }
        else
        {
            uniforms["u_t"] = (float)time; uniforms["u_px"] = 2f / height;
            optionUniform[0] = (float)options[0]; optionUniform[1] = (float)options[1]; uniforms["u_opt"] = optionUniform;
        }
        using var shader = effect.ToShader(uniforms);
        paint.Color = SKColors.White; paint.BlendMode = SKBlendMode.SrcOver; paint.Shader = shader;
        canvas.DrawRect(0, 0, width, height, paint); paint.Shader = null;
    }

    private void Composite(SKCanvas canvas, int width, int height)
    {
        if (fade == 1 && outgoing is null)
        {
            if (cache is not null) canvas.DrawImage(cache, new SKRect(0, 0, width, height));
            return;
        }
        var k = fade * fade * (3 - 2 * fade);
        // Plus blends the two premultiplied images linearly, rather than darkening the middle of the fade.
        canvas.SaveLayer();
        if (outgoing is not null)
        {
            paint.Color = SKColors.White.WithAlpha((byte)Math.Round(255 * (1 - k)));
            paint.BlendMode = SKBlendMode.SrcOver;
            canvas.DrawImage(outgoing, new SKRect(0, 0, width, height), paint);
        }
        if (cache is not null)
        {
            paint.Color = SKColors.White.WithAlpha((byte)Math.Round(255 * k)); paint.BlendMode = SKBlendMode.Plus;
            canvas.DrawImage(cache, new SKRect(0, 0, width, height), paint);
        }
        canvas.Restore(); paint.BlendMode = SKBlendMode.SrcOver;
    }

    private void FreezeOutgoing()
    {
        if (size.W <= 0 || size.H <= 0) return;
        using var frozen = (size.Gpu is null ? null : SKSurface.Create(size.Gpu, false,
            new SKImageInfo(size.W, size.H))) ?? SKSurface.Create(new SKImageInfo(size.W, size.H));
        frozen.Canvas.Clear(SKColors.Transparent); Composite(frozen.Canvas, size.W, size.H);
        outgoing?.Dispose(); outgoing = frozen.Snapshot();
    }

    private void Release()
    {
        cache?.Dispose(); cache = null; surface?.Dispose(); surface = null;
        uniforms?.Dispose(); uniforms = null; effect?.Dispose(); effect = null;
    }
    public void Dispose() { Release(); outgoing?.Dispose(); outgoing = null; paint.Dispose(); wall = null; }
}
