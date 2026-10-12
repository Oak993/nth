// PORT-ONLY: Tactus, the separate workspace (unit P18, preview; ED-NEW-pcws-7, ED-NEW-pcws-10; DESIGN.md 2.13, 3.1; NP-6,
// NP-7, NP-11). The window that holds the Remote Desktop control connected to the child session. It is cloaked
// (DWMWA_CLOAK, our own window) before it is ever shown, so it keeps the control rendering without being seen; it is not in
// the taskbar. "Watch workspace" uncloaks it as a view-only picture in picture: the control's window takes no input until
// "Take over", which gives it the user's mouse and keyboard while Tactus's steps in the workspace wait; "Give back" returns
// them. Esc, Close or the window's close button only hides the view again (the workspace keeps running) and gives focus back
// to the window the user came from. The control (ActiveX through ATL's AtlAxWin in a NativeControlHost, made from
// MsTscAx.MsTscAx as the P18a spike does) and the cloak sit behind seams, so headless tests host a plain control.
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform;
using Avalonia.Threading;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.PcControl.Workspace;

/// <summary>The workspace's window as ChildSessionHost drives it; every member is safe to call from any thread.</summary>
internal interface IWorkspaceSurface : IDisposable
{
    /// <summary>Connects to a new child session whose shell is <paramref name="startProgram"/>; null when connected, else why not.</summary>
    Task<string?> Connect(string startProgram, CancellationToken cancellationToken);

    /// <summary>Shows the view-only picture in picture.</summary>
    void Watch();

    /// <summary>The child session ended or disconnected.</summary>
    event Action? Ended;

    /// <summary>The user took over (true) or gave it back (false).</summary>
    event Action<bool>? TakenOver;
}

/// <summary>The Remote Desktop control (behind a seam: headless tests host a plain control).</summary>
internal interface IRdpControl : IDisposable
{
    Control View { get; }

    /// <summary>After the view is in a shown window: the control made and set for a child session; null when ready, else why not.</summary>
    string? Prepare();

    /// <summary>Starts connecting; null when it started, else why not (never the program line, which holds the token).</summary>
    string? Connect(string startProgram);

    /// <summary>0 not connected, 1 connected, 2 connecting; -1 when the control did not answer.</summary>
    int State { get; }

    void Disconnect();

    /// <summary>Whether the control's window takes the user's mouse and keyboard.</summary>
    void TakeInput(bool on);
}

/// <summary>Cloaking and focus for the view's own window (behind a seam for headless tests).</summary>
internal interface IWorkspaceWindowing
{
    void Cloak(Avalonia.Controls.Window window, bool on);

    /// <summary>The window in front before the view was shown.</summary>
    nint Foreground();

    void GiveFocus(nint window);
}

internal sealed class WorkspaceView : Avalonia.Controls.Window, IWorkspaceSurface
{
    public const string ViewTitle = "Separate workspace";
    public const string ViewOnly = "View only. Tactus keeps working here.";
    public const string YoursNow = "You have its mouse and keyboard. Tactus waits until you give it back.";
    public const string TakeOverLabel = "Take over";
    public const string GiveBackLabel = "Give back";
    public const string CloseLabel = "Close";
    public const string TakeOverHelp = "Gives the workspace your mouse and keyboard. Tactus's steps there wait until you give it back.";
    public const string CloseHelp = "Hides this view. The workspace keeps running. Esc";
    public const int ConnectTimeoutMs = 30_000;
    public const int PollMs = 500;
    public const int WatchSessionMs = 2_000;

    private readonly IRdpControl rdp;
    private readonly IWorkspaceWindowing windowing;
    private readonly Func<int, CancellationToken, Task> delay;
    private readonly TextBlock status = new() { Text = ViewOnly, VerticalAlignment = VerticalAlignment.Center, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private DispatcherTimer? session;
    private nint cameFrom;
    private bool opened;
    private bool closing;
    private bool connected;
    private TaskCompletionSource? shown;

    public WorkspaceView(IRdpControl rdp, IWorkspaceWindowing windowing, Func<int, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(rdp);
        ArgumentNullException.ThrowIfNull(windowing);
        this.rdp = rdp;
        this.windowing = windowing;
        this.delay = delay ?? Task.Delay;
        Title = ViewTitle;
        Width = 960;
        Height = 640;
        ShowInTaskbar = false;
        AutomationProperties.SetName(this, ViewTitle);
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);

        TakeOverButton = new CzCapsuleButton(TakeOverLabel, detail: TakeOverHelp);
        TakeOverButton.Invoked += TakeOver;
        GiveBackButton = new CzCapsuleButton(GiveBackLabel) { IsVisible = false };
        GiveBackButton.Invoked += GiveBack;
        CloseButton = new CzCapsuleButton(CloseLabel, detail: CloseHelp);
        CloseButton.Invoked += CloseView;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { TakeOverButton, GiveBackButton, CloseButton } };
        DockPanel.SetDock(buttons, Dock.Right);
        var bar = new DockPanel { Margin = new Thickness(12, 8), Children = { buttons, status } };
        DockPanel.SetDock(bar, Dock.Top);
        Content = new DockPanel { Children = { bar, rdp.View } };

