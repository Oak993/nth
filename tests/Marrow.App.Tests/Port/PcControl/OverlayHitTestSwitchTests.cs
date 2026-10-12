// PORT-ONLY: the one switch behind IOverlayHitTest (OD-0145, ED-NEW-pcctl-23, ED-NEW-pcctl-22 item 5; DESIGN.md 10.6):
// region by default, pixel and style flip by name, each mode's input-window styles, shadow window and hook path, and the
// per-pixel mode making no region or flip calls at all.
using Marrow.App.Port.PcControl.Overlay;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.App.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-23")]
public sealed class OverlayHitTestSwitchTests
{
    [Fact]
    public void The_switch_defaults_to_the_region_test_and_flips_by_name()
    {
        Assert.Equal(OverlayHitTestKind.Region, OverlayHitTestSwitch.Default);
        Assert.Equal(OverlayHitTestKind.Region, OverlayHitTestSwitch.Parse(null));
        Assert.Equal(OverlayHitTestKind.Region, OverlayHitTestSwitch.Parse(""));
        Assert.Equal(OverlayHitTestKind.Region, OverlayHitTestSwitch.Parse("something else"));
        Assert.Equal(OverlayHitTestKind.Pixel, OverlayHitTestSwitch.Parse(" Pixel "));
        Assert.Equal(OverlayHitTestKind.StyleFlip, OverlayHitTestSwitch.Parse("flip"));
        Assert.Equal(OverlayHitTestKind.StyleFlip, OverlayHitTestSwitch.Parse("style-flip"));
        Assert.Equal("MARROW_TACTUS_HIT_TEST", OverlayHitTestSwitch.Variable);
    }

    [Fact]
    public void Each_kind_makes_its_own_hit_test()
    {
        var native = new PcFakeBandNative();
        foreach (var kind in new[] { OverlayHitTestKind.Region, OverlayHitTestKind.Pixel, OverlayHitTestKind.StyleFlip })
        {
            using var hit = OverlayHitTests.Create(kind, native);
            Assert.Equal(kind, hit.Kind);
        }
    }

    [Fact]
    public void The_pixel_test_is_a_layered_input_window_that_is_not_click_through_beside_a_shadow_window()
    {
        var native = new PcFakeBandNative();
        var hit = new PixelHitTest(native);
        hit.Attach(0x9, PcTestMonitors.Main.Band);

        hit.Rest(PillShape.Resting(new PillRect(740, 8, 440, 48)));
        hit.Moving(PillShape.Resting(new PillRect(16, 8, 440, 48)));
        hit.Pointer(960, 30, 1);
        hit.Hidden();

        Assert.Equal(OverlayStyles.Layered, hit.InputStyles & (OverlayStyles.Layered | OverlayStyles.Transparent));
        Assert.True(hit.SeparateShadow);
        Assert.False(hit.GrabsInFlight);
        Assert.Equal(new[] { "through 9 False" }, native.Calls);
    }
}
