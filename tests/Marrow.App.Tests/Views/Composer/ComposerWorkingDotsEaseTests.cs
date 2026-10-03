using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Theme;

namespace Marrow.App.Tests.Views.Composer;

// OD-0088 (ED-253, round 4), SPEC 6.4 "Every use of the effective accent eases .32 s": the working dots wear the accent
// (ComposerWorkingDots.swift:15-16) and a later pick eases them over the same 0.32 s curve (CSS ease) as every other use, on
// the render server: the frame it is sent carries the colour it showed and when the change began, the server blends as it
// samples, asks for frames only while there is something to draw, and the UI thread is never asked for a frame
// (PR-COMPOSER-103).
[Collection("Headless")]
public sealed class ComposerWorkingDotsEaseTests(HeadlessSession headless)
{
    private static object Read(object owner, string name) =>
        owner.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(owner)!;

    private static Color FrameColor(DotFieldControl field) => Assert.IsAssignableFrom<ISolidColorBrush>(Read(Read(field, "LastRenderedFrame"), "Brush")).Color;

    private static void Render(Avalonia.Controls.Window window)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var pixels = window.CaptureRenderedFrame();
    }

    private static int Distance(Color a, Color b) => new[] { Math.Abs(a.R - b.R), Math.Abs(a.G - b.G), Math.Abs(a.B - b.B), Math.Abs(a.A - b.A) }.Max();

    private static double Ease(double seconds) => CssCurve.At(CssCurve.Ease, seconds * 1000, 320);

    private void InWindow(Func<Control> make, Action<Avalonia.Controls.Window, TestRuntime, ManualFrameSource> body) => headless.OnUiThread(() =>
    {
        var (theme, choice) = (ThemeManager.Current, AccentChoice.Current);
        using var runtime = new TestRuntime();
        using var executor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var window = new Avalonia.Controls.Window { Width = 64, Height = 24, Background = Brushes.Black, RequestedThemeVariant = ThemeVariant.Dark };
        try
        {
            ThemeManager.Current = AppTheme.System;
            AccentChoice.Current = "orchid";
            window.Content = make();
            window.Show();
            Render(window);
            body(window, runtime, frames);
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

    [Fact]
    [Trait("Decision", "ED-253")]
    [Trait("Req", "PR-COMPOSER-103")]
    public void A_later_pick_eases_the_dots_on_the_render_server_over_point_32_seconds_with_no_ui_frame() =>
        InWindow(() => new DotFieldControl(Chrome.AccentNSColor, animated: true, sweeps: false), (window, runtime, frames) =>
        {
            var field = (DotFieldControl)window.Content!;
            var orchid = FrameColor(field);
            Assert.Equal(Color.Parse("#E298C6"), orchid);
            AccentChoice.Current = "teal";
            var teal = AccentPalette.Pick("teal")!.Resolve(ThemeVariant.Dark);
            Render(window);
            Assert.Equal(orchid, FrameColor(field)); // the pick's own frame
            var elapsed = 0.0;
            foreach (var at in new[] { 0.05, 0.1, 0.16, 0.24, 0.3 })
            {
                runtime.Advance(TimeSpan.FromSeconds(at - elapsed));
                elapsed = at;
                Render(window);
                var expected = SwiftColor.PremultipliedMix(orchid, teal, Ease(at));
                Assert.True(Distance(expected, FrameColor(field)) <= 2, $"{at:F2} s: {FrameColor(field)}, the curve {expected}");
                Assert.Equal(0, frames.Pending); // never a UI frame
            }
            runtime.Advance(TimeSpan.FromSeconds(0.1));
            Render(window);
            Assert.Equal(teal, FrameColor(field));
            Assert.Equal(0, frames.Pending);
        });

    // A pick during a blend retargets it from what is shown, not from where it began or where it was going.
    [Fact]
    [Trait("Decision", "ED-253")]
    public void A_pick_during_the_blend_eases_on_from_what_the_dots_show() =>
        InWindow(() => new DotFieldControl(Chrome.AccentNSColor, animated: true, sweeps: false), (window, runtime, _) =>
        {
            var field = (DotFieldControl)window.Content!;
            var orchid = FrameColor(field);
            AccentChoice.Current = "teal";
            var teal = AccentPalette.Pick("teal")!.Resolve(ThemeVariant.Dark);
            Render(window);
            runtime.Advance(TimeSpan.FromSeconds(0.1));
            Render(window);
            var shown = FrameColor(field);
            Assert.True(Distance(shown, orchid) > 10 && Distance(shown, teal) > 10, $"mid-blend {shown}");
            AccentChoice.Current = "orchid";
            Render(window);
            Assert.True(Distance(shown, FrameColor(field)) <= 1, $"the second pick's frame {FrameColor(field)}, shown {shown}");
            runtime.Advance(TimeSpan.FromSeconds(0.16));
            Render(window);
            var expected = SwiftColor.PremultipliedMix(shown, orchid, Ease(0.16));
            Assert.True(Distance(expected, FrameColor(field)) <= 2, $"{FrameColor(field)}, the curve from what was shown {expected}");
            runtime.Advance(TimeSpan.FromSeconds(0.2));
            Render(window);
            Assert.Equal(orchid, FrameColor(field));
        });

    // A pick made while the field was away from the window is what it shows when it comes back, at once (nothing of it was on
    // screen to ease from).
    [Fact]
    [Trait("Decision", "ED-253")]
    public void A_field_attached_after_a_pick_shows_the_pick_at_once() =>
        InWindow(() => new DotFieldControl(Chrome.AccentNSColor, animated: true, sweeps: false), (window, runtime, _) =>
        {
            var field = (DotFieldControl)window.Content!;
            window.Content = null;
            Render(window);
            AccentChoice.Current = "teal";
            window.Content = field;
            Render(window);
            Assert.Equal(AccentPalette.Pick("teal")!.Resolve(ThemeVariant.Dark), FrameColor(field));
        });

    // The Windows accent changed (no pick, a theme without its own accent): the dots take it at once, as SwiftColor's own accent
    // brushes do (ED-250); the platform announces it through ColorValuesChanged.
    [Fact]
    [Trait("Decision", "ED-253")]
    public void A_change_of_the_windows_accent_reaches_the_dots() =>
        InWindow(() => new DotFieldControl(Chrome.AccentNSColor, animated: true, sweeps: false), (window, runtime, _) =>
        {
            var field = (DotFieldControl)window.Content!;
            var system = Color.Parse("#0078D7");
            using var accent = SwiftColor.AccentSource.Override(() => system);
            AccentChoice.Current = "windows";
            runtime.Advance(TimeSpan.FromSeconds(0.4));
            Render(window);
            var blue = Chrome.AccentNSColor.Resolve(ThemeVariant.Dark);
            Assert.Equal(blue, FrameColor(field));
            system = Color.Parse("#E81123");
            var settings = Avalonia.Application.Current!.PlatformSettings!;
            var values = settings.GetColorValues();
            var raise = settings.GetType().GetMethod("OnColorValuesChanged", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(raise);
            raise.Invoke(settings, [values]);
            Render(window);
            var red = Chrome.AccentNSColor.Resolve(ThemeVariant.Dark);
            Assert.NotEqual(blue, red);
            Assert.Equal(red, FrameColor(field));
        });

    // Outside a window that turns over (the main window; ED-254 marks only Settings) a change of appearance cuts every accent use,
    // the dots with them: the dots and a plain accent brush beside them take the new appearance's colour in the same frame
    // (the accent-picker final review's M13: easing in every window passed every other test).
    [Fact]
    [Trait("Decision", "ED-253")]
    public void Outside_a_window_that_turns_over_the_dots_cut_with_every_other_accent_use_on_a_change_of_appearance() =>
        InWindow(() => new StackPanel(), (window, runtime, _) =>
        {
            var panel = (StackPanel)window.Content!;
            var plate = new Border { Width = 20, Height = 10 };
            plate.Background = Chrome.AccentNSColor.Brush(plate);
            var field = new DotFieldControl(Chrome.AccentNSColor, animated: true, sweeps: false) { Width = 40, Height = 12 };
            panel.Children.Add(plate);
            panel.Children.Add(field);
            Render(window);
            Assert.Equal(AccentPalette.Pick("orchid")!.Resolve(ThemeVariant.Dark), FrameColor(field));
            window.RequestedThemeVariant = ThemeVariant.Light;
            Render(window);
            var light = AccentPalette.Pick("orchid")!.Resolve(ThemeVariant.Light);
            Assert.Equal(light, Assert.IsAssignableFrom<ISolidColorBrush>(plate.Background).Color);
            Assert.Equal(light, FrameColor(field));
        });

    // A field with nothing left to draw (a sweep, once its wave has passed) asks the render server for frames only while a blend
    // runs.
    [Fact]
    [Trait("Decision", "ED-253")]
    [Trait("Req", "PR-COMPOSER-103")]
    public void The_render_server_is_asked_for_frames_only_while_the_blend_runs() =>
        InWindow(() => new DotFieldControl(Chrome.AccentNSColor, animated: true, sweeps: true), (window, runtime, frames) =>
        {
            var field = (DotFieldControl)window.Content!;
            var handler = Read(field, "CompositionHandler");
            bool Animating() => (bool)Read(handler, "IsAnimating");
            runtime.Advance(TimeSpan.FromSeconds(2));
            Render(window);
            Render(window);
            Assert.False(Animating()); // the wave has passed
            AccentChoice.Current = "teal";
            Render(window);
            Assert.True(Animating());
            runtime.Advance(TimeSpan.FromSeconds(0.2));
            Render(window);
            Assert.True(Animating());
            runtime.Advance(TimeSpan.FromSeconds(0.2));
            Render(window);
            Render(window);
            Assert.False(Animating());
            Assert.Equal(AccentPalette.Pick("teal")!.Resolve(ThemeVariant.Dark), FrameColor(field));
            Assert.Equal(0, frames.Pending);
        });

    // The colour is sent resolved, as an immutable brush: resending it (each pick, each layout) leaves no binding behind that
    // keeps listening to the field.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Decision", "ED-253")]
    public void Each_resend_leaves_no_brush_binding_subscribed_behind(bool animated) =>
        InWindow(() => new DotFieldControl(Chrome.AccentNSColor, animated, sweeps: false), (window, runtime, _) =>
        {
            var field = (DotFieldControl)window.Content!;
            var listeners = typeof(Avalonia.StyledElement).GetField("ActualThemeVariantChanged", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(listeners);
            int Listeners(Control control) => (listeners.GetValue(control) as Delegate)?.GetInvocationList().Length ?? 0;
            var before = Listeners(field);
            foreach (var pick in new[] { "teal", "orchid", "teal", "orchid", "teal" })
            {
                AccentChoice.Current = pick;
                Render(window);
            }
            window.Width = 80;
            Render(window);
            Assert.Equal(before, Listeners(field));
            if (animated) Assert.IsAssignableFrom<IImmutableSolidColorBrush>(Read(Read(field, "LastRenderedFrame"), "Brush"));
            else
            {
                // The still dots (Reduce Motion) take the pick at once, as every accent use does under Reduce Motion.
                var dot = Assert.IsType<Avalonia.Controls.Shapes.Ellipse>(Assert.IsType<Canvas>(field.Layer).Children[0]);
                Assert.Equal(AccentPalette.Pick("teal")!.Resolve(ThemeVariant.Dark), Assert.IsAssignableFrom<ISolidColorBrush>(dot.Fill).Color);
            }
        });
}
