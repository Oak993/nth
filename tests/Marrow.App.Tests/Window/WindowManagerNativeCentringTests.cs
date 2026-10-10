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

    private static AvaloniaWindow SettingsOf(WindowManager manager) => (AvaloniaWindow)(typeof(WindowManager)
        .GetField("settingsWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)
        ?? throw new InvalidOperationException("Settings window was not retained."));

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
    [Trait("Req", "UI-LAUNCH-001")] // the first launch's 1320 x 860 window, centred
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
        // the window is the 1320 x 860 points of :19, drawn under its title bar (no caption above the client area)
        Assert.Equal((int)Math.Round(1320 * scale), reading.Client.Width);
        Assert.Equal((int)Math.Round(860 * scale), reading.Client.Height);
        Assert.Equal(reading.Frame.Top, visible.Top); // nothing invisible above it
        // ED-225: the visible window is centred across, and one third of the free height lies above it
        var left = visible.Left - work.Left;
        var right = work.Right - visible.Right;
        Assert.InRange(left - right, -1, 1);
        var expectedTop = (work.Height - visible.Height) / 3.0;
        Assert.InRange(visible.Top - work.Top - expectedTop, -1, 1);
        // the autosave is installed after the centring (:70): the frame now saved is where it was put
        var saved = native.OnUi(() => UserDefaults.Standard.Current.Dictionary("DroppyCodeMainWindow"));
        Assert.NotNull(saved);
    }, factType: typeof(WindowManagerNativeCentringTests));

    [Fact]
    [Trait("Req", "PR-SETTINGS-002")] // WindowManager.swift:32-55 the first showSettings of a window that has never been shown
    [Trait("Req", "PR-WIN-010")] // :80-84 a new Settings window is 780 x 580 and re-centred over the main window at its show
    public void A_first_ShowSettings_with_the_real_SettingsView_lands_centred_over_the_main_window_on_its_real_frame() => native.Run(subject =>
    {
        WindowManager manager = null!;
        AvaloniaWindow main = null!;
        native.OnUi(() =>
        {
            subject.Main.Hide();
            subject.Settings.Hide();
            FrameClock.Source.SetProcessDefault(new AvaloniaFrameSource(subject.Settings)); // the Rise open asks for frames
            manager = new WindowManager { MainContent = () => new Border() }; // the production Settings content (:81)
            main = manager.PrepareMain();
            native.Register(main);
            var area = main.Screens.Primary!.WorkingArea;
            main.Position = new PixelPoint(area.X + 160, area.Y + 90);
            manager.ShowMain();
        });
        var mainReading = Settled(main);
        var topology = native.OnUi(T.Read);
        var owner = Assert.Single(topology.Monitors, monitor => monitor.Handle == mainReading.Monitor);
        var primary = Assert.Single(topology.Monitors, monitor => monitor.Primary);

        AvaloniaWindow settings = null!;
        native.OnUi(() =>
        {
            manager.ShowSettings();
            settings = SettingsOf(manager);
            native.Register(settings);
        });
        // the open is Rise (OD-0074): shown cloaked, revealed at glass-ready
        N.Until(() => native.OnUi(() => manager.SettingsOpening is not { Waiting: true }), "the Settings open never finished", 8000);
        var shown = Settled(settings);
        Assert.Equal(0, native.OnUi(() => { N.DwmGetWindowAttribute((nint)shown.Handle, 14, out var cloaked, sizeof(int)); return cloaked; })); // DWMWA_CLOAKED
        // the content is the production SettingsView (:81), not a stand-in
        Assert.IsType<Marrow.App.Views.Settings.SettingsView>(typeof(WindowManager)
            .GetField("settingsView", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager));

        // RR-37 (OD-0074/0076): the Settings window is 780 x 580 points of visible card inside a transparent shadow space
        // (SettingsShell.ShadowSpace on every side) that draws the locked CSS shadow; the HWND's client area is the card and
        // that space, and the HWND has no invisible strip of its own (DWM's frame is off: the shell draws the shadow)
        var scale = shown.Dpi / 96.0;
        var (inset, card) = native.OnUi(() => settings.Content is SettingsShell shell ? (shell.Inset, shell.Client.Bounds) : (0, default));
        Assert.Equal(SettingsShell.ShadowSpace, inset);
        Assert.Equal(new Rect(inset, inset, 780, 580), card);
        Assert.Equal((int)Math.Round((780 + 2 * inset) * scale), shown.Client.Width);
        Assert.Equal((int)Math.Round((580 + 2 * inset) * scale), shown.Client.Height);
        Assert.Equal(shown.Client.Width, shown.Frame.Width);
        Assert.Equal(shown.Client.Height, shown.Frame.Height);
        // expected from native readings alone: the Swift arithmetic (:46 main.frame, the VISIBLE main window: its HWND less
        // the invisible strip on the left, right and bottom; DWM's extended frame bounds add back Windows 11's 1 px border)
        // on the visible card (no strip), clamped into the working area, and the HWND that origin less the shadow space
        var mainStrip = (mainReading.Frame.Width - mainReading.Client.Width) / 2;
        var mainVisible = mainReading.Frame with { Left = mainReading.Frame.Left + mainStrip, Right = mainReading.Frame.Right - mainStrip,
            Bottom = mainReading.Frame.Bottom - mainStrip };
        var dwm = native.OnUi(() => G.ReadVisibleBounds((nint)mainReading.Handle)).VisibleBounds!;
        Assert.Equal(mainVisible with { Left = mainVisible.Left - 1, Right = mainVisible.Right + 1, Bottom = mainVisible.Bottom + 1 }, dwm);
        var insetPixels = (int)Math.Round(inset * scale);
        var expected = G.Expected(mainReading with { Frame = mainVisible }, shown.Client.Width - 2 * insetPixels,
            shown.Client.Height - 2 * insetPixels, 0, shown.Dpi, primary.Bounds, owner.Work);
        Assert.Equal(new NativePlacementPoint(expected.Origin.X - insetPixels, expected.Origin.Y - insetPixels), shown.Origin);
    }, factType: typeof(WindowManagerNativeCentringTests));
}
