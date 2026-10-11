using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Marrow.App.Port;
using Marrow.App.Window;
using Marrow.Core.Port;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using G = Marrow.App.Tests.Window.Native.WindowManagerNativePlacementGeometry;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;
using T = Marrow.App.Tests.Window.Native.WindowManagerNativeMonitorTopology;

namespace Marrow.App.Tests.Window.Native;

// Where a factory window first lands, on its real HWND (the first open of a never-shown window). The Swift frame is the
// visible window; the HWND of the factory's chrome is larger than it, by an invisible strip (WindowInvisibleBorder), and
// Avalonia reports the standard overlapped frame, 39 px of caption on top, until the window has been shown once. A test
// reads only native frames (GetWindowRect, GetClientRect, DWM's extended frame bounds) and the working area.
[Collection("WindowManagerNative")]
public sealed class WindowManagerNativeCentringTests(WindowManagerNativeSession native)
{
    private static string F(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    // The reading of an owned window once two native readings a fence apart agree.
    private NativePlacementSnapshot Settled(AvaloniaWindow window)
    {
        var handle = native.OnUi(() => window.TryGetPlatformHandle()?.Handle ?? 0);
        NativePlacementSnapshot? previous = null;
        var equal = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(6))
        {
            Thread.Sleep(120);
            var reading = native.OnUi(() => { window.UpdateLayout(); return G.Read(handle); });
            equal = reading == previous ? equal + 1 : 0;
            if (equal >= 2 && reading.Visible) return reading;
            previous = reading;
        }
        throw new InvalidOperationException("NATIVE_PRECONDITION: the owned window's frame did not settle.");
    }

    // The monitor the pointer is on (ED-225's choice for a window with no owner), else the primary one.
    private static NativePlacementMonitor PointerMonitor(NativePlacementTopology topology)
    {
        N.Check(N.GetCursorPos(out var point), "GetCursorPos");
        return topology.Monitors.FirstOrDefault(monitor => point.X >= monitor.Bounds.Left && point.X < monitor.Bounds.Right &&
            point.Y >= monitor.Bounds.Top && point.Y < monitor.Bounds.Bottom) ?? Assert.Single(topology.Monitors, monitor => monitor.Primary);
    }

    [Fact]
    [Trait("Req", "PR-WIN-006")] // WindowManager.swift:67-69 `window.center()` when no frame is saved; ED-225
    [Trait("Req", "PR-LAUNCH-028")] // AppDelegate.swift:22 showMain at launch: the first-run window is centred (OD-0018 hands over to it)
    [Trait("Req", "UI-LAUNCH-001")] // the first launch's window, 69 % x 92 % of the work area (ED-NEW-settingsinapp-3), centred
    public void A_fresh_profile_main_window_is_centred_on_its_real_frame() => native.Run(subject =>
    {
        WindowManager manager = null!;
        AvaloniaWindow main = null!;
        native.OnUi(() =>
        {
            subject.Main.Hide();
            subject.Settings.Hide();
            // a fresh profile: nothing is saved under the main window's frame name (:24, :67)
            UserDefaults.Standard.Current.RemoveObject("DroppyCodeMainWindow");
            manager = new WindowManager { MainContent = () => new Border(), SettingsContent = () => new Border() };
            main = manager.PrepareMain();
            native.Register(main);
            manager.ShowMain();
        });
        var reading = Settled(main);
        var handle = reading.Handle;
        var topology = native.OnUi(T.Read);
        var work = PointerMonitor(topology).Work;
        var visible = native.OnUi(() => G.ReadVisibleBounds((nint)handle)).VisibleBounds ?? throw new InvalidOperationException("DWM gave no frame bounds");
        var scale = reading.Dpi / 96.0;
        // ED-NEW-settingsinapp-3 (owner, 2026-10-11): with nothing saved the window is 69 % x 92 % of the work area in points (at least the
        // 860 x 560 minimum), drawn under its title bar (no caption above the client area)
        var points = (Width: Math.Round(Math.Max(860, work.Width / scale * WindowCentering.FirstLaunchWidthShare)), Height: Math.Round(Math.Max(560, work.Height / scale * WindowCentering.FirstLaunchHeightShare)));
        Assert.InRange(reading.Client.Width - points.Width * scale, -1, 1);
        Assert.InRange(reading.Client.Height - points.Height * scale, -1, 1);
        Assert.Equal(reading.Frame.Top, visible.Top); // nothing invisible above it
        // the visible window is centred both ways (ED-NEW-settingsinapp-3: half the free height above it)
        var left = visible.Left - work.Left;
        var right = work.Right - visible.Right;
        Assert.InRange(left - right, -1, 1);
        var expectedTop = (work.Height - visible.Height) / 2.0;
        Assert.InRange(visible.Top - work.Top - expectedTop, -1, 1);
        // the autosave is installed after the centring (:70): the frame now saved is where it was put
        var saved = native.OnUi(() => UserDefaults.Standard.Current.Dictionary("DroppyCodeMainWindow"));
        Assert.NotNull(saved);
    }, factType: typeof(WindowManagerNativeCentringTests));
}
