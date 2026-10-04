using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// ED-327 (unit rec-shadow; the lead's E rulings, finding B, records UI-SIDEBAR-060 and UI-SIDEBAR-017):
// SwiftUI's .shadow(radius: r) is a Gaussian of standard deviation r, and SwiftShadow (src/Marrow.UI/Port/SwiftShadow.cs)
// turns r into the Avalonia blur that draws that sigma. These tests MEASURE what Avalonia.Skia 12.1.2 draws for a blur
// value rather than assume it: each renders a straight shadow edge (an opaque black half-plane casting an opaque black
// shadow, so the coverage left of the edge is the shadow alone) on the headless Skia raster surface, reads the alpha of
// one row, and fits Phi((x - edge) / sigma) to it by least squares (GaussianEdge, Marrow.Testing). The headless surface
// is Skia's CPU raster, whose blur is the W3C three-box approximation of the Gaussian (GaussianEdge.RasterBoxes): it draws
// 0.94-0.97 of the nominal sigma in window steps of about 0.5 px, and GaussianEdge.RasterSigma predicts what the fit reads
// on it. The real window's GPU blur draws the nominal sigma itself (SwiftShadowNativeTests).
[Collection("Headless")]
public sealed class SwiftShadowHeadlessTests(HeadlessSession headless)
{
    private const int Width = 1200, Height = 40, Edge = 900;

