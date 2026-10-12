using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Marrow.App.Window;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

// ED-225 headless monitor/cursor boundary. No real pointer movement or native screen reads.
public sealed class WindowCenteringCursor : ICursorPlatform
{
    public PixelPoint? Position { get; set; } = new PixelPoint(0, 0);
    public int Reads { get; private set; }
    public PixelPoint? ScreenPosition { get { Reads++; return Position; } }
}

public sealed class WindowCenteringScreen
{
    public PixelRect Bounds { get; }
    public PixelRect WorkingArea { get; }
    public double Scaling { get; }
    public bool IsPrimary { get; }
    public WindowCenteringScreen(PixelRect bounds, PixelRect work, double scale = 1, bool primary = false)
    {
        Bounds = bounds; WorkingArea = work; Scaling = scale; IsPrimary = primary;
    }
}

public sealed class WindowCenteringScreens(WindowCenteringScreen pointer, WindowCenteringScreen owner)
{
    public WindowCenteringScreen Pointer { get; set; } = pointer;
    public WindowCenteringScreen Owner { get; set; } = owner;
    public int OwnerReads { get; private set; }
    public int PointReads { get; private set; }
    public PixelPoint? LastPoint { get; private set; }
    public WindowBase? LastOwner { get; private set; }
    public (PixelRect WorkingArea, double Scaling)? ScreenFromWindow(WindowBase window) { OwnerReads++; LastOwner = window; return (Owner.WorkingArea, Owner.Scaling); }
    public (PixelRect WorkingArea, double Scaling)? ScreenFromPoint(Avalonia.Controls.Window window, PixelPoint point) { PointReads++; LastPoint = point; return (Pointer.WorkingArea, Pointer.Scaling); }
}

public static class WindowCenteringTestHooks
{
    // Reflection keeps the tests buildable before the port-only binding is added; the new tests require it.
    private static Type? Binding => typeof(WindowManager).Assembly.GetType("Marrow.App.Port.WindowCentering");
    public static IDisposable? OverrideCursor(WindowCenteringCursor cursor, bool required = false)
    {
        if (required) Assert.NotNull(Binding);
        if (Binding is null) return null;
        var ambient = Assert.IsType<Ambient<ICursorPlatform>>(Binding.GetProperty("Cursor", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));
        return ambient.Override(cursor);
    }
    public static IDisposable OverrideOwnerScreen(WindowCenteringScreens screens)
    {
        Assert.NotNull(Binding);
        var ambient = Assert.IsType<Ambient<Func<WindowBase, (PixelRect WorkingArea, double Scaling)?>>>(Binding.GetProperty("OwnerScreen", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));
        return ambient.Override(screens.ScreenFromWindow);
    }
    public static IDisposable OverridePointerScreen(WindowCenteringScreens screens)
    {
        Assert.NotNull(Binding);
        var ambient = Assert.IsType<Ambient<Func<Avalonia.Controls.Window, PixelPoint, (PixelRect WorkingArea, double Scaling)?>>>(Binding.GetProperty("PointerScreen", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));
        return ambient.Override(screens.ScreenFromPoint);
    }
    public static void Center(Avalonia.Controls.Window window, Size size)
    {
        Assert.NotNull(Binding);
        Marrow.App.Port.WindowCentering.Center(window, size); // ED-225's third above (the default)
    }
}
