using Avalonia;
using Marrow.Setup.Shell;
using Marrow.Setup.Tests.Fakes;

namespace Marrow.Setup.Tests.Shell;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3, "Custom caption" and "Close", and 10, "Close and shutdown"; unit U1
// (ED-NEW-inst-u1-1). The window's message hook on a stand-in window: WM_NCHITTEST is answered by hand (also at 150 %),
// the buttons light from the non-client mouse messages and click on release over the same button by posting
// WM_SYSCOMMAND, so the X goes the same way as Alt+F4, the taskbar and the system menu. Maximise never happens. While
// Close is paused, SC_CLOSE (the X, Alt+F4, the taskbar, the system menu) and WM_QUERYENDSESSION (sign-out, shutdown) are
// refused and the Close-paused line brightens; before that, they go through.
public sealed class SetupWindowHooksTests
{
    private const uint NcHitTest = 0x0084, NcMouseMove = 0x00A0, NcButtonDown = 0x00A1, NcButtonUp = 0x00A2, NcDoubleClick = 0x00A3;
    private const uint MouseMove = 0x0200, ButtonUp = 0x0202, SysCommand = 0x0112, CaptureChanged = 0x0215, NcMouseLeave = 0x02A2;
    private const uint QueryEndSession = 0x0011, SettingChange = 0x001A, SysColorChange = 0x0015;
    private const int ScMinimise = 0xF020, ScMaximise = 0xF030, ScClose = 0xF060, ScMove = 0xF010;
    private const int Caption = 2, Minimise = 8, Close = 20;
    private const nint Window = 0x5150;

    private readonly FakeCaptionSurface surface = new();
    private readonly FakeCaptionCalls calls = new();
    private readonly SetupWindowHooks hooks;

