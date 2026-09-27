using Avalonia;
using Avalonia.Controls;

namespace Marrow.UI.Port;

// Source: DroppyCode/UI/Chrome/WindowChrome.swift (B3 52bc792), :28-93;
// DroppyCode/App/Window/WindowManager.swift (B3 52bc792), :129-139.
// PORT-ONLY: ED-224's window-scoped bridge from App's native resize observations to UI chrome.
public sealed class WindowLiveResizeSignal
{
    private WindowLiveResizeSignal() { }

    // WindowChrome.swift:28, :216: NSWindow.inLiveResize. App publishes start/end on this window only.
    public static readonly AttachedProperty<bool> IsActiveProperty =
        AvaloniaProperty.RegisterAttached<WindowLiveResizeSignal, Window, bool>("IsActive", false);

    public static bool GetIsActive(Window window) => window.GetValue(IsActiveProperty);

    public static void SetIsActive(Window window, bool value) => window.SetValue(IsActiveProperty, value);
}
