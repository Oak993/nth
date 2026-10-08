using SkiaSharp;

namespace Marrow.UI.Theme.Wallpapers;

/// <summary>One window's wallpaper: phase clock, eased uniforms and a size-specific static bake.</summary>
public sealed class WallpaperRenderer : IDisposable
{
    private WallpaperEnvironment env;
    private StaticWallpaperController? staticController;
    private readonly StaticWallpaperFocus staticFocus = new();
    private readonly AnimatedWallpaperFocus animatedFocus = new();
    public StaticWallpaperState? StaticState { get; private set; }
    public double StaticGrain => staticController?.Grain ?? 0;
    public double StaticOption => staticController?.Option ?? 0;
    private WallpaperDefinition? wall;
    private WallpaperSelection selection = new();
    private bool darkTheme, reduceMotion;
    private (int Minute, string Accent) envKey;
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
    private readonly double[] sun = new double[2], fromSun = new double[2], targetSun = new double[2];
    private readonly float[] tiltUniform = new float[2], sunUniform = new float[2];
    private double focus, fromFocus, targetFocus, tiltX, tiltY;
    private double speed = 1, targetSpeed = 1;
    private bool dirty = true;
    public bool Visible { get; set; } = true;
    /// <summary>Under Reduce Motion Perspective is off at once: the tilt is exactly (0, 0), whatever the window position.</summary>
    public bool ReduceMotion
    {
        get => reduceMotion;
        set { if (reduceMotion == value) return; reduceMotion = value; RefreshStaticInputs(); if (value && (tiltX != 0 || tiltY != 0)) { tiltX = tiltY = 0; dirty = true; } }
    }
    /// <summary>The eased tilt that is drawn, each axis -1..1.</summary>
    public (double X, double Y) Tilt => (tiltX, tiltY);
    /// <summary>The eased light offset that is drawn (u_sun), in p units.</summary>
    public (double X, double Y) Sun => (sun[0], sun[1]);
    /// <summary>The eased Focus that is drawn, 0 sharp .. 1 dreamy.</summary>
    public double FocusValue => focus;
    /// <summary>The OKLab palette (6 x 3) the colours are heading for: pack, theme, Match accent and time of day applied.</summary>
    public float[] TargetPalette => (float[])targetPalette.Clone();
    /// <summary>How many times the Focus blur has been drawn.</summary>
    public int Blurs { get; private set; }
    public double Phase { get; private set; }
    public int Renders { get; private set; }
    public bool NeedsFrames => Visible && (fade < 1 || (wall is not null && ((staticController?.Busy ?? (transition < 1)) || (wall.Kind == WallpaperKind.Animated && !ReduceMotion))));
    internal bool HasTransitions => fade < 1 || (wall is not null && (staticController?.Busy ?? transition < 1));

    public WallpaperRenderer(WallpaperSelection selection, bool dark = true, WallpaperEnvironment? environment = null)
    {
        env = environment ?? WallpaperEnvironment.Create();
        Set(selection, dark);
    }

    public void Set(WallpaperSelection selection, bool dark)
    {
        selection = WallpaperLibraryStore.Normalize(selection);
        this.selection = selection; darkTheme = dark;
        if (selection.Kind == WallpaperKind.None)
        {
            if (wall is null) return;
            FreezeOutgoing(); Release(); wall = null; staticController = null; StaticState = null; transition = 1; fade = 0; return;
        }
        var next = WallpaperCatalogue.Get(selection.Kind, selection.Id!);
        var changed = wall != next;
        if (changed)
        {
            FreezeOutgoing(); Release(); fade = initialized ? 0 : 1; initialized = true; wall = next; effect = WallpaperCatalogue.Compile(next); uniforms = new(effect); Phase = 0;
        }
        this.selection = selection; darkTheme = dark; targetSpeed = selection.Speed;
        if (next.Static is { } data)
        {
            if (changed) staticController = new(data);
            dirty |= staticController!.Target(next, selection, dark, env, ReduceMotion, changed);
            if (changed) dirty = true;
            envKey = EnvKey(); return;
        }
        staticController = null; StaticState = null;
        Retarget(changed);
        if (changed) { speed = targetSpeed; transition = 1; dirty = true; }
    }

    public void SetEnvironment(WallpaperEnvironment environment)
    {
        env = environment; Set(selection, darkTheme);
    }

    // Compatibility entrypoints delegate to the shared environment and controller.
    public void SetStaticInputs(StaticWallpaperInputs inputs) => SetEnvironment(inputs);
    public void RefreshStaticInputs()
    {
        if (wall?.Static is not null)
            dirty |= staticController!.Target(wall, selection, darkTheme, env, ReduceMotion, false);
    }

    private double? Hour()
    {
        if (wall is null || selection.Dynamic != true) return null;
        var now = env.Clock.GetLocalNow();
        return now.Hour + now.Minute / 60.0;
    }

