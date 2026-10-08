// PORT-ONLY RR-37: the GPU-only padded HWND is click-through; a non-drawing owned input HWND
// covers exactly the inner client. Forward its native input to Avalonia's original dispatcher.
using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Port;

internal sealed class SettingsShellInput : IDisposable
{
    private readonly HostWindow window;
    private readonly SettingsShell shell;
    private readonly nint visual;
    private readonly SettingsInputProcedure procedure;
    private readonly string className = "Marrow.SettingsInput." + Guid.NewGuid().ToString("N");
    private readonly nint module;
    private readonly SettingsShellAutomation automation;
    private bool disposed;
    private bool dropRegistered, requestedVisible;
    internal nint Handle { get; }

    internal SettingsShellInput(HostWindow window, SettingsShell shell)
    {
        this.window = window; this.shell = shell;
        visual = window.TryGetPlatformHandle()!.Handle;
        automation = new SettingsShellAutomation(window);
        procedure = Dispatch;
        // No redirection bitmap, background brush or composition target: this HWND contributes no pixels.
        module = GetModuleHandle(null);
        var definition = new SettingsInputWindowClass { Procedure = Marshal.GetFunctionPointerForDelegate(procedure), Instance = module, Name = className };
        if (RegisterClass(ref definition) == 0) throw new Win32Exception();
        Handle = CreateWindowEx(0x08200080, className, "", 0x80000000, 0, 0, 1, 1, visual, 0, module, 0);
        if (Handle == 0) { UnregisterClass(className, module); throw new Win32Exception(); }
        try
        {
            var disabled = 1; var square = 1;
            Marshal.ThrowExceptionForHR(DwmSetWindowAttribute(Handle, 2, ref disabled, 4));
            Marshal.ThrowExceptionForHR(DwmSetWindowAttribute(Handle, 33, ref square, 4));
        }
        catch { DestroyWindow(Handle); UnregisterClass(className, module); throw; }
        window.PositionChanged += OnPosition;
        window.ScalingChanged += OnScaling;
        window.SizeChanged += OnSize;
        window.PropertyChanged += OnChanged;
        window.Closed += OnClosed;
        NativeWindowHooks.AddFirst(window, (nint hwnd, uint message, nint wp, nint lp, ref bool handled) =>
        {
            if (!disposed && message == 0x84)
            {
                // Without a system caption HitTestNCA returns HTCLIENT before Avalonia visits its controls.
                var point = new PixelPoint(unchecked((short)(lp.ToInt64() & 0xffff)), unchecked((short)((lp.ToInt64() >> 16) & 0xffff)));
                var caption = WindowChrome.CaptionHitTest(window, window.PointToClient(point));
                if (caption != 0) { handled = true; return caption; }
                var hit = window.GetVisualAt(window.PointToClient(point), visual =>
                    visual is not IInputElement input || (input.IsHitTestVisible && input.IsEffectivelyVisible));
                handled = true;
                return hit is null ? 1 : (nint)Win32Properties.GetNonClientHitTestResult(hit);
            }
            if (!disposed && message == 0x0a) EnableWindow(Handle, wp != 0); // WM_ENABLE: modal owner state
            if (!disposed && message == 0x18 && lp is 1 or 3) // WM_SHOWWINDOW: SW_PARENTCLOSING / SW_PARENTOPENING
            {
                // Main's minimise hides its directly owned Settings window; that
                // hide does not automatically hide this second-level owned proxy.
                // Preserve requestedVisible so an owner restore can bring input
                // back. An explicit Settings close still clears it through Hide.
                if (wp == 0) ShowWindow(Handle, 0);
                else if (requestedVisible && window.IsVisible && window.WindowState != WindowState.Minimized)
                { Place(); ShowWindow(Handle, 4); }
            }
            if (!disposed && message is 0x02a3 or 0x02a2 && ContainsCursor())
            {
                // TrackMouseEvent sees the input HWND instead of the GPU HWND. Keep the managed
                // hover while over its same region; the input HWND forwards the real leave.
                var point = Cursor();
                var hit = SendMessage(visual, 0x84, 0, Pack(point.X, point.Y));
                if ((message == 0x02a3 && hit == 1) || (message == 0x02a2 && hit is 8 or 9 or 20))
                { handled = true; return 0; }
            }
            return 0;
        });
    }

    internal void Reveal()
    {
        if (disposed) return;
        requestedVisible = true;
        if (!dropRegistered && GetProp(visual, "OleDropTargetInterface") is var drop and not 0)
        {
            Marshal.ThrowExceptionForHR(RegisterDragDrop(Handle, drop)); // OLE owns its AddRef until Revoke
            dropRegistered = true;
        }
        var style = GetWindowLongPtr(visual, -20);
        SetWindowLongPtr(visual, -20, style | 0x80000 | 0x20); // WS_EX_LAYERED | WS_EX_TRANSPARENT
        if (!SetLayeredWindowAttributes(visual, 0, 255, 2)) throw new Win32Exception();
        Place();
        EnableWindow(Handle, IsWindowEnabled(visual));
        if (window.WindowState != WindowState.Minimized) ShowWindow(Handle, 4); // activation remains on the real Settings window
    }
    internal void Hide() { requestedVisible = false; if (!disposed) ShowWindow(Handle, 0); }

