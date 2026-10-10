using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// A ProgressView() built as an Avalonia ProgressBar without a template is drawn by the nth primitives (OD-0136 amendment 2: NthSpinner, NthProgressBar). The older
// SystemSpinner class (the macOS spinner, OD-0042 / OD-0045) had no site left and was removed (ED-NEW-nth-motion).
[Collection("Headless")]
public sealed class SystemSpinnerTests(HeadlessSession headless)
{
    private static Window Host(Control content)
    {
        var window = new Window { Content = content, Width = 200, Height = 200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return window;
    }

    [Fact]
    [Trait("Decision", "ED-117")]
    public void An_indeterminate_progress_view_is_drawn_as_the_nth_spinner_at_the_bars_own_size() => headless.OnUiThread(() =>
    {
        // OD-0136 amendment 2, step 1 B: the bar's drawing is the nth tile trace, not the system spokes
        var bar = new ProgressBar { IsIndeterminate = true, Width = 16, Height = 16 };
        var window = Host(bar);
        try
        {
            var spinner = Assert.Single(bar.GetVisualDescendants().OfType<NthSpinner>());
            Assert.Equal(new Size(16, 16), spinner.Bounds.Size);
            Assert.Empty(bar.GetVisualDescendants().OfType<NthProgressBar>());
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(10.0)]
    [InlineData(12.0)]
    [InlineData(18.0)]
    [Trait("Decision", "ED-117")]
    public void The_nth_spinner_keeps_the_size_the_site_gave_the_bar(double size) => headless.OnUiThread(() =>
    {
        var bar = new ProgressBar { IsIndeterminate = true, Width = size, Height = size };
        var window = Host(new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { bar } });
        try
        {
            var spinner = Assert.Single(bar.GetVisualDescendants().OfType<NthSpinner>());
            Assert.Equal(new Size(size, size), spinner.Bounds.Size);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-117")]
    public void A_spinner_given_no_size_is_the_small_16_point_spinner() => headless.OnUiThread(() =>
    {
        // ChatView.cs:2040 and TimelineRows.cs:2535 build their ProgressView with no size: it must still be drawn.
        var bar = new ProgressBar { IsIndeterminate = true, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = Host(new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { bar } });
        try
        {
            var spinner = Assert.Single(bar.GetVisualDescendants().OfType<NthSpinner>());
            Assert.Equal(new Size(16, 16), spinner.Bounds.Size);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-117")]
    public void An_indeterminate_strip_is_the_nth_bar_growing_and_catching_up_not_a_spinner() => headless.OnUiThread(() =>
    {
        // step 3 B: a bar that is a strip (thin, or much wider than tall) keeps that shape
        var bar = new ProgressBar { IsIndeterminate = true, Width = 120, Height = 4 };
        var window = Host(bar);
        try
        {
            var strip = Assert.Single(bar.GetVisualDescendants().OfType<NthProgressBar>());
            Assert.True(strip.IsIndeterminate);
            Assert.Equal(4, strip.Bounds.Height);
            Assert.Equal(120, strip.Bounds.Width);
            Assert.Empty(bar.GetVisualDescendants().OfType<NthSpinner>());
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-117")]
    public void A_bar_switched_between_busy_and_determinate_redraws_as_the_other() => headless.OnUiThread(() =>
    {
        var bar = new ProgressBar { IsIndeterminate = true, Width = 120, Height = 16 };
        var window = Host(bar);
        try
        {
            Assert.Single(bar.GetVisualDescendants().OfType<NthProgressBar>(), strip => strip.IsIndeterminate);
            bar.IsIndeterminate = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(Assert.Single(bar.GetVisualDescendants().OfType<NthProgressBar>()).IsIndeterminate);
            bar.IsIndeterminate = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(Assert.Single(bar.GetVisualDescendants().OfType<NthProgressBar>()).IsIndeterminate);
            // a round bar switched the same way
            var round = new ProgressBar { IsIndeterminate = true, Width = 16, Height = 16 };
            window.Content = round;
            Dispatcher.UIThread.RunJobs();
            Assert.Single(round.GetVisualDescendants().OfType<NthSpinner>());
            round.IsIndeterminate = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(round.GetVisualDescendants().OfType<NthSpinner>());
            Assert.Single(round.GetVisualDescendants().OfType<NthProgressBar>());
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-117")]
    public void A_determinate_progress_view_is_the_nth_bar_following_its_value() => headless.OnUiThread(() =>
    {
        var bar = new ProgressBar { Minimum = 0, Maximum = 10, Value = 4, Width = 120, Height = 8 };
        var window = Host(bar);
        try
        {
            var fill = Assert.Single(bar.GetVisualDescendants().OfType<NthProgressBar>());
            Assert.Equal(new Size(120, 4), fill.Bounds.Size);
            Assert.False(fill.IsIndeterminate);
            Assert.Equal(.4, fill.Value, 6);
            Assert.Equal(.4, fill.Shown, 6); // shown at once the first time
            bar.Value = 7.5;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(.75, fill.Value, 6);
            bar.Maximum = 15;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(.5, fill.Value, 6);
        }
        finally { window.Close(); }
    });
}
