using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Marrow.UI.Chrome;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Chrome;

// ED-NEW-glass-g3, part C (OD-0107: rim effects that need no screen capture are checked on the window glass over Acrylic): the main and
// Settings windows' glass over the desktop is the system Acrylic (OD-0005), which draws no edge; the Mac's window glass
// (Chrome.swift:252-254 `shape.fill(.clear).glassEffect(in: shape)`) draws a specular rim and a light inner edge. WindowRim draws them in
// the glass layer, under the scrim. Measured on Mac frame 01 (transparency at its least: no scrim), the edge over the window's body, as alpha
// of white (a step s over a body b is s / (255 - b)): the first point 0.235 at the top, 0.21 at the bottom, 0.21 at the left and 0.158 at the
// right (the frame cuts the window's right edge), the second point 0.042 at the top and 0.023 at the bottom. Headless, the Acrylic is the
// window's own background (a flat 66 grey, the Mac body's level): what the app draws over it is the port's whole edge.
[Collection("Headless")]
public sealed class WindowRimTests(HeadlessSession headless)
{
    private const int W = 480, H = 320;

    private static Func<int, int, double> Render(double opacity, bool withRim = true)
    {
        var backdrop = new WindowBackdrop(opacity);
        var window = new Window
        {
            Width = W, Height = H, RequestedThemeVariant = ThemeVariant.Dark, Background = new SolidColorBrush(Color.FromRgb(66, 66, 66)), Content = backdrop,
        };
        window.Show();
        try
        {
            if (!withRim) foreach (var rim in backdrop.GetVisualDescendants().OfType<WindowRim>()) rim.IsVisible = false;
            using var bitmap = window.CaptureRenderedFrame()!;
            var size = bitmap.PixelSize;
            var bytes = new byte[size.Width * size.Height * 4];
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try { bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), bytes.Length, size.Width * 4); }
            finally { handle.Free(); }
            return (x, y) =>
            {
                var p = (y * size.Width + x) * 4;
                return 0.2126 * bytes[p] + 0.7152 * bytes[p + 1] + 0.0722 * bytes[p + 2];
            };
        }
        finally { window.Close(); }
    }

    // White's alpha over the body at the n-th point inside an edge (0 the first), along the edge's middle third, the body 10 pt in.
    private static double Edge(Func<int, int, double> luma, string side, int n)
    {
        double At(int i) => side switch
        {
            "top" => Enumerable.Range(W / 3, W / 3).Average(x => luma(x, i)),
            "bottom" => Enumerable.Range(W / 3, W / 3).Average(x => luma(x, H - 1 - i)),
            "left" => Enumerable.Range(H / 3, H / 3).Average(y => luma(i, y)),
            _ => Enumerable.Range(H / 3, H / 3).Average(y => luma(W - 1 - i, y)),
        };
        var body = At(10);
        return (At(n) - body) / (255 - body);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-glass-g3")]
    [Trait("Req", "PR-WIN-180")] // Chrome.swift:251-259 the window glass under its scrim; OD-0107, OD-0005
    public void The_window_glass_over_the_acrylic_draws_the_mac_frames_specular_rim_and_light_inner_edge() => headless.OnUiThread(() =>
    {
        var luma = Render(0);
        var failures = new List<string>();
        foreach (var (side, mac, tolerance) in new[] { ("top", 0.235, 0.03), ("bottom", 0.21, 0.03), ("left", 0.21, 0.03), ("right", 0.158, 0.06) })
            if (Math.Abs(Edge(luma, side, 0) - mac) > tolerance) failures.Add($"{side} first point {Edge(luma, side, 0):F3}, Mac {mac:F3}");
        foreach (var (side, mac) in new[] { ("top", 0.042), ("bottom", 0.023), ("left", 0.008) })
            if (Math.Abs(Edge(luma, side, 1) - mac) > 0.012) failures.Add($"{side} second point {Edge(luma, side, 1):F3}, Mac {mac:F3}");
        // Brightest along the top, as on the Mac; nothing past the fourth point.
        if (!(Edge(luma, "top", 0) > Edge(luma, "left", 0) && Edge(luma, "top", 1) > Edge(luma, "bottom", 1))) failures.Add("the top is not the brightest edge");
        if (Math.Abs(Edge(luma, "top", 5)) > 0.004) failures.Add($"the edge reaches 5 pt in ({Edge(luma, "top", 5):F3})");
        Assert.True(failures.Count == 0, string.Join("; ", failures));

        // The instrument, and the port before: the hairline alone (Chrome.overlay(0.14), :266-271) is about half the Mac's first point, and no
        // inner edge.
        var before = Render(0, withRim: false);
        Assert.InRange(Edge(before, "top", 0), 0.09, 0.14);
        Assert.InRange(Edge(before, "top", 1), -0.004, 0.004);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-glass-g3")]
    [Trait("Req", "PR-WIN-180")] // Chrome.swift:249-259 only the glass base carries the glass's edge; :255 the scrim is over it
    public void The_rim_is_the_glass_layers_under_the_scrim_and_only_where_the_window_is_glass() => headless.OnUiThread(() =>
    {
        // Under the scrim (:255 `.overlay`): at the default transparency 0.5 the dark scrim is 0.26, so the rim reads 0.74 of what it reads at 0.
        var clear = Edge(Render(0), "top", 0);
        var dimmed = Edge(Render(0.5), "top", 0);
        Assert.True(dimmed < clear - 0.02, $"the scrim is over the rim: {dimmed:F3} at 0.5 against {clear:F3} at 0");
        // A solid window (opacity 0.98 and up, :220-224) is no glass and has no rim; nor does the drawn material over a wallpaper (its own edge).
        var solid = new WindowBackdrop(1);
        var window = new Window { Width = 200, Height = 120, Content = solid };
        window.Show();
        try { Assert.Empty(solid.GetVisualDescendants().OfType<WindowRim>()); }
        finally { window.Close(); }
        var glass = new WindowBackdrop(0.3);
        window = new Window { Width = 200, Height = 120, Content = glass };
        window.Show();
        try { Assert.Single(glass.GetVisualDescendants().OfType<WindowRim>()); }
        finally { window.Close(); }
    });
}
