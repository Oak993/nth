// PORT-ONLY RR-37: the locked Settings glass includes its shadow; DWM's NC decoration cannot
// be a child of the WinUI root. Reserve transparent space and draw the CSS shadow in that root.
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Window;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Port;

internal sealed class SettingsShell : Panel
{
    internal const double ShadowSpace = 180; // three sigma of the 90 px CSS blur plus its 34 px offset
    internal Control Client { get; }
    internal Border Shadow { get; }
    internal double Inset { get; }
    internal SettingsAcrylicClip? AcrylicClip { get; set; }
    internal SettingsShellInput? Input { get; private set; }

    private SettingsShell(Control client, Size clientSize, bool native)
    {
        Client = client;
        Inset = native ? ShadowSpace : 0;
        Shadow = new Border
        {
            Name = "SettingsShellShadow", Background = Brushes.Transparent, CornerRadius = new CornerRadius(8),
            BoxShadow = new BoxShadows(
                new BoxShadow { OffsetY = 34, Blur = 90, Color = Color.FromArgb(107, 0, 0, 0) },
                [new BoxShadow { OffsetY = 8, Blur = 24, Color = Color.FromArgb(51, 0, 0, 0) }]),
            IsHitTestVisible = false,
            IsVisible = native, // a renderer without an HWND has no exterior surface on which to paint a window shadow
        };
        foreach (var part in new[] { Shadow, Client })
        {
            part.Width = clientSize.Width;
            part.Height = clientSize.Height;
            part.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            part.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            part.Margin = new Thickness(Inset);
        }
        if (native)
            Client.Clip = new RectangleGeometry(new Rect(clientSize), 8, 8);
        Children.Add(Shadow);
        Children.Add(Client); // includes the captions; shadow stays outside the inner rounded clip
    }

    internal static void Install(HostWindow window)
    {
        var native = window.TryGetPlatformHandle() is { Handle: not 0, HandleDescriptor: "HWND" };
        var clientSize = new Size(window.Width, window.Height);
        var client = (Control)window.Content!;
        window.Content = null; // detach the previous logical owner before reparenting the whole caption/client group
        var shell = new SettingsShell(client, clientSize, native);
        window.Content = shell;
        window.Closed += (_, _) => { shell.AcrylicClip?.Dispose(); shell.AcrylicClip = null; };
        if (!native) return;
        window.WindowDecorations = WindowDecorations.None;
        // Preserve Alt+Space and the source's minimise/close system commands without a system caption.
        Win32Properties.AddWindowStylesCallback(window, (style, extended) => (style | 0x00080000, extended));
        var handle = window.TryGetPlatformHandle()!.Handle;
        SetWindowLongPtr(handle, -16, GetWindowLongPtr(handle, -16) | 0x00080000); // WS_SYSMENU
        window.ExtendClientAreaToDecorationsHint = false;
        window.Width = clientSize.Width + 2 * ShadowSpace;
        window.Height = clientSize.Height + 2 * ShadowSpace;
        SuppressNonClient(window);
        shell.Input = new SettingsShellInput(window, shell);
    }

    internal static void SuppressNonClient(HostWindow window)
    {
        if (window.Content is not SettingsShell { Inset: > 0 }
            || window.TryGetPlatformHandle() is not { Handle: not 0, HandleDescriptor: "HWND" } handle) return;
        var none = unchecked((int)0xfffffffe); // DWMWA_COLOR_NONE, never restore a different shadow at rest
        var disabled = 1; // DWMNCRP_DISABLED
        var square = 1; // DWMWCP_DONOTROUND: only the inner client has rounded corners
        Check(DwmSetWindowAttribute(handle.Handle, 34, ref none, sizeof(int)));
        Check(DwmSetWindowAttribute(handle.Handle, 2, ref disabled, sizeof(int)));
        Check(DwmSetWindowAttribute(handle.Handle, 33, ref square, sizeof(int)));
    }

    private static void Check(int result) => Marshal.ThrowExceptionForHR(result);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
}