        Opened += (_, _) =>
        {
            opened = true;
            shown?.TrySetResult();
        };
        Closing += (_, e) =>
        {
            if (closing) return;
            // the close button hides the view; the workspace keeps running until nth ends it
            e.Cancel = true;
            CloseView();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None || IsTakenOver) return;
            e.Handled = true;
            CloseView();
        };
    }

    public event Action? Ended;

    public event Action<bool>? TakenOver;

    public CzCapsuleButton TakeOverButton { get; }

    public CzCapsuleButton GiveBackButton { get; }

    public CzCapsuleButton CloseButton { get; }

    public string Status => status.Text ?? "";

    /// <summary>Whether the view is on screen (not cloaked).</summary>
    public bool Watching { get; private set; }

    public bool IsTakenOver { get; private set; }

    public Task<string?> Connect(string startProgram, CancellationToken cancellationToken) =>
        Dispatcher.UIThread.InvokeAsync(() => ConnectHere(startProgram, cancellationToken));

    public void Watch() => Dispatcher.UIThread.Post(WatchHere);

    /// <summary>Hides the view: gives the input back, cloaks the window and returns focus to where the user was.</summary>
    public void CloseView()
    {
        if (!Watching) return;
        if (IsTakenOver) GiveBack();
        Watching = false;
        Topmost = false;
        windowing.Cloak(this, true);
        if (cameFrom != 0) windowing.GiveFocus(cameFrom);
        cameFrom = 0;
    }

    public void TakeOver()
    {
        if (!Watching || IsTakenOver) return;
        IsTakenOver = true;
        rdp.TakeInput(true);
        rdp.View.Focus();
        status.Text = YoursNow;
        TakeOverButton.IsVisible = false;
        GiveBackButton.IsVisible = true;
        TakenOver?.Invoke(true);
    }

    public void GiveBack()
    {
        if (!IsTakenOver) return;
        IsTakenOver = false;
        rdp.TakeInput(false);
        status.Text = ViewOnly;
        GiveBackButton.IsVisible = false;
        TakeOverButton.IsVisible = true;
        TakenOver?.Invoke(false);
        TakeOverButton.Focus();
    }

    public void Dispose() => Dispatcher.UIThread.Post(() =>
    {
        session?.Stop();
        if (IsTakenOver) GiveBack();
        if (connected) rdp.Disconnect();
        rdp.Dispose();
        closing = true;
        Close();
    });

    private async Task<string?> ConnectHere(string startProgram, CancellationToken cancellationToken)
    {
        // cloaked before it is ever shown: it renders, and nobody sees it until "Watch workspace"
        windowing.Cloak(this, true);
        if (!opened)
        {
            shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Show();
            await shown.Task.WaitAsync(cancellationToken);
        }
        rdp.TakeInput(false);
        if (rdp.Prepare() is { } notReady) return notReady;
        if (rdp.Connect(startProgram) is { } refused) return refused;
        for (var waited = 0; waited < ConnectTimeoutMs; waited += PollMs)
        {
            switch (rdp.State)
            {
                case 1:
                    connected = true;
                    WatchSession();
                    return null;
                case 0 when waited > 0:
                    return "the Remote Desktop control did not connect to a child session";
            }
            await delay(PollMs, cancellationToken);
        }
        rdp.Disconnect();
        return "the child session did not connect within 30 s";
    }

    // The session ends when the agent's shell ends or Windows disconnects it.
    private void WatchSession()
    {
        session = new DispatcherTimer(TimeSpan.FromMilliseconds(WatchSessionMs), DispatcherPriority.Background, (_, _) =>
        {
            if (rdp.State is 1 or 2) return;
            session?.Stop();
            connected = false;
            if (IsTakenOver) GiveBack();
            CloseView();
            Ended?.Invoke();
        });
        session.Start();
    }

    private void WatchHere()
    {
        if (!opened || Watching) return;
        cameFrom = windowing.Foreground();
        Watching = true;
        rdp.TakeInput(false);
        status.Text = ViewOnly;
        Topmost = true;
        windowing.Cloak(this, false);
        Activate();
        TakeOverButton.Focus();
    }
}

/// <summary>The Remote Desktop ActiveX control in ATL's container window, as the P18a spike hosts it.</summary>
internal sealed class RdpActiveX : IRdpControl
{
    public const string ControlClass = "MsTscAx.MsTscAx";

    private readonly RdpHost host = new(ControlClass);
    private dynamic? control;

    public Control View => host;

