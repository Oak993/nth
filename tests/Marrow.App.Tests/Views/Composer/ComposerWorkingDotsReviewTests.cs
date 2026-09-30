using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

[Collection("Headless")]
public sealed class ComposerWorkingDotsReviewTests(HeadlessSession headless)
{
    private static double RenderedOpacity(DotFieldControl control, int row, int column)
    {
        // Inspect the immutable sample actually drawn by the compositor, not the hidden static ellipse tree.
        var frame = control.GetType().GetProperty("LastRenderedFrame", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(control);
        Assert.NotNull(frame);
        return Assert.IsType<double>(frame.GetType().GetMethod("OpacityAt")!.Invoke(frame, [row, column]));
    }
    private static void Render(Avalonia.Controls.Window window)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var pixels = window.CaptureRenderedFrame();
        Assert.NotNull(pixels);
    }
    [Fact]
    [Trait("Req", "PR-COMPOSER-102")] // App/Views/Composer/ComposerWorkingDots.swift:124-148
    [Trait("Req", "PR-COMPOSER-104")] // App/Views/Composer/ComposerWorkingDots.swift:154-156
    [Trait("Req", "UI-COMPOSER-017")] // App/Views/Composer/ComposerWorkingDots.swift:137-148
    public void All_instances_exist_and_rows_fade_up_from_the_bottom_even_without_motion() => headless.OnUiThread(() =>
    {
        var c = new DotFieldControl(new SwiftColor(1, 0, 0), false, false);
        c.Measure(new Size(16, 17.6));
        c.Arrange(new Rect(0, 0, 16, 17.6));
        var dots = Assert.IsType<Canvas>(c.Layer).Children.Cast<Ellipse>().ToArray();
        Assert.Equal(9, dots.Length); // ceil(16/8)+1 columns, ceil(17.6/8) rows
        Assert.Equal(3, Canvas.GetLeft(dots[0]));
        Assert.Equal(19, Canvas.GetLeft(dots[2]));
        Assert.Equal(12.6, Canvas.GetTop(dots[0]), 8);
        Assert.Equal(4.6, Canvas.GetTop(dots[3]), 8);
        Assert.Equal(-3.4, Canvas.GetTop(dots[6]), 8);
        Assert.Equal(0.28, dots[0].Opacity, 8);
        Assert.Equal(0.28 * 2 / 3, dots[3].Opacity, 8);
        Assert.Equal(0.28 / 3, dots[6].Opacity, 8);
        var first = dots[0];
        c.InvalidateArrange();
        c.Arrange(new Rect(0, 0, 16, 17.6));
        Assert.Same(first, Assert.IsType<Canvas>(c.Layer).Children[0]);
    });