    public SetupWindowHooksTests() => hooks = new SetupWindowHooks(surface, calls);

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Hit_testing_is_answered_by_hand_in_screen_pixels()
    {
        Assert.Equal((Caption, true), Send(NcHitTest, 0, Screen(300, 17)));
        Assert.Equal((Minimise, true), Send(NcHitTest, 0, Screen(530, 17)));
        Assert.Equal((Close, true), Send(NcHitTest, 0, Screen(590, 17)));
        Assert.Equal((1, true), Send(NcHitTest, 0, Screen(300, 100)));

        surface.Scale = 1.5;
        Assert.Equal((Close, true), Send(NcHitTest, 0, Screen(590, 17)));
        Assert.Equal((1, true), Send(NcHitTest, 0, Screen(300, 40)));
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Hovering_lights_the_button_under_the_pointer_and_leaving_puts_it_out()
    {
        Send(NcMouseMove, Close, Screen(590, 17));
        Assert.Equal(Close, surface.Hover);
        Assert.Equal(1, calls.Tracks);

        Send(NcMouseMove, Minimise, Screen(530, 17));
        Assert.Equal(Minimise, surface.Hover);
        Assert.Equal(1, calls.Tracks); // asked once until Windows says the pointer has left

        Send(NcMouseMove, Caption, Screen(300, 17));
        Assert.Equal(0, surface.Hover);

        Send(NcMouseLeave, 0, 0);
        Assert.Equal(0, surface.Hover);
        Send(NcMouseMove, Close, Screen(590, 17));
        Assert.Equal(2, calls.Tracks);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_click_on_close_posts_SC_CLOSE_on_release_over_the_button()
    {
        Assert.Equal((0, true), Send(NcButtonDown, Close, Screen(590, 17)));
        Assert.Equal(Close, surface.Pressed);
        Assert.True(calls.Captured);
        Assert.Empty(calls.Commands);

        Assert.Equal((0, true), Send(ButtonUp, 0, Client(590, 17)));

        Assert.Equal(new[] { ScClose }, calls.Commands);
        Assert.False(calls.Captured);
        Assert.Equal(0, surface.Pressed);
        Assert.Equal(Close, surface.Hover);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_click_on_minimise_posts_SC_MINIMIZE_and_dragging_off_cancels_it()
    {
        Send(NcButtonDown, Minimise, Screen(530, 17));
        Send(NcButtonUp, Minimise, Screen(530, 17));
        Assert.Equal(new[] { ScMinimise }, calls.Commands);

        Send(NcButtonDown, Minimise, Screen(530, 17));
        Send(MouseMove, 0, Client(300, 17));
        Assert.Equal(0, surface.Pressed);
        Assert.Equal(0, surface.Hover);
        Send(MouseMove, 0, Client(531, 18));
        Assert.Equal(Minimise, surface.Pressed);
        Send(MouseMove, 0, Client(300, 120));
        Send(ButtonUp, 0, Client(300, 120));
        Assert.Equal(new[] { ScMinimise }, calls.Commands);
        Assert.Equal(0, hooks.Pressed);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Losing_the_capture_drops_the_press()
    {
        Send(NcButtonDown, Close, Screen(590, 17));
        Send(CaptureChanged, 0, 0x777);

        Assert.Equal(0, hooks.Pressed);
        Assert.Equal(0, surface.Pressed);
        Send(ButtonUp, 0, Client(590, 17));
        Assert.Empty(calls.Commands);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Nothing_maximises_the_window()
    {
        Assert.Equal((0, true), Send(SysCommand, ScMaximise, 0));
        Assert.Equal((0, true), Send(SysCommand, ScMaximise | 0x2, 0));
        Assert.Equal((0, true), Send(NcDoubleClick, Caption, Screen(300, 17)));
        Assert.Equal((0, true), Send(NcDoubleClick, Close, Screen(590, 17)));
        Assert.Equal((0, false), Send(SysCommand, ScMove, 0));
        Assert.Empty(calls.Commands);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Close_and_shutdown_go_through_before_anything_has_started()
    {
        Assert.Equal((0, false), Send(SysCommand, ScClose, 0));
        Assert.Equal((0, false), Send(QueryEndSession, 0, 0));
        Assert.Equal(0, surface.Refusals);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void While_paused_SC_CLOSE_and_sign_out_are_refused_and_brighten_the_line()
    {
        surface.ClosePaused = true;

        Assert.Equal((0, true), Send(SysCommand, ScClose, 0)); // the X, Alt+F4, the taskbar and the system menu
        Assert.Equal((0, true), Send(SysCommand, ScClose | 0x3, 0));
        Assert.Equal((0, true), Send(QueryEndSession, 0, 0)); // FALSE: sign-out and shutdown wait

        Assert.Equal(3, surface.Refusals);
        Assert.Equal((0, false), Send(SysCommand, ScMinimise, 0));
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Settings_messages_make_the_window_read_Windows_settings_again()
    {
        Assert.Equal((0, false), Send(SettingChange, 0, 0));
        Send(SysColorChange, 0, 0);

        Assert.Equal(2, surface.SettingsReads);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_mouse_messages_point_is_two_signed_16_bit_halves()
    {
        Assert.Equal(new PixelPoint(-5, -20), SetupWindowHooks.Point(SetupWindowHooks.Pack(-5, -20)));
        Assert.Equal(new PixelPoint(3000, 1600), SetupWindowHooks.Point(SetupWindowHooks.Pack(3000, 1600)));
        Assert.Equal(new PixelPoint(-1920, 400), SetupWindowHooks.Point(SetupWindowHooks.Pack(-1920, 400)));
    }

    private (nint Result, bool Handled) Send(uint message, nint wParam, nint lParam)
    {
        var handled = false;
        var result = hooks.WndProc(Window, message, wParam, lParam, ref handled);
        return (result, handled);
    }

    private nint Screen(double x, double y) =>
        SetupWindowHooks.Pack(surface.Origin.X + (int)Math.Round(x * surface.Scale), surface.Origin.Y + (int)Math.Round(y * surface.Scale));

    private nint Client(double x, double y) => SetupWindowHooks.Pack((int)Math.Round(x * surface.Scale), (int)Math.Round(y * surface.Scale));
}
