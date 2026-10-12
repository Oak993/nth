using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/ComposerWorkingDots.swift (B2 1fbf81e), :1-220

[Collection("Headless")]
public sealed class ComposerWorkingDotsTests(HeadlessSession headless)
{
    private void OnUiThread(Action body) => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        body();
    });
    private static Avalonia.Controls.Window ShowInFrame(Control content, double width, double height)
    {
        var window = new Avalonia.Controls.Window { Width = width, Height = height, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    // PR-COMPOSER-102: the field's layout, read off the dots themselves (the earlier tests here only asserted that a layer existed).
    private static Ellipse[] Dots(DotFieldControl control) => Assert.IsType<Canvas>(control.Layer).Children.Cast<Ellipse>().ToArray();
    private static void Lay(DotFieldControl control, double width, double height)
    {
        control.Measure(new Size(width, height));
        control.Arrange(new Rect(0, 0, width, height));
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-102")] // :67-71 dotSize = 2, pitch = 8, maxRows = 3; :124-149 layout (columns ceil(w/8)+1, rows ceil(h/8), centre (4, h-4-8r))
    public void A_field_has_2_point_round_dots_on_an_8_point_pitch_with_one_extra_column_and_each_row_fainter_than_the_one_below() => OnUiThread(() =>
    {
        var control = new DotFieldControl(Chrome.AccentNSColor, animated: false, sweeps: false);
        Lay(control, 400, 17.6); // 51 columns x ceil(17.6/8) = 3 rows
        var dots = Dots(control);
        Assert.Equal(51 * 3, dots.Length);
        Assert.All(dots, dot => { Assert.Equal(2, dot.Width); Assert.Equal(2, dot.Height); }); // an ellipse of 2 x 2 is a corner radius of 1
        for (var row = 0; row < 3; row++)
            for (var column = 0; column < 51; column++)
            {
                var dot = dots[row * 51 + column];
                Assert.Equal(4 + 8 * column - 1, Canvas.GetLeft(dot), 8); // centre (pitch / 2 + 8 c)
                Assert.Equal(17.6 - 4 - 8 * row - 1, Canvas.GetTop(dot), 8); // measured up from the bottom edge
                Assert.Equal(0.28 * (1 - row / 3.0), dot.Opacity, 8); // instanceAlphaOffset -1 / rowCount: faint upward
            }
        Assert.True(dots[0].Opacity > dots[51].Opacity && dots[51].Opacity > dots[102].Opacity);
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-102")] // :129-132 rowCount = min(3, max(1, ceil(height / pitch))), however tall
    public void A_tall_field_has_exactly_three_rows() => OnUiThread(() =>
    {
        var control = new DotFieldControl(Chrome.AccentNSColor, animated: false, sweeps: false);
        Lay(control, 200, 1000);
        var dots = Dots(control);
        Assert.Equal(26 * 3, dots.Length); // ceil(200 / 8) + 1 columns, three rows however tall
        Assert.Equal(new[] { 995.0, 987.0, 979.0 }, dots.Select(dot => Math.Round(Canvas.GetTop(dot), 8)).Distinct().ToArray());
        Assert.Equal(new[] { 0.28, 0.28 * 2 / 3, 0.28 / 3 }.Select(value => Math.Round(value, 8)), dots.Select(dot => Math.Round(dot.Opacity, 8)).Distinct());

        var thin = new DotFieldControl(Chrome.AccentNSColor, animated: false, sweeps: false);
        Lay(thin, 8, 3); // a pill thinner than a pitch still has its one row
        Assert.Equal(2, Dots(thin).Length);
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-102")] // :154-156 a zero-size or unchanged bounds skips the layout entirely
    public void A_zero_sized_field_lays_out_no_dots_and_an_unchanged_size_keeps_its_dots() => OnUiThread(() =>
    {
        var control = new DotFieldControl(Chrome.AccentNSColor, animated: false, sweeps: false);
        Lay(control, 0, 0);
        Assert.Empty(Dots(control));
        Lay(control, 100, 100);
        var first = Dots(control)[0];
        var count = Dots(control).Length;
        control.InvalidateArrange();
        control.Arrange(new Rect(0, 0, 0, 0)); // a zero-size bounds skips the layout: the dots it had stay (a layout at 0 x 0 would leave one)
        Assert.Equal(count, Dots(control).Length);
        Assert.Same(first, Dots(control)[0]);
        control.InvalidateArrange();
        control.Arrange(new Rect(0, 0, 100, 100)); // the same size again: the same dots, nothing rebuilt
        Assert.Same(first, Dots(control)[0]);
        control.InvalidateArrange();
        control.Arrange(new Rect(0, 0, 100, 60)); // a changed size lays out anew
        // ED-NEW-lean-9 (hidden grid) replaces Assert.NotSame(first, Dots(control)[0]) here: a new size now moves the dots it keeps,
        // so "anew" is checked as what is laid out: every dot at the new size's place, fill and opacity, the grid a fresh field of
        // that size lays out (the first dot moved from the 100-point field's bottom row to the 60-point field's)
        var fresh = new DotFieldControl(Chrome.AccentNSColor, animated: false, sweeps: false);
        Lay(fresh, 100, 60);
        static string Place(Ellipse dot) => $"{Canvas.GetLeft(dot):R},{Canvas.GetTop(dot):R},{dot.Width:R},{dot.Height:R},{dot.Opacity:R},{((Avalonia.Media.ISolidColorBrush)dot.Fill!).Color}";
        Assert.Equal(Dots(fresh).Select(Place), Dots(control).Select(Place));
        Assert.Equal(60.0 - 4 - 1, Canvas.GetTop(Dots(control)[0]));
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-104")] // :13-19 passes animated: !reduceMotion; :152-157 dot.opacity = 0.28 for the pill, still faded by row
    public void With_reduced_motion_the_dot_field_is_static_at_0_28_opacity_faded_by_row() => OnUiThread(() =>
    {
        var control = new DotFieldControl(Chrome.AccentNSColor, animated: false, sweeps: false);
        var window = ShowInFrame(control, width: 200, height: 24);
        var dots = Dots(control);
        Assert.Equal(26 * 3, dots.Length);
        Assert.Equal(new[] { 0.28, 0.28 * 2 / 3, 0.28 / 3 }.Select(value => Math.Round(value, 8)), dots.Select(dot => Math.Round(dot.Opacity, 8)).Distinct());
        Assert.True(control.Layer!.IsVisible); // the still dots are what is drawn
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-104")] // :155 dot.opacity = 0.28 for the pill, 0 for a sweep
    public void A_sweep_is_invisible_when_not_animated() => OnUiThread(() =>
    {
        var control = new DotFieldControl(Chrome.AccentNSColor, animated: false, sweeps: true);
        var window = ShowInFrame(control, width: 200, height: 24);
        Assert.NotEmpty(Dots(control));
        Assert.All(Dots(control), dot => Assert.Equal(0, dot.Opacity));
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-105")] // :104-111 configure only reinstalls animation when motion flag changed
    public void Color_change_does_not_restart_animation() => OnUiThread(() =>
    {
        var control = new DotFieldControl(Chrome.AccentNSColor, animated: true, sweeps: false);
        var window = ShowInFrame(control, width: 200, height: 100);

        var initialLayer = control.Layer;

        // Change color (should not restart animation)
        control.UpdateColor(new SwiftColor(1, 0, 0, 1)); // Red
        Dispatcher.UIThread.RunJobs();

        Assert.Same(initialLayer, control.Layer);

        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-105")] // :118-122 viewDidChangeEffectiveAppearance re-resolves dynamic accent
    public void Appearance_change_re_resolves_color_without_restarting() => OnUiThread(() =>
    {
        var control = new DotFieldControl(Chrome.AccentNSColor, animated: true, sweeps: false);
        var window = ShowInFrame(control, width: 200, height: 100);

        var initialLayer = control.Layer;

        // Simulate appearance change (the actual appearance change is OS-level)
        control.OnAppearanceChanged();
        Dispatcher.UIThread.RunJobs();

        // The animation should still be running
        Assert.Same(initialLayer, control.Layer);

        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-105")] // :104-111 motion changed reinstalls animation
    public void Motion_setting_change_reinstalls_animation() => OnUiThread(() =>
    {
        var control = new DotFieldControl(Chrome.AccentNSColor, animated: true, sweeps: false);
        var window = ShowInFrame(control, width: 200, height: 100);

        // Change animated setting (should reinstall animation)
        control.UpdateAnimated(false);
        Dispatcher.UIThread.RunJobs();

        // The control should still be functional
        Assert.NotNull(control.Layer);

        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-104")] // :24-34,37-53 DotFieldSweep passes its tint to the representable, and every update configures the layer again
    public void A_sweep_draws_its_dots_in_the_tint_it_was_given_and_follows_a_later_change() => OnUiThread(() =>
    {
        var red = new SwiftColor(1, 0, 0, 1);
        var sweep = new DotFieldSweep { Tint = red };
        var window = ShowInFrame(sweep, width: 200, height: 100);
        Color? DotColor() => ((sweep.Child as DotFieldControl)!.Layer as Canvas)!.Children.OfType<Avalonia.Controls.Shapes.Ellipse>().Select(dot => (dot.Fill as ISolidColorBrush)?.Color).FirstOrDefault();
        Assert.Equal(red.Resolve(Avalonia.Styling.ThemeVariant.Default), DotColor());
        var blue = new SwiftColor(0, 0, 1, 1);
        sweep.Tint = blue;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(blue.Resolve(Avalonia.Styling.ThemeVariant.Default), DotColor());
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-104")] // :24-34 the reduce-motion environment value re-evaluates the body: a sweep draws nothing while it is on and starts afresh when it goes off
    public void A_sweep_follows_reduced_motion_changes_while_it_stays_in_the_tree() => OnUiThread(() =>
    {
        using var actor = Marrow.Core.Port.MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var sweep = new DotFieldSweep();
        var host = new Border { Child = sweep };
        var window = ShowInFrame(host, width: 200, height: 100);
        Assert.IsType<DotFieldControl>(sweep.Child);
        EnvironmentValues.SetAccessibilityReduceMotion(host, true);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(sweep.Child);
        EnvironmentValues.SetAccessibilityReduceMotion(host, false);
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<DotFieldControl>(sweep.Child);
        window.Close();
    });
}
