// REVIEW (unit/glass-g1 at 36abb91a0): a glass piece that fills its window (Chrome's window glass layer, Chrome.cs:417; the floating panels) must keep
// its outer ring of pixels opaque over an opaque backdrop. The blur surface is cleared to nothing (GlassMaterial.Draw, blurredCanvas.Clear) and the blur
// reaches only 3 sigma (36 pt) past the window, while the edge pixel's displaced read goes up to 50 pt outward (bend 40 + ring 10): that read lands
// on transparent, so the edge pixels come out translucent (alpha about 90 of 255). The veil clear this unit replaced made them opaque.
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Port;

[Collection("Headless")]
public sealed class REVIEW_GlassWindowEdgeTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(256, 160, 16)]
    [InlineData(520, 320, 26)]
    [Trait("Rule", "R-VIEW-11")]
    [Trait("Decision", "ED-NEW-glass-g1")]
    [Trait("Req", "PR-WIN-180")]
    public void REVIEW_a_window_filling_piece_keeps_its_outer_ring_opaque_over_an_opaque_backdrop(int w, int h, int radius) => headless.OnUiThread(() =>
    {
        var piece = new Border { Background = Brushes.Transparent, CornerRadius = new CornerRadius(radius), Width = w, Height = h };
        SwiftGlass.SetEffect(piece, GlassEffect.Regular(GlassShape.RoundedRectangle(radius, RoundedCornerStyle.Continuous)));
        var window = new Window
        {
            Width = w, Height = h, RequestedThemeVariant = ThemeVariant.Dark, Background = Brushes.Black,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            Content = new Panel { Background = new SolidColorBrush(Color.FromRgb(120, 130, 140)), Children = { piece } },
        };
        window.Show();
        try
        {
            for (var i = 0; i < 4; i++) GlassScene.Drain();
            using var bitmap = window.CaptureRenderedFrame()!;
            var size = bitmap.PixelSize;
            var bytes = new byte[size.Width * size.Height * 4];
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try { bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), bytes.Length, size.Width * 4); }
            finally { handle.Free(); }
            byte A(int x, int y) => bytes[(y * size.Width + x) * 4 + 3];
            var low = new List<string>();
            for (var t = radius + 4; t < w - radius - 4; t++) foreach (var (x, y) in new[] { (t, 0), (t, h - 1) }) if (A(x, y) < 250) low.Add($"({x},{y}):{A(x, y)}");
            for (var t = radius + 4; t < h - radius - 4; t++) foreach (var (x, y) in new[] { (0, t), (w - 1, t) }) if (A(x, y) < 250) low.Add($"({x},{y}):{A(x, y)}");
            Assert.True(low.Count == 0, $"{low.Count} pixels of the outer ring are translucent over an opaque backdrop, e.g. {string.Join(" ", low.Take(5))}");
        }
        finally { window.Close(); }
    });
}
