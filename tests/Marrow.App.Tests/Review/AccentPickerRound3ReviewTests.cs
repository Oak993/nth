using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Window;
using Marrow.App.Views.Composer;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Review;

[Collection("Headless")]
public sealed class AccentPickerRound3ReviewTests(HeadlessSession session) : SettingsWindowFixture(session)
{
    private readonly HeadlessSession ui = session;
    private static Color FrameColor(DotFieldControl field)
    {
        var frame = field.GetType().GetProperty("LastRenderedFrame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(field)!;
        return Assert.IsAssignableFrom<ISolidColorBrush>(frame.GetType().GetProperty("Brush")!.GetValue(frame)).Color;
    }

    private static void Render(HostWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var pixels = window.CaptureRenderedFrame();
    }

    private static int Distance(Color a, Color b) => new[] { Math.Abs(a.R - b.R), Math.Abs(a.G - b.G), Math.Abs(a.B - b.B), Math.Abs(a.A - b.A) }.Max();

    // REVIEW (OD-0076, SPEC 6.4 "Every use of the effective accent eases .32 s ease to the new colour"): every other accent brush in
    // the main window (SwiftColor.BrushBinding) eases a pick over 0.32 s; the working dots, the same accent, must not cut to it.
    [Fact]
    [Trait("Decision", "ED-253")]
    public void REVIEW_The_working_dots_ease_a_later_pick_with_every_other_accent_use() => ui.OnUiThread(() =>
    {
        var (theme, choice) = (ThemeManager.Current, AccentChoice.Current);
        using var runtime = new TestRuntime();
        using var executor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        using var rig = new SettingsRiseRig();
        var window = new HostWindow { Width = 64, Height = 24, Background = Brushes.Black, RequestedThemeVariant = ThemeVariant.Dark };
        try
        {
            ThemeManager.Current = AppTheme.System;
            AccentChoice.Current = "orchid";
            var dots = new ComposerWorkingDots();
            var reference = new Border { Width = 4, Height = 4, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
            reference.Background = Marrow.UI.Chrome.Chrome.AccentNSColor.Brush(reference);
            window.Content = new Panel { Children = { dots, reference } };
            window.Show();
            Render(window);
            var field = dots.GetVisualDescendants().OfType<DotFieldControl>().Single();
            var orchid = FrameColor(field);
            Assert.Equal(Color.Parse("#E298C6"), orchid);
            AccentChoice.Current = "teal";
            Render(window);
            // The pick's own frame: the reference has not moved yet, nor may the dots.
            Assert.True(Distance(orchid, FrameColor(field)) <= 2, $"pick frame: dots {FrameColor(field)}, reference {((ISolidColorBrush)reference.Background!).Color}");
            for (var k = 1; k <= 24; k++)
            {
                rig.Frame(1.0 / 60);
                Render(window);
                var expected = ((ISolidColorBrush)reference.Background!).Color;
                Assert.True(Distance(expected, FrameColor(field)) <= 3, $"frame {k}: dots {FrameColor(field)}, the accent's other uses {expected}");
            }
        }
        finally
        {
            window.Content = null;
            window.Close();
            AccentChoice.Current = choice;
            ThemeManager.Current = theme;
            Dispatcher.UIThread.RunJobs();
        }
    });

    // REVIEW (ED-253 (1), the popup audit): a link's popover (LinkPopover, a right-click on a link in Settings' markdown: the About
    // page's release notes, the Licenses popover) is an NSPopover on the Mac whose Escape monitor runs before the window's
    // sendEvent; the Settings window must stay when Escape closes it.
    [Fact]
    [Trait("Decision", "ED-253")]
    public void REVIEW_Escape_over_a_link_popover_in_settings_closes_the_popover_and_the_window_stays() => WithManager((h, manager) =>
    {
        var presenter = new RecordingPresenter();
        using var presentation = Presentation.Presenter.Override(presenter);
        using var rig = new SettingsRiseRig { Accepts = false };
        manager.Model.Settings.Theme = AppTheme.Dark;
        manager.ShowSettings(SettingsPage.About);
        Dispatcher.UIThread.RunJobs();
        var window = Settings(manager);
        Drain(window);
        for (var i = 0; i < 60; i++) { rig.Frame(1.0 / 60); Drain(window); }
        var anchor = View(manager).GetVisualDescendants().OfType<TextBlock>().First(t => t.IsEffectivelyVisible);
        try
        {
            LinkPopover.Shared.Show(SwiftURL.Create("https://example.com")!, null, new Rect(0, 0, 10, 10), anchor);
            Drain(window);
            var popup = Assert.Single(presenter.Calls, c => c.Presented).Popup;
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.Equal((popup, false), presenter.Calls[^1]);
            Assert.True(window.IsVisible, "Escape over the link popover closed the Settings window too");
        }
        finally { LinkPopover.Shared.Close(); }
    });

    private static (byte[] Pixels, int Width, int Height, int Stride) Capture(HostWindow window)
    {
        using var frame = window.CaptureRenderedFrame()!;
        using var buffer = frame.Lock();
        var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
        Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
        return (bytes, buffer.Size.Width, buffer.Size.Height, buffer.RowBytes);
    }

    // REVIEW (OD-0076 "nothing cuts"; ED-253 (2) "a scroll redraws at once"): a scroll of one pixel during the 0.32 s turnover
    // jumps both veil fades to the new appearance's colours while the rest of the window is still turning.
    [Fact]
    [Trait("Decision", "ED-253")]
    public void REVIEW_A_scroll_during_the_turnover_does_not_cut_the_veil_to_its_end() => WithManager((h, manager) =>
    {
        using var rig = new SettingsRiseRig { Accepts = false };
        using var accent = SwiftColor.AccentSource.Override(() => Color.Parse("#0067C0"));
        manager.Model.Settings.Theme = AppTheme.Dark;
        manager.ShowSettings(SettingsPage.Appearance);
        Dispatcher.UIThread.RunJobs();
        var window = Settings(manager);
        Drain(window);
        for (var i = 0; i < 60; i++) { rig.Frame(1.0 / 60); Drain(window); }
        var card = Nodes<LookCard>(View(manager)).Single();
        var page = Nodes<ScrollViewer>((Control)window.Content!).Single(s => s.GetVisualDescendants().Contains(card));
        page.Offset = new Vector(0, 30);
        Drain(window);
        for (var i = 0; i < 60; i++) { rig.Frame(1.0 / 60); Drain(window); }
        var fade = Nodes<PaneTopVeil>((Control)window.Content!).Single().GetVisualDescendants().OfType<Border>().First(b => b.Background is LinearGradientBrush);
        Color Top() => ((LinearGradientBrush)fade.Background!).GradientStops[0].Color;
        var from = Top();
        manager.Model.Settings.Theme = AppTheme.Light;
        Drain(window);
        for (var i = 0; i < 4; i++) { rig.Frame(1.0 / 60); Drain(window); }
        var before = Top();
        page.Offset = new Vector(0, 32);
        Drain(window);
        var after = Top();
        for (var i = 0; i < 40; i++) { rig.Frame(1.0 / 60); Drain(window); }
        var to = Top();
        Assert.True(Distance(from, to) > 40, $"the fade turns over ({from} to {to})");
        Assert.True(Distance(before, after) <= Distance(from, to) / 4, $"a two-pixel scroll at frame 4 moved the fade {before} -> {after} (the whole turn {from} -> {to})");
    });
}
