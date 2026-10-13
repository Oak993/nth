using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Xunit;

namespace Marrow.App.Tests.Views.Chat;

// ED-NEW-perf-motion-8: the rows' visibility report (ThreadTimeline.ReportVisibility, :335-338) runs on every layout pass of the window; it walks
// the rows only when the offset, the extent, the viewport or the rows drawn changed, and then reports what a walk of every row reports.
[Collection("Headless")]
public sealed class ThreadTimelineVisibilityBudgetTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-NEW-perfmotion-8")]
    public void A_layout_pass_that_moves_no_row_walks_none_and_a_scroll_reports_every_row_it_shows() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.SetEntries(TimelineMount.Turns(40, sentences: 6, unique: true));
        mount.Settle();
        var reports = mount.View.VisibilityReports;
        for (var pass = 0; pass < 5; pass++)
        {
            mount.Host.InvalidateMeasure(); // a layout pass of the window that moves no row
            mount.Window.UpdateLayout();
        }
        Assert.Equal(reports, mount.View.VisibilityReports);

        var end = mount.Scroll.Extent.Height - mount.Scroll.Viewport.Height;
        Assert.True(end > 1000, $"the thread is {end} points past one screen");
        foreach (var offset in new[] { end / 3, end / 2, 0 })
        {
            mount.Scroll.Offset = new Vector(0, offset);
            mount.Advance(200);
            Assert.True(mount.View.VisibilityReports > reports, "a scroll reports");
            reports = mount.View.VisibilityReports;
            Assert.Equal(Walked(mount), Visible(mount));
        }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-gateprep-5")]
    public void A_cleared_visible_set_is_reported_again_by_the_next_layout_pass_though_no_row_moved() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.SetEntries(TimelineMount.Turns(8, sentences: 8).ToList());
        mount.Settle();
        var tracking = mount.Field<TimelineScrollTracking>("tracking");
        Assert.NotEmpty(Visible(mount));
        // The memo of ED-NEW-perf-motion-8 names the offset, extent, viewport and rows drawn; the tracking's visible set is a fifth input, and clearing it moves none of the four.
        tracking.ClearVisible();
        Assert.Empty(Visible(mount));
        mount.Host.InvalidateMeasure();
        mount.Window.UpdateLayout();
        Assert.NotEmpty(Visible(mount));
        Assert.Equal(Walked(mount), Visible(mount));
    });

    // What a walk of every row reports: the rows with at least 0.001 of their area in the viewport.
    private static HashSet<string> Walked(TimelineMount mount)
    {
        var scroll = mount.Scroll;
        var shown = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, row) in mount.Field<Dictionary<string, Control>>("laidOutRows"))
        {
            if (row.TranslatePoint(default, scroll) is not { } at || row.Bounds.Width <= 0 || row.Bounds.Height <= 0) continue;
            var rect = new Rect(at, row.Bounds.Size);
            var inside = rect.Intersect(new Rect(scroll.Viewport));
            if (inside.Width * inside.Height / (rect.Width * rect.Height) >= 0.001) shown.Add(id);
        }
        return shown;
    }

    private static HashSet<string> Visible(TimelineMount mount) =>
        new((HashSet<string>)typeof(TimelineScrollTracking).GetField("visibleIDs", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(mount.Field<TimelineScrollTracking>("tracking"))!, StringComparer.Ordinal);
}
