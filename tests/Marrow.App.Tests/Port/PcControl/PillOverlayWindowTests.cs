// PORT-ONLY: the pill's band windows (OD-0145, ED-NEW-pcctl-23, ED-NEW-pcctl-20; DESIGN.md 10.6): the input window is the
// "Tactus" pane with the hit test's own styles in every mode, the shadow window has the shadow role's styles and is hidden
// from UI Automation, and only the display, DPI and work-area messages count as display changes. Headless; no real
// window is created or styled.
using Avalonia.Automation;
using Marrow.App.Port.PcControl.Overlay;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Port.PcControl;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-pcctl-23")]
public sealed class PillOverlayWindowTests(HeadlessSession headless)
{
    [Fact]
    public void The_input_window_takes_its_hit_test_s_styles_and_is_the_Tactus_pane() => headless.OnUiThread(() =>
    {
        var native = new PcFakeBandNative();
        foreach (var kind in new[] { OverlayHitTestKind.Region, OverlayHitTestKind.Pixel })
        {
            using var hit = OverlayHitTests.Create(kind, native);
            var window = new PillOverlayWindow(hit, new PcFakeOverlayNative());

            Assert.Equal(hit.InputStyles, window.ExtendedStyles);
            Assert.Equal(OverlayRole.Input, window.Role);
            Assert.Equal("Tactus", AutomationProperties.GetName(window));
            Assert.False(window.ShowActivated);
            Assert.False(window.ShowInTaskbar);
            Assert.True(window.Topmost);
            Assert.Same(hit, window.HitTest);
        }
    });

    [Fact]
    public void The_shadow_window_never_takes_input_and_is_hidden_from_UI_Automation() => headless.OnUiThread(() =>
    {
        var window = new ShadowOverlayWindow(new PcFakeOverlayNative());

        Assert.Equal(OverlayStyles.Extended(OverlayRole.Shadow), window.ExtendedStyles);
        Assert.Equal(OverlayStyles.Layered | OverlayStyles.Transparent, window.ExtendedStyles & (OverlayStyles.Layered | OverlayStyles.Transparent));
        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(window));
    });

    [Fact]
    public void Only_display_DPI_and_work_area_messages_are_display_changes()
    {
        Assert.True(PillOverlayWindow.IsDisplayMessage(0x007E, 0));
        Assert.True(PillOverlayWindow.IsDisplayMessage(0x02E0, 0));
        Assert.True(PillOverlayWindow.IsDisplayMessage(0x001A, 0x2F));
        Assert.False(PillOverlayWindow.IsDisplayMessage(0x001A, 0x44));
        Assert.False(PillOverlayWindow.IsDisplayMessage(0x0021, 0));
    }
}
