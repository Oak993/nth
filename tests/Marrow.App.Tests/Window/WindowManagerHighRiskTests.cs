using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
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
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(WindowState.Minimized, main.WindowState);

                main.WindowState = WindowState.Normal;
                NativeMessageWindow.Send(native.Handle, WM_NCLBUTTONDOWN, HTMAXBUTTON);
                NativeMessageWindow.Send(native.Handle, WM_NCLBUTTONUP, HTMAXBUTTON);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(WindowState.Maximized, main.WindowState);

                NativeMessageWindow.Send(native.Handle, WM_NCLBUTTONDOWN, HTMAXBUTTON);
                NativeMessageWindow.Send(native.Handle, WM_NCLBUTTONUP, HTMAXBUTTON);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(WindowState.Normal, main.WindowState);

                manager.ShowSettings();
                var settings = Assert.IsType<SecondaryWindow>(typeof(WindowManager)
                    .GetField("settingsWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager));
                using var settingsNative = new NativeMessageWindow((nint hwnd, uint msg, nint wParam, nint lParam, ref bool handled) =>
                    DispatchCaptionMessage(manager, settings, hwnd, msg, wParam, lParam, ref handled));
                NativeMessageWindow.Send(settingsNative.Handle, WM_NCLBUTTONDOWN, HTMAXBUTTON);
                NativeMessageWindow.Send(settingsNative.Handle, WM_NCLBUTTONUP, HTMAXBUTTON);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(WindowState.Normal, settings.WindowState); // :81 resizable: false, so zoom is inert

                NativeMessageWindow.Send(native.Handle, WM_NCLBUTTONDOWN, HTCLOSE);
                NativeMessageWindow.Send(native.Handle, WM_NCLBUTTONUP, HTCLOSE);
                Dispatcher.UIThread.RunJobs();
                Assert.False(main.IsVisible);
            }
            finally
            {
                manager.MainWindow?.Hide();
                if (typeof(WindowManager).GetField("settingsWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager) is AvaloniaWindow settings)
                    settings.Hide();
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
        var arguments = new object?[] { hwnd, msg, wParam, lParam, handled };
        var result = Assert.IsType<nint>(method!.Invoke(manager, arguments));
        handled = Assert.IsType<bool>(arguments[4]);
        _ = window;
        return result;
    }

    internal sealed class NativeMessageWindow : IDisposable
    {
        private const int ERROR_CLASS_ALREADY_EXISTS = 1410;
        private static readonly nint HWND_MESSAGE = -3;
        private readonly string className = "MarrowWindowManagerHighRisk_" + Guid.NewGuid().ToString("N");
        private readonly NativeWindowProc callback;
        private readonly NativeHandler handler;
        private readonly ushort atom;

        internal NativeMessageWindow(NativeHandler handler)
        {
            this.handler = handler;
            callback = WindowProc;
            var klass = new WndClassEx
            {
                Size = (uint)Marshal.SizeOf<WndClassEx>(),
                WindowProc = callback,
                Instance = GetModuleHandle(null),
                ClassName = className,
            };
            atom = RegisterClassEx(ref klass);
            if (atom == 0 && Marshal.GetLastWin32Error() != ERROR_CLASS_ALREADY_EXISTS)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            Handle = CreateWindowEx(0, className, className, 0, 0, 0, 1, 1, HWND_MESSAGE, 0, klass.Instance, 0);
            if (Handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        public nint Handle { get; }

        public void Dispose()
        {
            DestroyWindow(Handle);
            if (atom != 0) UnregisterClass(className, GetModuleHandle(null));
            GC.KeepAlive(callback);
            GC.KeepAlive(handler);
        }

        public static void Send(nint hwnd, uint message, nint wParam = 0, nint lParam = 0) =>
            SendMessage(hwnd, message, wParam, lParam);

        private nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam)
        {
            var handled = false;
            var result = handler(hwnd, message, wParam, lParam, ref handled);
            return handled ? result : DefWindowProc(hwnd, message, wParam, lParam);
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate nint NativeWindowProc(nint hwnd, uint message, nint wParam, nint lParam);

        internal delegate nint NativeHandler(nint hwnd, uint message, nint wParam, nint lParam, ref bool handled);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WndClassEx
        {
            public uint Size;
            public uint Style;
            public NativeWindowProc WindowProc;
            public int ClassExtra;
            public int WindowExtra;
            public nint Instance;
            public nint Icon;
            public nint Cursor;
            public nint Background;
            public string? MenuName;
            public string ClassName;
            public nint SmallIcon;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassEx(ref WndClassEx windowClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint CreateWindowEx(uint extendedStyle, string className, string windowName, uint style,
            int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

        [DllImport("user32.dll", SetLastError = true)] private static extern bool DestroyWindow(nint hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool UnregisterClass(string className, nint instance);
        [DllImport("user32.dll")] private static extern nint DefWindowProc(nint hwnd, uint message, nint wParam, nint lParam);
        [DllImport("user32.dll", SetLastError = true)] private static extern nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? moduleName);
    }
}
