using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// PORT-ONLY (Port/StyledMeasure.cs): NSHostingView(rootView:).fittingSize measures a hosted view. A detached Avalonia
// control has had no styles or control templates applied, so a Button measures empty until it is in a window.
[Collection("Headless")]
public sealed class StyledMeasureTests(HeadlessSession headless)
{
    private static readonly Size Infinite = new(double.PositiveInfinity, double.PositiveInfinity);

    private static Button MakeButton(string text) => new() { Content = new TextBlock { Text = text } };

    // What a control measures once it is shown in a window of its own.
    private static Size ShownSize(Func<Control> make, Size available)
    {
        var control = make();
        var window = new Window { Width = 800, Height = 600, Content = control };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        control.Measure(available);
        var size = control.DesiredSize;
        window.Close();
        return size;
    }

    [Fact]
    [Trait("Rule", "R-VIEW-1")]
    public void A_detached_control_measures_as_it_does_in_a_window() => headless.OnUiThread(() =>
    {
        var detached = MakeButton("A button label");
        detached.Measure(Infinite);
        var inWindow = ShownSize(() => MakeButton("A button label"), Infinite);
        Assert.True(inWindow.Height > 0 && inWindow.Width > detached.DesiredSize.Width,
            $"the fixture shows the defect only if a detached button measures short: {detached.DesiredSize} detached, {inWindow} in a window");

        Assert.Equal(inWindow, StyledMeasure.Of(MakeButton("A button label"), Infinite));
    });

    [Fact]
    [Trait("Rule", "R-VIEW-1")]
    public void The_width_it_is_offered_is_the_width_it_is_measured_at() => headless.OnUiThread(() =>
    {
        Control Make() => new StackPanel
        {
            Children = { MakeButton("One"), new TextBlock { Text = string.Join(" ", Enumerable.Repeat("wrapping words", 20)), TextWrapping = Avalonia.Media.TextWrapping.Wrap } },
        };
        var available = new Size(200, double.PositiveInfinity);
        Assert.Equal(ShownSize(Make, available), StyledMeasure.Of(Make(), available));
    });

    [Fact]
    [Trait("Rule", "R-VIEW-1")]
    public void The_control_is_left_without_a_parent_and_goes_on_to_a_window_as_measured() => headless.OnUiThread(() =>
    {
        var control = MakeButton("Moves on");
        var measured = StyledMeasure.Of(control, Infinite);

        Assert.Null(control.Parent);
        Assert.Null(TopLevel.GetTopLevel(control));
        var window = new Window { Width = 800, Height = 600, Content = control };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        control.Measure(Infinite);
        Assert.Equal(measured, control.DesiredSize);
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-1")]
    public void Controls_measured_one_after_another_each_get_their_own_size() => headless.OnUiThread(() =>
    {
        var first = StyledMeasure.Of(MakeButton("Short"), Infinite);
        var second = StyledMeasure.Of(MakeButton("A much longer label than the first"), Infinite);
        var third = StyledMeasure.Of(MakeButton("Short"), Infinite);

        Assert.True(second.Width > first.Width);
        Assert.Equal(first, third);
    });

    [Fact]
    [Trait("Rule", "R-VIEW-1")]
    public void A_control_already_in_a_window_is_measured_where_it_is() => headless.OnUiThread(() =>
    {
        var button = MakeButton("Already here");
        var window = new Window { Width = 800, Height = 600, Content = button };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var measured = StyledMeasure.Of(button, Infinite);

        Assert.Same(window, TopLevel.GetTopLevel(button));
        button.Measure(Infinite);
        Assert.Equal(button.DesiredSize, measured);
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-1")]
    public void A_control_with_a_parent_but_no_window_is_measured_in_place() => headless.OnUiThread(() =>
    {
        var button = MakeButton("Held by a panel");
        var panel = new StackPanel { Children = { button } };

        var measured = StyledMeasure.Of(button, Infinite);

        Assert.Same(panel, button.Parent);
        button.Measure(Infinite);
        Assert.Equal(button.DesiredSize, measured);
    });

    [Fact]
    [Trait("Rule", "R-VIEW-1")]
    public void A_control_that_measures_another_while_it_is_attached_leaves_both_measured() => headless.OnUiThread(() =>
    {
        var inner = MakeButton("Inner");
        var innerSize = default(Size);
        var outer = MakeButton("Outer");
        outer.AttachedToVisualTree += (_, _) => innerSize = StyledMeasure.Of(inner, Infinite);

        var outerSize = StyledMeasure.Of(outer, Infinite);

        Assert.True(outerSize.Width > 0);
        Assert.Equal(inner.DesiredSize, innerSize);
        Assert.Null(outer.Parent);
    });
}
