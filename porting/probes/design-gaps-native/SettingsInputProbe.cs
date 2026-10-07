// Native acceptance checks on the probe-owned Settings HWNDs only; no global input or Windows settings.
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Marrow.App.Views.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Theme;

internal static class SettingsInputProbe
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static object CheckInput(Window window, Control client, Window backdrop)
    {
        var origin = client.PointToScreen(default);
        var owner = window.TryGetPlatformHandle()!.Handle;
        var behind = backdrop.TryGetPlatformHandle()!.Handle;
        var inside = WindowFromPoint(new PixelPoint(origin.X + 80, origin.Y + 100));
        var outside = WindowFromPoint(new PixelPoint(origin.X - 80, origin.Y + 100));
        if (outside != behind || inside == behind || inside == owner)
            throw new InvalidOperationException("Settings requires an inner-only input HWND and click-through shadow padding.");
        var close = client.PointToScreen(new Point(client.Bounds.Width - 29, 28));
        var hit = SendMessage(inside, 0x84, 0, Pack(close.X, close.Y));
        if (hit != 20) throw new InvalidOperationException($"The input HWND lost the Close caption hit target: {hit}.");
        if (GetSystemMenu(owner, false) == 0) throw new InvalidOperationException("Settings lost its Alt+Space system menu.");
        if (GetProp(inside, "OleDropTargetInterface") == 0) throw new InvalidOperationException("The Settings input HWND has no OLE drop target.");
        EnableWindow(owner, false);
        try
        {
            if (IsWindowEnabled(inside)) throw new InvalidOperationException("The modal owner did not disable the input HWND.");
            SendMessage(inside, 0x201, 1, Pack(80, 100)); SendMessage(inside, 0x202, 0, Pack(80, 100));
        }
        finally { EnableWindow(owner, true); }
        if (!IsWindowEnabled(inside)) throw new InvalidOperationException("The input HWND did not re-enable with its owner.");
        return new { owner = owner.ToString(System.Globalization.CultureInfo.InvariantCulture), input = inside.ToString(System.Globalization.CultureInfo.InvariantCulture), backdrop = behind.ToString(System.Globalization.CultureInfo.InvariantCulture),
            paddingHit = outside.ToString(System.Globalization.CultureInfo.InvariantCulture), closeHit = (int)hit, modalDisable = true, oleRegistered = true, systemMenu = true };
    }

    internal static string Page(Window window)
    {
        var view = window.GetVisualDescendants().OfType<SettingsView>().Single();
        var state = typeof(SettingsView).GetField("state", Members)!.GetValue(view)!;
        return state.GetType().GetProperty("Page")!.GetValue(state)!.ToString()!;
    }

    internal static void ClickGeneral(Window window, Control client)
    {
        var point = client.PointToScreen(new Point(80, 100));
        var hwnd = WindowFromPoint(point);
        var scaling = window.RenderScaling;
        var packed = Pack((int)Math.Round(80 * scaling), (int)Math.Round(100 * scaling));
        SendMessage(hwnd, 0x200, 0, packed);
        SendMessage(hwnd, 0x201, 1, packed);
        SendMessage(hwnd, 0x202, 0, packed);
    }

    internal static object Drop(Window window, string source)
    {
        var button = window.GetVisualDescendants().OfType<WallpaperCard>().Single().GetVisualDescendants().OfType<Button>()
            .Single(DragDrop.GetAllowDrop);
        var point = button.PointToScreen(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2));
        var hwnd = WindowFromPoint(point);
        var raw = GetProp(hwnd, "OleDropTargetInterface");
        if (raw == 0) throw new InvalidOperationException("Wallpaper is not reachable through the registered input HWND.");
        var assembly = typeof(ScreenGrab).Assembly;
        var contract = assembly.GetType("Marrow.App.Tests.Window.Native.NativeOleDropTarget", true)!;
        var pointType = assembly.GetType("Marrow.App.Tests.Window.Native.NativeWindowPoint", true)!;
        var dataType = assembly.GetType("Marrow.App.Tests.Window.Native.NativeDropDataObject", true)!;
        var nativePoint = Activator.CreateInstance(pointType, Members, null, [point.X, point.Y], null)!;
        var data = Activator.CreateInstance(dataType, Members, null, [new[] { source }], null)!;
        var target = Marshal.GetObjectForIUnknown(raw);
        try
        {
            object[] enter = [data, 0, nativePoint, 7];
            Marshal.ThrowExceptionForHR((int)contract.GetMethod("DragEnter")!.Invoke(target, enter)!);
            if ((int)enter[3] != 1) throw new InvalidOperationException($"Wallpaper DragEnter effect {enter[3]}.");
            object[] drop = [data, 0, nativePoint, enter[3]]; // OLE's selected effect after DragEnter/Over
            Marshal.ThrowExceptionForHR((int)contract.GetMethod("Drop")!.Invoke(target, drop)!);
            if ((int)drop[3] != 1) throw new InvalidOperationException($"Wallpaper Drop effect {drop[3]}.");
            return new { input = hwnd.ToString(System.Globalization.CultureInfo.InvariantCulture), enterEffect = enter[3], dropEffect = drop[3] };
        }
        finally { Marshal.ReleaseComObject(target); }
    }

    internal static void BringWallpaperIntoView(Window window)
    {
        var button = window.GetVisualDescendants().OfType<WallpaperCard>().Single().GetVisualDescendants().OfType<Button>()
            .Single(DragDrop.GetAllowDrop);
        button.BringIntoView();
    }

    internal static bool WallpaperInstalled() => WallpaperStore.Shared.Current.HasWallpaper;
    internal static void Minimize(Window window, Control client)
    {
        var point = client.PointToScreen(new Point(client.Bounds.Width - 125, 28));
        var hwnd = WindowFromPoint(point);
        var hit = SendMessage(hwnd, 0x84, 0, Pack(point.X, point.Y));
        if (hit != 8) throw new InvalidOperationException($"Minimize hit {hit}.");
        SendMessage(hwnd, 0xa1, 8, Pack(point.X, point.Y));
        SendMessage(hwnd, 0xa2, 8, Pack(point.X, point.Y));
    }
    private static nint Pack(int x, int y) => (nint)((uint)(ushort)x | ((uint)(ushort)y << 16));
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(PixelPoint point);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint SendMessage(nint hwnd, uint message, nint wp, nint lp);
    [DllImport("user32.dll")] private static extern bool EnableWindow(nint hwnd, bool enabled);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetSystemMenu(nint hwnd, bool revert);
    [DllImport("user32.dll", EntryPoint = "GetPropW", CharSet = CharSet.Unicode)] private static extern nint GetProp(nint hwnd, string name);
}
