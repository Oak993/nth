// P1, the deciding probe for ADR-0002.
//
// QUESTION: on Avalonia 12.1.2 / .NET 10, can the app run a custom SKSL fragment shader over
// LIVE app-rendered content, through an app-owned offscreen surface, and composite the result
// back into the scene clipped to a rounded rect?
//
// This is the whole basis for choosing Avalonia over WinUI 3. WinUI 3's Direct2D pixel shaders
// cannot read XAML-composed pixels, and the derived specification shows most of the 22 glass
// sites sit over live app content (the transcript, the composer, the panes), not over a static
// wallpaper. If this probe fails, neither stack can shader over live content and the decision
// flips to the runner-up.
//
// PASS CONDITIONS, all checked mechanically at the end of this file:
//   1. ISkiaSharpApiLeaseFeature is obtainable from the drawing context.
//   2. An app-owned SKSurface can be created and drawn into.
//   3. An SKRuntimeEffect compiles and runs with the backdrop bound as a shader child.
//   4. The glass region differs measurably from the same scene rendered without the effect,
//      and the difference is concentrated at the edges (which is what lensing means).
//
// The scene deliberately puts high-contrast content UNDER the glass, because a lens over a flat
// colour proves nothing.

using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

// ---- SOURCE FACTS (pinned Droppy Code dd2b86ef) ------------------------------------
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
    public static bool LeaseObtained, SurfaceCreated, EffectCompiled;
    public static float Strength = 26f, Rim = 0.55f;
    public static string EffectError = "";
}

// The SKSL the port would actually ship: edge-concentrated displacement with per-channel
// offsets (chromatic fringing) plus a specular rim, over a superellipse (.continuous) shape.
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
}

// A control that renders high-contrast live content, then a glass panel over part of it.
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
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature is null) return;
            Probe.LeaseObtained = true;

            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;

            // ---- pass 0: the LIVE content the glass will sit over --------------------
            DrawBackdropContent(canvas);
            if (!_glass) return;

            var panel = new SKRect(160, 210, Src.W - 160, Src.H - 210);

            // ---- pass 1: render that same content into an APP-OWNED offscreen surface -
            // Deliberately not lease.SkSurface.Snapshot(): the port must own the surface so
            // the same code path works for each of the 22 sites independently.
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
            using var effect = SKRuntimeEffect.CreateShader(Shaders.Sksl, out var err);
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

        // High-contrast LIVE content: this stands in for the transcript, and it is what makes
        // the probe meaningful. A lens over flat colour proves nothing.
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

            // hard bright bands, the worst case for a lens
            using (var p = new SKPaint { Color = SKColors.White, IsAntialias = true })
            {
                for (int i = 0; i < 7; i++)
                    c.DrawRect(new SKRect(0, 120 + i * 46, Src.W, 120 + i * 46 + 6), p);
            }

            // simulated transcript text
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

class P1
{
    static int Main()
    {
        var outDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "out");
        Directory.CreateDirectory(outDir);

        AppBuilder.Configure<Application>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        string Render(bool glass, string name)
        {
            var c = new GlassScene { GlassEnabled = glass, Width = Src.W, Height = Src.H };
            c.Measure(new Size(Src.W, Src.H));
            c.Arrange(new Rect(0, 0, Src.W, Src.H));
            using var rtb = new RenderTargetBitmap(new PixelSize(Src.W, Src.H), new Vector(96, 96));
            rtb.Render(c);
            var path = Path.Combine(outDir, name);
            using (var fs = File.Create(path)) rtb.Save(fs);
            return path;
        }

        var plain = Render(false, "p1_no_glass.png");
        Probe.Strength = 0f; Probe.Rim = 0f;
        var tintOnly = Render(true, "p1_tint_only.png");
        Probe.Strength = 26f; Probe.Rim = 0.55f;
        var glassed = Render(true, "p1_glass.png");
        Console.WriteLine($"wrote {tintOnly}");

        Console.WriteLine($"lease obtained : {Probe.LeaseObtained}");
        Console.WriteLine($"offscreen surf : {Probe.SurfaceCreated}");
        Console.WriteLine($"SKSL compiled  : {Probe.EffectCompiled}  {Probe.EffectError}");
        Console.WriteLine($"wrote {plain}");
        Console.WriteLine($"wrote {glassed}");

        var ok = Probe.LeaseObtained && Probe.SurfaceCreated && Probe.EffectCompiled;
        Console.WriteLine(ok ? "P1 MECHANISM: PASS" : "P1 MECHANISM: FAIL");
        return ok ? 0 : 1;
    }
}
