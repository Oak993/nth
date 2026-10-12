// PORT-ONLY: Tactus's capture-test switch (P30; ED-NEW-pcctl-30; DESIGN.md 10.11 "Native"): only a debug build with
// `--tactus-capture-test` leaves the overlays capturable; every other start hides them from capture as before. The Win32
// calls go to a fake; no window is created.
using Marrow.App.Port.PcControl.Overlay;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-30")]
public sealed class TactusCaptureTestTests
{
    [Fact]
    public void Only_a_debug_build_with_the_flag_leaves_the_overlays_capturable()
    {
        Assert.Equal("--tactus-capture-test", TactusCaptureTest.Flag);
        Assert.False(TactusCaptureTest.From(["Marrow.exe"]));
        Assert.False(TactusCaptureTest.From(["Marrow.exe", "--tactus-capture-tests"]));
#if DEBUG
        Assert.True(TactusCaptureTest.From(["Marrow.exe", "--tactus-capture-test"]));
#else
        Assert.False(TactusCaptureTest.From(["Marrow.exe", "--tactus-capture-test"]));
#endif
        // the test host was not started with it
        Assert.False(TactusCaptureTest.Enabled);

        var capturable = new PcFakeOverlayNative();
        var setup = OverlayWindowSetup.Apply(0x79, OverlayStyles.Extended(OverlayRole.Input), capturable, captureTest: true);
        Assert.False(setup.HiddenFromCapture);
        Assert.Equal(new[] { "style 79", "top 79" }, capturable.Calls);

        var hidden = new PcFakeOverlayNative();
        Assert.True(OverlayWindowSetup.Apply(0x7a, OverlayRole.Input, hidden).HiddenFromCapture);
        Assert.Equal(new[] { "style 7a", "capture 7a", "top 7a" }, hidden.Calls);
    }
}
