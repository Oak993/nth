using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Theme;

namespace Marrow.UI.Tests.Common;

// Source under test: DroppyCode/UI/Common/WorkingIndicators.swift (B2 1fbf81e), :1-345 (R-TEST-7).
// PORT-ONLY surface these tests fix, in src/Marrow.UI/Common/WorkingIndicators.cs, namespace Marrow.UI.Common:
//   WorkingSpinner(cellSize), MiniSpinner(cellSize, isStill): Decorator { Control Child }
//   internal SpinnerLayerView(cells, columns, cellSize) : Control, reads the frame clock seam (F.FrameClock) and
//     the reduce-motion environment property (F.ViewLifetime.EnvironmentValues)
[Collection("Headless")]
public sealed class WorkingIndicatorsHeadlessTests(HeadlessSession headless)
{
    private static Window Show(Control content)
    {
        var window = new Window { Width = 64, Height = 64, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void Frame(ManualTimeProvider time, ManualFrameSource frames, double seconds)
    {
        time.Advance(TimeSpan.FromSeconds(seconds));
        frames.Advance(TimeSpan.FromSeconds(seconds));
        Dispatcher.UIThread.RunJobs();
    }

    // ---- Accessibility and hit testing (UI-THEME-049, UI-THEME-050, PR-THEME-170) ----

    [Fact]
    [Trait("Req", "UI-THEME-049")] // WorkingIndicators.swift:79 .accessibilityHidden(true)
    public void WorkingSpinner_is_hidden_from_assistive_technology() => headless.OnUiThread(() =>
    {
        var spinner = new WorkingSpinner();
        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(spinner));
    });

    [Fact]
    [Trait("Req", "UI-THEME-050")] // WorkingIndicators.swift:110 .accessibilityLabel(Text("Working"))
    public void MiniSpinner_carries_the_accessibility_label_Working_whether_running_or_still() => headless.OnUiThread(() =>
    {
        Assert.Equal("Working", AutomationProperties.GetName(new MiniSpinner()));
        Assert.Equal("Working", AutomationProperties.GetName(new MiniSpinner(isStill: true)));
    });

    [Fact]
    [Trait("Req", "PR-THEME-170")] // WorkingIndicators.swift:208 hitTest(_:) -> NSView? { nil }
    public void The_running_spinners_grid_takes_no_hit_tests() => headless.OnUiThread(() =>
    {
        Assert.False(new WorkingSpinner().Child!.IsHitTestVisible);
        Assert.False(new MiniSpinner().Child!.IsHitTestVisible);
    });

    // ---- Layout (UI-THEME-049, UI-THEME-050) ----

    [Fact]
    [Trait("Req", "UI-THEME-049")]
    public void WorkingSpinner_measures_to_its_161_point_content_size() => headless.OnUiThread(() =>
    {
        var spinner = new WorkingSpinner { UseLayoutRounding = false };
        spinner.Measure(Size.Infinity);
        Assert.Equal(16.1, spinner.DesiredSize.Width, 9);
        Assert.Equal(16.1, spinner.DesiredSize.Height, 9);
    });

    [Fact]
    [Trait("Req", "UI-THEME-050")]
    public void MiniSpinner_measures_to_its_672_by_1104_content_size() => headless.OnUiThread(() =>
    {
        var spinner = new MiniSpinner { UseLayoutRounding = false };
        spinner.Measure(Size.Infinity);
        Assert.Equal(6.72, spinner.DesiredSize.Width, 9);
        Assert.Equal(11.04, spinner.DesiredSize.Height, 9);
    });

    // ---- Retinting from the accent (PR-THEME-162, PR-THEME-169) ----

    [Fact]
    [Trait("Req", "PR-THEME-169")] // WorkingIndicators.swift:252-254 themeDidChange -> applyTints()
    public void A_spinner_retints_when_the_theme_changes() => headless.OnUiThread(() =>
    {
        var saved = ThemeManager.Current;
        using var source = FrameClock.Source.Override(new ManualFrameSource());
        try
        {
            // System keeps the system control accent (no fixed hex); CatppuccinMocha pins its own (PR-THEME-030),
            // so the two are guaranteed to resolve to different tints.
            ThemeManager.Current = AppTheme.System;
            var inner = new SpinnerLayerView(WorkingSpinner.Cells, 3, 3.5);
            Show(inner);
            Assert.Equal(GradientSpin.RowTints(Marrow.UI.Chrome.Chrome.AccentNSColor.Resolve(inner.ActualThemeVariant)), Tints(inner));
            var systemTints = Tints(inner);

            ThemeManager.Current = AppTheme.CatppuccinMocha;
            Dispatcher.UIThread.RunJobs();

            var expected = GradientSpin.RowTints(Marrow.UI.Chrome.Chrome.AccentNSColor.Resolve(inner.ActualThemeVariant));
            Assert.NotEqual(systemTints, Tints(inner));
            Assert.Equal(expected, Tints(inner)); // the retint tracks whichever theme is current when read
        }
        finally
        {
            ThemeManager.Current = saved;
        }
    });

    private static Color[] Tints(SpinnerLayerView view)
    {
        var field = typeof(SpinnerLayerView).GetField("tints",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (Color[])field.GetValue(view)!;
    }

    // ---- The shared-clock pulse (PR-THEME-167, UI-THEME-049) ----

    [Fact]
    [Trait("Req", "PR-THEME-167")] // WorkingIndicators.swift:295-316: one shared clock origin for every spinner
    public void Two_spinners_built_at_different_times_still_read_the_same_absolute_clock() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);

        var first = new SpinnerLayerView(WorkingSpinner.Cells, 3, 3.5);
        Show(first);
        Frame(time, frames, 0.3); // advance the shared clock before the second spinner exists

        var second = new SpinnerLayerView(WorkingSpinner.Cells, 3, 3.5);
        Show(second);

        var phaseNow = time.GetTimestamp() / (double)time.TimestampFrequency / GradientSpin.Period;
        var cell = WorkingSpinner.Cells[0];
        var expected = GradientSpin.Opacity(phaseNow - cell.Lag);

        // Both spinners agree at once, though the second one only just joined: neither restarts its own clock.
        Assert.Equal(expected, OpacityAt(first, cell), 6);
        Assert.Equal(expected, OpacityAt(second, cell), 6);
    });

    [Fact]
    [Trait("Req", "PR-THEME-167")] // WorkingIndicators.swift:296-297: removed before reinstalled, never doubled
    public void The_spinner_keeps_requesting_frames_while_it_is_animated() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var spinner = new SpinnerLayerView(WorkingSpinner.Cells, 3, 3.5);
        Show(spinner);
        Assert.Equal(1, frames.Pending);
        frames.Advance(TimeSpan.FromSeconds(0.1));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, frames.Pending); // exactly one outstanding request at a time
    });

    // ---- Reduce motion freezes the wave (PR-THEME-168) ----

    [Fact]
    [Trait("Req", "PR-THEME-168")] // WorkingIndicators.swift:298-300: no animation, frozen at opacity(-lag)
    public void Reduce_motion_installs_no_animation_and_freezes_every_cell_at_minus_its_own_lag() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var grid = new Panel();
        EnvironmentValues.SetAccessibilityReduceMotion(grid, true);
        var spinner = new SpinnerLayerView(WorkingSpinner.Cells, 3, 3.5);
        grid.Children.Add(spinner);
        Show(grid);

        Assert.Equal(0, frames.Pending); // no frame requested: no animation was installed
        var cell = WorkingSpinner.Cells[1];
        Assert.Equal(GradientSpin.Opacity(-cell.Lag), OpacityAt(spinner, cell), 12);
    });

    [Fact]
    [Trait("Req", "PR-THEME-168")] // WorkingIndicators.swift:296-300: turning reduce motion on stops a running spinner
    public void Turning_reduce_motion_on_mid_flight_removes_the_running_animation() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var grid = new Panel();
        var spinner = new SpinnerLayerView(WorkingSpinner.Cells, 3, 3.5);
        grid.Children.Add(spinner);
        Show(grid);
        Assert.Equal(1, frames.Pending);

        EnvironmentValues.SetAccessibilityReduceMotion(grid, true);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, frames.Pending);
        var cell = WorkingSpinner.Cells[2];
        Assert.Equal(GradientSpin.Opacity(-cell.Lag), OpacityAt(spinner, cell), 12);
    });

    // Reads the opacity SpinnerLayerView is about to paint for one cell, the same way GradientSpin.RowTints/Opacity
    // are combined in Render(): there is no per-cell layer to inspect directly (Avalonia has no CALayer), so this
    // recomputes Render()'s own phase from the same seam the control reads (F.SwiftClock, F.FrameClock).
    private static double OpacityAt(SpinnerLayerView view, SpinnerCell cell)
    {
        var animatedField = typeof(SpinnerLayerView).GetField("animated",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var animated = (bool)animatedField.GetValue(view)!;
        var phase = animated ? SwiftClock.Now.Ticks / (double)TimeSpan.TicksPerSecond / GradientSpin.Period : 0;
        return GradientSpin.Opacity(phase - cell.Lag);
    }

    // ---- The still grid (PR-THEME-171) ----

    [Fact]
    [Trait("Req", "PR-THEME-171")] // WorkingIndicators.swift:101-107: MiniSpinner(isStill: true) hosts the frozen grid
    public void MiniSpinner_isStill_hosts_a_control_distinct_from_the_running_grid() => headless.OnUiThread(() =>
    {
        var running = new MiniSpinner();
        var still = new MiniSpinner(isStill: true);
        Assert.IsType<SpinnerLayerView>(running.Child);
        Assert.IsNotType<SpinnerLayerView>(still.Child);
        Assert.IsAssignableFrom<Control>(still.Child);
    });

    [Fact]
    [Trait("Req", "PR-THEME-171")] // WorkingIndicators.swift:112-138: the still grid measures like the running one
    public void The_still_grids_content_size_matches_the_running_grids() => headless.OnUiThread(() =>
    {
        var still = new MiniSpinner(isStill: true) { UseLayoutRounding = false };
        still.Measure(Size.Infinity);
        Assert.Equal(6.72, still.DesiredSize.Width, 9);
        Assert.Equal(11.04, still.DesiredSize.Height, 9);
    });
}
