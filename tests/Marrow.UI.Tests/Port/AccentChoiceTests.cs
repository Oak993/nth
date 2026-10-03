using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Theme;
using UiChrome = Marrow.UI.Chrome.Chrome;

namespace Marrow.UI.Tests.Port;

// OD-0088 (ED-250): the user's accent reaches what the source paints with Chrome.accent (Chrome.swift:80-87): the spec
// everything reads, Chrome.accent and Chrome.accentNSColor, and a brush drawn with them, which eases to a new pick over
// 0.32 s (SPEC 6.4) on the frame clock and at once under Reduce Motion. ThemeManager is process state: these run in the
// Headless collection, one at a time, and restore it.
[Collection("Headless")]
public sealed class AccentChoiceTests(HeadlessSession headless)
{
    private static Color C(string hex) => Color.Parse(hex);

    private static void Restore(Action body)
    {
        var (theme, choice) = (ThemeManager.Current, AccentChoice.Current);
        try { body(); }
        finally
        {
            AccentChoice.Current = choice;
            ThemeManager.Current = theme;
        }
    }

    [Fact]
    [Trait("Decision", "ED-250")]
    public void A_pick_replaces_the_theme_accent_in_the_spec_and_chrome_accent_and_the_default_keeps_the_themes_own() => Restore(() =>
    {
        var posts = 0;
        void Count() => posts++;
        ThemeManager.DidChange += Count;
        try
        {
            ThemeManager.Current = AppTheme.System;
            AccentChoice.Current = "windows";
            Assert.Null(ThemeManager.Spec.Accent); // as the source: System has no accent of its own
            var before = UiChrome.Accent;
            posts = 0;
            AccentChoice.Current = "orchid";
            Assert.Equal(1, posts); // posted as a theme change is
            Assert.Same(AccentPalette.Pick("orchid"), ThemeManager.Spec.Accent);
            Assert.NotSame(before, UiChrome.Accent);
            Assert.Same(UiChrome.Accent, UiChrome.AccentNSColor);
            Assert.Equal(C("#E298C6"), UiChrome.Accent.Resolve(ThemeVariant.Dark));
            Assert.Equal(C("#733E67"), UiChrome.Accent.Resolve(ThemeVariant.Light));
            // Kept across a theme change; the default gives the theme's own back.
            ThemeManager.Current = AppTheme.Dracula;
            Assert.Equal(C("#E298C6"), UiChrome.Accent.Resolve(ThemeVariant.Dark));
            AccentChoice.Current = "windows";
            Assert.Equal(C("#BD93F9"), UiChrome.Accent.Resolve(ThemeVariant.Dark));
            Assert.Equal(C("#BD93F9"), ThemeManager.Spec.Accent!.Resolve(ThemeVariant.Dark));
            // A colour taken earlier follows: it is read when it is resolved.
            Assert.Equal(C("#BD93F9"), before.Resolve(ThemeVariant.Dark));
            Assert.Equal(Color.FromArgb(41, 0xBD, 0x93, 0xF9), before.Opacity(0.16).Resolve(ThemeVariant.Dark));
            // Unknown ids are the default.
            AccentChoice.Current = "notAColour";
            Assert.Equal("windows", AccentChoice.Current);
        }
        finally { ThemeManager.DidChange -= Count; }
    });

    [Fact]
    [Trait("Decision", "ED-250")]
    public void Under_the_default_the_system_accent_is_drawn_lifted_and_a_pick_is_drawn_as_given() => Restore(() =>
    {
        using var accent = SwiftColor.AccentSource.Override(() => C("#0067C0"));
        ThemeManager.Current = AppTheme.Dark;
        AccentChoice.Current = "windows";
        Assert.Equal(C("#4B9FFC"), UiChrome.Accent.Resolve(ThemeVariant.Dark)); // the board's preview of #0067C0
        Assert.Equal(C("#0067C0"), UiChrome.Accent.Resolve(ThemeVariant.Light));
        AccentChoice.Current = "graphite";
        Assert.Equal(C("#9C9C9C"), UiChrome.Accent.Resolve(ThemeVariant.Dark));
        Assert.Equal(C("#727272"), UiChrome.Accent.Resolve(ThemeVariant.Light));
    });

    // A window in Dark with one border filled with Chrome.accent, driven by a manual frame clock and SwiftClock.
    private void InWindow(bool reduceMotion, Action<Border, Action<double>, ManualFrameSource> body) => headless.OnUiThread(() => Restore(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        using var accent = SwiftColor.AccentSource.Override(() => C("#0067C0"));
        ThemeManager.Current = AppTheme.Dark;
        AccentChoice.Current = "windows";
        var border = new Border { Width = 40, Height = 40 };
        border.Background = UiChrome.Accent.Brush(border);
        var window = new Window { Width = 100, Height = 100, Content = border, RequestedThemeVariant = ThemeVariant.Dark };
        EnvironmentValues.SetAccessibilityReduceMotion(window, reduceMotion);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            body(border, ms =>
            {
                var step = TimeSpan.FromTicks((long)Math.Round(ms * TimeSpan.TicksPerMillisecond));
                time.Advance(step);
                frames.Advance(step);
                Dispatcher.UIThread.RunJobs();
            }, frames);
        }
        finally { window.Close(); }
    }));

    private static Color Drawn(Border border) => Assert.IsAssignableFrom<ISolidColorBrush>(border.Background).Color;

    [Fact]
    [Trait("Decision", "ED-250")]
    public void A_brush_drawn_with_the_accent_eases_to_a_new_pick_over_point_32_seconds_frame_by_frame()
    {
        InWindow(reduceMotion: false, (border, frame, frames) =>
        {
            Assert.Equal(C("#4B9FFC"), Drawn(border));
            AccentChoice.Current = "teal"; // #40C8E0 in Dark
            Assert.Equal(C("#4B9FFC"), Drawn(border)); // the frame of the pick still shows the old colour
            var (from, to) = ((75.0, 159.0, 252.0, 1.0), (64.0, 200.0, 224.0, 1.0));
            var previous = 0.0;
            for (var k = 1; k <= 19; k++)
            {
                frame(1000.0 / 60);
                var t = k * 1000.0 / 60;
                var p = CssCurve.At(CssCurve.Ease, t, 320);
                var expected = CssCurve.Mix(from, to, p);
                var drawn = Drawn(border);
                Assert.True(Math.Abs(drawn.R - expected.Item1) <= 1 && Math.Abs(drawn.G - expected.Item2) <= 1 && Math.Abs(drawn.B - expected.Item3) <= 1, $"frame {k} ({t:F1} ms): {drawn}, expected p {p:F3}");
                Assert.True(p - previous <= 0.2, $"frame {k}: a jump of {p - previous:F3}"); // no single-frame jump
                previous = p;
            }
            frame(1000.0 / 60); // 333 ms: done
            Assert.Equal(C("#40C8E0"), Drawn(border));
            frame(1000.0 / 60);
            Assert.Equal(C("#40C8E0"), Drawn(border));
        });
    }

    [Fact]
    [Trait("Decision", "ED-250")]
    public void Under_reduce_motion_a_brush_takes_the_new_accent_at_once()
    {
        InWindow(reduceMotion: true, (border, _, frames) =>
        {
            AccentChoice.Current = "teal";
            Assert.Equal(C("#40C8E0"), Drawn(border));
            ThemeManager.Current = AppTheme.Dracula; // a theme change reaches it too
            Assert.Equal(C("#40C8E0"), Drawn(border));
            AccentChoice.Current = "windows";
            Assert.Equal(C("#BD93F9"), Drawn(border));
        });
    }
}
