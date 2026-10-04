// Source: none for the repaint itself (PORT-ONLY, ED-320 7b): once an eased turnover of a window's glass has ended, the window is drawn
// whole once. The review of 98acdd51e: the repaint followed only the last piece to start, so when that piece left the tree in the middle of the turnover
// (its run stopped and never reached its end) the repaint never came. It now follows every piece that turns: it comes a frame after the last one still
// turning ends or leaves.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Port;

[Collection("Headless")]
public sealed class GlassMaterialTurnoverRepaintTests(HeadlessSession headless)
{
    private static void Drain()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static Border Piece(HorizontalAlignment side)
    {
        var piece = new Border { Background = Brushes.Transparent, Width = 100, Height = 100, HorizontalAlignment = side, VerticalAlignment = VerticalAlignment.Center };
        SwiftGlass.SetEffect(piece, GlassEffect.Regular(GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous)));
        return piece;
    }

    // Two glass pieces turn over with the window; in the middle of the turnover none, the first, the second or both leave the tree. The backdrop under
    // them draws the same in both appearances, so it is drawn again only by the whole repaint: exactly once, after the turnover (or, when both leave,
    // a frame after they left).
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [Trait("Decision", "ED-254")]
    public void The_window_is_drawn_whole_once_after_the_turnover_even_when_a_turning_piece_leaves_in_the_middle(bool firstLeaves, bool secondLeaves) => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var time = new ManualTimeProvider();
        using var clock = SwiftClock.Provider.Override(time);
        var source = (ManualFrameSource)FrameClock.Source.Current;
        void Frame() { source.Advance(TimeSpan.FromSeconds(1.0 / 60)); time.Advance(TimeSpan.FromSeconds(1.0 / 60)); Drain(); }
        var backdrop = new GlassBackdropPattern { Flat = true };
        var first = Piece(HorizontalAlignment.Left);
        var second = Piece(HorizontalAlignment.Right);
        var panel = new Panel { Children = { backdrop, first, second } };
        var window = new Window { Width = 256, Height = 160, RequestedThemeVariant = ThemeVariant.Dark, Content = panel };
        AppearanceTurn.SetTurnsOver(window, true);
        window.Show();
        try
        {
            for (var i = 0; i < 6; i++) Frame();
            Assert.True(AppearanceTurn.Eases(first), "the window has drawn a frame, so it turns over");
            var drawn = backdrop.RenderCount;
            window.RequestedThemeVariant = ThemeVariant.Light;
            Drain();
            for (var k = 0; k < 8; k++) Frame();
            if (firstLeaves) panel.Children.Remove(first);
            if (secondLeaves) panel.Children.Remove(second);
            for (var k = 0; k < 8; k++) Frame();
            var midway = backdrop.RenderCount - drawn;
            for (var k = 0; k < 40; k++) Frame();
            var after = backdrop.RenderCount - drawn;
            Assert.True(after == 1,
                $"the backdrop was drawn {after} times after the click ({midway} by frame 16), expected once, by the whole repaint after the turnover");
        }
        finally { window.Close(); }
    });
}
