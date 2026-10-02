// PORT-ONLY (ED-084). SwiftUI's `\.accessibilityReduceMotion` follows the system's Reduce Motion switch; on Windows it
// follows the "Animation effects" setting (SPI_GETCLIENTAREAANIMATION, inverted), re-read on WM_SETTINGCHANGE, as the
// inherited environment property every view reads (EnvironmentValues.AccessibilityReduceMotionProperty). Each window the
// app opens gets the setting on its root, so its whole tree (popups included, through their logical parent) reads it;
// a settings change re-applies it to every open window. Only the app installs it (MarrowApplication): headless tests set
// the property themselves, so the PC's own setting never reaches them.
using Avalonia.Controls;
using Avalonia.Interactivity;
using Marrow.Core.Port;

namespace Marrow.UI.Port;

/// <summary>PORT-ONLY. The system Reduce Motion setting, applied to every open window (see the file note).</summary>
public sealed class SystemReduceMotion
{
    private readonly Func<bool> read;
    private readonly List<WeakReference<Window>> windows = [];

    /// <param name="read">Reads the system setting: true when animations should be reduced.</param>
    public SystemReduceMotion(Func<bool> read) => this.read = read;

    /// <summary>The app's instance (<see cref="Install"/> makes it read Windows; until then it follows no window). A
    /// test overrides it for its own flow.</summary>
    public static Ambient<SystemReduceMotion> Shared { get; } = new(new SystemReduceMotion(() => false));

    private static bool installed;

    /// <summary>App start-up: every window opened from now on follows the Windows setting.</summary>
    public static void Install()
    {
        if (installed) return;
        installed = true;
        Shared.SetProcessDefault(new SystemReduceMotion(ReadAnimationEffectsOff));
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => Shared.Current.Follow(window), RoutingStrategies.Direct);
    }

    /// <summary>Sets the setting on <paramref name="window"/>'s root and keeps it in step until the window closes.</summary>
    public void Follow(Window window)
    {
        windows.RemoveAll(entry => !entry.TryGetTarget(out _)); // drop windows already collected
        windows.Add(new WeakReference<Window>(window));
        window.Closed += (_, _) => windows.RemoveAll(entry => !entry.TryGetTarget(out var alive) || ReferenceEquals(alive, window));
        EnvironmentValues.SetAccessibilityReduceMotion(window, read());
    }

    /// <summary>WM_SETTINGCHANGE: reads the setting again and applies it to every open window.</summary>
    public void Refresh()
    {
        var reduce = read();
        foreach (var entry in windows.ToArray())
            if (entry.TryGetTarget(out var window)) EnvironmentValues.SetAccessibilityReduceMotion(window, reduce);
    }

    // "Animation effects" off is reduce motion (as AppDelegate, AttachmentFlightPlatform and GeniePlatform read it).
    private static bool ReadAnimationEffectsOff() =>
        OperatingSystem.IsWindows() && SystemParametersInfo(0x1042, 0, out var enabled, 0) && !enabled; // SPI_GETCLIENTAREAANIMATION

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, out bool enabled, uint update);
}
