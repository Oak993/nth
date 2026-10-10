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
// SystemSpinner class (the macOS spinner, OD-0042 "Mac look", OD-0045: "Apple macOS 27 UI Kit", Progress Indicators / Circular / Indeterminate, 2 Sm and 3 Rg,
// SOURCE FACT (Apple design resource), read 2026-10-02) is tested directly while it exists.
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
            Assert.Empty(bar.GetVisualDescendants().OfType<SystemSpinner>());
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
            Assert.Empty(bar.GetVisualDescendants().OfType<SystemProgressTrack>());
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-117")] // the port's frame clock drives the turn (the class itself; no bar draws it any more)
    public void The_system_spinner_class_still_turns_with_the_frames_and_stands_still_with_none() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var scope = FrameClock.Source.Override(frames);
        var spinner = new SystemSpinner { Width = 16, Height = 16 };
        var window = Host(spinner);
        try
        {
            Assert.Equal(0, spinner.Lead);
            frames.Advance(TimeSpan.FromSeconds(0.1));
            Assert.Equal(0, spinner.Lead);
            frames.Advance(TimeSpan.FromSeconds(0.05));
            Assert.Equal(1, spinner.Lead);
            frames.Advance(TimeSpan.FromSeconds(0.25));
            Assert.Equal(3, spinner.Lead);
            frames.Advance(TimeSpan.FromSeconds(0.625));
            Assert.Equal(0, spinner.Lead); // a full turn later it starts over
        }
        finally { window.Close(); }
        var bare = new SystemSpinner { Width = 16, Height = 16 };
        var other = Host(bare); // no frame source bound: it stands still instead of throwing
        try { Assert.Equal(0, bare.Lead); }
        finally { other.Close(); }
    });

    [Theory]
    [InlineData(16.0, 3.0, 8.0, 2.0)]
    [InlineData(32.0, 6.0, 16.0, 4.0)]
    [Trait("Decision", "ED-231")]
    public void The_spokes_are_capsules_scaled_from_the_kits_32_point_spinner(double size, double inner, double outer, double width)
    {
        // 3 Rg: eight 4 x 10 capsules spanning 6 to 16 from the centre; 2 Sm: exactly half (2 x 5, 3 to 8).
        var spoke = SystemSpinner.Spoke(size);
        Assert.Equal(inner, spoke.Inner, 6);
        Assert.Equal(outer, spoke.Outer, 6);
        Assert.Equal(width, spoke.Width, 6);
    }

    [Theory]
    [InlineData(0, new[] { 0.55, 0.06, 0.13, 0.20, 0.27, 0.34, 0.41, 0.48 })]
    [InlineData(3, new[] { 0.34, 0.41, 0.48, 0.55, 0.06, 0.13, 0.20, 0.27 })]
    [Trait("Decision", "ED-231")]
    public void The_lead_spoke_is_brightest_and_the_trail_fades_by_seven_hundredths_a_spoke(int lead, double[] opacities)
    {
        // Spokes from 12 o'clock clockwise, lead at 12: 0.55, then 0.06, 0.13 ... 0.48.
        for (var spoke = 0; spoke < 8; spoke++) Assert.Equal(opacities[spoke], SystemSpinner.SpokeOpacity(spoke, lead), 6);
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    [Trait("Decision", "ED-231")]
    public void The_spokes_are_white_in_dark_and_black_in_light_not_the_accent(string variant) => headless.OnUiThread(() =>
    {
        var theme = variant == "Dark" ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
        var bar = new SystemSpinner { Width = 16, Height = 16 };
        var window = new Window { Content = bar, Width = 200, Height = 200, RequestedThemeVariant = theme };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var spinner = bar;
            var expected = variant == "Dark" ? Avalonia.Media.Colors.White : Avalonia.Media.Colors.Black;
            Assert.Equal(expected, spinner.SpokeColor);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-231")]
    public void Each_spoke_is_drawn_eleven_points_out_at_its_own_opacity_with_the_lead_at_twelve_o_clock() => headless.OnUiThread(() =>
    {
        // A 32-point spinner, white on black (Dark): a spoke's centre (11 out) reads as its opacity, the middle stays black.
        var bar = new SystemSpinner { Width = 32, Height = 32, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Window { Content = bar, Width = 64, Height = 64, Background = Avalonia.Media.Brushes.Black, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
            using var buffer = frame.Lock();
            double Level(double x, double y)
            {
                var row = new byte[buffer.RowBytes];
                System.Runtime.InteropServices.Marshal.Copy(buffer.Address + (int)Math.Floor(y) * buffer.RowBytes, row, 0, buffer.RowBytes);
                var at = (int)Math.Floor(x) * 4;
                return Math.Max(row[at], Math.Max(row[at + 1], row[at + 2])) / 255.0;
            }
            Assert.Equal(0, Level(16, 16), 2);
            for (var spoke = 0; spoke < 8; spoke++)
            {
                var angle = spoke * Math.PI / 4;
                var level = Level(16 + 11 * Math.Sin(angle), 16 - 11 * Math.Cos(angle));
                Assert.True(Math.Abs(level - SystemSpinner.SpokeOpacity(spoke, 0)) < 0.04, $"spoke {spoke}: drawn {level:F3}, kit {SystemSpinner.SpokeOpacity(spoke, 0):F2}");
            }
        }
        finally { window.Close(); }
    });
}
