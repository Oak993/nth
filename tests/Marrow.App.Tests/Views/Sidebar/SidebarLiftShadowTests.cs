using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Sidebar;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;
using static Marrow.App.Tests.Views.Sidebar.SidebarViewTests;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Sidebar;

// The dragged row's lifted shadow, as drawn (unit rec-shadow; the lead's E rulings, finding B; ED-327).
// SidebarView.swift:1843 `.shadow(color: .black.opacity(0.28), radius: 10, y: 4)` under :1846 `.scaleEffect(1.02)`: SwiftUI's
// radius is the Gaussian's standard deviation, so the Mac draws the lift's shadow with sigma 10 pt, scaled with the row to
// 10.2 pt. This renders the port's real lift (SidebarThreadRow.ApplyDraggedLift, fully lifted) over white and fits the
// shadow's falloff above and below the row, along its centre column, to 0.28 * (Phi((y - top') / s) - Phi((y - bottom') / s))
// with the row's drawn top and bottom moved down by the 4 pt offset (times the scale): s is the drawn sigma.
[Collection("Headless")]
public sealed class SidebarLiftShadowTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "UI-SIDEBAR-060")] // SidebarView.swift:1838-1849, :1843 `.shadow(color: .black.opacity(0.28), radius: 10, y: 4)`: the dragged row's lift
    [Trait("Req", "UI-SIDEBAR-017")] // SidebarView.swift:1843: the row face's dragged state, "black 0.28 shadow radius 10 y 4"
    public void The_lifted_rows_shadow_is_drawn_as_a_gaussian_of_sigma_10_scaled_with_the_row() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        var grabbed = Guid.NewGuid();
        SidebarView view = null!;
        Control host = null!;
        AvaloniaWindow window = null!;
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        harness.Main.OnMain(() =>
        {
            view = Detached(model);
            var content = new Border { Width = 220, Height = 28 };
            host = (Control)CallStatic(Private("SidebarThreadRow"), "ApplyDraggedLift", content, (Func<bool>)(() => Drag(view).Id == grabbed))!;
            host.HorizontalAlignment = HorizontalAlignment.Center;
            host.VerticalAlignment = VerticalAlignment.Center;
            window = new AvaloniaWindow { Content = host, Width = 400, Height = 300, Background = Brushes.White };
            window.Show();
            Call(view, "DragChanged", grabbed, 5d);
        });
        for (var i = 0; i < 100; i++) // 1.6 s of 16 ms frames: the 0.25 s response spring has settled at 1.02 and opacity 1
        {
            harness.Main.Advance(TimeSpan.FromMilliseconds(16));
            manual.Advance(TimeSpan.FromMilliseconds(16));
        }
        try
        {
            harness.Main.OnMain(() =>
            {
                var lift = LiftOf(host);
                Assert.Equal(1d, lift.Opacity);
                // ED-NEW-glass-g3: the real fill (the label colour's 0.85 at 0.12: 26 of 255) casts black 0.28 at its own alpha, so the shadow
                // is 0.029 at its strongest (it was the colour's full 0.28): just below the row the white reads 1 - 0.029 x 0.64, about 4.7 levels.
                var below = lift.TranslatePoint(new Point(lift.Bounds.Width / 2, lift.Bounds.Height), window)!.Value;
                var dark = 255 * (1 - FramePixels.Levels(window, ((int)below.X, (int)Math.Ceiling(below.Y) + 1))[0]);
                Assert.InRange(dark, 2.5, 7.5);
                // The sigma is fitted on the same caster with the fill made opaque (black 1.0 casts the colour's full 0.28): the falloff then spans
                // 71 levels, where the real fill's 7 are too coarse to fit a sigma to.
                lift.Background = Brushes.Black;
                lift.Opacity = 1;
                var scale = ((ScaleTransform)host.RenderTransform!).ScaleX;
                Assert.Equal(1.02, scale, 4);
                var topLeft = lift.TranslatePoint(new Point(0, 0), window)!.Value;
                var bottomRight = lift.TranslatePoint(new Point(lift.Bounds.Width, lift.Bounds.Height), window)!.Value;
                var column = (int)((topLeft.X + bottomRight.X) / 2);
                var rows = Enumerable.Range(0, 300).Where(y => y + 1 < topLeft.Y - 1 || y > bottomRight.Y + 1).ToArray();
                var levels = FramePixels.Levels(window, rows.Select(y => (column, y)).ToArray());
                var samples = rows.Select((y, i) => (y + .5, 1 - levels[i])).ToArray();
                var offset = 4 * scale; // :1843 y: 4, scaled with the row
                var (sigma, residual) = GaussianEdge.FitSigma(samples, topLeft.Y + offset, bottomRight.Y + offset, 71 / 255.0);
                // Expected: the nominal sigma 10 * 1.02 = 10.2 px. The headless surface is Skia's CPU raster, whose three-box
                // blur draws 0.94-0.97 of a nominal sigma (SwiftShadowHeadlessTests; the real window's GPU blur draws it
                // exactly, SwiftShadowNativeTests), so the tolerance is 7 % of 10.2 px, and the drawn sigma is also the
                // raster's draw of 10.2 px on a straight edge (9.71) to 0.2 px (this edge is a 28.6 px band, not a
                // half-plane). Measured 2026-10-04: 9.587 px. Before the ruling the literal Blur = 10 drew 3.202 px.
                Assert.True(Math.Abs(sigma - 10 * scale) <= .07 * 10 * scale, $"the lifted row's shadow is drawn with sigma {sigma:F3} px, expected {10 * scale:F3}");
                var raster = GaussianEdge.RasterSigma(10 * scale);
                Assert.True(Math.Abs(sigma - raster) <= .2, $"the lifted row's shadow is drawn with sigma {sigma:F3} px; the raster draws 10.2 px as {raster:F3}");
                Assert.True(residual < .004, $"the shadow's falloff is not a Gaussian's (rms residual {residual:F4})");
            });
        }
        finally { harness.Main.OnMain(() => window.Close()); }
    });
}
