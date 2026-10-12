// PORT-ONLY: where the pill rests and where a release sends it (OD-0145, ED-NEW-pcctl-13; DESIGN.md 10.3, 10.11): the three
// anchors on the work area's top edge, the 0.499 s flick projection, the nearest anchor, the monitor under the release (a
// flick never leaves it), the work-area clamp, and the design's 1920 DIP examples.
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.Core.Tests.Port.PcControl.Pill;

[Trait("Decision", "ED-NEW-pcctl-13")]
public sealed class PillAnchorsTests
{
    private static readonly PillArea Main = new(0, 0, 1920, 1032);
    private static readonly PillArea Second = new(1920, -200, 1280, 760);

    [Fact]
    public void Anchors_sit_8_DIP_below_the_work_area_top_and_16_DIP_in_from_its_sides()
    {
        var topTaskbar = new PillArea(0, 48, 1920, 1032);

        Assert.Equal(new PillVector(16, 8), PillAnchors.Position(PillAnchor.Left, Main, 320));
        Assert.Equal(new PillVector(800, 8), PillAnchors.Position(PillAnchor.Centre, Main, 320));
        Assert.Equal(new PillVector(1584, 8), PillAnchors.Position(PillAnchor.Right, Main, 320));
        Assert.Equal(56, PillAnchors.Position(PillAnchor.Centre, topTaskbar, 320).Y);
    }

    [Fact]
    public void The_projection_is_UIKits_0_499_seconds()
    {
        Assert.Equal(0.499, PillAnchors.ProjectionSeconds, 12);
    }

    [Fact]
    public void The_design_examples_on_a_1920_DIP_work_area_with_a_320_DIP_pill()
    {
        Assert.Equal(176, PillAnchors.CentreX(PillAnchor.Left, Main, 320));
        Assert.Equal(960, PillAnchors.CentreX(PillAnchor.Centre, Main, 320));
        Assert.Equal(1744, PillAnchors.CentreX(PillAnchor.Right, Main, 320));
        // Released at 960 moving left at 1,800 DIP/s: projects to 61.8, Left.
        Assert.Equal((0, PillAnchor.Left), PillAnchors.Choose([Main], new PillVector(960, 30), 960, -1_800, 320));
        // Released still at 700: nearest 960, Centre.
        Assert.Equal((0, PillAnchor.Centre), PillAnchors.Choose([Main], new PillVector(700, 30), 700, 0, 320));
        Assert.Equal((0, PillAnchor.Right), PillAnchors.Choose([Main], new PillVector(1400, 30), 1400, 0, 320));
    }

    [Fact]
    public void A_flick_never_throws_the_pill_to_another_monitor_and_is_clamped_to_its_work_area()
    {
        // Released near the right edge of the main monitor, thrown hard right: Right of the main monitor.
        Assert.Equal((0, PillAnchor.Right), PillAnchors.Choose([Main, Second], new PillVector(1800, 20), 1800, 8_000, 320));
        // Released on the second monitor, thrown hard left: Left of the second monitor.
        Assert.Equal((1, PillAnchor.Left), PillAnchors.Choose([Main, Second], new PillVector(2600, 20), 2600, -8_000, 320));
    }

    [Fact]
    public void The_monitor_is_the_one_under_the_release_or_the_nearest()
    {
        Assert.Equal(1, PillAnchors.MonitorAt([Main, Second], new PillVector(2000, -100)));
        Assert.Equal(0, PillAnchors.MonitorAt([Main, Second], new PillVector(500, -40)));
        Assert.Equal(1, PillAnchors.MonitorAt([Main, Second], new PillVector(3300, 100)));
        Assert.Throws<ArgumentException>(() => PillAnchors.MonitorAt([], PillVector.Zero));
    }
}
