using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Theme;

namespace Marrow.App.Tests.Views.Composer;

// customization-SPEC CZ-10.4 (CZ-T54), Q-CZ-7: a new colour for the working dots (the setting, the model, the theme) reaches the running field through
// DotFieldControl.Retarget(ease: true): one blend on the render thread over the field's own 0.32 s ease (ED-253), a change during it easing on from what is shown;
// the still dots (Reduce Motion) change at once. Every frame is held to the curve.
[Collection("Headless")]
public sealed class ComposerWorkingDotsTintTests(HeadlessSession headless)
{
    private static readonly SwiftColor Orchid = SwiftColor.Hex("#E298C6"), Teal = SwiftColor.Hex("#40C8E0"), Gold = SwiftColor.Hex("#C89152");

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

    private static Color Dark(SwiftColor color) => color.Resolve(ThemeVariant.Dark);

    private static DotFieldControl FieldOf(ComposerWorkingDots dots) => (DotFieldControl)dots.Child!;

    private void InWindow(bool reduceMotion, Action<Avalonia.Controls.Window, ComposerWorkingDots, TestRuntime, ManualFrameSource> body) => headless.OnUiThread(() =>
    {
        var theme = ThemeManager.Current;
        using var runtime = new TestRuntime();
        using var executor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var window = new Avalonia.Controls.Window { Width = 64, Height = 24, Background = Brushes.Black, RequestedThemeVariant = ThemeVariant.Dark };
        try
        {
            ThemeManager.Current = AppTheme.System;
            var dots = new ComposerWorkingDots { Tint = Orchid };
            if (reduceMotion) EnvironmentValues.SetAccessibilityReduceMotion(dots, true);
            window.Content = dots;
            window.Show();
            Render(window);
            body(window, dots, runtime, frames);
        }
        finally
        {
            window.Content = null;
            window.Close();
            ThemeManager.Current = theme;
            Dispatcher.UIThread.RunJobs();
        }
    });

    [Fact]
    [Trait("Decision", "ED-253")] // CZ-T54: the first frame after the new tint is the colour shown so far; then every frame is on the 0.32 s ease; then the new colour
    public void A_new_tint_blends_the_running_dots_over_point_32_seconds_on_the_curve_in_every_frame() =>
        InWindow(false, (window, dots, runtime, frames) =>
        {
            var field = FieldOf(dots);
            Assert.Equal(Dark(Orchid), FrameColor(field));
            dots.Tint = Teal;
            Render(window);
            Assert.Equal(Dark(Orchid), FrameColor(field)); // the change's own frame
            var tested = 0;
            for (var frame = 1; frame <= 19; frame++) // 60 Hz to 0.317 s: every frame of the blend
            {
                runtime.Advance(TimeSpan.FromSeconds(1.0 / 60));
                Render(window);
                var at = frame / 60.0;
                var expected = SwiftColor.PremultipliedMix(Dark(Orchid), Dark(Teal), Ease(at));
                Assert.True(Distance(expected, FrameColor(field)) <= 2, $"{at:F3} s: {FrameColor(field)}, the curve {expected}");
                Assert.Equal(0, frames.Pending); // never a UI frame
                tested++;
            }
            Assert.Equal(19, tested);
            runtime.Advance(TimeSpan.FromSeconds(0.1));
            Render(window);
            Assert.Equal(Dark(Teal), FrameColor(field));
        });

    [Fact]
    [Trait("Decision", "ED-253")] // CZ-T54: a tint set during the blend eases on from what is shown, over its own 0.32 s
    public void A_tint_during_the_blend_eases_on_from_what_the_dots_show() =>
        InWindow(false, (window, dots, runtime, frames) =>
        {
            var field = FieldOf(dots);
            dots.Tint = Teal;
            Render(window);
            runtime.Advance(TimeSpan.FromSeconds(0.1));
            Render(window);
            var shown = FrameColor(field);
            Assert.True(Distance(shown, Dark(Orchid)) > 10 && Distance(shown, Dark(Teal)) > 10, $"mid-blend {shown}");
            dots.Tint = Gold;
            Render(window);
            Assert.True(Distance(shown, FrameColor(field)) <= 1, $"the second tint's frame {FrameColor(field)}, shown {shown}");
            for (var frame = 1; frame <= 19; frame++)
            {
                runtime.Advance(TimeSpan.FromSeconds(1.0 / 60));
                Render(window);
                var expected = SwiftColor.PremultipliedMix(shown, Dark(Gold), Ease(frame / 60.0));
                Assert.True(Distance(expected, FrameColor(field)) <= 2, $"{frame / 60.0:F3} s after the second tint: {FrameColor(field)}, the curve from what was shown {expected}");
            }
            runtime.Advance(TimeSpan.FromSeconds(0.1));
            Render(window);
            Assert.Equal(Dark(Gold), FrameColor(field));
        });

    [Fact]
    [Trait("Decision", "ED-253")] // CZ-T54: the same colour again starts no blend
    public void The_tint_it_already_wears_starts_no_blend() =>
        InWindow(false, (window, dots, runtime, frames) =>
        {
            var field = FieldOf(dots);
            dots.Tint = Orchid;
            Render(window);
            runtime.Advance(TimeSpan.FromSeconds(0.1));
            Render(window);
            Assert.Equal(Dark(Orchid), FrameColor(field));
            Assert.Null(Read(field, "LastRenderedFrame") is { } frame ? frame.GetType().GetProperty("From")!.GetValue(frame) : null);
        });

    [Fact]
    [Trait("Decision", "ED-253")] // CZ-T54: under Reduce Motion the still dots take the new colour at once, with no frame between
    public void The_still_dots_under_reduce_motion_change_at_once() =>
        InWindow(true, (window, dots, runtime, frames) =>
        {
            var field = FieldOf(dots);
            Color Fill() => Assert.IsAssignableFrom<ISolidColorBrush>(Assert.IsType<Avalonia.Controls.Shapes.Ellipse>(Assert.IsType<Canvas>(field.Layer).Children[0]).Fill).Color;
            Assert.Equal(Dark(Orchid), Fill());
            dots.Tint = Teal;
            Assert.Equal(Dark(Teal), Fill()); // before any frame
            Render(window);
            Assert.Equal(Dark(Teal), Fill());
            dots.Tint = Gold;
            Assert.Equal(Dark(Gold), Fill());
            Assert.Equal(0, frames.Pending);
        });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-10.4: dots that are given no tint wear the accent, as the source draws them
    public void Dots_given_no_tint_wear_the_accent() => headless.OnUiThread(() =>
    {
        var dots = new ComposerWorkingDots();
        Assert.Same(Marrow.UI.Chrome.Chrome.AccentNSColor, dots.Tint);
    });
}
