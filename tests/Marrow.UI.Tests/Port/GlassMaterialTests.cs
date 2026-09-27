// Source: DroppyCode/UI/Chrome/Chrome.swift (B3 52bc792), :153-184.
// OD-0005: the app-drawn material must frost its own backdrop. Pixel thresholds below are
// engineering acceptance criteria for that requirement, not measurements of macOS (OD-0001).
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Port;

[Collection("Headless")]
public sealed class GlassMaterialTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-WIN-180")] // UI/Chrome/Chrome.swift:179-182 .regular frosts the in-window backdrop (OD-0005)
    public void FineTextBehindGlassLosesContrast() => headless.OnUiThread(() =>
    {
        var backdrop = new Pattern();
        var glass = Surface(GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous));
        using var scene = new Scene(backdrop, glass);
        var pixels = scene.Pixels();
        var contrast = Enumerable.Range(90, 60).Select(x => pixels(x, 80).R).ToArray();
        Assert.True(contrast.Max() - contrast.Min() < 64, "Fine black/white backdrop strokes must lose at least 75% of their contrast.");
    });

    [Fact]
    [Trait("Req", "PR-WIN-180")] // UI/Chrome/Chrome.swift:179-182 .regular (OD-0005 in-window frost)
    public void BlurSpreadsAnIsolatedBrightStroke() => headless.OnUiThread(() =>
    {
        var backdrop = new Pattern { IsolatedStroke = true };
        using var scene = new Scene(backdrop, Surface(GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous)));
        var pixels = scene.Pixels();
        Assert.True(pixels(124, 80).R > pixels(96, 80).R + 2, "The bright stroke must spread into neighbouring backdrop pixels.");
        Assert.True(pixels(128, 80).R < 240, "The stroke must be softened rather than passed through.");
    });

    [Fact]
    [Trait("Req", "PR-WIN-180")] // UI/Chrome/Chrome.swift:180 tint(color.opacity(0.3)); :166-167 Circle()
    public void TintIsAppliedAndCircleIsClipped() => headless.OnUiThread(() =>
    {
        var backdrop = new Pattern { Flat = true };
        var glass = Surface(GlassShape.Circle);
        SwiftGlass.SetEffect(glass, GlassEffect.Regular(GlassShape.Circle).Tint(new SwiftColor(1, 0, 0), 0.3));
        using var scene = new Scene(backdrop, glass);
        var pixels = scene.Pixels();
        Assert.True(pixels(128, 80).R > pixels(128, 80).G + 50, "The exact source tint opacity must affect the material.");
        Assert.Equal(pixels(10, 10), pixels(49, 21)); // outside the centred circle, inside its rectangular bounds
    });

    [Fact]
    [Trait("Req", "PR-WIN-180")] // UI/Chrome/Chrome.swift:179-182; OD-0005 LIVE app content
    public void BackdropChangesReachTheMaterial() => headless.OnUiThread(() =>
    {
        var backdrop = new Pattern { Flat = true };
        using var scene = new Scene(backdrop, Surface(GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous)));
        var before = scene.Pixels()(128, 80);
        backdrop.Blue = true;
        backdrop.InvalidateVisual();
        var after = scene.Pixels()(128, 80);
        Assert.True(after.B > before.B + 40, "Changing app content behind glass must update its sampled backdrop.");
        Assert.True(after.R < 240, "The material must continue to frost the changed backdrop.");
    });

    [Fact]
    [Trait("Req", "PR-WIN-180")] // UI/Chrome/Chrome.swift:179-182; F.GlassMaterial performance guard
    public void IdleFramesDoNotRerecordTheWindowBackdrop() => headless.OnUiThread(() =>
    {
        var backdrop = new Pattern();
        using var scene = new Scene(backdrop, Surface(GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous)));
        _ = scene.Pixels();
        var renders = backdrop.RenderCount;
        for (var i = 0; i < 8; i++) Scene.Drain();
        Assert.Equal(renders, backdrop.RenderCount);
    });

    private static Border Surface(GlassShape shape)
    {
        var glass = new Border { Width = 160, Height = 120, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        SwiftGlass.SetEffect(glass, GlassEffect.Regular(shape));
        return glass;
    }

    private sealed class Pattern : Control
    {
        public bool Flat { get; init; }
        public bool IsolatedStroke { get; init; }
        public bool Blue { get; set; }
        public int RenderCount { get; private set; }
        public override void Render(DrawingContext context)
        {
            RenderCount++;
            context.DrawRectangle(Blue ? Brushes.Blue : Flat ? Brushes.Gray : Brushes.Black, null, new Rect(Bounds.Size));
            if (Flat) return;
            if (IsolatedStroke) context.DrawRectangle(Brushes.White, null, new Rect(127, 0, 2, 160));
            else for (var x = 0; x < 256; x += 4) context.DrawRectangle(Brushes.White, null, new Rect(x, 0, 2, 160));
        }
    }

    private sealed class Scene : IDisposable
    {
        private readonly Window window;
        public Scene(Control backdrop, Control glass)
        {
            window = new Window { Width = 256, Height = 160, RequestedThemeVariant = ThemeVariant.Dark,
                Content = new Panel { Children = { backdrop, glass } } };
            window.Show();
        }
        public static void Drain()
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
        public Func<int, int, Color> Pixels()
        {
            for (var i = 0; i < 4; i++) Drain();
            using var bitmap = window.CaptureRenderedFrame()!;
            var size = bitmap.PixelSize;
            var bytes = new byte[size.Width * size.Height * 4];
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try { bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), bytes.Length, size.Width * 4); }
            finally { handle.Free(); }
            var rgba = bitmap.Format == Avalonia.Platform.PixelFormat.Rgba8888;
            return (x, y) =>
            {
                var p = (y * size.Width + x) * 4;
                return Color.FromArgb(bytes[p + 3], bytes[p + (rgba ? 0 : 2)], bytes[p + 1], bytes[p + (rgba ? 2 : 0)]);
            };
        }
        public void Dispose() => window.Close();
    }
}
