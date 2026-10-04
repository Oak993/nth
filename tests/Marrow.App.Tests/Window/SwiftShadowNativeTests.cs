using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.Testing;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window.Native;

// The real window's draw of a SwiftUI shadow (unit rec-shadow, ED-327; the lead's E rulings, finding B). The
// headless tests run on Skia's CPU raster, whose three-box blur draws 0.94-0.97 of a nominal sigma
// (SwiftShadowHeadlessTests); this shows what the user's screen gets. A topmost white window holds an opaque black slab whose
// left edge stands at x = 600 DIP and which casts SwiftShadow.BoxShadows(black, r); the composed screen (BitBlt with
// CAPTUREBLT) is read along one row left of the edge and fitted to Phi((x - edge) / sigma) (GaussianEdge), in physical pixels,
// then divided by the display scale: sigma in DIPs, SwiftUI's points. Measured 2026-10-05 (OAK-BLACK, scale 1, Avalonia's
// default Win32 GPU path): radius 28 drew sigma 28.062, radius 10 drew 10.035 (rms residuals 0.0027, 0.0016).
[Collection("WindowManagerNative")]
public sealed class SwiftShadowNativeTests(WindowManagerNativeSession native, Xunit.Abstractions.ITestOutputHelper output)
{
    private const double EdgeDip = 600, WidthDip = 800, HeightDip = 300;

    [Fact]
    [Trait("Rule", "R-VIEW-11")]
    [Trait("Req", "UI-SIDEBAR-060")] // SidebarView.swift:1843 .shadow(radius: 10): sigma 10 on the real window
    public void On_a_real_window_a_swift_shadow_of_radius_10_and_28_is_drawn_as_a_gaussian_of_sigma_10_and_28() => native.Run(subject =>
    {
        Border slab = null!;
        var (window, scale) = native.OnUi(() =>
        {
            slab = new Border { Background = Brushes.Black, Margin = new Thickness(EdgeDip, 0, 0, 0) };
            subject.Settings.Hide();
            subject.Main.Hide();
            var screen = subject.Main.Screens.Primary ?? throw new InvalidOperationException("No native screen is available.");
            var work = screen.WorkingArea;
            var window = new AvaloniaWindow
            {
                WindowDecorations = WindowDecorations.None, ShowInTaskbar = false, Topmost = true, CanResize = false, ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Position = new PixelPoint(work.X + 40, work.Y + 40),
                Width = WidthDip, Height = HeightDip, Background = Brushes.White,
                Content = new Panel { Background = Brushes.White, ClipToBounds = false, Children = { slab } },
            };
            window.Template = new FuncControlTemplate<AvaloniaWindow>((owner, _) => new ContentPresenter { Content = owner.Content });
            native.Register(window);
            window.Show();
            return (window, screen.Scaling);
        });
        var results = new List<string>();
        foreach (var radius in new[] { 28.0, 10 })
        {
            native.OnUi(() => slab.BoxShadow = SwiftShadow.BoxShadows(Colors.Black, radius));
            Thread.Sleep(700);
            var (origin, edge) = native.OnUi(() => (window.Position, EdgeDip * scale));
            var width = (int)Math.Round(edge);
            var grab = ScreenGrab.Of(origin.X, origin.Y + (int)(HeightDip * scale / 2), width, 1);
            var samples = new List<(double, double)>();
            for (var x = 0; x < width; x++) samples.Add((x + .5, 1 - grab.Luma(x, 0) / 255));
            var (sigma, residual) = GaussianEdge.FitSigma(samples, edge, double.PositiveInfinity, 1);
            var dip = sigma / scale;
            results.Add($"radius {radius}: sigma {dip:F3} DIP ({sigma:F3} px at scale {scale}), rms residual {residual:F4}");
            Assert.True(Math.Abs(dip - radius) <= .03 * radius, string.Join("; ", results));
            Assert.True(residual < .006, string.Join("; ", results));
        }
        output.WriteLine(string.Join("; ", results)); // the measured values, in the child's TRX
    }, factType: typeof(SwiftShadowNativeTests));
}