    private static double[] AlphaRow(RenderTargetBitmap bitmap)
    {
        using var copy = new WriteableBitmap(bitmap.PixelSize, bitmap.Dpi, Avalonia.Platform.PixelFormat.Rgba8888, Avalonia.Platform.AlphaFormat.Premul);
        using (var buffer = copy.Lock())
            bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), buffer.Address, buffer.RowBytes * buffer.Size.Height, buffer.RowBytes);
        var alpha = new double[Width];
        using (var locked = copy.Lock())
        {
            var row = new byte[locked.RowBytes];
            System.Runtime.InteropServices.Marshal.Copy(locked.Address + Height / 2 * locked.RowBytes, row, 0, locked.RowBytes);
            for (var x = 0; x < Width; x++) alpha[x] = row[x * 4 + 3] / 255.0;
        }
        return alpha;
    }

    // The fitted sigma (device px) and residual of the shadow left of the edge at x = Edge.
    private static (double Sigma, double Residual) Fit(double[] alpha)
    {
        var samples = new List<(double, double)>();
        for (var x = 0; x < Edge; x++) samples.Add((x + .5, alpha[x]));
        return GaussianEdge.FitSigma(samples, Edge, double.PositiveInfinity, 1);
    }

    // A BoxShadow on a rectangle whose left edge lands on device x = Edge, drawn under a scale (the DIP-to-pixel factor).
    private static (double Sigma, double Residual) BoxShadowEdge(BoxShadows shadows, double scale = 1)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(Width, Height));
        using (var context = bitmap.CreateDrawingContext())
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
            context.DrawRectangle(Brushes.Black, null, new RoundedRect(new Rect(Edge / scale, -1000, 2000, 3000)), shadows);
        return Fit(AlphaRow(bitmap));
    }

    // A DropShadowEffect on drawn content whose left edge lands on device x = Edge.
    private static (double Sigma, double Residual) DropShadowEdge(DropShadowEffect effect, double scale = 1)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(Width, Height));
        using (var context = bitmap.CreateDrawingContext())
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
        using (context.PushEffect(effect, new Rect(0, -1000, 3000 / scale, 3000)))
            context.FillRectangle(Brushes.Black, new Rect(Edge / scale, -1000, 2000, 3000));
        return Fit(AlphaRow(bitmap));
    }

    [Fact]
    [Trait("Rule", "R-TEST-7")]
    [Trait("Rule", "R-VIEW-11")]
    public void A_box_shadow_blur_and_a_drop_shadow_blur_radius_b_draw_the_skia_sigma_0_288675_b_plus_0_5_in_drawing_units() => headless.OnUiThread(() =>
    {
        // Avalonia.Skia's SkBlurRadiusToSigma (DrawingContextImpl.BoxShadowFilter.Create, and CreateEffect for the
        // DropShadowEffect) is sigma = 0.288675 b + 0.5; the CSS reading would be b / 2. What the raster draws for that
        // nominal sigma is GaussianEdge.RasterSigma's prediction to 0.05 px (measured 2026-10-04: b 3.5, 5, 10, 20, 32.9,
        // 60, 95.3, 150 read 1.431, 2.176, 3.209, 6.310, 9.710, 17.593, 27.198, 42.263 px, each the prediction to 0.001).
        foreach (var blur in new[] { 3.5, 5, 10, 20, 32.9, 60, 95.3, 150 })
        {
            var skia = SwiftShadow.Slope * blur + SwiftShadow.Floor;
            var predicted = GaussianEdge.RasterSigma(skia);
            var box = BoxShadowEdge(new BoxShadows(new BoxShadow { Blur = blur, Color = Colors.Black }));
            var drop = DropShadowEdge(new DropShadowEffect { BlurRadius = blur, Color = Colors.Black, Opacity = 1, OffsetX = 0, OffsetY = 0 });
            foreach (var (kind, (sigma, residual)) in new[] { ("BoxShadow.Blur", box), ("DropShadowEffect.BlurRadius", drop) })
            {
                Assert.True(Math.Abs(sigma - predicted) <= .05,
                    $"{kind} {blur}: measured sigma {sigma:F3} px, the raster's draw of Skia's {skia:F3} is {predicted:F3}, CSS reading {blur / 2:F3}");
                Assert.True(Math.Abs(sigma - skia) <= Math.Max(.3, .07 * skia), $"{kind} {blur}: measured sigma {sigma:F3} px, Skia's function {skia:F3}"); // a window step is about 0.5 px
                Assert.True(residual < .004, $"{kind} {blur}: the edge is not a Gaussian's (rms residual {residual:F4})");
                if (blur >= 20) Assert.True(sigma < .7 * blur / 2, $"{kind} {blur}: sigma {sigma:F3} reads as CSS's b / 2");
            }
        }
        // In drawing units: under a 2x transform (200 % display scaling) the same blur draws twice the device sigma.
        foreach (var blur in new[] { 10.0, 20 })
        {
            var skia = 2 * (SwiftShadow.Slope * blur + SwiftShadow.Floor);
            var predicted = GaussianEdge.RasterSigma(skia);
            var (sigma, _) = BoxShadowEdge(new BoxShadows(new BoxShadow { Blur = blur, Color = Colors.Black }), scale: 2);
            Assert.True(Math.Abs(sigma - predicted) <= .05, $"blur {blur} at scale 2: measured {sigma:F3} px, the raster's draw of {skia:F3} px is {predicted:F3}");
        }
    });

    [Fact]
    [Trait("Rule", "R-TEST-7")]
    [Trait("Rule", "R-VIEW-11")]
    public void A_swift_shadow_of_radius_r_draws_a_gaussian_of_sigma_r_within_5_percent_for_the_ports_radii_1_5_10_and_28() => headless.OnUiThread(() =>
    {
        // The three radii the Swift source uses: ShortcutsSettingsPage.swift:344 (1.5), SidebarView.swift:1843,
        // RowGlide.swift:208, FollowUpQueueTab.swift:477, ModelsSettingsPage.swift:259 (10), HydraPanel.swift:120,
        // SubagentPanel.swift:354, FloatingSidebarPanel.swift:100, UsageFloatingPanel.swift:72 (28).
        foreach (var radius in new[] { 1.5, 10, 28 })
        {
            Assert.Equal((radius - .5) / .288675, SwiftShadow.Blur(radius), 9);
            Assert.Equal(radius, SwiftShadow.Slope * SwiftShadow.Blur(radius) + SwiftShadow.Floor, 9); // the nominal Skia sigma is r
            var predicted = GaussianEdge.RasterSigma(radius);
            var box = BoxShadowEdge(SwiftShadow.BoxShadows(Colors.Black, radius));
            var drop = DropShadowEdge(SwiftShadow.DropShadowEffect(Colors.Black, 1, radius));
            foreach (var (kind, (sigma, residual)) in new[] { ("BoxShadow", box), ("DropShadowEffect", drop) })
            {
                // The raster draws r 1.5, 10, 28 as 1.431, 9.710, 27.198 px: within 5 % of r, and the prediction to 0.05 px.
                Assert.True(Math.Abs(sigma - radius) <= .05 * radius, $"{kind} radius {radius}: measured sigma {sigma:F3} px");
                Assert.True(Math.Abs(sigma - predicted) <= .05, $"{kind} radius {radius}: measured sigma {sigma:F3} px, predicted {predicted:F3}");
                Assert.True(residual < .004, $"{kind} radius {radius}: rms residual {residual:F4}");
            }
        }
        // At 200 % scaling the radius is in DIPs, as SwiftUI's points are: twice the device sigma.
        var (scaled, _) = BoxShadowEdge(SwiftShadow.BoxShadows(Colors.Black, 10), scale: 2);
        Assert.True(Math.Abs(scaled - 20) <= 1, $"radius 10 at scale 2: measured sigma {scaled:F3} px");
        // The helper keeps the rest of the recipe as the Swift states it.
        var shadow = SwiftShadow.BoxShadow(Color.FromArgb(71, 0, 0, 0), 10, y: 4);
        Assert.Equal((Color.FromArgb(71, 0, 0, 0), 0d, 4d, 0d, false), (shadow.Color, shadow.OffsetX, shadow.OffsetY, shadow.Spread, shadow.IsInset));
        var effect = SwiftShadow.DropShadowEffect(Colors.Black, .45, 1.5, 0, 1);
        Assert.Equal((Colors.Black, .45, 0d, 1d), (effect.Color, effect.Opacity, effect.OffsetX, effect.OffsetY));
        Assert.Equal(0, SwiftShadow.Blur(.5)); // under Skia's least blurred sigma: an unblurred shadow
    });
}
