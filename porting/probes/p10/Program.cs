// P10, the regression harness for the [Unstable] lease API (task packet T-M0-009).
//
// QUESTION: can the glass subsystem's rendering path (ISkiaSharpApiLeaseFeature -> app-owned SKSurface
// -> SKRuntimeEffect with the backdrop bound as a shader child -> clipped composite) be captured as a
// deterministic, byte-comparable reference, so an Avalonia patch bump can be gated on it?
//
// The scene, the SKSL and the draw order are P1's (porting/probes/p1-glass/Program.cs), copied
// unchanged apart from three hooks: a perturbation mode, per-render flag reset, and the GrContext
// report. The harness adds rendering to an --out folder, a manifest of hashes, and a compare mode.
//
// Usage:
//   p10 render  --out <dir> [--perturb none|shader|nolease]
//   p10 compare <dirA> <dirB> [--diff-out <dir>]
//
// render exits 0 only if every glass render obtained the lease, created the app-owned surface and
// compiled the effect, and every scene rendered twice in-process gave identical bytes.
// compare exits 0 only if every PNG is byte-identical AND every decoded pixel buffer is identical.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

// ---- SOURCE FACTS (pinned Droppy Code dd2b86ef), as recorded by P1 -------------------
// window 1320x860              WindowManager.swift:19
// windowCornerRadius 26        Chrome.swift:8
// cardCornerRadius 16          Chrome.swift:58
// glassTint #1E1E20 @ 0.30     AppTheme.swift:116 surface, Chrome.swift:178/:180
// -------------------------------------------------------------------------------------
static class Src
{
    public const int W = 1320, H = 860;
    public const float WindowCorner = 26f;
    public const float CardCorner = 16f;
    public static readonly SKColor Tint = new SKColor(0x1E, 0x1E, 0x20);
    public const float TintAlpha = 0.30f;
}

static class Probe
{
    public static bool LeaseObtained, SurfaceCreated, EffectCompiled, GrContextNull;
    public static float Strength = 26f, Rim = 0.55f;
    public static string EffectError = "";
    public static string Sksl = Shaders.Sksl;
    public static bool SkipLease;               // control: simulates the lease feature being absent

    public static void Reset()
    {
        LeaseObtained = SurfaceCreated = EffectCompiled = GrContextNull = false;
        EffectError = "";
    }
}

// P1's SKSL, verbatim.
static class Shaders
{
    public const string Sksl = @"
uniform shader backdrop;
uniform float2 size;
uniform float radius;
uniform float band;
uniform float strength;
uniform float4 tint;
uniform float rimGain;

// Superellipse SDF: this is what reproduces SwiftUI's .continuous corner exactly,
// as opposed to a circular arc.
float sdSquircle(float2 p, float2 b, float r) {
    float2 q = abs(p) - b + r;
    float n = 4.0;                      // continuous-corner exponent
    float2 qc = max(q, 0.0);
    float outside = pow(pow(qc.x, n) + pow(qc.y, n), 1.0 / n);
    return outside + min(max(q.x, q.y), 0.0) - r;
}

half4 main(float2 xy) {
    float2 c = size * 0.5;
    float d = -sdSquircle(xy - c, c, radius);      // px inside the border
    float t = clamp(1.0 - d / band, 0.0, 1.0);
    float lens = pow(t, 2.2);

    // inward normal by finite difference of the field
    float e = 1.0;
    float dx = (-sdSquircle(xy + float2(e,0) - c, c, radius)) - (-sdSquircle(xy - float2(e,0) - c, c, radius));
    float dy = (-sdSquircle(xy + float2(0,e) - c, c, radius)) - (-sdSquircle(xy - float2(0,e) - c, c, radius));
    float2 n2 = normalize(float2(dx, dy) + float2(1e-6));

    float disp = lens * strength;
    half4 r = backdrop.eval(xy - n2 * disp * 1.10);
    half4 g = backdrop.eval(xy - n2 * disp * 1.00);
    half4 b = backdrop.eval(xy - n2 * disp * 0.90);
    half4 col = half4(r.r, g.g, b.b, 1.0);

    col.rgb = mix(col.rgb, half3(tint.rgb), half(tint.a));

    float rim = exp(-pow(d - 1.5, 2.0) / 6.0);
    float light = pow(clamp(-n2.x * 0.7 - n2.y * 0.7, 0.0, 1.0), 1.5);
    col.rgb += half3(half(rim * light * rimGain));

    return col;
}";

