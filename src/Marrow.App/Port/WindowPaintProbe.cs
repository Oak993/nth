using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Port;

// T44 copied-profile diagnostics: time the original native paint without changing its result or handling.
internal sealed class WindowPaintProbe
{
    private static readonly nuint Id = 0x54443434;
    private readonly nint handle;
    private readonly Subclass callback;
    private readonly string paintName, positionName;

    private WindowPaintProbe(AvaloniaWindow window, nint handle, string role)
    {
        this.handle = handle;
        paintName = $"Win32: {role} HWND 0x{handle:X} WM_PAINT";
        positionName = $"Win32: {role} HWND 0x{handle:X} WM_WINDOWPOSCHANGED";
        callback = OnMessage;
        if (SetWindowSubclass(handle, callback, Id, 0)) window.Closed += Closed;
    }

    internal static void Attach(AvaloniaWindow window, string role)
    {
        if (OperatingSystem.IsWindows() && window.TryGetPlatformHandle() is { HandleDescriptor: "HWND", Handle: not 0 } platform)
            _ = new WindowPaintProbe(window, platform.Handle, role);
    }

    private nint OnMessage(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data)
    {
        using var work = message switch
        {
            0x000F => AuroraBootTrace.Begin(paintName),
            0x0047 => AuroraBootTrace.Begin(positionName),
            _ => default,
        };
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private void Closed(object? sender, EventArgs args)
    {
        RemoveWindowSubclass(handle, callback, Id);
        if (sender is AvaloniaWindow window) window.Closed -= Closed;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint Subclass(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint hwnd, Subclass callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint hwnd, Subclass callback, nuint id);
    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint hwnd, uint message, nint wParam, nint lParam);
}