    private (int Minute, string Accent) EnvKey() => (Hour() is { } h ? (int)Math.Round(h * 60) : -1,
        selection.Pack is WallpaperMatchPack.Id or StaticWallpaperPalette.AccentPack ? WallpaperMatchPack.Normalize(env.Accent.Hex) : "");

    /// <summary>Works out where palette, sun, options and focus should go; a difference starts a crossfade from the current values, a first or new wallpaper snaps.</summary>
    private void Retarget(bool snap)
    {
        var next = wall!;
        var pack = selection.Pack == WallpaperMatchPack.Id ? WallpaperMatchPack.Build(next, env.Accent.Hex) : next.Packs.Single(p => p.Id == selection.Pack);
        var looked = WallpaperTimeOfDay.PaletteFor(next, pack, darkTheme ? 1 : 0, Hour());
        var opt = next.Options.Select(o => selection.Options![o.Id]).ToArray();
        var nextFocus = selection.Focus ?? next.Premium?.FocusDefault ?? 0; var nextDark = darkTheme ? 1 : 0;
        envKey = EnvKey();
        if (snap)
        {
            palette = looked.Palette; targetPalette = (float[])looked.Palette.Clone(); dark = targetDark = nextDark;
            for (var i = 0; i < 2; i++) options[i] = targetOptions[i] = opt[i];
            sun[0] = targetSun[0] = looked.Sun.X; sun[1] = targetSun[1] = looked.Sun.Y; focus = targetFocus = nextFocus;
            tiltX = tiltY = 0;
        }
        else if (!targetPalette.SequenceEqual(looked.Palette) || targetDark != nextDark || !targetOptions.SequenceEqual(opt)
            || targetSun[0] != looked.Sun.X || targetSun[1] != looked.Sun.Y || targetFocus != nextFocus)
        {
            palette.CopyTo(fromPalette, 0); fromDark = dark; options.CopyTo(fromOptions, 0); sun.CopyTo(fromSun, 0); fromFocus = focus;
            targetPalette = looked.Palette; targetDark = nextDark; opt.CopyTo(targetOptions, 0);
            targetSun[0] = looked.Sun.X; targetSun[1] = looked.Sun.Y; targetFocus = nextFocus; transition = 0;
        }
    }

    /// <summary>
    /// The clock (Dynamic) or the Windows accent (Match accent) has changed: read both and, if a minute or the accent differs, retarget and ease.
    /// They are read only here, in <see cref="Set"/>, and nowhere per frame; <see cref="WallpaperWakeSource"/> calls this at each minute boundary and
    /// on an accent change. A hidden window has nobody to watch the ease, so it lands at once.
    /// </summary>
    public void Wake()
    {
        if (wall is null || EnvKey() == envKey) return;
        if (staticController is not null)
        {
            dirty |= staticController.Target(wall, selection, darkTheme, env, ReduceMotion, false);
            envKey = EnvKey(); if (!Visible) Settle(); return;
        }
        Retarget(false);
        if (!Visible) Settle();
    }

    /// <summary>Completes every crossfade and the tilt at once (the first picture on screen, a proof); the phase and speed stay.</summary>
    public void Settle()
    {
        if (wall is null) return;
        if (staticController is not null) { staticController.Settle(); dirty = true; return; }
        transition = 1; targetPalette.CopyTo(palette, 0); dark = targetDark; targetOptions.CopyTo(options, 0);
        sun[0] = targetSun[0]; sun[1] = targetSun[1]; focus = targetFocus;
        (tiltX, tiltY) = TiltTarget(); dirty = true;
    }

    private (double X, double Y) TiltTarget()
    {
        if (reduceMotion || wall?.Premium is null || selection.Persp != true) return (0, 0);
        var (x, y) = env.Position.Tilt;
        return (double.IsFinite(x) ? Math.Clamp(x, -1, 1) : 0, double.IsFinite(y) ? Math.Clamp(y, -1, 1) : 0);
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
        if (staticController is not null)
        {
            dirty |= staticController.Advance(seconds, size.H); return;
        }
        if (transition < 1)
        {
            transition = Math.Min(1, transition + seconds / .55);
            var k = transition * transition * (3 - 2 * transition);
            for (var i = 0; i < 18; i++) palette[i] = (float)(fromPalette[i] + (targetPalette[i] - fromPalette[i]) * k);
            dark = fromDark + (targetDark - fromDark) * k;
            for (var i = 0; i < 2; i++) { options[i] = fromOptions[i] + (targetOptions[i] - fromOptions[i]) * k; sun[i] = fromSun[i] + (targetSun[i] - fromSun[i]) * k; }
            focus = fromFocus + (targetFocus - fromFocus) * k;
            dirty = true;
        }
        if (wall.Kind != WallpaperKind.Animated || ReduceMotion) return;
        EaseTilt(seconds);
        // Integral of the 0.18 s speed follower: changing the target does not change the phase or current speed.
        var decay = Math.Exp(-seconds / .18);
        var travel = targetSpeed * seconds + (speed - targetSpeed) * .18 * (1 - decay);
        speed = targetSpeed + (speed - targetSpeed) * decay;
        Phase = (Phase + travel) % wall.Period;
        if (seconds > 0) dirty = true;
    }

