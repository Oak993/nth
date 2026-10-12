using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

// ED-NEW-lean-11 (lean pass L11), the pixel-equality guard of the Hydra shimmer (TimelineRows.swift:480-521): the shimmer that keeps its brush and its
// translation draws the same pixels as the code it replaced, frozen below, at 30 timestamps through more than a whole 1.8 s sweep, in both themes.
[Collection("Headless")]
public sealed class HydraShimmerPixelTests(HeadlessSession headless)
{
    // HydraShimmer.Body as it was before ED-NEW-lean-11 (a new translation and a new gradient at every draw).
    private static Control Frozen(Control content)
    {
        var band = new Border { HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
        var layer = new Panel { IsHitTestVisible = false, OpacityMask = new VisualBrush(content) { Stretch = Stretch.Fill } };
        layer.Children.Add(band);
        var root = new Panel { Children = { content, layer } };
        var width = 0.0;
        DateTimeOffset date = SwiftDate.Now;
        void Draw()
        {
            var side = Math.Max(24, width / 3);
            band.Width = side;
            var phase = SwiftDate.ReferenceSeconds(date) % 1.8 / 1.8;
            band.RenderTransform = new TranslateTransform(-side + phase * (width + side), 0);
            var primary = Chrome.PrimaryText;
            band.Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops = new GradientStops { new(primary.Opacity(0).Resolve(band.ActualThemeVariant), 0),
                    new(primary.Opacity(0.9).Resolve(band.ActualThemeVariant), 0.5), new(primary.Opacity(0).Resolve(band.ActualThemeVariant), 1) }
            };
        }
        content.SizeChanged += (_, e) => { width = e.NewSize.Width; Draw(); };
        band.ActualThemeVariantChanged += (_, _) => Draw();
        root.TimelineView(() => new AnimationTimelineSchedule(1.0 / 30.0, ScrollActivity.Shared.Current.IsScrolling), now => { date = now; Draw(); });
        return root;
    }

    private static TextBlock Label() => new() { Text = "Cooking the plan for the heads", FontSize = 28, FontWeight = FontWeight.Bold, Foreground = Brushes.Gray };

    private static AvaloniaWindow Show(Control content, Avalonia.Styling.ThemeVariant theme)
    {
        var window = new AvaloniaWindow { Width = 520, Height = 80, Background = Brushes.White, RequestedThemeVariant = theme, Content = content };
        window.Show();
        return window;
    }

    private static string Frame(AvaloniaWindow window)
    {
        for (var pass = 0; pass < 4; pass++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        using var bitmap = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
        using var buffer = bitmap.Lock();
        var pixels = new byte[buffer.RowBytes * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, pixels, 0, pixels.Length);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels));
    }

    [Theory]
    [Trait("Decision", "ED-NEW-lean-11")]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void The_shimmer_draws_as_the_code_it_replaced(string variant) => headless.OnUiThread(() =>
    {
        var theme = variant == "Dark" ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
        using var h = TimelineRowsPrSupport.Harness();
        var frames = new ManualFrameSource();
        using var seam = FrameClock.Source.Override(frames);
        var before = Show(Frozen(Label()), theme);
        var after = Show(new HydraShimmer().Body(Label()), theme);
        var seen = new HashSet<string>();
        for (var step = 0; step < 30; step++)
        {
            h.Main.AdvanceMilliseconds(70);
            frames.Advance(TimeSpan.FromMilliseconds(70));
            var expected = Frame(before);
            Assert.Equal(expected, Frame(after));
            seen.Add(expected);
        }
        Assert.True(seen.Count >= 20, $"the band moved through {seen.Count} distinct frames");
        TimelineRowsPrSupport.Close(before);
        TimelineRowsPrSupport.Close(after);
    });
}
