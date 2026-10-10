using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.UI.Port.Nth;
using Marrow.UI.Port.Settings;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Nth;

// OD-0136 amendment 2, group A: the voice hardware card's busy marks and plain ProgressBars are drawn by the nth primitives.
[Collection("Headless")]
public sealed class NthSitesATests(HeadlessSession headless)
{
    private static Window Host(Control content)
    {
        var window = new Window { Content = content, Width = 400, Height = 200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return window;
    }

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void The_check_buttons_spinner_is_the_nth_spinner_at_the_designs_11_points() => headless.OnUiThread(() =>
    {
        var button = new VoiceCheckButton("Check my hardware");
        var window = Host(button);
        try
        {
            var spinner = Assert.Single(button.GetVisualDescendants().OfType<NthSpinner>());
            Assert.Equal(11, spinner.Width);
            Assert.Equal(11, spinner.Height);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void A_probe_rows_spinner_is_the_nth_spinner_at_12_points_hidden_until_the_probe_runs() => headless.OnUiThread(() =>
    {
        var row = new VoiceProbeRow("Processor");
        var window = Host(row);
        try
        {
            var spinner = Assert.Single(row.GetVisualDescendants().OfType<NthSpinner>());
            Assert.Equal(new Size(12, 12), spinner.Bounds.Size);
            Assert.Equal(0, spinner.Opacity);
        }
        finally { window.Close(); }
    });

    [Theory]
    [Trait("Decision", "OD-0136")]
    [InlineData(3.0, 3.0)]
    [InlineData(4.0, 4.0)]
    [InlineData(6.0, 4.0)]
    public void A_thin_unknown_length_bar_is_the_nth_strip_at_3_or_4_high(double barHeight, double stripHeight) => headless.OnUiThread(() =>
    {
        var bar = new ProgressBar { IsIndeterminate = true, Width = 160, Height = barHeight };
        var window = Host(bar);
        try
        {
            var strip = Assert.Single(bar.GetVisualDescendants().OfType<NthProgressBar>());
            Assert.True(strip.IsIndeterminate);
            Assert.Equal(stripHeight, strip.Bounds.Height);
            Assert.Empty(bar.GetVisualDescendants().OfType<NthSpinner>());
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void A_wide_unknown_length_bar_is_a_strip_and_a_square_one_is_a_spinner() => headless.OnUiThread(() =>
    {
        var wide = new ProgressBar { IsIndeterminate = true, Width = 120, Height = 16 };
        var square = new ProgressBar { IsIndeterminate = true, Width = 18, Height = 18 };
        var window = Host(new StackPanel { Children = { wide, square } });
        try
        {
            Assert.Single(wide.GetVisualDescendants().OfType<NthProgressBar>());
            Assert.Single(square.GetVisualDescendants().OfType<NthSpinner>());
            Assert.Equal(new Size(18, 18), square.GetVisualDescendants().OfType<NthSpinner>().Single().Bounds.Size);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void A_known_length_bar_jumps_on_first_show_then_springs_to_its_value() => headless.OnUiThread(() =>
    {
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = 25, Width = 200, Height = 4 };
        var window = Host(bar);
        try
        {
            var fill = Assert.Single(bar.GetVisualDescendants().OfType<NthProgressBar>());
            Assert.Equal(.25, fill.Shown, 6);
            bar.Value = 80;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(.8, fill.Value, 6);
            Assert.True(fill.Shown < .8); // it springs: not there yet
        }
        finally { window.Close(); }
    });
}