    public string? Prepare()
    {
        if (!WorkspaceNative.AtlAxWinInit()) return "the ActiveX container could not start";
        if (host.Container == 0) return "the Remote Desktop control's window was not made";
        if (WorkspaceNative.AtlAxGetControl(host.Container, out var unknown) != 0 || unknown is null) return "the Remote Desktop control did not load";
        if (unknown is not IMsRdpExtendedSettings settings) return "this Remote Desktop control cannot connect to a child session";
        try
        {
            object on = true;
            settings.put_Property("ConnectToChildSession", ref on);
        }
        catch (COMException error)
        {
            return "the Remote Desktop control refused the child-session setting (0x" + error.HResult.ToString("x8", System.Globalization.CultureInfo.InvariantCulture) + ")";
        }
        control = unknown;
        return null;
    }

    public string? Connect(string startProgram)
    {
        if (control is null) return "the Remote Desktop control is not ready";
        try
        {
            control.Server = "localhost";
            control.SecuredSettings.StartProgram = startProgram;
            control.Connect();
            return null;
        }
        catch (Exception error) when (error is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            // the HRESULT only: the message could carry the program line, which holds the token
            return "the Remote Desktop control did not start connecting (0x" + error.HResult.ToString("x8", System.Globalization.CultureInfo.InvariantCulture) + ")";
        }
    }

    public int State
    {
        get
        {
            if (control is null) return 0;
            try
            {
                return (int)control.Connected;
            }
            catch (Exception error) when (error is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
            {
                _ = error;
                return -1;
            }
        }
    }

    public void Disconnect()
    {
        if (control is null || State == 0) return;
        try
        {
            control.Disconnect();
        }
        catch (Exception error) when (error is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            // the session may already be gone; the window goes with the view
            _ = error;
        }
    }

    public void TakeInput(bool on)
    {
        if (host.Container != 0) WorkspaceNative.EnableWindow(host.Container, on);
        if (on && host.Container != 0) WorkspaceNative.SetFocus(host.Container);
    }

    public void Dispose()
    {
        if (control is not null) Marshal.ReleaseComObject(control);
        control = null;
    }
}

/// <summary>DWMWA_CLOAK on the view's own window, and focus back to where the user was.</summary>
internal sealed class WorkspaceWindowing : IWorkspaceWindowing
{
    public void Cloak(Avalonia.Controls.Window window, bool on)
    {
        if (window.TryGetPlatformHandle()?.Handle is { } handle and not 0) WorkspaceNative.Cloak(handle, on);
    }

    public nint Foreground() => WorkspaceNative.GetForegroundWindow();

    public void GiveFocus(nint window) => WorkspaceNative.SetForegroundWindow(window);
}

/// <summary>The native child window ATL's container makes for the control (its window text names the class).</summary>
internal sealed class RdpHost(string progId) : NativeControlHost
{
    private const uint WsChild = 0x40000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsClipChildren = 0x02000000;
    private const uint WsClipSiblings = 0x04000000;

    public nint Container { get; private set; }

    public int Error { get; private set; }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        Container = WorkspaceNative.CreateWindowExW(0, "AtlAxWin", progId, WsChild | WsVisible | WsClipChildren | WsClipSiblings, 0, 0, 1024, 600,
            parent.Handle, 0, WorkspaceNative.GetModuleHandleW(null), 0);
        if (Container != 0) return new PlatformHandle(Container, "HWND");
        Error = Marshal.GetLastWin32Error();
        return base.CreateNativeControlCore(parent);
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (control.Handle != 0 && control.Handle == Container)
        {
            WorkspaceNative.DestroyWindow(Container);
            Container = 0;
            return;
        }
        base.DestroyNativeControlCore(control);
    }
}

/// <summary>The control's extended settings (mstscax.dll): "ConnectToChildSession" is set through it.</summary>
[ComImport]
[Guid("302D8188-0052-4807-806A-362B628F9AC5")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMsRdpExtendedSettings
{
    void put_Property([MarshalAs(UnmanagedType.BStr)] string name, [MarshalAs(UnmanagedType.Struct)] ref object value);

    void get_Property([MarshalAs(UnmanagedType.BStr)] string name, [MarshalAs(UnmanagedType.Struct)] out object value);
}

internal static class WorkspaceNative
{
    private const int DwmwaCloak = 13;

    /// <summary>Cloaks or uncloaks one of our own windows (DWMWA_CLOAK); the HRESULT.</summary>
    public static int Cloak(nint window, bool on)
    {
        var value = on ? 1 : 0;
        return DwmSetWindowAttribute(window, DwmwaCloak, ref value, sizeof(int));
    }

    [DllImport("atl.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AtlAxWinInit();

    [DllImport("atl.dll")]
    public static extern int AtlAxGetControl(nint window, [MarshalAs(UnmanagedType.IUnknown)] out object? control);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(nint window);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandleW(string? module);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnableWindow(nint window, [MarshalAs(UnmanagedType.Bool)] bool enable);

    [DllImport("user32.dll")]
    public static extern nint SetFocus(nint window);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(nint window);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