    // The perturbation control: one shader constant, the red-channel chromatic offset 1.10 -> 1.11.
    public const string PerturbFrom = "disp * 1.10";
    public const string PerturbTo = "disp * 1.11";
}

// P1's scene, unchanged except that it reads the SKSL and the lease switch from Probe.
class GlassScene : Control
{
    public bool GlassEnabled { get; init; } = true;

    public override void Render(DrawingContext ctx)
    {
        ctx.Custom(new Op(new Rect(0, 0, Src.W, Src.H), GlassEnabled));
    }

    sealed class Op : ICustomDrawOperation
    {
        readonly bool _glass;
        public Op(Rect b, bool glass) { Bounds = b; _glass = glass; }
        public Rect Bounds { get; }
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            var feature = Probe.SkipLease ? null : context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature is null) return;
            Probe.LeaseObtained = true;

            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            Probe.GrContextNull = lease.GrContext is null;

            // ---- pass 0: the LIVE content the glass will sit over --------------------
            DrawBackdropContent(canvas);
            if (!_glass) return;

            var panel = new SKRect(160, 210, Src.W - 160, Src.H - 210);

            // ---- pass 1: render that same content into an APP-OWNED offscreen surface -
            var info = new SKImageInfo((int)panel.Width, (int)panel.Height,
                                       SKColorType.Rgba8888, SKAlphaType.Premul);
            using var offscreen = lease.GrContext is not null
                ? SKSurface.Create(lease.GrContext, false, info)
                : SKSurface.Create(info);
            if (offscreen is null) return;
            Probe.SurfaceCreated = true;

            var oc = offscreen.Canvas;
            oc.Clear(SKColors.Transparent);
            oc.Save();
            oc.Translate(-panel.Left, -panel.Top);
            DrawBackdropContent(oc);          // the identical live scene, re-rendered
            oc.Restore();
            using var backdropImage = offscreen.Snapshot();

            // ---- pass 2: the SKSL effect over that surface ---------------------------
            using var effect = SKRuntimeEffect.CreateShader(Probe.Sksl, out var err);
            if (effect is null) { Probe.EffectError = err ?? "unknown"; return; }
            Probe.EffectCompiled = true;

            var uniforms = new SKRuntimeEffectUniforms(effect)
            {
                ["size"] = new[] { panel.Width, panel.Height },
                ["radius"] = Src.WindowCorner,
                ["band"] = 46f,
                ["strength"] = Probe.Strength,
                ["rimGain"] = Probe.Rim,
                ["tint"] = new[] { Src.Tint.Red / 255f, Src.Tint.Green / 255f,
                                   Src.Tint.Blue / 255f, Src.TintAlpha },
            };
            using var backdropShader = backdropImage.ToShader(
                SKShaderTileMode.Clamp, SKShaderTileMode.Clamp);
            var children = new SKRuntimeEffectChildren(effect) { ["backdrop"] = backdropShader };
            using var shader = effect.ToShader(uniforms, children);

            using var paint = new SKPaint { Shader = shader, IsAntialias = true };
            canvas.Save();
            canvas.Translate(panel.Left, panel.Top);
            using (var clip = new SKPath())
            {
                clip.AddRoundRect(new SKRect(0, 0, panel.Width, panel.Height),
                                  Src.WindowCorner, Src.WindowCorner);
                canvas.ClipPath(clip, SKClipOperation.Intersect, true);
                canvas.DrawRect(new SKRect(0, 0, panel.Width, panel.Height), paint);
            }
            canvas.Restore();
        }

        static void DrawBackdropContent(SKCanvas c)
        {
            c.Clear(new SKColor(0x0B, 0x0B, 0x0D));

            using (var p = new SKPaint { IsAntialias = true })
            {
                p.Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, 0), new SKPoint(Src.W, Src.H),
                    new[] { new SKColor(0x0A, 0x2A, 0x4A), new SKColor(0x00, 0x8C, 0xC8),
                            new SKColor(0xE8, 0xF4, 0xFF) },
                    new[] { 0f, 0.55f, 1f }, SKShaderTileMode.Clamp);
                c.DrawRect(new SKRect(0, Src.H * 0.42f, Src.W, Src.H), p);
            }

            using (var p = new SKPaint { Color = SKColors.White, IsAntialias = true })
            {
                for (int i = 0; i < 7; i++)
                    c.DrawRect(new SKRect(0, 120 + i * 46, Src.W, 120 + i * 46 + 6), p);
            }

            using (var font = new SKFont(SKTypeface.Default, 22))
            using (var p = new SKPaint { Color = new SKColor(0xEC, 0xEC, 0xF0), IsAntialias = true })
            {
                for (int i = 0; i < 16; i++)
                    c.DrawText("Worked for 3m 0s   git commit -c core.quotepath=false   +51 -37",
                               70, 470 + i * 30, font, p);
            }
        }
    }
}