    private void Place()
    {
        if (disposed) return;
        var origin = window.PointToScreen(new Point(shell.Inset, shell.Inset));
        var size = PixelSize.FromSize(new Size(shell.Client.Width, shell.Client.Height), window.RenderScaling);
        if (!SetWindowPos(Handle, window.Topmost ? -1 : 0, origin.X, origin.Y, size.Width, size.Height, 0x10)) throw new Win32Exception();
    }
    private void OnPosition(object? _, PixelPointEventArgs e) => Place();
    private void OnScaling(object? _, EventArgs e) => Place();
    private void OnSize(object? _, SizeChangedEventArgs e) => Place();
    private void OnChanged(object? _, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.IsVisibleProperty && !window.IsVisible) Hide();
        if (e.Property == HostWindow.TopmostProperty) Place();
        if (e.Property == HostWindow.WindowStateProperty)
        {
            if (window.WindowState == WindowState.Minimized) ShowWindow(Handle, 0);
            else if (requestedVisible && window.IsVisible) { Place(); ShowWindow(Handle, 4); }
        }
    }
    private void OnClosed(object? _, EventArgs e) => Dispose();

    private nint Dispatch(nint hwnd, uint message, nint wp, nint lp)
    {
        if (message == 0x02) automation.Release(hwnd); // WM_DESTROY, including destruction by the visual owner
        if (message == 0x21) return 3; // MA_NOACTIVATE: the forwarding handler activates the actual owner
        if (message is 0x14 or 0x85) return 1; // no erasure or NC painting
        if (message == 0x0f) { ValidateRect(hwnd, 0); return 0; }
        if (message == 0x84) return SendMessage(visual, message, wp, lp); // screen-space NC/caption hit testing
        if (message == 0x20) return SendMessage(visual, message, visual, lp);
        if (message == 0x3d && unchecked((int)lp) == -25) return automation.Return(hwnd, wp, lp);
        if (message == 0x3d && unchecked((int)lp) == -4) return SendMessage(visual, message, wp, lp);
        var clientMouse = message is >= 0x200 and <= 0x209 or >= 0x20b and <= 0x20d;
        var screenMouse = message is 0x20a or 0x20e or >= 0xa0 and <= 0xad;
        var pointer = message is >= 0x245 and <= 0x24f;
        if (clientMouse || screenMouse || pointer || message is 0x02a2 or 0x02a3 or 0x7b)
        {
            if (!IsWindowEnabled(visual)) return 0;
            if (message is 0x201 or 0x204 or 0x207 or 0x20b or 0xa1 or 0xa4 or 0x246) window.Activate();
            if (message is 0x200 or 0xa0)
            {
                var track = new SettingsMouseTrack { Size = (uint)Marshal.SizeOf<SettingsMouseTrack>(), Flags = message == 0xa0 ? 0x12u : 2u, Window = Handle };
                TrackMouseEvent(ref track);
            }
            if (clientMouse)
            {
                var x = unchecked((short)(lp.ToInt64() & 0xffff)); var y = unchecked((short)((lp.ToInt64() >> 16) & 0xffff));
                var inset = (int)Math.Round(shell.Inset * window.RenderScaling);
                lp = Pack(x + inset, y + inset);
            }
            return SendMessage(visual, message, wp, lp);
        }
        return DefWindowProc(hwnd, message, wp, lp);
    }
    private bool ContainsCursor()
    {
        var point = Cursor();
        var origin = window.PointToScreen(new Point(shell.Inset, shell.Inset));
        return point.X >= origin.X && point.Y >= origin.Y
            && point.X < origin.X + shell.Client.Width * window.RenderScaling
            && point.Y < origin.Y + shell.Client.Height * window.RenderScaling;
    }
    private static PixelPoint Cursor() { GetCursorPos(out var point); return point; }
    private static nint Pack(int x, int y) => unchecked((nint)((uint)(ushort)x | ((uint)(ushort)y << 16)));

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        window.PositionChanged -= OnPosition; window.ScalingChanged -= OnScaling;
        window.SizeChanged -= OnSize;
        window.PropertyChanged -= OnChanged; window.Closed -= OnClosed;
        if (dropRegistered) RevokeDragDrop(Handle);
        automation.Release(Handle);
        DestroyWindow(Handle);
        UnregisterClass(className, module);
        GC.KeepAlive(procedure);
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowEx(uint extended, string name, string title, uint style, int x, int y, int width, int height, nint owner, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")] private static extern nint DefWindowProc(nint hwnd, uint message, nint wp, nint lp);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint SendMessage(nint hwnd, uint message, nint wp, nint lp);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out PixelPoint point);
    [DllImport("user32.dll")] private static extern bool ValidateRect(nint hwnd, nint rect);
    [DllImport("user32.dll")] private static extern bool TrackMouseEvent(ref SettingsMouseTrack track);
    [DllImport("user32.dll")] private static extern bool EnableWindow(nint hwnd, bool enabled);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "GetPropW", CharSet = CharSet.Unicode)] private static extern nint GetProp(nint hwnd, string name);
    [DllImport("ole32.dll")] private static extern int RegisterDragDrop(nint hwnd, nint drop);
    [DllImport("ole32.dll")] private static extern int RevokeDragDrop(nint hwnd);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll", EntryPoint = "RegisterClassW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClass(ref SettingsInputWindowClass definition);
    [DllImport("user32.dll", EntryPoint = "UnregisterClassW", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string name, nint module);
}
