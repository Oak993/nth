using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// SystemSpinner (PORT-ONLY, proposal ED-229 (3)): a ProgressView() built as an Avalonia ProgressBar without a template is
// drawn as the system busy indicator, turning with the frame clock.
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
    [Trait("Decision", "ED-117")] // the port's frame clock drives the turn
    public void An_indeterminate_progress_view_is_drawn_as_a_spinner_that_turns_with_the_frames() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var scope = FrameClock.Source.Override(frames);
        var bar = new ProgressBar { IsIndeterminate = true, Width = 16, Height = 16 };
        var window = Host(bar);
        try
        {
            var spinner = Assert.Single(bar.GetVisualDescendants().OfType<SystemSpinner>());
            Assert.Equal(new Size(16, 16), spinner.Bounds.Size);
            frames.Advance(TimeSpan.FromSeconds(0.25));
            Assert.Equal(90, spinner.Angle, 3); // a quarter turn in a quarter of SystemSpinner.Period
            frames.Advance(TimeSpan.FromSeconds(0.25));
            Assert.Equal(180, spinner.Angle, 3);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-117")]
    public void With_no_frame_source_bound_the_spinner_stands_still_instead_of_throwing() => headless.OnUiThread(() =>
    {
        var bar = new ProgressBar { IsIndeterminate = true, Width = 16, Height = 16 };
        var window = Host(bar);
        try
        {
            var spinner = Assert.Single(bar.GetVisualDescendants().OfType<SystemSpinner>());
            Assert.Equal(0, spinner.Angle);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-117")]
    public void A_determinate_progress_view_is_a_track_and_fill() => headless.OnUiThread(() =>
    {
        var bar = new ProgressBar { Minimum = 0, Maximum = 10, Value = 4, Width = 120, Height = 8 };
        var window = Host(bar);
        try
        {
            var track = Assert.Single(bar.GetVisualDescendants().OfType<SystemProgressTrack>());
            Assert.Equal(new Size(120, 8), track.Bounds.Size);
        }
        finally { window.Close(); }
    });
}
