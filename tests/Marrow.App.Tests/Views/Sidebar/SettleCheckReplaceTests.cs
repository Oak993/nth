using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Chrome;
using Marrow.App.Views.Sidebar;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Sidebar;

// SidebarView.swift:2122-2132 SettleCheck: a settled thread's check is filled until hovered, then the outline
// (:2124), and the two replace each other with .contentTransition(.symbolEffect(.replace)) (:2129, SymbolReplace).
[Collection("Headless")]
public sealed class SettleCheckReplaceTests(HeadlessSession headless)
{
    private static string[] Symbols(Control root) =>
        root.GetLogicalDescendants().OfType<SwiftImage>().Select(image => image.SymbolName ?? "").ToArray();

    [Fact]
    [Trait("Req", "PR-SIDEBAR-078")]
    [Trait("Req", "UI-SIDEBAR-020")] // App/Views/Sidebar/SidebarView.swift:2129: hovering a settled check swaps filled for outline with a symbol-replace transition
    public void Hovering_a_settled_check_replaces_the_filled_check_with_the_outline_in_two_steps() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource(); using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        var check = new SettleCheck(isSettled: true, isPopping: false, () => { });
        var window = new Avalonia.Controls.Window { Width = 100, Height = 100, Content = check };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.Equal(["checkmark.circle.fill"], Symbols(check));
            var centre = check.TranslatePoint(new Point(check.Bounds.Width / 2, check.Bounds.Height / 2), window)!.Value;
            window.MouseMove(centre, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["checkmark.circle", "checkmark.circle.fill"], Symbols(check).Order().ToArray()); // both, while the filled one goes
            clock.Advance(TimeSpan.FromSeconds(0.4)); frames.Advance(TimeSpan.FromSeconds(0.4));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["checkmark.circle"], Symbols(check));
        }
        finally { window.Close(); }
    });
    [Theory]
    [Trait("Req", "UI-SIDEBAR-020")] // App/Views/Sidebar/SidebarView.swift:2122-2140 SettleCheck; UI/Chrome/Chrome.swift:29 inlineIconFont
    [InlineData(false, false, false, "checkmark.circle", "secondary", 1d)] // an open row's unhovered check
    [InlineData(false, false, true, "checkmark.circle", "success 1", 1d)] // hovering it
    [InlineData(true, false, false, "checkmark.circle.fill", "success 0.8", 1d)] // a settled row's check
    [InlineData(true, false, true, "checkmark.circle", "success 1", 1d)] // hovering a settled check outlines it
    [InlineData(false, true, false, "checkmark.circle.fill", "success 1", 1.35d)] // just clicked: filled and popped
    [InlineData(false, true, true, "checkmark.circle.fill", "success 1", 1.35d)]
    public void A_settle_check_is_a_20_point_square_in_the_inline_icon_font_with_the_recorded_glyph_colour_and_pop_in_every_state(
        bool settled, bool popping, bool hover, string symbol, string colour, double scale) => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource(); using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        var clicks = 0;
        var check = new SettleCheck(settled, popping, () => clicks++)
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, // the check's own size, not the window's
        };
        var window = new Avalonia.Controls.Window { Width = 100, Height = 100, Content = check };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            if (hover)
            {
                window.MouseMove(check.TranslatePoint(new Point(10, 10), window)!.Value, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                clock.Advance(TimeSpan.FromSeconds(1)); frames.Advance(TimeSpan.FromSeconds(1)); // any symbol replacement has finished
                Dispatcher.UIThread.RunJobs();
            }
            var glyph = Assert.Single(check.GetLogicalDescendants().OfType<SwiftImage>());
            Assert.Equal(symbol, glyph.SymbolName); // :2126
            Assert.Equal(new Size(20, 20), check.Bounds.Size); // :2130 .frame(width: 20, height: 20)
            Assert.Equal(SwiftFont.System(SwiftFont.TextStyle.Subheadline, weight: SwiftFont.Weight.Semibold), SwiftImage.GetFont(glyph)); // :2127 Chrome.inlineIconFont
            Assert.Equal(11d, SwiftImage.GetFont(glyph)!.PointSize);
            var expected = colour switch
            {
                "secondary" => Chrome.SecondaryText,
                "success 0.8" => Chrome.Success.Opacity(0.8),
                _ => Chrome.Success,
            };
            Assert.Equal(expected.Resolve(glyph.ActualThemeVariant), ((Avalonia.Media.ISolidColorBrush)glyph.GetValue(SwiftImage.ForegroundProperty)!).Color); // :2128
            Assert.Equal(scale, ((Avalonia.Media.ScaleTransform)glyph.RenderTransform!).ScaleX); // :2132
            Assert.Equal(scale, ((Avalonia.Media.ScaleTransform)glyph.RenderTransform!).ScaleY);
            Assert.Equal(settled ? "Reopen thread" : "Settle thread", Avalonia.Automation.AutomationProperties.GetName(Assert.Single(check.GetLogicalDescendants().OfType<Button>()))); // :2139
            // a rectangular content shape: the corner of the 20 pt square clicks, not just the drawn glyph
            window.MouseDown(check.TranslatePoint(new Point(1, 1), window)!.Value, MouseButton.Left);
            window.MouseUp(check.TranslatePoint(new Point(1, 1), window)!.Value, MouseButton.Left);
            Assert.Equal(1, clicks); // :2125 Button(action: action)
        }
        finally { window.Close(); }
    });
}
