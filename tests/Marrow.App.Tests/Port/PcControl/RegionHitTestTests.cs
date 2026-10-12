// PORT-ONLY: the region hit test, the default (OD-0145, ED-NEW-pcctl-23, ED-NEW-pcctl-22 item 5; DESIGN.md 10.6 "Hit
// testing" 1): the region is the resting shape inflated 2 DIP in the monitor's physical pixels, set only at rest and at a
// state change, empty while the shape moves and when hidden, never set twice for the same shape, and held back while a
// mouse button is down in another app. Every call goes to a fake.
using Avalonia;
using Marrow.App.Port.PcControl.Overlay;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.App.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-23")]
public sealed class RegionHitTestTests
{
    private const nint Window = 0x42;

    private static PillShape Rest => PillShape.Resting(new PillRect(740, 56, 440, 48));

    private static PillShape Travel => PillShape.Moving(PillTravel.Start(0, new PillVector(16, 56), new PillVector(740, 56), PillVector.Zero, false), null, 440, null, 48);

    [Fact]
    public void At_rest_the_region_is_the_shape_inflated_2_DIP_in_the_monitor_s_pixels()
    {
        var native = new PcFakeBandNative();
        var hit = new RegionHitTest(native);
        hit.Attach(Window, PcTestMonitors.Right.Band);

        hit.Rest(Rest);

        Assert.Equal(new PixelRect(1107, 9, 666, 78), hit.Region);
        Assert.Equal(new[] { "region 42 empty", "region 42 1107,9,666,78 r78" }, native.Calls);
    }

    [Fact]
    public void While_the_shape_moves_or_hides_the_region_is_empty_and_the_same_region_is_never_set_twice()
    {
        var native = new PcFakeBandNative();
        var hit = new RegionHitTest(native);
        hit.Attach(Window, PcTestMonitors.Main.Band);
        hit.Rest(Rest);

        hit.Moving(Travel);
        hit.Moving(Travel);
        hit.Rest(Rest);
        hit.Rest(Rest);
        hit.Hidden();

        Assert.Null(hit.Region);
        Assert.Equal(new[] { "region 42 empty", "region 42 738,54,444,52 r52", "region 42 empty", "region 42 738,54,444,52 r52", "region 42 empty" }, native.Calls);
    }

    [Fact]
    public void Nothing_changes_while_a_button_is_held_in_another_app_and_the_newest_change_lands_at_its_release()
    {
        var native = new PcFakeBandNative();
        var hit = new RegionHitTest(native);
        hit.Attach(Window, PcTestMonitors.Main.Band);
        hit.Rest(Rest);
        native.Calls.Clear();

        hit.ButtonHeldElsewhere(true);
        hit.Moving(Travel);
        hit.Rest(PillShape.Resting(new PillRect(16, 8, 440, 48)));
        Assert.Empty(native.Calls);

        hit.ButtonHeldElsewhere(false);

        Assert.Equal(new[] { "region 42 14,6,444,52 r52" }, native.Calls);
    }

    [Fact]
    public void A_display_change_recomputes_the_resting_region_in_the_new_pixels()
    {
        var native = new PcFakeBandNative();
        var hit = new RegionHitTest(native);
        hit.Attach(Window, PcTestMonitors.Main.Band);
        hit.Rest(PillShape.Resting(new PillRect(740, 56, 440, 48)));

        hit.Moved(PcTestMonitors.Right.Band);

        Assert.Equal(new PixelRect(1107, 9, 666, 78), hit.Region);
    }

    [Fact]
    public void The_region_test_keeps_the_input_window_unlayered_beside_a_shadow_window_and_grabs_in_flight()
    {
        var hit = new RegionHitTest(new PcFakeBandNative());

        Assert.Equal(OverlayHitTestKind.Region, hit.Kind);
        Assert.Equal(0u, hit.InputStyles & (OverlayStyles.Layered | OverlayStyles.Transparent));
        Assert.Equal(OverlayStyles.Extended(OverlayRole.Input), hit.InputStyles);
        Assert.True(hit.SeparateShadow);
        Assert.True(hit.GrabsInFlight);
    }
}
