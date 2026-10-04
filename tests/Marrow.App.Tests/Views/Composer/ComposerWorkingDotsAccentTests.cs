using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Theme;

namespace Marrow.App.Tests.Views.Composer;

// OD-0088 (ED-253), owner ruling 2026-10-03: the composer's working dots wear the user's accent by default. The source tints
// them with Chrome.accentNSColor (ComposerWorkingDots.swift:15-16; the theme's accent, else the system's), which the port gives
// the pick; the field re-reads it as its body is drawn again (:104-111 configure(color:)), so a later pick or theme recolours the
// dots on the render server (the frame it is sent) as well as the still dots.
[Collection("Headless")]
public sealed class ComposerWorkingDotsAccentTests(HeadlessSession headless)
{
    private static Color FrameColor(DotFieldControl field)
    {
        var frame = field.GetType().GetProperty("LastRenderedFrame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(field)!;
        return Assert.IsAssignableFrom<ISolidColorBrush>(frame.GetType().GetProperty("Brush")!.GetValue(frame)).Color;
    }

    private static void Render(Avalonia.Controls.Window window)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var pixels = window.CaptureRenderedFrame();
    }

    [Fact]
    [Trait("Decision", "ED-253")]
    [Trait("Req", "UI-COMPOSER-017")] // ComposerWorkingDots.swift:15-16 colour: Chrome.accentNSColor, live, on the wrapper the pill mounts
    [Trait("Rule", "R-TEST-4")] // UI-COMPOSER-017 (OD-0098, the Swift is the proof): the accent colour clause
    public void The_working_dots_wear_the_picked_accent_and_a_later_pick() => headless.OnUiThread(() =>
    {
        var (theme, choice) = (ThemeManager.Current, AccentChoice.Current);
        using var runtime = new TestRuntime();
        using var executor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        using var source = FrameClock.Source.Override(new ManualFrameSource());
        var window = new Avalonia.Controls.Window { Width = 64, Height = 24, Background = Brushes.Black, RequestedThemeVariant = ThemeVariant.Dark };
        try
        {
            ThemeManager.Current = AppTheme.System;
            AccentChoice.Current = "orchid";
            var dots = new ComposerWorkingDots();
            window.Content = dots;
            window.Show();
            Render(window);
            var field = dots.GetVisualDescendants().OfType<DotFieldControl>().Single();
            Assert.Equal(Color.Parse("#E298C6"), FrameColor(field)); // Orchid, dark: the pick, not the Windows accent
            // A later pick eases there over 0.32 s on the render server (SPEC 6.4, ED-253 round 4: ComposerWorkingDotsEaseTests);
            // the colour it comes to rest in.
            AccentChoice.Current = "teal";
            Render(window);
            runtime.Advance(TimeSpan.FromSeconds(0.4));
            Render(window);
            Assert.Equal(AccentPalette.Pick("teal")!.Resolve(ThemeVariant.Dark), FrameColor(field));
            ThemeManager.Current = AppTheme.Dracula;
            AccentChoice.Current = "windows"; // the theme's own
            Render(window);
            runtime.Advance(TimeSpan.FromSeconds(0.4));
            Render(window);
            Assert.Equal(Color.Parse("#BD93F9"), FrameColor(field));
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
}
