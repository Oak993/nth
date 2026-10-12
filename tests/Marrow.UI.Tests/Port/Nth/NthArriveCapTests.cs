using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port.Nth;

// ED-NEW-lean-19 (lean pass L19): NthArrive.Rows lets rise only the rows that can be on screen when they arrive. Rows 0-24 keep the exact
// rise (6 px, 0.36 s, (.2,.8,.2,1), 40 ms apart); a taller window lets rise every row that fits in it at the shortest row pitch and the
// smallest zoom; the rest stand at once in the state the rise ends in, with no transition.
[Collection("Headless")]
public sealed class NthArriveCapTests(HeadlessSession headless)
{
    private static (Window Window, List<Control> Rows) Show(int count, double height)
    {
        var stack = new StackPanel();
        var rows = Enumerable.Range(0, count).Select(_ => (Control)new Border { Height = 34 }).ToList();
        foreach (var row in rows) stack.Children.Add(row);
        var window = new Window { Width = 780, Height = height, Content = new ScrollViewer { Content = stack } };
        window.Show();
        return (window, rows);
    }

    private static void AssertRises(Control row, int index)
    {
        Assert.Equal(0, row.Opacity);
        Assert.Equal(Matrix.CreateTranslation(0, 6), row.RenderTransform!.Value);
        var transitions = row.Transitions!;
        Assert.Equal(2, transitions.Count);
        var opacity = Assert.IsType<DoubleTransition>(transitions[0]);
        Assert.Equal(Visual.OpacityProperty, opacity.Property);
        Assert.Equal(TimeSpan.FromSeconds(.36), opacity.Duration);
        Assert.Equal(TimeSpan.FromSeconds(index * .04), opacity.Delay);
        var spline = Assert.IsType<SplineEasing>(opacity.Easing);
        var reference = new SplineEasing(.2, .8, .2, 1);
        foreach (var t in new[] { 0, .1, .25, .5, .75, .9, 1 }) Assert.Equal(reference.Ease(t), spline.Ease(t));
        var move = Assert.IsType<TransformOperationsTransition>(transitions[1]);
        Assert.Equal(Visual.RenderTransformProperty, move.Property);
        Assert.Equal(TimeSpan.FromSeconds(.36), move.Duration);
        Assert.Equal(TimeSpan.FromSeconds(index * .04), move.Delay);
        Assert.Same(spline, move.Easing);
    }

    private static void AssertStands(Control row)
    {
        Assert.Null(row.Transitions);
        Assert.Equal(1, row.Opacity);
        Assert.Equal(Matrix.Identity, row.RenderTransform!.Value);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-19")]
    public void In_a_Settings_sized_window_rows_0_to_24_rise_exactly_as_before_and_the_rest_stand_at_once() => headless.OnUiThread(() =>
    {
        var (window, rows) = Show(300, 580);
        try
        {
            NthArrive.Rows(rows);
            for (var i = 0; i < 25; i++) AssertRises(rows[i], i);
            for (var i = 25; i < rows.Count; i++) AssertStands(rows[i]);
            Dispatcher.UIThread.RunJobs(); // the next frame moves the risen rows to the state the standing rows already have
            for (var i = 0; i < 25; i++) // the values the rise moves to (its transitions draw the way there)
            {
                Assert.Equal(1, rows[i].GetBaseValue(Visual.OpacityProperty).Value);
                Assert.Equal(Matrix.Identity, rows[i].GetBaseValue(Visual.RenderTransformProperty).Value!.Value);
            }
            for (var i = 25; i < rows.Count; i++) AssertStands(rows[i]);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-19")]
    public void Twenty_five_rows_or_fewer_all_rise() => headless.OnUiThread(() =>
    {
        var (window, rows) = Show(25, 580);
        try
        {
            NthArrive.Rows(rows);
            for (var i = 0; i < rows.Count; i++) AssertRises(rows[i], i);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-19")]
    public void A_window_as_tall_as_a_4K_screen_lets_rise_every_row_that_fits_in_it_at_75_percent_zoom() => headless.OnUiThread(() =>
    {
        var (window, rows) = Show(300, 2160);
        try
        {
            var height = window.ClientSize.Height;
            Assert.True(height > 1000, $"the headless window is {height} tall");
            NthArrive.Rows(rows);
            var fit = (int)Math.Ceiling(height / (35 * .75)); // the composer's 34 px model row and 1 px of spacing, at the smallest zoom
            for (var i = 0; i < fit; i++) AssertRises(rows[i], i);
            Assert.Contains(rows, row => row.Transitions is null); // 300 rows are taller than the window
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-19")]
    public void Rows_outside_any_window_rise_up_to_the_first_25() => headless.OnUiThread(() =>
    {
        var rows = Enumerable.Range(0, 60).Select(_ => (Control)new Border { Height = 34 }).ToList();
        NthArrive.Rows(rows);
        for (var i = 0; i < 25; i++) AssertRises(rows[i], i);
        for (var i = 25; i < rows.Count; i++) AssertStands(rows[i]);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-19")]
    public void Under_reduce_motion_the_rows_past_the_cap_stand_with_no_transform_as_the_calm_fade_ends() => headless.OnUiThread(() =>
    {
        var (window, rows) = Show(60, 580);
        try
        {
            EnvironmentValues.SetAccessibilityReduceMotion(window, true);
            NthArrive.Rows(rows);
            for (var i = 0; i < 25; i++)
            {
                Assert.Null(rows[i].RenderTransform);
                var opacity = Assert.IsType<DoubleTransition>(rows[i].Transitions![0]);
                Assert.Equal(TimeSpan.FromSeconds(.2), opacity.Duration);
                Assert.Equal(TimeSpan.FromSeconds(i * .04), opacity.Delay);
            }
            for (var i = 25; i < rows.Count; i++) { Assert.Null(rows[i].Transitions); Assert.Equal(1, rows[i].Opacity); Assert.Null(rows[i].RenderTransform); }
        }
        finally { window.Close(); }
    });
}
