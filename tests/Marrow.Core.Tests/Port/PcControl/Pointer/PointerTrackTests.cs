// PORT-ONLY: one app's Tactus pointer (OD-0144, OD-0145, ED-NEW-pcctl-21; DESIGN.md 3.1, 4.5, 10.1; owner's motion rule:
// designed poses, no fades): it arrives by growing out of its tip and leaves by shrinking into it, at full opacity; it
// glides between targets and its press ripple starts when it lands; a hidden target parks it ("working behind"); another
// monitor is a leave and an arrival, never a glide across; paused it dims to 40 % in 200 ms, wears its badge and holds
// still until Resume; it glows during a borrow; Reduce Motion fades instead of moving; its window is the rest box or a
// glide's span, set once per glide, and lives 1 s past the leave.
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Core.Port.PcControl.Pointer;

namespace Marrow.Core.Tests.Port.PcControl.Pointer;

[Trait("Decision", "ED-NEW-pcctl-21")]
public sealed class PointerTrackTests
{
    private static readonly PillVector Cell = new(400, 300);
    private static readonly PillVector Chart = new(900, 520);

    [Fact]
    public void It_arrives_by_growing_out_of_its_tip_at_full_opacity_and_never_passes_full_size()
    {
        var track = new PointerTrack();

        track.Aim(1, 0, Cell);

        Assert.Equal(PointerPhase.Shown, track.Phase(1));
        var last = -1.0;
        for (var ms = 0; ms <= 600; ms += 5)
        {
            var pose = track.Pose(1 + (ms / 1000.0));
            Assert.Equal(Cell, pose.Tip);
            Assert.Equal(1, pose.Opacity);
            Assert.True(pose.Scale >= last && pose.Scale <= 1, $"scale {pose.Scale} at {ms} ms");
            last = pose.Scale;
        }
        Assert.Equal(0, track.Pose(1).Scale);
        Assert.Equal(1, track.Pose(2).Scale);
        Assert.False(track.Moving(2));
    }

    [Fact]
    public void It_leaves_by_shrinking_into_its_tip_in_180_ms_and_its_window_lives_1_s_more()
    {
        var track = Shown(out var at);

        track.Hide(at);

        Assert.Equal(PointerPhase.Leaving, track.Phase(at + 0.1));
        Assert.Equal(1, track.Pose(at + 0.1).Opacity);
        Assert.InRange(track.Pose(at + 0.1).Scale, 0.01, 0.99);
        Assert.True(track.Pose(at + 0.05).Scale > track.Pose(at + 0.1).Scale);
        Assert.Equal(PointerPhase.Hidden, track.Phase(at + PointerTrack.LeaveSeconds));
        Assert.Equal(PointerPose.None, track.Pose(at + 0.2));
        Assert.True(track.NeedsWindow(at + PointerTrack.LeaveSeconds + 0.99));
        Assert.False(track.NeedsWindow(at + PointerTrack.LeaveSeconds + 1));
        Assert.False(track.Parked);
    }

    [Fact]
    public void It_glides_to_the_next_target_and_the_press_ripple_starts_when_it_lands()
    {
        var track = Shown(out var at);

        track.Aim(at, 0, Chart);
        var ripple = track.Press(at);

        var lands = PointerGlide.Start(at, Cell, Chart, PillVector.Zero).LandsAt();
        Assert.Equal(lands, ripple, 9);
        Assert.False(track.Pose(ripple - 0.001).Rippling);
        var mid = track.Pose(at + 0.1).Tip;
        Assert.True(mid.X > Cell.X && mid.X < Chart.X);
        var start = track.Pose(ripple);
        Assert.True(start.Rippling);
        Assert.Equal(PointerTrack.RippleFrom, start.RippleScale, 6);
        Assert.Equal(1, start.RippleOpacity, 6);
        var half = track.Pose(ripple + 0.2);
        Assert.InRange(half.RippleScale, 0.31, 0.99);
        Assert.InRange(half.RippleOpacity, 0.01, 0.99);
        Assert.False(track.Pose(ripple + PointerTrack.RippleSeconds).Rippling);
        Assert.True(track.Moving(ripple + 0.3));
        Assert.Equal(Chart, track.Pose(ripple + 1).Tip);
        Assert.False(track.Moving(ripple + 1));

        // At rest, a press ripples at once.
        Assert.Equal(ripple + 1, track.Press(ripple + 1), 9);
    }

    [Fact]
    public void A_target_the_user_cannot_see_parks_it_on_the_row_until_it_is_aimed_at_a_visible_point()
    {
        var track = Shown(out var at);

        track.Park(at);

        Assert.True(track.Parked);
        Assert.Equal(PointerPhase.Hidden, track.Phase(at + 0.2));
        track.Aim(at + 0.5, 0, Chart);
        Assert.False(track.Parked);
        Assert.Equal(Chart, track.Pose(at + 0.5).Tip);
        Assert.Equal(0, track.Pose(at + 0.5).Scale);
    }

