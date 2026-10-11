using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Window;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window;

// Source: DroppyCode/App/Window/WindowManager.swift (B3 52bc792), :93-103 and :118-140.
// These tests use real user32 windows and synchronous SendMessage calls. They never start Marrow or its executable;
// the Avalonia controls remain test-owned headless windows while the Win32 message loop is real Windows user32.
[Collection("Headless")]
public sealed class WindowManagerHighRiskTests(HeadlessSession headless)
{
    private const uint WM_NCLBUTTONDOWN = 0x00A1;
    private const uint WM_NCLBUTTONUP = 0x00A2;
    private const uint WM_ENTERSIZEMOVE = 0x0231;
    private const uint WM_SIZING = 0x0214;
    private const uint WM_EXITSIZEMOVE = 0x0232;
    private const nint HTMINBUTTON = 8;
    private const nint HTMAXBUTTON = 9;
    private const nint HTCLOSE = 20;

    [Fact]
    [Trait("Req", "PR-WIN-017")] // WindowManager.swift:118-140; Windows path WM_ENTERSIZEMOVE/WM_SIZING/WM_EXITSIZEMOVE
    public void A_real_user32_window_delivers_the_resize_loop_to_its_WindowLiveResize_state()
    {
        headless.OnUiThread(() =>
        {
            var avaloniaWindow = new AvaloniaWindow();
            var liveResize = new WindowLiveResize(avaloniaWindow);
            using var native = new NativeMessageWindow((nint hwnd, uint msg, nint wParam, nint lParam, ref bool handled) =>
                liveResize.WndProc(hwnd, msg, wParam, lParam, ref handled));

            NativeMessageWindow.Send(native.Handle, WM_SIZING);
            Assert.False(liveResize.IsActive); // a move/size step outside the loop is ignored
            NativeMessageWindow.Send(native.Handle, WM_ENTERSIZEMOVE);
            NativeMessageWindow.Send(native.Handle, WM_SIZING);
            Assert.True(liveResize.IsActive);
            NativeMessageWindow.Send(native.Handle, WM_EXITSIZEMOVE);
            Assert.False(liveResize.IsActive);
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-007")] // WindowManager.swift:93-101; ED-063 caption controls
    [Trait("Req", "PR-WIN-008")] // WindowManager.swift:98-100; retained window actions
    public void Real_user32_nonclient_button_messages_drive_close_minimise_and_maximise_and_keep_Settings_zoom_inert()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness(withRuntime: false);
            var manager = new WindowManager
            {
                MainContent = () => new Border(),
                SettingsContent = () => new Border(),
            };
            try
            {
                manager.ShowMain();
                var main = Assert.IsAssignableFrom<AvaloniaWindow>(manager.MainWindow);
                Assert.NotNull(typeof(WindowManager).GetMethod("HandleNativeWindowMessage", BindingFlags.Instance | BindingFlags.NonPublic));
                using var native = new NativeMessageWindow((nint hwnd, uint msg, nint wParam, nint lParam, ref bool handled) =>
                    DispatchCaptionMessage(manager, main, hwnd, msg, wParam, lParam, ref handled));

                NativeMessageWindow.Send(native.Handle, WM_NCLBUTTONDOWN, HTMINBUTTON);
                NativeMessageWindow.Send(native.Handle, WM_NCLBUTTONUP, HTMINBUTTON);
                NativeMessageWindow.Pump(native.Handle);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(WindowState.Minimized, main.WindowState);

                main.WindowState = WindowState.Normal;
                NativeMessageWindow.Send(native.Handle, WM_NCLBUTTONDOWN, HTMAXBUTTON);
                NativeMessageWindow.Send(native.Handle, WM_NCLBUTTONUP, HTMAXBUTTON);
                NativeMessageWindow.Pump(native.Handle);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(WindowState.Maximized, main.WindowState);

                NativeMessageWindow.Send(native.Handle, WM_NCLBUTTONDOWN, HTMAXBUTTON);
                NativeMessageWindow.Send(native.Handle, WM_NCLBUTTONUP, HTMAXBUTTON);
                NativeMessageWindow.Pump(native.Handle);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(WindowState.Normal, main.WindowState);

                // OD-0140: Settings has no window of its own (a panel in this window), so there is no Settings caption to maximise.

                NativeMessageWindow.Send(native.Handle, WM_NCLBUTTONDOWN, HTCLOSE);
                NativeMessageWindow.Send(native.Handle, WM_NCLBUTTONUP, HTCLOSE);
                NativeMessageWindow.Pump(native.Handle);
                Dispatcher.UIThread.RunJobs();
                Assert.False(main.IsVisible);
            }
            finally
            {
                manager.MainWindow?.Hide();
                manager.SettingsOverlay?.Close();
            }
        });
    }

    private static nint DispatchCaptionMessage(
        WindowManager manager,
        AvaloniaWindow window,
        nint hwnd,
        uint msg,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        var method = typeof(WindowManager).GetMethod("HandleNativeWindowMessage", BindingFlags.Instance | BindingFlags.NonPublic);
        if (method is null) return 0; // the outer assertion reports the red test before any native callback runs
        var arguments = new object?[] { window, hwnd, msg, wParam, lParam, handled };
        var result = Assert.IsType<nint>(method!.Invoke(manager, arguments));
        handled = Assert.IsType<bool>(arguments[5]);
        return result;
    }

}
