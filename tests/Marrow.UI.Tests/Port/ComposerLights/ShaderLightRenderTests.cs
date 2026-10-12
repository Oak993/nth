using System.Diagnostics;
using System.Globalization;
using Marrow.Core.Models.ComposerLights;
using Marrow.UI.Port.ComposerLights;
using Marrow.UI.Tests.Fixtures;
using SkiaSharp;

namespace Marrow.UI.Tests.Port.ComposerLights;

// PORT-ONLY, owner-ruled addition (OD-0150 Composer Light v2, v3 and v4; ED-NEW-lightv2-1, -5, -6): the shader lights as
// the renderer draws them. Every light's SkSL compiles with every uniform bound; a frame lights the window outside the
// composer and nothing past its band; every colour pack draws on every light; the pack, spread and intensity change what is
// drawn; the composer's own text keeps 4.5:1 over the inner light at its strongest (the v4 page's text-zone cap); the three
// Text and light modes compose with the window's pixels as the page's do; the wallpaper's veil measures bright and busy
// wallpapers as wanting more dimming than dark and calm ones.
public sealed class ShaderLightRenderTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private const double W = 400, H = 46, R = 23;

    private static readonly string[] Uniforms =
    [
        "uC", "uB", "uR", "uLoop", "uEnergy", "uWork", "uInt", "uSpread", "uKey", "uCaret", "uPal", "uAvg", "uCore", "uAA", "uReach",
        "uOpacity", "uPulse", "uPulseA", "uUseBack", "uModes", "uBehind", "uHalo", "uText", "uTextLight", "uFade",
        "uGuard", "uGuardLift", "uGlyph", "uGround",
    ];

    // a frame of light `id` with `options`, at its still (reduce motion), in `phase`, in the dark appearance
    internal static ShaderLightFrame Frame(string id, ShaderLightOptions? options = null, ComposerLightPhase phase = ComposerLightPhase.Typing,
        bool dark = true, ComposerLightSettings? settings = null)
    {
        var s = (settings ?? ComposerLightSettings.Default) with { Style = ComposerLightStyle.Shader, ShaderId = id };
        if (options is not null) s = s with { Shaders = s.Shaders.With(id, options) };
        var light = new ShaderLightAnimator(s, phase, dark, reduceMotion: true, ComposerLightPalette.Default);
        light.Produce(0, W, H, R);
        Assert.Equal(1, light.DrawnCount);
        return light.Drawn[0];
    }

    // draws `f` with the composer's top left at (margin + reach) on a transparent surface (or `under`'s pixels), at `scale`
    private static SKBitmap Render(ShaderLightFrame f, float scale = 1, float margin = 20, Action<SKCanvas, float>? under = null,
        bool compose = false, SKRect? clip = null, bool guard = true)
    {
        var reach = (float)f.Reach;
        var size = new SKSizeI((int)Math.Ceiling(((float)W + 2 * (reach + margin)) * scale), (int)Math.Ceiling(((float)H + 2 * (reach + margin)) * scale));
        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(scale);
        canvas.Translate(reach + margin, reach + margin);
        under?.Invoke(canvas, reach + margin);
        if (clip is { } c) canvas.ClipRect(c);
        using var renderer = new ShaderLightRenderer { Guard = guard };
        Assert.True(renderer.Draw(canvas, f, scale, 1, compose ? surface : null));
        var bitmap = new SKBitmap(info);
        Assert.True(surface.ReadPixels(info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0));
        return bitmap;
    }

    private static SKColor At(SKBitmap b, ShaderLightFrame f, double x, double y, float scale = 1, float margin = 20)
    {
        var o = (float)f.Reach + margin;
        return b.GetPixel((int)Math.Floor((x + o) * scale), (int)Math.Floor((y + o) * scale));
    }

    [Fact]
    [Trait("Decision", "OD-0150")] // ED-NEW-lightv2-1
    public void Every_light_compiles_with_its_uniforms_bound()
    {
        Assert.Equal(21, ShaderLightCatalog.All.Count); // v2's six, v3's seven (G retired), v4's eight
        foreach (var def in ShaderLightCatalog.All)
        {
            Assert.True(ShaderLightShaders.Has(def.Id), def.Id);
            var effect = ShaderLightShaders.Effect(def.Id);
            foreach (var name in Uniforms) Assert.Contains(name, effect.Uniforms);
            Assert.Contains("uBack", effect.Children);
            using var renderer = new ShaderLightRenderer();
            using var shader = renderer.Shader(Frame(def.Id), 2);
            Assert.NotNull(shader);
        }
    }

    [Fact]
    [Trait("Decision", "OD-0150")] // ED-NEW-lightv2-1
    public void A_frame_lights_the_window_outside_the_composer_and_nothing_past_its_band()
    {
        const float scale = .5f, margin = 24;
        foreach (var def in ShaderLightCatalog.All)
        {
            var f = Frame(def.Id);
            using var b = Render(f, scale, margin);
            var o = (float)f.Reach + margin;
            int outside = 0, past = 0;
            for (var y = 0; y < b.Height; y++)
                for (var x = 0; x < b.Width; x++)
                {
                    if (b.GetPixel(x, y).Alpha <= 2) continue;
                    double cx = x / scale - o + .5 / scale, cy = y / scale - o + .5 / scale;
                    var inComposer = cx >= 0 && cx <= W && cy >= 0 && cy <= H;
                    if (!inComposer) outside++;
                    if (cx < -f.Reach - 2 || cx > W + f.Reach + 2 || cy < -f.Reach - 2 || cy > H + f.Reach + 2) past++;
                }
            Assert.True(outside > 200, $"{def.Id}: only {outside} lit pixels outside the composer");
            Assert.Equal(0, past);
        }
    }

    [Fact]
    [Trait("Decision", "OD-0150")] // ED-NEW-lightv2-4
    public void Every_pack_draws_on_every_light()
    {
        var custom = new ShaderLightColours("#FF3355", "#33FF99", "#3366FF", "#FFEE44");
        var clip = new SKRect(-30, -30, (float)W + 30, (float)H + 30);
        foreach (var def in ShaderLightCatalog.All)
            foreach (var pack in ShaderLightCatalog.Packs)
            {
                var f = Frame(def.Id, def.Defaults with { Pack = pack.Id, Custom = custom });
                using var b = Render(f, .5f, 0, clip: clip);
                long sum = 0;
                foreach (var p in b.Pixels) sum += p.Alpha;
                Assert.True(sum > 255 * 40, $"{def.Id} on {pack.Id}: nearly nothing drawn ({sum})");
            }
    }

    [Fact]
    [Trait("Decision", "OD-0150")] // ED-NEW-lightv2-4
    public void The_pack_the_spread_and_the_intensity_change_what_is_drawn()
    {
        foreach (var id in new[] { "lumen", "nebula", "crystallum" })
        {
            var def = ShaderLightCatalog.Find(id)!;
            static (double Sum, int Lit, double Red, double Blue) Measure(SKBitmap b)
            {
                double sum = 0, red = 0, blue = 0; var lit = 0;
                foreach (var p in b.Pixels) { sum += p.Alpha; red += p.Red; blue += p.Blue; if (p.Alpha > 4) lit++; }
                return (sum, lit, red, blue);
            }
            using var dim = Render(Frame(id, def.Defaults with { Intensity = .5 }), .5f);
            using var bright = Render(Frame(id, def.Defaults with { Intensity = 2 }), .5f);
            Assert.True(Measure(bright).Sum > Measure(dim).Sum * 1.3, $"{id}: intensity 2 is not brighter than .5");
            using var near = Render(Frame(id, def.Defaults with { Spread = 60 }), .5f);
            using var far = Render(Frame(id, def.Defaults with { Spread = 200 }), .5f);
            Assert.True(Measure(far).Lit > Measure(near).Lit, $"{id}: spread 200 lights no more of the window than 60");
            using var ember = Render(Frame(id, def.Defaults with { Pack = "ember" }), .5f);
            using var ice = Render(Frame(id, def.Defaults with { Pack = "ice" }), .5f);
            var (_, _, er, eb) = Measure(ember);
            var (_, _, ir, ib) = Measure(ice);
            Assert.True(er / Math.Max(1, eb) > ir / Math.Max(1, ib), $"{id}: Ember is not redder than Ice");
        }
    }

    [Fact]
    [Trait("Decision", "OD-0150")] // ED-NEW-lightv2-1
    public void With_no_GPU_a_still_band_is_shaded_once_and_its_pixels_kept()
    {
        var f = Frame("lumen");
        var reach = (float)f.Reach;
        var info = new SKImageInfo((int)Math.Ceiling((W + 2 * reach + 8) * 1.5), (int)Math.Ceiling((H + 2 * reach + 8) * 1.5), SKColorType.Rgba8888, SKAlphaType.Premul);
        using var renderer = new ShaderLightRenderer();
        SKBitmap Draw(Func<SKCanvas, bool> draw, float x)
        {
            using var surface = SKSurface.Create(info);
            surface.Canvas.Clear(SKColors.Transparent);
            surface.Canvas.Scale(1.5f);
            surface.Canvas.Translate(reach + x, reach + 4.3f);
            Assert.True(draw(surface.Canvas));
            var bitmap = new SKBitmap(info);
            Assert.True(surface.ReadPixels(info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0));
            return bitmap;
        }
        using var direct = Draw(c => renderer.Draw(c, f, 1.5f), 3.7f);
        using var first = Draw(c => renderer.DrawStill(c, f, 1.5f), 3.7f);
        using var again = Draw(c => renderer.DrawStill(c, f, 1.5f), 3.7f);
        Assert.Equal(1, renderer.StillShades); // the second redraw copied the pixels
        // the premultiplied bytes as drawn (GetPixel unpremultiplies, which magnifies a rounding step where the light is faint)
        ReadOnlySpan<byte> drawn = direct.GetPixelSpan(), kept = first.GetPixelSpan(), copied = again.GetPixelSpan();
        Assert.True(kept.SequenceEqual(copied), "the copied pixels differ from the kept ones");
        int worst = 0, at = 0;
        for (var i = 0; i < drawn.Length; i++)
            if (Math.Abs(drawn[i] - kept[i]) > worst) (worst, at) = (Math.Abs(drawn[i] - kept[i]), i);
        Assert.True(worst <= 2, $"the kept pixels differ from a direct draw by {worst}/255 at ({at / 4 % info.Width}, {at / 4 / info.Width})"); // the blends' rounding
        using (Draw(c => renderer.DrawStill(c, Frame("lumen", ShaderLightCatalog.Find("lumen")!.Defaults with { Intensity = 2 }), 1.5f), 3.7f)) { }
        Assert.Equal(2, renderer.StillShades); // a changed frame is shaded again
        using (Draw(c => renderer.DrawStill(c, f, 1.5f), 4.1f)) { }
        Assert.Equal(3, renderer.StillShades); // and so is a band that moved on the device
    }

    // WCAG's contrast of two encoded colours
    internal static double Contrast((double R, double G, double B) a, (double R, double G, double B) b)
    {
        static double L((double R, double G, double B) c)
        {
            static double Lin(double v) => v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4);
            return .2126 * Lin(c.R) + .7152 * Lin(c.G) + .0722 * Lin(c.B);
        }
        double la = L(a), lb = L(b);
        return (Math.Max(la, lb) + .05) / (Math.Min(la, lb) + .05);
    }

    private static (double R, double G, double B) Over((double R, double G, double B) top, double alpha, (double R, double G, double B) under) =>
        (top.R * alpha + under.R * (1 - alpha), top.G * alpha + under.G * (1 - alpha), top.B * alpha + under.B * (1 - alpha));

    private static (double R, double G, double B) Rgb(SKColor c) => (c.Red / 255.0, c.Green / 255.0, c.Blue / 255.0);

    [Fact]
    [Trait("Decision", "OD-0150")] // ED-NEW-lightv2-5
    public void The_composers_text_keeps_four_and_a_half_to_one_over_the_inner_light_at_its_strongest()
    {
        // the strongest light: intensity 2, spread 220, Working, a keystroke's full lift at the caret; the glass under it the
        // dark window (#1E1E20) and the light one (white); the text is labelColor (85 % white, 85 % black) over the lit glass
        var custom = new ShaderLightColours("#FFFFFF", "#FFFF00", "#00FFFF", "#FF00FF");
        var interior = new SKRect(16, 13, (float)W - 16, (float)H - 13); // more than 12 px inside the rim: the text zone
        var worst = double.MaxValue; var where = "";
        foreach (var dark in new[] { true, false })
        {
            var glass = dark ? new SKColor(0x1E, 0x1E, 0x20) : SKColors.White;
            var text = dark ? (1.0, 1.0, 1.0) : (0.0, 0.0, 0.0);
            foreach (var def in ShaderLightCatalog.All)
                foreach (var pack in ShaderLightCatalog.Packs)
                {
                    var settings = ComposerLightSettings.Default;
                    var light = new ShaderLightAnimator(settings with { Style = ComposerLightStyle.Shader, ShaderId = def.Id, Shaders = settings.Shaders.With(def.Id, def.Defaults with { Pack = pack.Id, Spread = 220, Intensity = 2, Custom = custom }) },
                        ComposerLightPhase.Running, dark, reduceMotion: false, ComposerLightPalette.Default);
                    light.Keystroke(W / 2);
                    light.Produce(0, W, H, R);
                    var f = light.Drawn[0];
                    using var b = Render(f, 1, 0, (canvas, _) => canvas.Clear(glass), clip: interior);
                    for (var y = 14.0; y < H - 13; y += 4)
                        for (var x = 18.0; x < W - 16; x += 6)
                        {
                            var bg = Rgb(At(b, f, x, y, 1, 0));
                            var c = Contrast(Over(text, .85, bg), bg);
                            if (c < worst) { worst = c; where = $"{def.Id} on {pack.Id} ({(dark ? "dark" : "light")}) at ({x}, {y}): {At(b, f, x, y, 1, 0)}"; }
                        }
                }
        }
        Assert.True(worst >= 4.5, $"the composer's text has {worst:F2}:1 against {where}");
    }

    // a window of chat text above the composer: dark (#1E1E20) with white strokes 9 px tall and 2 px wide, 6 px apart, on three lines
    private static readonly float[] ChatLines = [-96f, -78f, -60f];

    private static void ChatText(SKCanvas canvas, float offset)
    {
        canvas.Clear(new SKColor(0x1E, 0x1E, 0x20));
        using var ink = new SKPaint { Color = SKColors.White, IsAntialias = false };
        foreach (var top in ChatLines)
            for (var x = 24f; x < W - 24; x += 6) canvas.DrawRect(x, top, 2, 9, ink);
    }

    // the chat text as the app draws it: labelColor strokes (85 % white over the window, #1E1E20; 85 % black over white)
    private static void LabelChatText(SKCanvas canvas, bool dark)
    {
        canvas.Clear(dark ? new SKColor(0x1E, 0x1E, 0x20) : SKColors.White);
        using var ink = new SKPaint { Color = dark ? new SKColor(221, 221, 221) : new SKColor(38, 38, 38), IsAntialias = false };
        foreach (var top in ChatLines)
            for (var x = 24f; x < W - 24; x += 6) canvas.DrawRect(x, top, 2, 9, ink);
    }

    private static bool IsGlyph(int x, int y) =>
        x >= 24 && x - (x - 24) % 6 < W - 24 && (x - 24) % 6 < 2 && ChatLines.Any(top => y >= top && y < top + 9);

    // the chat text block alone (composer px), drawn on a surface of just its size: the window's text, then the light over it
    // (composing with the surface, with Screen from no surface, or from the kept pixels of the CPU fallback)
    private static readonly SKRectI TextBlock = new(8, -112, (int)W - 8, -40);

    private static SKBitmap TextRegion(ShaderLightRenderer renderer, ShaderLightFrame f, bool dark, ShaderLightRoute path)
    {
        var info = new SKImageInfo(TextBlock.Width, TextBlock.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Translate(-TextBlock.Left, -TextBlock.Top);
        LabelChatText(canvas, dark);
        Assert.True(path == ShaderLightRoute.Still ? renderer.DrawStill(canvas, f, 1) : renderer.Draw(canvas, f, 1, 1, path == ShaderLightRoute.Compose ? surface : null));
        var bitmap = new SKBitmap(info);
        Assert.True(surface.ReadPixels(info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0));
        return bitmap;
    }

    // WCAG's relative luminance of every pixel of a text block
    private static double[] Luminances(SKBitmap b)
    {
        static double Lin(double v) => v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4);
        var pixels = b.Pixels;
        var l = new double[pixels.Length];
        for (var i = 0; i < pixels.Length; i++)
            l[i] = .2126 * Lin(pixels[i].Red / 255.0) + .7152 * Lin(pixels[i].Green / 255.0) + .0722 * Lin(pixels[i].Blue / 255.0);
        return l;
    }

    // the least contrast between a glyph's pixel and the window's pixels around it (every one within `reach` px, across and
    // up and down), and the pairs the 4.5:1 test this replaces took (a stroke's right column against the pixel 3 to its
    // right, on each line's fifth row); and where
    private static (double Worst, int X, int Y) WorstGlyph(SKBitmap b, int reach = 2)
    {
        var l = Luminances(b);
        var width = b.Width;
        double L(int x, int y) => l[(y - TextBlock.Top) * width + x - TextBlock.Left];
        static double Ratio(double a, double c) => (Math.Max(a, c) + .05) / (Math.Min(a, c) + .05);
        (double Worst, int X, int Y) worst = (double.MaxValue, 0, 0);
        foreach (var top in ChatLines)
            for (var gy = (int)top; gy < top + 9; gy++)
                for (var gx = 24; gx < W - 22; gx++)
                {
                    if (!IsGlyph(gx, gy)) continue;
                    for (var dy = -reach; dy <= reach; dy++)
                        for (var dx = -reach; dx <= reach; dx++)
                        {
                            int x = gx + dx, y = gy + dy;
                            if (IsGlyph(x, y) || x < TextBlock.Left || x >= TextBlock.Right || y < TextBlock.Top || y >= TextBlock.Bottom) continue;
                            var c = Ratio(L(gx, gy), L(x, y));
                            if (c < worst.Worst) worst = (c, x, y);
                        }
                }
        foreach (var top in ChatLines)
            for (var x = 24; x < W - 24; x += 6)
            {
                var c = Ratio(L(x + 1, (int)top + 4), L(x + 4, (int)top + 4));
                if (c < worst.Worst) worst = (c, x + 4, (int)top + 4);
            }
        return worst;
    }

    [Fact]
    [Trait("Decision", "OD-0150")] // ED-NEW-lightv2-8
    public void The_readability_guard_holds_every_chat_glyph_at_four_and_a_half_to_one()
    {
        // every light, every pack (Custom too), intensity 2 while a turn runs, in all three Text and light modes composing with
        // the window, and with no window to read (with Screen: the CPU fallback's program, whose kept pixels are the same as
        // With_no_GPU_a_still_band_is_shaded_once_and_its_pixels_kept shows); the light appearance too. The text is the app's
        // labelColor (dimmer than the white of the test this replaces), every glyph pixel against every window pixel within 2 px
        var custom = new ShaderLightColours("#FF3355", "#33FF99", "#3366FF", "#FFEE44");
        ComposerLightTextMode[] modes = [ComposerLightTextMode.TextAbove, ComposerLightTextMode.LightAbove, ComposerLightTextMode.Difference];
        var cases = new List<(string Id, string Pack, ComposerLightTextMode Mode, bool Dark, ShaderLightRoute Path)>();
        foreach (var def in ShaderLightCatalog.All)
        {
            ShaderLightShaders.Effect(def.Id); // compiled once before the cases run side by side
            foreach (var pack in ShaderLightCatalog.Packs)
            {
                foreach (var mode in modes) cases.Add((def.Id, pack.Id, mode, true, ShaderLightRoute.Compose));
                cases.Add((def.Id, pack.Id, ComposerLightTextMode.TextAbove, true, ShaderLightRoute.Screen));
            }
            foreach (var mode in modes) cases.Add((def.Id, def.Pack, mode, false, ShaderLightRoute.Compose));
            cases.Add((def.Id, def.Pack, ComposerLightTextMode.TextAbove, false, ShaderLightRoute.Screen));
        }
        cases.Add(("velum", "aurora", ComposerLightTextMode.TextAbove, true, ShaderLightRoute.Still)); // and the kept pixels themselves, once
        var results = new (double Worst, string Where, string Group)[cases.Count];
        Parallel.For(0, cases.Count, i =>
        {
            var (id, pack, mode, dark, path) = cases[i];
            var f = Frame(id, ShaderLightCatalog.Find(id)!.Defaults with { Intensity = 2, Pack = pack, Custom = custom }, ComposerLightPhase.Running, dark,
                ComposerLightSettings.Default with { TextMode = mode });
            using var renderer = new ShaderLightRenderer();
            using var b = TextRegion(renderer, f, dark, path);
            var (worst, x, y) = WorstGlyph(b);
            results[i] = (worst, $"{id}, {pack}, {mode}, {(dark ? "dark" : "light")}, {path}, at ({x}, {y})", $"{mode}, {(dark ? "dark" : "light")}, {path}");
        });
        var least = results.MinBy(r => r.Worst);
        var line = string.Format(CultureInfo.InvariantCulture, "Readability guard, {0} cases at intensity 2: worst {1:F2}:1 ({2})", cases.Count, least.Worst, least.Where);
        var lines = new List<string> { line };
        foreach (var group in results.GroupBy(r => r.Group).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var w = group.MinBy(r => r.Worst);
            lines.Add(string.Format(CultureInfo.InvariantCulture, "  {0}: {1} cases, worst {2:F2}:1 ({3})", group.Key, group.Count(), w.Worst, w.Where));
        }
        foreach (var l in lines) output.WriteLine(l);
        var folder = Environment.GetEnvironmentVariable("MARROW_COMPOSER_LIGHT_EVIDENCE");
        if (!string.IsNullOrEmpty(folder)) File.WriteAllLines(Path.Combine(folder, "shader-light-guard.txt"), lines);
        Assert.True(least.Worst >= 4.5, line);
    }

    [Fact]
    [Trait("Decision", "OD-0150")] // ED-NEW-lightv2-8
    public void The_guard_changes_nothing_where_the_text_already_reads()
    {
        // each light at its defaults while typing (the shipped look), Text above light: where the pages' composition already
        // keeps every glyph at 5:1 or more against every window pixel within 5 px (all the guard reaches), the guard leaves the
        // window's pixels as they were (a glyph's own by at most 3 of 255: a tenth of the light reaches it); away from any text
        // it never acts
        var lines = new List<string>();
        var untouched = 0;
        foreach (var def in ShaderLightCatalog.All)
        {
            var f = Frame(def.Id, def.Defaults, ComposerLightPhase.Typing);
            using var guarded = new ShaderLightRenderer();
            using var bare = new ShaderLightRenderer { Guard = false };
            using var g = TextRegion(guarded, f, true, ShaderLightRoute.Compose);
            using var p = TextRegion(bare, f, true, ShaderLightRoute.Compose);
            var (pageWorst, _, _) = WorstGlyph(p, 5);
            var (worst, _, _) = WorstGlyph(g);
            int window = 0, glyph = 0;
            for (var y = TextBlock.Top; y < TextBlock.Bottom; y++)
                for (var x = TextBlock.Left; x < TextBlock.Right; x++)
                {
                    SKColor a = g.GetPixel(x - TextBlock.Left, y - TextBlock.Top), z = p.GetPixel(x - TextBlock.Left, y - TextBlock.Top);
                    var d = Math.Max(Math.Abs(a.Red - z.Red), Math.Max(Math.Abs(a.Green - z.Green), Math.Abs(a.Blue - z.Blue)));
                    if (IsGlyph(x, y)) glyph = Math.Max(glyph, d); else window = Math.Max(window, d);
                }
            lines.Add(string.Format(CultureInfo.InvariantCulture, "{0}: the pages {1:F2}:1, guarded {2:F2}:1, the window's pixels changed by up to {3}/255, the glyphs' by {4}/255",
                def.Id, pageWorst, worst, window, glyph));
            Assert.True(worst >= 4.5, lines[^1]);
            if (pageWorst >= 5)
            {
                Assert.True(window <= 1 && glyph <= 3, lines[^1]);
                untouched++;
            }
            // below the composer there is no text: the guard does not act there
            var below = new SKRect((float)W / 2 - 4, (float)H + 8, (float)W / 2 + 4, (float)H + 16);
            using var clear = Render(f, 1, 0, clip: below);
            using var clearBare = Render(f, 1, 0, clip: below, guard: false);
            Assert.Equal(At(clearBare, f, W / 2, H + 12, 1, 0), At(clear, f, W / 2, H + 12, 1, 0));
        }
        lines.Add($"{untouched} of {ShaderLightCatalog.All.Count} lights read at 5:1 at their defaults without the guard (left as they were)");
        var folder = Environment.GetEnvironmentVariable("MARROW_COMPOSER_LIGHT_EVIDENCE");
        if (!string.IsNullOrEmpty(folder)) File.WriteAllLines(Path.Combine(folder, "shader-light-guard-defaults.txt"), lines);
        foreach (var l in lines) output.WriteLine(l);
    }

    // the chat text's strokes blurred by a Gaussian of `sigma` at (px, py): how CSS draws a text shadow's coverage there
    private static double ShadowCover(double px, double py, double sigma)
    {
        double cover = 0;
        foreach (var top in ChatLines)
            for (var x = 24f; x < W - 24; x += 6)
                cover += (Phi((x + 2 - px) / sigma) - Phi((x - px) / sigma)) * (Phi((top + 9 - py) / sigma) - Phi((top - py) / sigma));
        return cover;
    }

    // the normal distribution's CDF (Abramowitz and Stegun 7.1.26: erf within 1.5e-7)
    private static double Phi(double z)
    {
        var x = Math.Abs(z) / Math.Sqrt(2);
        var t = 1 / (1 + .3275911 * x);
        var erf = 1 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - .284496736) * t + .254829592) * t * Math.Exp(-x * x);
        return .5 * (1 + (z < 0 ? -erf : erf));
    }

    private static ShaderLightFrame Mode(string id, ComposerLightTextMode mode, double halo = ComposerLightSettings.DefaultTextHalo) =>
        Frame(id, ShaderLightCatalog.Find(id)!.Defaults with { Intensity = 2 }, ComposerLightPhase.Running,
            settings: ComposerLightSettings.Default with { TextMode = mode, TextHalo = halo });

    [Fact]
    [Trait("Decision", "OD-0150")] // ED-NEW-lightv2-5
    public void Each_text_mode_composes_with_the_chat_text_as_the_page_does()
    {
        foreach (var id in new[] { "lumen", "velum", "fulgor" })
        {
            var above = Mode(id, ComposerLightTextMode.TextAbove);
            var over = Mode(id, ComposerLightTextMode.LightAbove);
            var diff = Mode(id, ComposerLightTextMode.Difference);
            // the pages' composition itself: without the readability guard (ED-NEW-lightv2-8, tested on its own below)
            using var a = Render(above, 1, 0, ChatText, compose: true, guard: false);
            using var l = Render(over, 1, 0, ChatText, compose: true, guard: false);
            using var d = Render(diff, 1, 0, ChatText, compose: true, guard: false);
            using var plain = Render(above, 1, 0, ChatText, compose: false, guard: false); // no surface: Screen, the text is not read
            double glyphA = 0, glyphD = 0, gapA = 0, gapL = 0; var n = 0;
            for (var x = 24.0; x < W - 24; x += 6)
            {
                glyphA += At(a, above, x + 1, -56, 1, 0).Red; glyphD += At(d, diff, x + 1, -56, 1, 0).Red;
                gapA += At(a, above, x + 4, -56, 1, 0).Red; gapL += At(l, over, x + 4, -56, 1, 0).Red;
                n++;
            }
            (glyphA, glyphD, gapA, gapL) = (glyphA / n, glyphD / n, gapA / n, gapL / n);
            Assert.True(glyphA > 240, $"{id}: text above light keeps the glyphs white ({glyphA:F0})");
            Assert.True(glyphD < glyphA - 20, $"{id}: difference inverts the glyphs against the light ({glyphD:F0} vs {glyphA:F0})");
            Assert.True(gapL > gapA, $"{id}: the halo holds the light back beside the glyphs only with text above light ({gapA:F0} vs {gapL:F0})");
            // away from any text the three modes draw the same light
            var clear = (x: W / 2, y: H + 12.0);
            Assert.InRange(At(a, above, clear.x, clear.y, 1, 0).Red - At(l, over, clear.x, clear.y, 1, 0).Red, -2, 2);
            Assert.InRange(At(d, diff, clear.x, clear.y, 1, 0).Red - At(l, over, clear.x, clear.y, 1, 0).Red, -2, 2);
            Assert.InRange(At(plain, above, clear.x, clear.y, 1, 0).Red - At(l, over, clear.x, clear.y, 1, 0).Red, -3, 3);
        }
    }

    [Fact]
    [Trait("Decision", "OD-0150")] // ED-NEW-lightv2-5
    public void Text_above_light_lifts_the_chat_text_as_the_pages_halo_does()
    {
        // under every light at intensity 2 while a turn runs, Text above light at the defaults: a chat glyph against the window
        // beside it, and the same glyph under the v4 page's own halo over the same light (drawn with no halo): updateHalo()'s
        // estimate for its line, then the three text shadows (3, 10 and 22 px at .95, .7 and .5 of it, in rgba(8, 8, 10); CSS
        // blurs a shadow by a Gaussian of half its blur radius), worked out here from the strokes' rectangles
        double worst = double.MaxValue, worstBare = double.MaxValue, worstPage = double.MaxValue, least = double.MaxValue;
        string where = "", leastWhere = "";
        var ink = (8 / 255.0, 8 / 255.0, 10 / 255.0);
        foreach (var def in ShaderLightCatalog.All)
        {
            var f = Mode(def.Id, ComposerLightTextMode.TextAbove);
            var bare = Mode(def.Id, ComposerLightTextMode.TextAbove, halo: 0);
            using var b = Render(f, 1, 0, ChatText, compose: true, guard: false); // the halo itself, without the guard over it
            using var nb = Render(bare, 1, 0, ChatText, compose: true, guard: false);
            var g = f.Intensity * (.38 + .82 * f.Energy) * (1 + f.Key * .3);
            foreach (var top in ChatLines)
            {
                double y = top + 4, centre = top + 4.5;
                var ha = f.Halo * Math.Min(1, g * 1.25 * Math.Exp(centre / (f.Spread * .6)) * (1 - f.Behind * .85) + .15 * g);
                for (var x = 24.0; x < W - 24; x += 6)
                {
                    var glyph = Rgb(At(b, f, x + 1, y, 1, 0));
                    var ours = Contrast(glyph, Rgb(At(b, f, x + 4, y, 1, 0)));
                    var lit = Rgb(At(nb, bare, x + 4, y, 1, 0));
                    double px = x + 4.5, py = y + .5;
                    var keep = (1 - .95 * ha * ShadowCover(px, py, 1.5)) * (1 - .7 * ha * ShadowCover(px, py, 5)) * (1 - .5 * ha * ShadowCover(px, py, 11));
                    var page = Contrast(glyph, Over(ink, 1 - keep, lit));
                    worstBare = Math.Min(worstBare, Contrast(Rgb(At(nb, bare, x + 1, y, 1, 0)), lit));
                    worstPage = Math.Min(worstPage, page);
                    if (ours < worst) { worst = ours; where = $"{def.Id} at ({x + 4}, {y})"; }
                    if (ours / page < least) { least = ours / page; leastWhere = $"{def.Id} at ({x + 4}, {y}): {ours:F2}:1, the page's {page:F2}:1"; }
                }
            }
        }
        var line = $"Text above light at intensity 2: worst {worst:F2}:1 ({where}); the page's halo: worst {worstPage:F2}:1; no halo: worst {worstBare:F2}:1; "
            + $"least of ours to the page's {least:F3} ({leastWhere})";
        output.WriteLine(line);
        var folder = Environment.GetEnvironmentVariable("MARROW_COMPOSER_LIGHT_EVIDENCE");
        if (!string.IsNullOrEmpty(folder)) File.WriteAllText(Path.Combine(folder, "shader-light-text.txt"), line + Environment.NewLine);
        Assert.True(worst > worstBare, $"the halo does not lift the contrast ({worst:F2} vs {worstBare:F2} without it)");
        Assert.True(least >= 1 - HaloTolerance, $"the halo is weaker than the page's: {leastWhere}");
    }

    /// <summary>
    /// How far below the page's contrast the halo may fall at a glyph (ED-NEW-lightv2-5): it reads the chat text from 16 taps
    /// 2.5 and 8 px out, where the page blurs the glyphs themselves.
    /// </summary>
    internal const double HaloTolerance = .08;

    [Fact]
    [Trait("Decision", "OD-0150")] // ED-NEW-lightv2-6
    public void The_veil_measures_a_bright_busy_wallpaper_as_wanting_more_dimming_than_a_dark_calm_one()
    {
        using var veil = new WallpaperDimVeil();
        var info = new SKImageInfo(480, 60, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bright = SKSurface.Create(info);
        var random = new Random(7);
        using (var paint = new SKPaint())
            for (var y = 0; y < 60; y += 4)
                for (var x = 0; x < 480; x += 4)
                {
                    paint.Color = new SKColor((byte)random.Next(120, 256), (byte)random.Next(120, 256), (byte)random.Next(60, 256));
                    bright.Canvas.DrawRect(x, y, 4, 4, paint);
                }
        using var dark = SKSurface.Create(info);
        dark.Canvas.Clear(new SKColor(0x14, 0x16, 0x1A));
        using var brightImage = bright.Snapshot();
        using var darkImage = dark.Snapshot();
        var hi = veil.Measure(brightImage, null);
        var lo = veil.Measure(darkImage, null);
        Assert.True(hi.Luminance > lo.Luminance && hi.Busyness > lo.Busyness, $"{hi} vs {lo}");
        var more = WallpaperDim.Auto(hi.Luminance, hi.Busyness);
        var less = WallpaperDim.Auto(lo.Luminance, lo.Busyness);
        Assert.True(more > less + .25, $"Auto {more:F2} on the bright wallpaper, {less:F2} on the dark one");
        Assert.InRange(less, 0, .15);
    }

    [Fact]
    [Trait("Decision", "OD-0150")] // ED-NEW-lightv2-6
    public void The_veil_darkens_the_wallpaper_near_the_composer_and_fades_out_with_no_edge()
    {
        using var veil = new WallpaperDimVeil();
        var info = new SKImageInfo(900, 500, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(new SKColor(200, 200, 200));
        surface.Canvas.Translate(250, 230);
        veil.Draw(surface.Canvas, W, H, R, 120, .5);
        using var bitmap = new SKBitmap(info);
        surface.ReadPixels(info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0);
        Assert.True(bitmap.GetPixel(450, 253).Red < 135); // under the composer: near half (the blur's .82 at its middle)
        Assert.Equal(200, bitmap.GetPixel(450, 20).Red); // far above: untouched
        // straight up from the composer the veil only ever lightens toward the wallpaper, a step at a time (no edge)
        var last = (int)bitmap.GetPixel(450, 230).Red;
        for (var y = 229; y >= 20; y--)
        {
            int v = bitmap.GetPixel(450, y).Red;
            Assert.True(v >= last && v - last <= 3, $"a step of {v - last} at y {y}");
            last = v;
        }
    }

    [Fact]
    [Trait("Decision", "OD-0150")] // ED-NEW-lightv2-1
    public void Per_frame_cost_of_each_light_on_the_software_raster()
    {
        // the GPU's time cannot be measured headless; Skia's software raster of the whole band is its upper bound here
        var lines = new List<string>();
        using var renderer = new ShaderLightRenderer();
        foreach (var def in ShaderLightCatalog.All)
        {
            var f = Frame(def.Id);
            var reach = (float)f.Reach;
            var info = new SKImageInfo((int)(W + 2 * reach), (int)(H + 2 * reach), SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            surface.Canvas.Translate(reach, reach);
            renderer.Draw(surface.Canvas, f, 1);
            var watch = Stopwatch.StartNew();
            const int n = 3;
            for (var i = 0; i < n; i++) { renderer.Draw(surface.Canvas, f, 1); surface.Flush(); }
            lines.Add(string.Format(CultureInfo.InvariantCulture, "{0}: band {1} x {2} px, software raster {3:F1} ms/frame",
                def.Id, info.Width, info.Height, watch.Elapsed.TotalMilliseconds / n));
        }
        var folder = Environment.GetEnvironmentVariable("MARROW_COMPOSER_LIGHT_EVIDENCE");
        if (!string.IsNullOrEmpty(folder)) File.WriteAllLines(Path.Combine(folder, "shader-light-cost.txt"), lines);
        foreach (var l in lines) output.WriteLine(l);
    }
}
