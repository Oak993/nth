using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// ED-358, part A: SwiftUI's `shape.fill(f).shadow(color:radius:x:y:)` is the fill's own coverage, blurred, coloured and drawn UNDER
// the fill: its strength is the colour's alpha times the fill's, and through a translucent fill it shows. The head card's scrim
// (HydraPanel.swift:116-120; SubagentPanel.swift:351-354, FloatingSidebarPanel.swift:97-100, UsageFloatingPanel.swift:68-72) is black 0.3
// casting black 1.0 at radius 28, y 10: a shadow of 0.3. Avalonia's BoxShadow (the port's draw before) took the colour's full alpha and
// drew only outside the box. These render SwiftShadow.Caster's draw of the scrim over a white window (headless Skia raster) and read the
// shadow as 1 - level, against the Mac video's own (frames 18 and 31 against frame 01, E:\Droppy\work\scratch\glass-g3\video_shadow.py).
[Collection("Headless")]
public sealed class SwiftShadowCastTests(HeadlessSession headless)
{
    private const double Left = 100, Top = 100, Width = 400, Height = 267; // the Mac head card, 400 x 267 pt (frame 18)

    // The scrim (r 22) with its caster, over white; returns the darkness (1 - level, 0 to 1) at a window point.
    private static Func<int, int, double> Render(Color fill, Color shadow, double radius, double y, bool boxShadow = false)
    {
        var scrim = new Border { Width = Width, Height = Height, CornerRadius = new CornerRadius(22), Background = new SolidColorBrush(fill) };
        var host = new Grid { Margin = new Thickness(Left, Top, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        if (boxShadow) scrim.BoxShadow = SwiftShadow.BoxShadows(shadow, radius, y: y);
        else host.Children.Add(new SwiftShadow.Caster(scrim, shadow, radius, y: y));
        host.Children.Add(scrim);
        var window = new Window { Width = 600, Height = 520, Background = Brushes.White, Content = host };
        window.Show();
        try
        {
            using var bitmap = window.CaptureRenderedFrame()!;
            var size = bitmap.PixelSize;
            var bytes = new byte[size.Width * size.Height * 4];
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try { bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), bytes.Length, size.Width * 4); }
            finally { handle.Free(); }
            return (x, py) =>
            {
                var p = (py * size.Width + x) * 4;
                return 1 - Math.Max(bytes[p], Math.Max(bytes[p + 1], bytes[p + 2])) / 255.0;
            };
        }
        finally { window.Close(); }
    }

    private static double Above(Func<int, int, double> dark, int d) => Enumerable.Range((int)Left + 120, 160).Average(x => dark(x, (int)Top - d));
    private static double Beside(Func<int, int, double> dark, int d) => Enumerable.Range((int)Top + 60, 120).Average(y => dark((int)(Left + Width) + d - 1, y));

    [Fact]
    // ED-358 (numbered by the lead); the existing Req trait below/above names the source.
    [Trait("Rule", "R-VIEW-11")]
    [Trait("Req", "PR-HYDRA-229")] // HydraPanel.swift:116-120 the scrim's shape.fill(black.opacity(0.3)).shadow(color: .black, radius: 28, y: 10)
    public void A_filled_shapes_shadow_is_cast_at_the_fills_alpha_and_drawn_under_it_through_the_fill() => headless.OnUiThread(() =>
    {
        var dark = Render(Color.FromArgb(77, 0, 0, 0), Colors.Black, 28, 10);
        // The amplitude: beside the card, 2 pt out at its middle, 0.3 x Phi(-2 / 28) = 0.141 (the colour's full alpha would be 0.47).
        var beside = Beside(dark, 2);
        Assert.True(Math.Abs(beside - 0.141) < 0.012, $"beside the card the shadow is {beside:F3}, the design 0.141 (black 1.0 cast by the 0.3 fill)");
        // Under the fill, through it: the card's middle reads the fill over the whole shadow, 1 - 0.7 x 0.7 = 0.51 (the fill alone, 0.30, is
        // what a shadow drawn outside the box leaves).
        var centre = dark((int)(Left + Width / 2), (int)(Top + Height / 2));
        Assert.True(Math.Abs(centre - 0.51) < 0.012, $"inside the card the fill over its shadow reads {centre:F3}, the design 0.51");
        // Near the top edge inside, the shadow (moved 10 down) is weaker than at the middle, but there: 2.5 pt in, 1 - 0.7 x (1 - 0.3 x
        // Phi(-7.5 / 28)) = 0.383, more than the fill's 0.30 alone.
        var underTop = dark((int)(Left + Width / 2), (int)Top + 2);
        Assert.True(Math.Abs(underTop - 0.383) < 0.012, $"2.5 pt inside the top edge the fill over its shadow reads {underTop:F3}, the design 0.383");

        // The light theme's white 0.34 fill casting black 0.65: 0.221 at full strength.
        var light = Render(Color.FromArgb(87, 255, 255, 255), Color.FromArgb(166, 0, 0, 0), 28, 10);
        var lightBeside = Beside(light, 2);
        Assert.True(Math.Abs(lightBeside - 0.221 * 0.4715) < 0.012, $"light: beside the card the shadow is {lightBeside:F3}, the design {0.221 * 0.4715:F3}");

        // The instrument, and what the port drew before (BoxShadow): the colour's full alpha outside, nothing under the fill.
        var before = Render(Color.FromArgb(77, 0, 0, 0), Colors.Black, 28, 10, boxShadow: true);
        Assert.True(Beside(before, 2) > 0.4, $"the BoxShadow draws the colour's full alpha beside the card ({Beside(before, 2):F3})");
        Assert.True(Math.Abs(before((int)(Left + Width / 2), (int)(Top + Height / 2)) - 0.30) < 0.01, "the BoxShadow leaves the fill alone inside");
    });