    [Fact]
    public void Another_monitor_is_a_leave_here_and_an_arrival_there_never_a_glide_across()
    {
        var track = Shown(out var at);

        track.Aim(at, 1, new PillVector(200, 150));

        Assert.Equal(0, track.Monitor);
        Assert.Equal(Cell, track.Pose(at + 0.1).Tip);
        var there = at + PointerTrack.LeaveSeconds;
        Assert.Equal(PointerPhase.Shown, track.Phase(there));
        Assert.Equal(1, track.Monitor);
        Assert.Equal(new PillVector(200, 150), track.Pose(there).Tip);
        Assert.Equal(0, track.Pose(there).Scale);
        Assert.Equal(1, track.Pose(there + 1).Scale);
    }

    [Fact]
    public void Aimed_again_while_leaving_it_grows_back_from_where_it_is_without_a_jump()
    {
        var track = Shown(out var at);
        track.Hide(at);
        var before = track.Pose(at + 0.09).Scale;

        track.Aim(at + 0.09, 0, Chart);

        Assert.Equal(before, track.Pose(at + 0.09).Scale, 9);
        Assert.Equal(Cell, track.Pose(at + 0.09).Tip);
        Assert.Equal(1, track.Pose(at + 1.5).Scale);
        Assert.Equal(Chart, track.Pose(at + 1.5).Tip);
    }

    [Fact]
    public void Paused_it_dims_to_40_percent_in_200_ms_wears_its_badge_and_holds_still_until_Resume()
    {
        var track = Shown(out var at);

        track.Pause(at, true);
        track.Aim(at + 0.05, 0, Chart);

        Assert.InRange(track.Pose(at + 0.1).Opacity, 0.41, 0.99);
        Assert.Equal(PointerTrack.PausedOpacity, track.Pose(at + PointerTrack.PauseSeconds).Opacity, 9);
        Assert.Equal(Cell, track.Pose(at + 1).Tip);
        var badge = Enumerable.Range(0, 100).Select(ms => track.Pose(at + (ms / 100.0)).PauseBadge).ToArray();
        Assert.All(badge, b => Assert.InRange(b, 0, 1));
        Assert.Equal(1, track.Pose(at + 1).PauseBadge);

        track.Pause(at + 2, false);
        Assert.True(track.Pose(at + 2.1).Tip.X > Cell.X);
        Assert.Equal(Chart, track.Pose(at + 3).Tip);
        Assert.Equal(1, track.Pose(at + 3).Opacity);
        Assert.Equal(0, track.Pose(at + 3).PauseBadge);
    }

    [Fact]
    public void It_glows_while_it_borrows_the_real_mouse()
    {
        var track = Shown(out var at);

        track.Borrow(at, true);

        Assert.InRange(track.Pose(at + 0.07).Glow, 0.01, 0.99);
        Assert.Equal(1, track.Pose(at + PointerTrack.GlowSeconds).Glow);
        track.Borrow(at + 1, false);
        Assert.Equal(0, track.Pose(at + 1 + PointerTrack.GlowSeconds).Glow);
    }

    [Fact]
    public void Reduce_Motion_fades_it_in_and_through_a_move_and_its_ripple_only_fades()
    {
        var track = new PointerTrack(reduceMotion: true);
        track.Aim(1, 0, Cell);

        Assert.Equal(1, track.Pose(1.05).Scale);
        Assert.InRange(track.Pose(1.05).Opacity, 0.01, 0.99);
        Assert.Equal(1, track.Pose(1 + PointerTrack.ReducedSeconds).Opacity);

        track.Aim(2, 0, Chart);
        Assert.Equal(Cell, track.Pose(2.05).Tip);
        Assert.Equal(Chart, track.Pose(2.08).Tip);
        Assert.Equal(0, track.Pose(2.075).Opacity, 6);
        Assert.Equal(1, track.Pose(2 + PointerTrack.ReducedSeconds).Opacity);

        var ripple = track.Press(3);
        Assert.Equal(1, track.Pose(ripple + 0.2).RippleScale);
        Assert.InRange(track.Pose(ripple + 0.2).RippleOpacity, 0.01, 0.99);

        track.Pause(4, true);
        Assert.Equal(1, track.Pose(4).PauseBadge);
    }

    [Fact]
    public void Its_window_is_the_rest_box_or_the_glide_s_span_set_once_per_glide()
    {
        var track = Shown(out var at);

        Assert.Equal(Box(Cell), track.WindowRect(at));

        track.Aim(at, 0, Chart);
        var span = track.WindowRect(at);
        Assert.True(span.X <= Box(Cell).X && span.Y <= Box(Cell).Y && span.Right >= Box(Chart).Right && span.Bottom >= Box(Chart).Bottom);
        for (var ms = 0; ms < 200; ms += 16) Assert.Equal(span, track.WindowRect(at + (ms / 1000.0)));
        Assert.Equal(Box(Chart), track.WindowRect(at + 0.5));
    }

    private static PillRect Box(PillVector tip) =>
        new(tip.X + PointerTrack.Extent.X, tip.Y + PointerTrack.Extent.Y, PointerTrack.Extent.Width, PointerTrack.Extent.Height);

    // A pointer that arrived at the cell and rests there; at is a moment after it settled.
    private static PointerTrack Shown(out double at)
    {
        var track = new PointerTrack();
        track.Aim(1, 0, Cell);
        at = 3;
        Assert.False(track.Moving(at));
        return track;
    }
}