static class Harness
{
    static readonly string[] Scenes = { "p10_no_glass", "p10_tint_only", "p10_glass" };

    static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "render") return RenderAll(args);
        if (args.Length >= 3 && args[0] == "compare") return Compare(args);
        Console.Error.WriteLine("usage: p10 render --out <dir> [--perturb none|shader|nolease]");
        Console.Error.WriteLine("       p10 compare <dirA> <dirB> [--diff-out <dir>]");
        return 64;
    }

    static string? Opt(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    static string Ver(Type t) =>
        t.Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? t.Assembly.GetName().Version?.ToString() ?? "?";

    static string Hex(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    // Lossless decode to a fixed pixel format, so pixel comparison does not depend on the PNG encoder.
    static (int w, int h, byte[] px) Decode(string path)
    {
        using var codec = SKCodec.Create(path) ?? throw new InvalidOperationException("cannot decode " + path);
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bmp = new SKBitmap(info);
        var r = codec.GetPixels(info, bmp.GetPixels());
        if (r != SKCodecResult.Success) throw new InvalidOperationException($"decode {path}: {r}");
        return (info.Width, info.Height, bmp.GetPixelSpan().ToArray());
    }

    static int RenderAll(string[] args)
    {
        var outDir = Opt(args, "--out") ?? throw new ArgumentException("--out required");
        var perturb = Opt(args, "--perturb") ?? "none";
        Directory.CreateDirectory(outDir);

        switch (perturb)
        {
            case "none": break;
            case "shader":
                Probe.Sksl = Shaders.Sksl.Replace(Shaders.PerturbFrom, Shaders.PerturbTo, StringComparison.Ordinal);
                if (Probe.Sksl == Shaders.Sksl) { Console.Error.WriteLine("perturbation did not apply"); return 3; }
                break;
            case "nolease": Probe.SkipLease = true; break;
            default: Console.Error.WriteLine("unknown --perturb " + perturb); return 64;
        }

        AppBuilder.Configure<Application>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        var lines = new List<string>
        {
            $"avalonia        {Ver(typeof(AvaloniaObject))}",
            $"avalonia.skia   {Ver(typeof(ISkiaSharpApiLeaseFeature))}",
            $"avalonia.headl. {Ver(typeof(AvaloniaHeadlessPlatformOptions))}",
            $"skiasharp       {Ver(typeof(SKCanvas))}  native {SkiaSharpVersion.Native}",
            $"perturb         {perturb}",
        };

        byte[] RenderOnce(bool glass, float strength, float rim, out string flags)
        {
            Probe.Reset();
            Probe.Strength = strength; Probe.Rim = rim;
            var c = new GlassScene { GlassEnabled = glass, Width = Src.W, Height = Src.H };
            c.Measure(new Size(Src.W, Src.H));
            c.Arrange(new Rect(0, 0, Src.W, Src.H));
            using var rtb = new RenderTargetBitmap(new PixelSize(Src.W, Src.H), new Vector(96, 96));
            rtb.Render(c);
            using var ms = new MemoryStream();
            rtb.Save(ms, PngBitmapEncoderOptions.Default);   // P1 used Save(Stream), obsolete in 12.1.2 (CS0618 is an error here)
            flags = $"lease={Probe.LeaseObtained} surface={Probe.SurfaceCreated} effect={Probe.EffectCompiled} grContextNull={Probe.GrContextNull}{(Probe.EffectError.Length > 0 ? " err=" + Probe.EffectError : "")}";
            return ms.ToArray();
        }

        var ok = true;
        var specs = new (string name, bool glass, float strength, float rim)[]
        {
            (Scenes[0], false, 26f, 0.55f),
            (Scenes[1], true, 0f, 0f),       // P1's tint-only control
            (Scenes[2], true, 26f, 0.55f),
        };
        foreach (var (name, glass, strength, rim) in specs)
        {
            var a = RenderOnce(glass, strength, rim, out var flags);
            var b = RenderOnce(glass, strength, rim, out _);
            var repeatSame = a.AsSpan().SequenceEqual(b);
            var path = Path.Combine(outDir, name + ".png");
            File.WriteAllBytes(path, a);
            var (_, _, px) = Decode(path);
            var mechOk = Probe.LeaseObtained && (!glass || (Probe.SurfaceCreated && Probe.EffectCompiled));
            ok &= repeatSame && mechOk;
            lines.Add($"{name,-14} png={Hex(a)} pixels={Hex(px)} bytes={a.Length} repeatIdentical={repeatSame} {flags}");
        }
        lines.Add(ok ? "RENDER: OK" : "RENDER: FAIL (mechanism flag false or in-process repeat differed)");
        File.WriteAllLines(Path.Combine(outDir, "manifest.txt"), lines);
        foreach (var l in lines) Console.WriteLine(l);
        return ok ? 0 : 2;
    }

    static int Compare(string[] args)
    {
        string a = args[1], b = args[2];
        var diffOut = Opt(args, "--diff-out");
        if (diffOut is not null) Directory.CreateDirectory(diffOut);
        var allSame = true;
        foreach (var name in Scenes)
        {
            var pa = Path.Combine(a, name + ".png");
            var pb = Path.Combine(b, name + ".png");
            var ba = File.ReadAllBytes(pa);
            var bb = File.ReadAllBytes(pb);
            var pngSame = ba.AsSpan().SequenceEqual(bb);
            var (wa, ha, xa) = Decode(pa);
            var (wb, hb, xb) = Decode(pb);
            if (wa != wb || ha != hb)
            {
                Console.WriteLine($"{name,-14} SIZE DIFFERS {wa}x{ha} vs {wb}x{hb}");
                allSame = false; continue;
            }
            long diffPx = 0; int maxDelta = 0; long sumDelta = 0;
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            byte[]? map = diffOut is null ? null : new byte[wa * ha * 4];
            for (int i = 0, p = 0; i < xa.Length; i += 4, p++)
            {
                int d = 0;
                for (int k = 0; k < 4; k++) d = Math.Max(d, Math.Abs(xa[i + k] - xb[i + k]));
                if (map is not null)
                {
                    byte v = (byte)Math.Min(255, d * 16);
                    map[i] = v; map[i + 1] = v; map[i + 2] = v; map[i + 3] = 255;
                }
                if (d == 0) continue;
                diffPx++; sumDelta += d; maxDelta = Math.Max(maxDelta, d);
                int x = p % wa, y = p / wa;
                x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
            }
            var pxSame = diffPx == 0;
            allSame &= pngSame && pxSame;
            var bbox = diffPx == 0 ? "-" : $"[{x0},{y0}]-[{x1},{y1}]";
            Console.WriteLine($"{name,-14} pngBytesIdentical={pngSame} pixelsIdentical={pxSame} diffPixels={diffPx}/{(long)wa * ha} maxChannelDelta={maxDelta} meanDeltaOfDiffering={(diffPx == 0 ? 0 : (double)sumDelta / diffPx):F2} bbox={bbox}");
            if (map is not null && diffPx > 0)
            {
                var info = new SKImageInfo(wa, ha, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                using var bmp = new SKBitmap(info);
                System.Runtime.InteropServices.Marshal.Copy(map, 0, bmp.GetPixels(), map.Length);
                using var img = SKImage.FromBitmap(bmp);
                using var data = img.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(Path.Combine(diffOut!, "diff_" + name + ".png"), data.ToArray());
            }
        }
        Console.WriteLine(allSame ? "COMPARE: IDENTICAL" : "COMPARE: DIFFERENT");
        return allSame ? 0 : 1;
    }
}