    [Fact]
    // ED-358 (numbered by the lead); the existing Req trait below/above names the source.
    [Trait("Rule", "R-VIEW-11")]
    [Trait("Req", "UI-SIDEBAR-060")] // the lead's E rulings, finding B, item 3: the panel's shadow profile against the video at 2 / 14 / 30 / 46 pt
    public void The_head_cards_shadow_above_and_beside_its_edge_is_the_mac_videos_to_two_hundredths() => headless.OnUiThread(() =>
    {
        // Measured on the Mac video (frame 18 against frame 01; frame 31 agrees to 0.01): the shadow d pt above the card's top edge and beside
        // its right edge (video_shadow.py). The design 0.3 x Phi(-(d + 10) / 28) above and 0.3 x Phi(-d / 28) beside is within 0.02 of them;
        // the colour's full alpha (the port before) drew 0.334, 0.196, 0.077, 0.023 above at 2, 14, 30, 46.
        (int D, double Mac)[] above = [(2, 0.089), (4, 0.084), (8, 0.069), (14, 0.048), (20, 0.024), (30, 0.009), (46, -0.002)];
        (int D, double Mac)[] beside = [(2, 0.146), (4, 0.141), (8, 0.123), (14, 0.109), (20, 0.084), (30, 0.037), (46, 0.028)];
        // The usage panel's scrim, the same recipe (UsageFloatingPanel.swift:68-72), beside its left edge (frame 15 against frame 14).
        (int D, double Mac)[] usage = [(2, 0.145), (4, 0.137), (8, 0.123), (14, 0.094), (20, 0.076), (30, 0.044), (46, 0.013)];
        var dark = Render(Color.FromArgb(77, 0, 0, 0), Colors.Black, 28, 10);
        var failures = new List<string>();
        foreach (var (d, mac) in usage)
            if (Math.Abs(Beside(dark, d) - mac) > 0.02) failures.Add($"usage beside {d}: port {Beside(dark, d):F3}, Mac {mac:F3}");
        foreach (var (d, mac) in above)
            if (Math.Abs(Above(dark, d) - mac) > 0.02) failures.Add($"above {d}: port {Above(dark, d):F3}, Mac {mac:F3}");
        foreach (var (d, mac) in beside)
            if (Math.Abs(Beside(dark, d) - mac) > 0.02) failures.Add($"beside {d}: port {Beside(dark, d):F3}, Mac {mac:F3}");
        Assert.True(failures.Count == 0, string.Join("; ", failures));
    });

    [Fact]
    // ED-358 (numbered by the lead); the existing Req trait below/above names the source.
    [Trait("Rule", "R-VIEW-11")]
    public void A_cast_shadow_of_radius_r_is_a_gaussian_of_sigma_r_within_5_percent() => headless.OnUiThread(() =>
    {
        // SwiftShadow.DrawCast blurs the shape with Skia's mask filter of sigma r (ED-327's SwiftUI radius); fitted beside a long edge (its
        // middle rows, past the corners' reach), where the shadow is A x Phi(-d / sigma).
        foreach (var radius in new[] { 10.0, 28 })
        {
            var dark = Render(Colors.Black, Colors.Black, radius, 0);
            var samples = new List<(double, double)>();
            var edge = Left + Width;
            for (var x = (int)edge; x < (int)edge + 4 * radius && x < 600; x++)
                samples.Add((x + .5, Enumerable.Range((int)Top + 100, 60).Average(y => dark(x, y))));
            var (sigma, residual) = GaussianEdge.FitSigma(samples, double.NegativeInfinity, edge, 1);
            Assert.True(Math.Abs(sigma - radius) <= .05 * radius, $"radius {radius}: the cast is drawn with sigma {sigma:F3} px");
            Assert.True(residual < .006, $"radius {radius}: the falloff is not a Gaussian's (rms residual {residual:F4})");
        }
        // The cast colour: the colour's alpha times the fill's.
        Assert.Equal(Color.FromArgb(77, 0, 0, 0), SwiftShadow.Cast(Colors.Black, 77 / 255.0));
        Assert.Equal(Color.FromArgb(57, 0, 0, 0), SwiftShadow.Cast(Color.FromArgb(166, 0, 0, 0), 87 / 255.0));
        Assert.Equal(0.102, SwiftShadow.FillAlpha(new SolidColorBrush(Color.FromArgb(26, 255, 255, 255))), 3);
        Assert.Equal(0, SwiftShadow.FillAlpha(null));
    });
}