    [Fact]
    [Trait("Req", "UI-COMPOSER-017")] // App/Views/Composer/ComposerWorkingDots.swift:17,32-33
    public void Decorations_are_hidden_from_automation_and_hit_testing() => headless.OnUiThread(() =>
    {
        Control[] controls = [new ComposerWorkingDots(), new DotFieldSweep(), new DotFieldControl(new SwiftColor(1), false, false)];
        foreach (var c in controls)
        {
            Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(c));
            Assert.False(c.IsHitTestVisible);
        }
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-103")] // App/Views/Composer/ComposerWorkingDots.swift:142,147,177-190
    public void Pulse_reaches_the_source_keyframes_with_column_and_row_delays_and_stops_when_detached() => headless.OnUiThread(() =>
    {
        using var rt = new TestRuntime();
        using var executor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var frames = new ManualFrameSource();
        using var scope = FrameClock.Source.Override(frames);
        var c = new DotFieldControl(new SwiftColor(1), true, false);
        var w = new Avalonia.Controls.Window { Width = 16, Height = 24, Content = c };
        w.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var dots = Assert.IsType<Canvas>(c.Layer).Children.Cast<Ellipse>().ToArray();
        rt.Advance(TimeSpan.FromSeconds(1.77)); // shared beginTime 1 + 0.35 * 2.2
        frames.Advance(TimeSpan.FromSeconds(1.77));
        Render(w);
        Assert.Equal(0.75, RenderedOpacity(c, 0, 0), 8);
        Assert.InRange(RenderedOpacity(c, 0, 1), 0.08, 0.7499999);
        Assert.InRange(RenderedOpacity(c, 1, 0), 0.08 * 2 / 3, 0.5);
        rt.Advance(TimeSpan.FromSeconds(1.43));
        frames.Advance(TimeSpan.FromSeconds(1.43));
        Render(w);
        Assert.Equal(0.08, RenderedOpacity(c, 0, 0), 8); // 1+2.2, next cycle begins
        c.UpdateAnimated(false);
        Assert.Equal(0.28, dots[0].Opacity, 8);
        Assert.Equal(0.28 * 2 / 3, dots[3].Opacity, 8);
        w.Close();
        Assert.Equal(0, frames.Pending);
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-103")] // App/Views/Composer/ComposerWorkingDots.swift:142-148,158-175
    public void Sweep_is_one_pass_with_022_column_delay_no_row_delay_and_zero_after_completion() => headless.OnUiThread(() =>
    {
        using var rt = new TestRuntime();
        using var executor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var frames = new ManualFrameSource();
        using var scope = FrameClock.Source.Override(frames);
        var c = new DotFieldControl(new SwiftColor(1), true, true);
        var w = new Avalonia.Controls.Window { Width = 16, Height = 24, Content = c };
        w.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var dots = Assert.IsType<Canvas>(c.Layer).Children.Cast<Ellipse>().ToArray();
        rt.Advance(TimeSpan.FromSeconds(0.32)); // .4 * .8
        frames.Advance(TimeSpan.FromSeconds(0.32));
        Render(w);
        Assert.Equal(0.7, RenderedOpacity(c, 0, 0), 8);
        Assert.Equal(RenderedOpacity(c, 0, 0), RenderedOpacity(c, 1, 0));
        Assert.True(RenderedOpacity(c, 0, 1) < RenderedOpacity(c, 0, 0));
        rt.Advance(TimeSpan.FromSeconds(0.524)); // .8 + 2*.022, last column completes
        frames.Advance(TimeSpan.FromSeconds(0.524));
        Render(w);
        for (var row = 0; row < 3; row++)
            for (var column = 0; column < 3; column++) Assert.Equal(0, RenderedOpacity(c, row, column), 8);
        c.UpdateAnimated(false);
        Assert.All(dots, dot => Assert.Equal(0, dot.Opacity));
        w.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-105")] // App/Views/Composer/ComposerWorkingDots.swift:104-111,118-122
    public void Effective_theme_and_color_changes_re_resolve_existing_dots() => headless.OnUiThread(() =>
    {
        var color = SwiftColor.Dynamic(new SwiftColor(1, 0, 0), new SwiftColor(0, 0, 1));
        var c = new DotFieldControl(color, false, false);
        var w = new Avalonia.Controls.Window { Width = 16, Height = 24, Content = c, RequestedThemeVariant = ThemeVariant.Light };
        w.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var dot = Assert.IsType<Ellipse>(Assert.IsType<Canvas>(c.Layer).Children[0]);
        Assert.Equal(Colors.Red, Assert.IsAssignableFrom<ISolidColorBrush>(dot.Fill).Color);
        w.RequestedThemeVariant = ThemeVariant.Dark;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(Colors.Blue, Assert.IsAssignableFrom<ISolidColorBrush>(dot.Fill).Color);
        c.UpdateColor(new SwiftColor(0, 1, 0));
        Assert.Same(dot, Assert.IsType<Canvas>(c.Layer).Children[0]);
        Assert.Equal(Colors.Lime, Assert.IsAssignableFrom<ISolidColorBrush>(dot.Fill).Color);
        w.Close();
    });
}
