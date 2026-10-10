using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Sidebar;
using Marrow.UI.Port.Nth;
using Xunit;

namespace Marrow.App.Tests.Nth;

// OD-0136 amendment 2, group A: the sidebar's working marks and the subagent panel's helper box carry the nth spinner
// (step 1 B) in place of MiniSpinner; the ghost's still mark is the calm trace drawn with ordinary rendering.
[Collection("Headless")]
public sealed class NthSitesATests(HeadlessSession headless)
{
    private static Avalonia.Controls.Window Host(Control content)
    {
        var window = new Avalonia.Controls.Window { Content = content, Width = 200, Height = 100 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return window;
    }

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void The_working_mark_is_a_12_point_nth_spinner_named_Working() => headless.OnUiThread(() =>
    {
        var mark = NthWorkingMarks.Working();
        var window = Host(mark);
        try
        {
            Assert.Equal(new Size(12, 12), mark.Bounds.Size);
            Assert.Equal("Working", Avalonia.Automation.AutomationProperties.GetName(mark));
            Assert.Equal(Avalonia.Automation.AccessibilityView.Content, Avalonia.Automation.AutomationProperties.GetAccessibilityView(mark));
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void The_still_mark_is_a_12_point_plain_drawing_with_no_moving_visual() => headless.OnUiThread(() =>
    {
        var mark = NthWorkingMarks.Still();
        var host = new StackPanel { Children = { mark } };
        var window = Host(host);
        try
        {
            Assert.Equal(new Size(12, 12), mark.Bounds.Size);
            Assert.Empty(host.GetVisualDescendants().OfType<NthSpinner>());
            Assert.Equal("Working", Avalonia.Automation.AutomationProperties.GetName(mark));
        }
        finally { window.Close(); }
    });

    private static double Dist(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void The_still_trace_is_a_third_of_the_tile_from_twelve_o_clock()
    {
        var track = NthStillSpinner.Track(12);
        Assert.Equal(NthMotion.TilePoints, track.Length);
        Assert.Equal(0, track[0].X, 3);
        Assert.True(track[0].Y < 0); // 12 o'clock
        double total = 0;
        for (var i = 0; i < track.Length; i++) total += Dist(track[(i + 1) % track.Length], track[i]);
        var line = NthStillSpinner.Lead(track, .3);
        double drawn = 0;
        for (var i = 1; i < line.Count; i++) drawn += Dist(line[i], line[i - 1]);
        Assert.Equal(.3 * total, drawn, 3);
        Assert.Equal(track[0], line[0]);
    }

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void The_helper_working_box_carries_a_12_point_nth_spinner() => headless.OnUiThread(() =>
    {
        var box = new SubagentWorkingBox("Helper", true);
        var window = Host(box);
        try
        {
            var spinner = Assert.Single(box.GetVisualDescendants().OfType<NthSpinner>());
            Assert.Equal(new Size(12, 12), spinner.Bounds.Size);
        }
        finally { window.Close(); }
    });
}