    // Perspective follows the window position with a 0.35 s time constant (OPTIONS-FORMAT 3); within 0.002 of the target it lands on it.
    private void EaseTilt(double seconds)
    {
        var (targetX, targetY) = TiltTarget(); var a = 1 - Math.Exp(-seconds / .35);
        static double Ease(double now, double to, double a) => Math.Abs(to - now) > 2e-3 ? now + (to - now) * a : to;
        var (nextX, nextY) = (Ease(tiltX, targetX, a), Ease(tiltY, targetY, a));
        if (nextX != tiltX || nextY != tiltY) { tiltX = nextX; tiltY = nextY; dirty = true; }
    }

    /// <summary>A still preview/proof at raw shader time, including T; independent of the wrapped playback clock.</summary>
    public void DrawFrameAt(SKCanvas canvas, int width, int height, double loopTime)
    {
        if (!double.IsFinite(loopTime)) throw new ArgumentOutOfRangeException(nameof(loopTime));
        if (wall?.Kind == WallpaperKind.Static) Draw(canvas, width, height);
        else if (wall is not null) DrawShader(canvas, width, height, 1, loopTime);
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
            if (StaticState is { Sigma: > 0 } state)
                staticFocus.Apply(surface, width, height, gpu, state.Sigma, state.Dream, StaticGrain);
            cache = surface.Snapshot(); dirty = false; Renders++;
        }
        Composite(canvas, width, height);
    }

    private void DrawShader(SKCanvas canvas, int width, int height, float dpr, double time)
    {
        if (wall is null || effect is null || uniforms is null) return;
        // Focus: the picture is drawn without the dither, blurred with a Gaussian of the page's sigma, and dithered after (as the page's blur pass does).
        if (wall.Kind == WallpaperKind.Animated && WallpaperFocus.Sigma(height, focus) is var sigma && sigma >= WallpaperFocus.MinSigma)
        {
            DrawBlurred(canvas, width, height, dpr, time, (float)sigma); return;
        }
        DrawPlain(canvas, width, height, dpr, time, 1);
    }

    private void DrawPlain(SKCanvas canvas, int width, int height, float dpr, double time, float dither)
    {
        resolution[0] = width; resolution[1] = height; uniforms!["u_res"] = resolution;
        uniforms["u_pal"] = palette; uniforms["u_dark"] = (float)dark;
        if (wall!.Kind == WallpaperKind.Static)
        {
            var data = wall.Static!; StaticState = staticController!.State(height);
            uniforms["u_pal"] = StaticState.Palette; uniforms["u_dark"] = (float)StaticState.Dark;
            uniforms["u_dpr"] = dpr; uniforms["u_grain"] = StaticState.Sigma > 0 ? 0 : (float)StaticGrain;
            uniforms["u_opt"] = (float)StaticOption;
            uniforms["u_tilt"] = new float[] { (float)StaticState.Tilt.X, (float)StaticState.Tilt.Y };
            uniforms["u_tg"] = data.OwnPerspective ? 0f : 1f;
            uniforms["u_light"] = new float[] { (float)StaticState.Light[0], (float)StaticState.Light[1] };
            uniforms["u_light0"] = new float[] { (float)data.Light[0], (float)data.Light[1] };
            uniforms["u_lw"] = data.OwnLight ? 0 : .028f; uniforms["u_focus"] = (float)StaticState.Focus;
        }
        else
        {
            uniforms["u_t"] = (float)time; uniforms["u_px"] = 2f / height;
            optionUniform[0] = (float)options[0]; optionUniform[1] = (float)options[1]; uniforms["u_opt"] = optionUniform;
            tiltUniform[0] = (float)tiltX; tiltUniform[1] = (float)tiltY; uniforms["u_tilt"] = tiltUniform;
            sunUniform[0] = (float)sun[0]; sunUniform[1] = (float)sun[1]; uniforms["u_sun"] = sunUniform;
            uniforms["u_focus"] = (float)focus; uniforms["u_dither"] = dither;
            // a flat wallpaper pans as a whole by up to 0.012 p units; one with layers reads u_tilt itself
            uniforms["u_pan"] = wall.Premium?.Perspective.Mode == "pan" ? 0.012f : 0f;
        }
        using var shader = effect!.ToShader(uniforms);
        paint.Color = SKColors.White; paint.BlendMode = SKBlendMode.SrcOver; paint.Shader = shader;
        canvas.DrawRect(0, 0, width, height, paint); paint.Shader = null;
    }

    private void DrawBlurred(SKCanvas canvas, int width, int height, float dpr, double time, float sigma)
    {
        animatedFocus.Apply(canvas, width, height, size.Gpu, sigma, target => DrawPlain(target, width, height, dpr, time, 0));
        Blurs++;
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
    public void Dispose() { Release(); outgoing?.Dispose(); outgoing = null; paint.Dispose(); staticFocus.Dispose(); animatedFocus.Dispose(); wall = null; }
}
