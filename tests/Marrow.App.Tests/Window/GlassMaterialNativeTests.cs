using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window.Native;

// ED-NEW-glass-g1 (OD-0107 and its amendment) on a real window and real screen pixels: the composer pill's glass (818 x 46, the 22 pt corner circular
// in its band, ComposerView.swift:1304 .glassEffect(.regular)) in an Acrylic window over the black and white stripes (GlassWindowNativeTests' pattern).
//   - over the window's own busy content (colour bars that move every frame) the glass blurs it, lifts it and draws its rim, and its draw costs
//     under 2 ms a frame, the GPU's work included (GlassMaterial.FrameCostForTests waits for the GPU before and after the draw);
//   - over the window's transparent pixels it shows the Acrylic through it, lifted, never the near-black veil it was (#1E1E20 at 18 % over an
//     opaque clear), with its rim.
// The pixels are the composed screen (BitBlt with CAPTUREBLT): what the user sees. The PNGs go to E:\Droppy\work\scratch\glass-g1\native.
[Collection("WindowManagerNative")]
public sealed class GlassMaterialNativeTests(WindowManagerNativeSession native)
{
    private const string Evidence = @"E:\Droppy\work\scratch\glass-g1\native";

    private static void Themeless(AvaloniaWindow window) =>
        window.Template = new FuncControlTemplate<AvaloniaWindow>((owner, _) => new ContentPresenter { Content = owner.Content });

    // Colour bars 6 pt wide, moved 2 pt every frame: what lies under the glass changes each frame, so it is drawn each frame.
    private sealed class Bars : Control
    {
        private static readonly IBrush[] Colours =
        [
            new SolidColorBrush(Color.FromRgb(230, 60, 60)), new SolidColorBrush(Color.FromRgb(250, 210, 70)), new SolidColorBrush(Color.FromRgb(60, 190, 90)),
            new SolidColorBrush(Color.FromRgb(50, 120, 230)), new SolidColorBrush(Color.FromRgb(240, 240, 240)), new SolidColorBrush(Color.FromRgb(20, 20, 24)),
        ];
        public double Phase { get; set; }
        public override void Render(DrawingContext context)
        {
            for (var i = -1; i * 6 < Bounds.Width + 6; i++)
                context.FillRectangle(Colours[((i % Colours.Length) + Colours.Length) % Colours.Length], new Rect(i * 6 + Phase % 6 - 6, 0, 6, Bounds.Height));
        }
    }

    private static Border Pill()
    {
        var pill = new Border { Width = 818, Height = 46, Background = Brushes.Transparent, CornerRadius = new CornerRadius(22), HorizontalAlignment = HorizontalAlignment.Left };
        SwiftGlass.SetEffect(pill, GlassEffect.Regular(GlassShape.RoundedRectangle(22, RoundedCornerStyle.Continuous, circularInBand: true)));
        return pill;
    }

