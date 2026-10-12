using System.Collections;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Window;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window.Native;

internal sealed class WindowManagerNativeSubject : IDisposable
{
    private readonly WindowManagerNativeSession session;
    private int quitRequests;

    internal WindowManagerNativeSubject(WindowManagerNativeSession session)
    {
        Dispatcher.UIThread.VerifyAccess();
        this.session = session;
        Manager = new WindowManager
        {
            MainContent = () => MainContent,
            SettingsContent = () => SettingsContent,
            MainWindowClose = () => Interlocked.Increment(ref quitRequests),
        };
        Manager.ShowMain();
        Main = Manager.MainWindow ?? throw new InvalidOperationException("ShowMain did not retain its window.");
        session.Register(Main);
        Template(Main);
        // OD-0151: the app no longer makes a Settings window (Settings is a panel in the main window, SettingsOverlay). The suite's second
        // window is now a stand-in the fixture makes itself: a SecondaryWindow of the old Settings frame holding SettingsContent, owned by Main.
        // It is not managed by WindowManager (no caption state, no live-resize environment), so the native tests that read those through
        // it are listed for a rework at their next native run.
        Settings = new SecondaryWindow { Title = "Settings", Width = 780, Height = 580, CanResize = false, Content = SettingsContent };
        Marrow.UI.Chrome.WindowChrome.Configure(Settings);
        Settings.Show(Main);
        session.Register(Settings);
        Template(Settings);
        Place(Main, 880, 570);
        // ED-NEW-native-fix: Settings is owned by Main and so always above it (T45, nonmodal ownership). The fixture
        // therefore puts it clear of Main, or every Main caption/edge target would be covered by the Settings window.
        Place(Settings, 780, 580, ClearOf(Main, Settings, 780, 580));
        // Reads the exact instances created by MakeWindow and attached by HostedEnvironment.
        _ = AppEnvironment.GetWindowLiveResize(MainContent);
        MainProbe = new WindowManagerNativeProbe(Main, MainContent, () => ReadPendingCaption(Main));
        SettingsProbe = new WindowManagerNativeProbe(Settings, SettingsContent, () => ReadPendingCaption(Settings));
    }

    internal WindowManager Manager { get; }
    internal AvaloniaWindow Main { get; }
    internal AvaloniaWindow Settings { get; }
    internal WindowManagerNativeContent MainContent { get; } = new();
    internal WindowManagerNativeContent SettingsContent { get; } = new();
    internal WindowManagerNativeProbe MainProbe { get; }
    internal WindowManagerNativeProbe SettingsProbe { get; }
    internal int QuitRequests => Volatile.Read(ref quitRequests);
    internal WindowManagerNativeSession Session => session;

    // A bare Application has no production theme/Window template. The test supplies only the host
    // template needed to display the factory's unchanged Panel/FrameHostingView/HostedRoot subtree.
    private static void Template(AvaloniaWindow window)
    {
        window.ShowInTaskbar = false;
        window.Template = new FuncControlTemplate<AvaloniaWindow>((owner, _) => new ContentPresenter { Content = owner.Content });
        window.ApplyTemplate();
        window.UpdateLayout();
    }

    // The first free origin beside the placed window (to its right, else below it), 24 px apart, within the working area.
    private static PixelPoint ClearOf(AvaloniaWindow placed, AvaloniaWindow window, double width, double height)
    {
        var screen = window.Screens.ScreenFromWindow(placed) ?? window.Screens.Primary
            ?? throw new InvalidOperationException("No native screen is available.");
        var area = screen.WorkingArea;
        var scale = window.DesktopScaling;
        var frame = placed.FrameSize ?? placed.ClientSize;
        var right = new PixelPoint(placed.Position.X + (int)Math.Ceiling(frame.Width * scale) + 24, area.Position.Y + 96);
        if (right.X + width * scale + 48 <= area.Right && right.Y + height * scale + 48 <= area.Bottom) return right;
        var below = new PixelPoint(area.Position.X + 96, placed.Position.Y + (int)Math.Ceiling(frame.Height * scale) + 24);
        if (below.X + width * scale + 48 <= area.Right && below.Y + height * scale + 48 <= area.Bottom) return below;
        throw new InvalidOperationException("NATIVE_PRECONDITION: the working area cannot hold the Main and Settings fixture windows side by side; Settings (owned, so above Main) would cover the Main caption and edge targets.");
    }

    private static void Place(AvaloniaWindow window, double width, double height, PixelPoint? origin = null)
    {
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary
            ?? throw new InvalidOperationException("No native screen is available.");
        var area = screen.WorkingArea;
        var scale = window.DesktopScaling;
        if (area.Width < width * scale + 192 || area.Height < height * scale + 160)
            throw new InvalidOperationException("The native fixture needs enough working area for the factory minimum size and guarded edge drag.");
        window.Width = width;
        window.Height = height;
        window.Position = origin ?? new PixelPoint(area.Position.X + 96, area.Position.Y + 96);
        window.UpdateLayout();
    }

    internal void Focus(AvaloniaWindow window, WindowManagerNativeProbe probe)
    {
        try
        {
            session.OnUi(() =>
            {
                foreach (var owned in new[] { Main, Settings }) owned.Topmost = ReferenceEquals(owned, window);
                window.Show();
                window.UpdateLayout();
                WindowManagerNativeForeground.Acquire(probe.Handle, session.ThreadId);
            });
        }
        catch (TimeoutException error)
        {
            throw new InvalidOperationException("NATIVE_PRECONDITION: the owned STA did not complete foreground acquisition; no test pointer input was sent. The child job owns bounded cleanup.", error);
        }
        probe.Fence();
        WindowManagerNativeForeground.RequireStillForeground(probe.Handle, session.ThreadId);
    }

    internal NativeWindowPoint Center(Control control) => session.OnUi(() =>
    {
        var point = control.PointToScreen(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2));
        return new NativeWindowPoint(point.X, point.Y);
    });

    internal NativeWindowPoint Caption(WindowManagerNativeContent content) => session.OnUi(() =>
    {
        var point = content.CaptionZone.PointToScreen(new Point(280, content.CaptionZone.Bounds.Height / 2));
        return new NativeWindowPoint(point.X, point.Y);
    });

    // Supplemental mutation evidence only: observe state reached by real native delivery, never write it
    // and never invoke HandleNativeWindowMessage. A09/A19 are otherwise masked by redundant guards/routing.
    internal nint PendingCaption(AvaloniaWindow window) => session.OnUi(() => ReadPendingCaption(window));

    private nint ReadPendingCaption(AvaloniaWindow window)
    {
        Dispatcher.UIThread.VerifyAccess();
        var field = typeof(WindowManager).GetField("captionStates", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(WindowManager), "captionStates");
        var states = (IDictionary)field.GetValue(Manager)!;
        if (!states.Contains(window)) return (nint)0;
        var state = states[window]!;
        return (nint)state.GetType().GetField("PendingHitTest", BindingFlags.Instance | BindingFlags.Public)!.GetValue(state)!;
    }

    internal void RestoreForNextCheck(AvaloniaWindow window, WindowManagerNativeProbe probe)
    {
        session.OnUi(() => window.WindowState = WindowState.Normal);
        WindowManagerNativeMethods.Until(() => !WindowManagerNativeMethods.IsIconic(probe.Handle) && !WindowManagerNativeMethods.IsZoomed(probe.Handle),
            "Fixture normalization did not restore the native window.");
        Focus(window, probe);
    }

    public void Dispose()
    {
        Dispatcher.UIThread.VerifyAccess();
        MainProbe.Dispose();
        SettingsProbe.Dispose();
    }
}
