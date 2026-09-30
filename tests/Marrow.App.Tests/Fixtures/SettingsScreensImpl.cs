// Controlled platform screen data, never real monitor evidence.
using System.Reflection;
using Avalonia;
using Avalonia.Platform;

namespace Marrow.App.Tests.Window;

// IScreenImpl's nine members were read from the pinned local Avalonia.Controls.dll metadata.
// This is a new TEST helper, not a production seam. It records which actual window platform was queried.
public class SettingsScreensImpl : DispatchProxy
{
    public int ScreenCount => AllScreens.Count;
    public IReadOnlyList<Screen> AllScreens { get; set; } = [];
    public Action? Changed { get; set; }
    public Screen? WindowScreen { get; set; }
    public ITopLevelImpl? ExpectedWindow { get; set; }
    public ITopLevelImpl? LastWindow { get; private set; }
    public int WindowLookups { get; private set; }

    public Screen? ScreenFromWindow(IWindowBaseImpl window) => Lookup(window);
    public Screen? ScreenFromTopLevel(ITopLevelImpl topLevel) => Lookup(topLevel);
    public Screen? ScreenFromPoint(PixelPoint point) => AllScreens.FirstOrDefault(screen => screen.Bounds.Contains(point));
    public Screen? ScreenFromRect(PixelRect rectangle) => ScreenFromPoint(rectangle.Position);
    public Task<bool> RequestScreenDetails() => Task.FromResult(true);

    // Avalonia 12's reference assembly prohibits C# implementations of its private platform contract.
    // Like the existing IWindowImpl proxy, this test adapter implements it dynamically and touches
    // only the Screens object installed on a test-owned headless window.
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
    {
        "get_ScreenCount" => ScreenCount,
        "get_AllScreens" => AllScreens,
        "get_Changed" => Changed,
        "set_Changed" => SetChanged((Action?)args![0]),
        "ScreenFromWindow" => ScreenFromWindow((IWindowBaseImpl)args![0]!),
        "ScreenFromTopLevel" => ScreenFromTopLevel((ITopLevelImpl)args![0]!),
        "ScreenFromPoint" => ScreenFromPoint((PixelPoint)args![0]!),
        "ScreenFromRect" => ScreenFromRect((PixelRect)args![0]!),
        "RequestScreenDetails" => RequestScreenDetails(),
        _ => throw new NotSupportedException("Unexpected screen platform call: " + targetMethod?.Name),
    };

    private object? SetChanged(Action? changed) { Changed = changed; return null; }

    private Screen? Lookup(ITopLevelImpl window)
    {
        WindowLookups++;
        LastWindow = window;
        if (ExpectedWindow is not null && !ReferenceEquals(ExpectedWindow, window))
            throw new InvalidOperationException("Screens was asked about a different window platform.");
        return WindowScreen;
    }
}