    [Fact]
    [Trait("Decision", "ED-NEW-glass-g1")]
    [Trait("Req", "PR-WIN-180")] // Chrome.swift:179-182; ComposerView.swift:1304; OD-0107, OD-0005
    public void The_composer_pill_glass_on_a_real_window_is_translucent_over_the_Acrylic_lit_over_busy_content_and_costs_under_two_ms_a_frame() => native.Run(subject =>
    {
        Directory.CreateDirectory(Evidence);
        var (work, scale) = native.OnUi(() =>
        {
            var screen = subject.Main.Screens.Primary ?? throw new InvalidOperationException("No native screen is available.");
            if (screen.WorkingArea.Width < 1400 || screen.WorkingArea.Height < 900)
                throw new InvalidOperationException("NATIVE_PRECONDITION: the glass tests need a working area of at least 1400 x 900 pixels.");
            subject.Settings.Hide();
            subject.Main.Hide();
            return (screen.WorkingArea, screen.Scaling);
        });
        var origin = new PixelPoint(work.X + (work.Width - (int)(1000 * scale)) / 2, work.Y + (work.Height - (int)(400 * scale)) / 2);
        var (stripes, window, bars, acrylicPill, busyPill) = native.OnUi(() =>
        {
            var stripes = new AvaloniaWindow
            {
                WindowDecorations = WindowDecorations.None, ShowInTaskbar = false, Topmost = true, CanResize = false, ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(origin.X - 40, origin.Y - 40),
                Width = 1080, Height = 480, Content = new Stripes(),
            };
            Themeless(stripes);
            native.Register(stripes);
            stripes.Show();
            var bars = new Bars { Height = 160, VerticalAlignment = VerticalAlignment.Bottom };
            var acrylicPill = Pill();
            acrylicPill.Margin = new Thickness(91, 60, 0, 0);
            acrylicPill.VerticalAlignment = VerticalAlignment.Top;
            var busyPill = Pill();
            busyPill.Margin = new Thickness(91, 0, 0, 57);
            busyPill.VerticalAlignment = VerticalAlignment.Bottom;
            var window = new AvaloniaWindow
            {
                WindowDecorations = WindowDecorations.None, ShowInTaskbar = false, Topmost = true, CanResize = false, ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Position = origin, Width = 1000, Height = 400,
                Background = Brushes.Transparent, TransparencyLevelHint = [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Transparent],
                RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark,
                Content = new Panel { Children = { bars, acrylicPill, busyPill } },
            };
            Themeless(window);
            native.Register(window);
            window.Show();
            return (stripes, window, bars, acrylicPill, busyPill);
        });
        Thread.Sleep(1200);
        PixelPoint At(Control piece, double x, double y) => native.OnUi(() => piece.PointToScreen(new Point(x, y)));

        // Over the Acrylic: the pill's middle against the Acrylic just above it, and the pill's top rim against its middle.
        var a = At(acrylicPill, -20, -24);
        var acrylic = ScreenGrab.Of(a.X, a.Y, (int)(858 * scale), (int)(94 * scale));
        acrylic.SavePng(Path.Combine(Evidence, "pill-over-acrylic.png"));
        var aIn = ScreenGrab.Of(At(acrylicPill, 300, 23).X, At(acrylicPill, 300, 23).Y, (int)(200 * scale), 1);
        var aOut = ScreenGrab.Of(At(acrylicPill, 300, -12).X, At(acrylicPill, 300, -12).Y, (int)(200 * scale), 1);
        var aRim = ScreenGrab.Of(At(acrylicPill, 300, 0.5).X, At(acrylicPill, 300, 0.5).Y, (int)(200 * scale), 1);
        var aBelowRim = ScreenGrab.Of(At(acrylicPill, 300, 8).X, At(acrylicPill, 300, 8).Y, (int)(200 * scale), 1);
        Assert.True(aOut.StripeContrast() < 3, $"the instrument: the Acrylic blurs the stripes ({aOut.StripeContrast():F2})");
        Assert.True(aIn.MeanLuma() >= aOut.MeanLuma() + 8, $"over the Acrylic the glass is a light film over it, not a dark veil: inside {aIn.MeanLuma():F1}, Acrylic {aOut.MeanLuma():F1}");
        Assert.True(aIn.MeanLuma() <= aOut.MeanLuma() + 70, $"and the Acrylic shows through it: inside {aIn.MeanLuma():F1}, Acrylic {aOut.MeanLuma():F1}");
        Assert.True(aRim.MeanLuma() >= aBelowRim.MeanLuma() + 10, $"its top rim: {aRim.MeanLuma():F1} against {aBelowRim.MeanLuma():F1}");

        // Over the busy content: blurred (the bars' steps are gone), lit at the top, and the frame cost while the bars move under it.
        var b = At(busyPill, -20, -24);
        var costs = new List<double>();
        GlassMaterial.FrameCostForTests = (size, elapsed) => { if (size.Width == 818 && size.Height == 46) lock (costs) costs.Add(elapsed.TotalMilliseconds); };
        try
        {
            var bIn = ScreenGrab.Of(At(busyPill, 300, 23).X, At(busyPill, 300, 23).Y, (int)(200 * scale), 1);
            var bOut = ScreenGrab.Of(At(busyPill, 300, 80).X, At(busyPill, 300, 80).Y, (int)(200 * scale), 1);
            ScreenGrab.Of(b.X, b.Y, (int)(858 * scale), (int)(160 * scale)).SavePng(Path.Combine(Evidence, "pill-over-busy-content.png"));
            Assert.True(bOut.StripeContrast() > 8, $"the instrument: the bars are sharp outside the glass ({bOut.StripeContrast():F2})");
            Assert.True(bIn.StripeContrast() < bOut.StripeContrast() * 0.25, $"the glass blurs them: {bIn.StripeContrast():F2} against {bOut.StripeContrast():F2}");
            var watch = Stopwatch.StartNew();
            var frames = 0;
            while (watch.ElapsedMilliseconds < 2500)
            {
                native.OnUi(() => { bars.Phase += 2; bars.InvalidateVisual(); });
                frames++;
                Thread.Sleep(16);
            }
            Thread.Sleep(200);
        }
        finally { GlassMaterial.FrameCostForTests = null; }
        double[] sorted;
        lock (costs) sorted = [.. costs.OrderBy(c => c)];
        File.WriteAllText(Path.Combine(Evidence, "frame-cost.txt"),
            $"composer pill 818 x 46 at scale {scale}: {sorted.Length} draws; median {sorted[sorted.Length / 2]:F3} ms, p95 {sorted[(int)(sorted.Length * 0.95)]:F3} ms, max {sorted[^1]:F3} ms\n");
        Assert.True(sorted.Length >= 60, $"the pill was drawn {sorted.Length} times while the bars moved");
        Assert.True(sorted[sorted.Length / 2] < 2.0, $"median cost {sorted[sorted.Length / 2]:F3} ms");
        Assert.True(sorted[(int)(sorted.Length * 0.95)] < 4.0, $"p95 cost {sorted[(int)(sorted.Length * 0.95)]:F3} ms");
        GC.KeepAlive(stripes);
        GC.KeepAlive(window);
    }, factType: typeof(GlassMaterialNativeTests));
}
