using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Sidebar;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Sidebar;

// SidebarView.swift:2128 `.foregroundStyle(isDone || isHovering ? success... : secondaryText)` changes inside :2135-2137
// `withAnimation(Chrome.hover)`, so an open row's check (outline both times) eases secondaryText -> success over Chrome.hover
// instead of cutting (OD-0076). The symbol-replace for a changed symbol is SettleCheckReplaceTests'.
[Collection("Headless")]
public sealed class SettleCheckHoverColourTests(HeadlessSession headless)
{
    private static Color Shown(SettleCheck check) =>
        ((ISolidColorBrush)Assert.Single(check.GetLogicalDescendants().OfType<SwiftImage>()).GetValue(SwiftImage.ForegroundProperty)!).Color;

    [Theory]
    [Trait("Req", "UI-SIDEBAR-020")] // SidebarView.swift:2128 colour, :2135-2137 hover in withAnimation(Chrome.hover); OD-0076
    [InlineData(false)]
    [InlineData(true)]
    public void An_open_rows_check_eases_its_colour_to_success_on_hover_and_back_frame_by_frame_and_takes_it_at_once_under_Reduce_Motion(bool reduceMotion) => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource(); using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        using var reduction = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = reduceMotion });
        var check = new SettleCheck(isSettled: false, isPopping: false, () => { })
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };
        var window = new Avalonia.Controls.Window { Width = 100, Height = 100, Content = check };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        void Tick(double seconds) { clock.Advance(TimeSpan.FromSeconds(seconds)); frames.Advance(TimeSpan.FromSeconds(seconds)); Dispatcher.UIThread.RunJobs(); }
        try
        {
            var theme = check.ActualThemeVariant;
            var secondary = Chrome.SecondaryText.Resolve(theme);
            var success = Chrome.Success.Resolve(theme);
            Assert.NotEqual(secondary, success);
            var glyph = Assert.Single(check.GetLogicalDescendants().OfType<SwiftImage>());
            Assert.Equal("checkmark.circle", glyph.SymbolName);
            Assert.Equal(secondary, Shown(check));

            // Distance of a shown colour from the start along the way to the target, in [0, 1] (all four channels move together).
            static double Along(Color from, Color to, Color now)
            {
                var channels = new[] { (from.A, to.A, now.A), (from.R, to.R, now.R), (from.G, to.G, now.G), (from.B, to.B, now.B) };
                var moving = channels.First(c => c.Item1 != c.Item2);
                return (double)(moving.Item3 - moving.Item1) / (moving.Item2 - moving.Item1);
            }

            window.MouseMove(check.TranslatePoint(new Point(10, 10), window)!.Value, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            if (reduceMotion)
            {
                Assert.Equal(success, Shown(check)); // taken at once, in the frame of the hover
                window.MouseMove(new Point(90, 90), RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(secondary, Shown(check));
                return;
            }
            var progress = new List<double> { Along(secondary, success, Shown(check)) };
            for (var step = 0; step < 4; step++)
            {
                Tick(0.016);
                progress.Add(Along(secondary, success, Shown(check)));
            }
            Assert.InRange(progress[1], 0.01, 0.99); // on its way after one frame: not cut
            for (var i = 1; i < progress.Count; i++) Assert.True(progress[i] >= progress[i - 1], $"monotonic {string.Join(", ", progress)}");
            Assert.InRange(progress[^1], 0.3, 0.99); // after 64 ms of the 0.1 s hover ease, still short of the end
            Assert.Same(glyph, Assert.Single(check.GetLogicalDescendants().OfType<SwiftImage>())); // the same glyph: the symbol did not change
            Tick(0.2);
            Assert.Equal(success, Shown(check));

            window.MouseMove(new Point(90, 90), RawInputModifiers.None); // and back
            Dispatcher.UIThread.RunJobs();
            Tick(0.016);
            var back = Along(success, secondary, Shown(check));
            Assert.InRange(back, 0.01, 0.99);
            Tick(0.2);
            Assert.Equal(secondary, Shown(check));
            Assert.Same(glyph, Assert.Single(check.GetLogicalDescendants().OfType<SwiftImage>()));
        }
        finally { window.Close(); }
    });
}
