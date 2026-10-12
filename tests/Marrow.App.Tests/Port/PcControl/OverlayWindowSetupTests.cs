// PORT-ONLY: the no-activate overlay host (OD-0144, OD-0145, ED-NEW-pcctl-20; DESIGN.md 3.1, 4.1, 10.4 "It never steals
// focus", 10.6): the extended styles per role, the setup that adds them, hides the window from capture and puts it on
// top without activating it, WM_MOUSEACTIVATE answering MA_ACTIVATE only on the composer's field, and the window's
// Avalonia properties. Every Win32 call goes to a fake; no real window is created or styled.
using Avalonia;
using Avalonia.Controls;
using Marrow.App.Port.PcControl.Overlay;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Port.PcControl;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-pcctl-20")]
public sealed class OverlayWindowSetupTests(HeadlessSession headless)
{
    [Fact]
    public void Every_overlay_is_topmost_toolwindow_and_no_activate_and_only_drawing_windows_are_click_through()
    {
        const uint ClickThrough = OverlayStyles.Layered | OverlayStyles.Transparent;
        const uint Base = OverlayStyles.Topmost | OverlayStyles.ToolWindow | OverlayStyles.NoActivate;

        Assert.Equal(Base, OverlayStyles.Extended(OverlayRole.Input));
        Assert.Equal(Base | ClickThrough, OverlayStyles.Extended(OverlayRole.Shadow));
        Assert.Equal(Base | ClickThrough, OverlayStyles.Extended(OverlayRole.Pointer));
        Assert.Equal(0x08000000u, OverlayStyles.NoActivate);
        Assert.Equal(0x11u, OverlayStyles.ExcludeFromCapture);
        Assert.Equal(0x0013u, OverlayStyles.TopmostFlags);
        Assert.Equal((nint)(-1), OverlayStyles.HwndTopmost);
    }

    [Fact]
    public void Setup_adds_the_role_s_styles_hides_the_window_from_capture_and_puts_it_on_top_without_activating_it()
    {
        var native = new PcFakeOverlayNative { Style = 0x100 };

        var setup = OverlayWindowSetup.Apply(0x77, OverlayRole.Pointer, native);

        Assert.True(setup.HiddenFromCapture);
        Assert.Equal(0x100u | OverlayStyles.Extended(OverlayRole.Pointer), native.Style);
        Assert.Equal(new[] { "style 77", "capture 77", "top 77" }, native.Calls);

        var refused = OverlayWindowSetup.Apply(0x78, OverlayRole.Input, new PcFakeOverlayNative { Excludes = false });
        Assert.False(refused.HiddenFromCapture);
    }

    [Fact]
    public void A_click_activates_the_overlay_only_on_the_composer_s_field()
    {
        var onField = new Point(40, 300);
        bool Activates(Point point) => point == onField;

        var handled = false;
        Assert.Equal(OverlayStyles.MaActivate, OverlayWindowSetup.OnMessage(OverlayStyles.MouseActivateMessage, () => onField, Activates, ref handled));
        Assert.True(handled);

        handled = false;
        Assert.Equal(OverlayStyles.MaNoActivate, OverlayWindowSetup.OnMessage(OverlayStyles.MouseActivateMessage, () => new Point(300, 20), Activates, ref handled));
        Assert.True(handled);

        handled = false;
        Assert.Equal(OverlayStyles.MaNoActivate, OverlayWindowSetup.OnMessage(OverlayStyles.MouseActivateMessage, () => null, Activates, ref handled));
        Assert.True(handled);

        handled = false;
        Assert.Equal((nint)0, OverlayWindowSetup.OnMessage(0x0201, () => onField, Activates, ref handled));
        Assert.False(handled);
    }

    [Fact]
    public void The_overlay_window_is_borderless_transparent_topmost_and_shown_without_activation() => headless.OnUiThread(() =>
    {
        var native = new PcFakeOverlayNative();
        var window = new TactusOverlayWindow(OverlayRole.Input, native);

        Assert.Equal(WindowDecorations.None, window.WindowDecorations);
        Assert.False(window.ShowActivated);
        Assert.False(window.ShowInTaskbar);
        Assert.True(window.Topmost);
        Assert.False(window.CanResize);
        Assert.Equal(new[] { WindowTransparencyLevel.Transparent }, window.TransparencyLevelHint);
        Assert.Equal(OverlayRole.Input, window.Role);
        // Without a native handle (headless) nothing is called.
        window.Reassert();
        Assert.Empty(native.Calls);
    });
}
