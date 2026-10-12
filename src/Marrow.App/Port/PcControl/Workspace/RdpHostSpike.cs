// PORT-ONLY: Tactus's workspace hosting spike (unit P18a, debug builds only, never shipped; ED-NEW-pcctl-33; DESIGN.md 2.13
// "Revised 2026-10-11 (critique C20)", section 8 P18a, NP-11). Run by hand, never a test: `Marrow.exe --tactus-overlay-spike
// np11-rdp [--rdp-class <ProgID or {CLSID}>]`, steps in scripts/pc-control-overlay-spike.ps1. It answers NP-11: can Avalonia 12
// host the Remote Desktop ActiveX control (on Avalonia's STA UI thread, in a native child HWND under a NativeControlHost, the
// control made from its ProgID by ATL's AtlAxWin container), connect it to a child session, and keep it rendering while its
// window is cloaked (DWMWA_CLOAK) and while the window is minimized. It changes no Windows setting: when child sessions are
// off (WTSIsChildSessionsEnabled) it hosts the control, says so and connects nothing (NP-5 is where they are turned on). It
// sends no input to any window. The child session's shell is a bounded heartbeat (SecuredSettings.StartProgram: a cmd loop
// that appends a line to %TEMP%\tactus-np11-heartbeat.txt every second for 2 minutes, then ends, which logs the session off),
// so the session's own progress in each phase is the file's growth, and what the control draws is read by PrintWindow
// (PW_RENDERFULLCONTENT) of the host window every 250 ms: how many grabs differ from the one before. RESULT np11 hosted=..
// extended=.. childSessions=.. connected=.. visible=a/n cloaked=b/n minimized=c/n heartbeat=visible:x cloaked:y minimized:z.
#if DEBUG
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Marrow.App.Port.PcControl.Overlay;

namespace Marrow.App.Port.PcControl.Workspace;

/// <summary>NP-11: the Remote Desktop control hosted in an Avalonia window, connected to a child session, rendering while hidden.</summary>
internal sealed class RdpProbe(SpikeLog log, SpikeOptions options) : SpikeProbe(log, options)
{
    public const string Probe = "np11-rdp";
    public const string ClassOption = "--rdp-class";
    public const string DefaultClass = "MsTscAx.MsTscAx";
    private const double ConnectSeconds = 30;
    private const double PhaseSeconds = 10;
    private const double GrabSeconds = 0.25;
    private const int HeartbeatLoops = 120;

    private readonly Dictionary<string, (int Changed, int Grabs)> phases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> heartbeats = new(StringComparer.Ordinal);
    private readonly string heartbeat = Path.Combine(Path.GetTempPath(), "tactus-np11-heartbeat.txt");
    private Avalonia.Controls.Window? window;
    private RdpHost? host;
    private dynamic? control;
    private bool hosted;
    private bool extended;
    private bool childSessions;
    private bool connected;

    public override void Open()
    {
        var progId = ClassOf(Environment.GetCommandLineArgs());
        Log.Line($"thread apartment {Thread.CurrentThread.GetApartmentState()}, control class {progId}");
        childSessions = RdpNative.WTSIsChildSessionsEnabled(out var enabled) && enabled;
        Log.Line($"WTSIsChildSessionsEnabled: {childSessions} (this probe never turns them on)");
        if (!RdpNative.AtlAxWinInit())
        {
            Log.Line($"AtlAxWinInit failed ({Marshal.GetLastWin32Error()}): no ActiveX container, nothing hosted");
            return;
        }
        host = new RdpHost(progId);
        window = new Avalonia.Controls.Window { Title = "Tactus NP-11", Width = 1024, Height = 640, Content = host };
        window.Opened += (_, _) => DispatcherTimer.RunOnce(Hosted, TimeSpan.FromSeconds(1));
        window.Show();
    }

    public override void Finish()
    {
        try
        {
            if (connected) control?.Disconnect();
        }
        catch (COMException error)
        {
            Log.Line($"Disconnect failed: 0x{error.HResult:x8}");
        }
        string Phase(string name) => phases.TryGetValue(name, out var p) ? $"{p.Changed}/{p.Grabs}" : "n/a";
        string Beat(string name) => heartbeats.TryGetValue(name, out var lines) ? lines.ToString(System.Globalization.CultureInfo.InvariantCulture) : "n/a";
        Log.Line($"RESULT {Probe} hosted={hosted} extended={extended} childSessions={childSessions} connected={connected} "
            + $"visible={Phase("visible")} cloaked={Phase("cloaked")} minimized={Phase("minimized")} "
            + $"heartbeat=visible:{Beat("visible")} cloaked:{Beat("cloaked")} minimized:{Beat("minimized")}");
        window?.Close();
    }

    public static string ClassOf(IReadOnlyList<string> args)
    {
        var at = args.ToList().IndexOf(ClassOption);
        return at >= 0 && at + 1 < args.Count && args[at + 1].Length > 0 ? args[at + 1] : DefaultClass;
    }

    // The container window exists: is there a control in it, and does it take the child-session setting?
    private void Hosted()
    {
        if (host is not { Container: not 0 } made)
        {
            Log.Line($"the AtlAxWin child window was not made ({host?.Error}): nothing hosted");
            return;
        }
        if (RdpNative.AtlAxGetControl(made.Container, out var unknown) != 0 || unknown is null)
        {
            Log.Line("AtlAxGetControl gave no control: the class did not load in the container");
            return;
        }
        hosted = true;
        control = unknown;
        try
        {
            Log.Line($"control version {control.Version}, SecuredSettingsEnabled {control.SecuredSettingsEnabled}");
        }
        catch (Exception error) when (error is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            Log.Line($"the control does not answer Version or SecuredSettingsEnabled: {error.Message}");
        }
        if (unknown is IMsRdpExtendedSettings settings)
        {
            extended = true;
            if (!childSessions)
            {
                Log.Line("IMsRdpExtendedSettings is there; child sessions are off, so nothing connects (NP-5 first)");
                return;
            }
            object on = true;
            settings.put_Property("ConnectToChildSession", ref on);
        }
        else
        {
            Log.Line("the control has no IMsRdpExtendedSettings: it cannot connect to a child session");
            return;
        }
        Connect();
    }

    private void Connect()
    {
        try
        {
            if (File.Exists(heartbeat)) File.Delete(heartbeat);
            control!.Server = "localhost";
            control.SecuredSettings.StartProgram = $"cmd.exe /c for /l %i in (1,1,{HeartbeatLoops}) do (echo .>>\"{heartbeat}\" & ping -n 2 127.0.0.1 >nul)";
            control.Connect();
            Log.Line("Connect called");
        }
        catch (Exception error) when (error is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            Log.Line($"Connect failed: {error.Message}");
            return;
        }
        var started = SpikeShuttle.Clock();
        DispatcherTimer? poll = null;
        poll = new DispatcherTimer(TimeSpan.FromSeconds(0.5), DispatcherPriority.Background, (_, _) =>
        {
            int state;
            try
            {
                state = (int)control!.Connected;
            }
            catch (COMException)
            {
                state = -1;
            }
            if (state == 1)
            {
                poll!.Stop();
                connected = true;
                Log.Line($"connected after {SpikeShuttle.Clock() - started:F1} s");
                Phases();
            }
            else if (SpikeShuttle.Clock() - started > ConnectSeconds)
            {
                poll!.Stop();
                Log.Line($"not connected after {ConnectSeconds} s (Connected = {state})");
            }
        });
        poll.Start();
    }

    // Visible, then cloaked, then minimized: what the control draws and how far the session's heartbeat gets in each.
    private void Phases()
    {
        var shown = window!;
        var top = shown.TryGetPlatformHandle()?.Handle ?? 0;
        Phase("visible", top, () => { }, () =>
            Phase("cloaked", top, () => Log.Line($"cloak: 0x{RdpNative.Cloak(top, true):x8}"), () =>
            {
                Log.Line($"uncloak: 0x{RdpNative.Cloak(top, false):x8}");
                Phase("minimized", top, () => shown.WindowState = WindowState.Minimized, () => shown.WindowState = WindowState.Normal);
            }));
    }

    private void Phase(string name, nint top, Action enter, Action next)
    {
        enter();
        var lines = HeartbeatLines();
        var grabs = 0;
        var changed = 0;
        ulong? last = null;
        var until = SpikeShuttle.Clock() + PhaseSeconds;
        DispatcherTimer? timer = null;
        timer = new DispatcherTimer(TimeSpan.FromSeconds(GrabSeconds), DispatcherPriority.Background, (_, _) =>
        {
            if (RdpNative.Grab(top) is { } hash)
            {
                grabs++;
                if (last is { } before && before != hash) changed++;
                last = hash;
            }
            if (SpikeShuttle.Clock() < until) return;
            timer!.Stop();
            phases[name] = (changed, grabs);
            heartbeats[name] = HeartbeatLines() - lines;
            Log.Line($"{name}: {changed} of {grabs} grabs changed, heartbeat +{heartbeats[name]} lines");
            next();
        });
        timer.Start();
    }

    private long HeartbeatLines()
    {
        try
        {
            return File.Exists(heartbeat) ? File.ReadLines(heartbeat).LongCount() : 0;
        }
        catch (IOException)
        {
            return -1;
        }
    }
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
        Container = RdpNative.CreateWindowExW(0, "AtlAxWin", progId, WsChild | WsVisible | WsClipChildren | WsClipSiblings, 0, 0, 1024, 600,
            parent.Handle, 0, RdpNative.GetModuleHandleW(null), 0);
        if (Container != 0) return new PlatformHandle(Container, "HWND");
        Error = Marshal.GetLastWin32Error();
        return base.CreateNativeControlCore(parent);
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (control.Handle != 0 && control.Handle == Container)
        {
            RdpNative.DestroyWindow(Container);
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

internal static class RdpNative
{
    private const int DwmwaCloak = 13;
    private const uint PwRenderFullContent = 2;

    /// <summary>Cloaks or uncloaks one of our own windows (DWMWA_CLOAK); the HRESULT.</summary>
    public static int Cloak(nint window, bool on)
    {
        var value = on ? 1 : 0;
        return DwmSetWindowAttribute(window, DwmwaCloak, ref value, sizeof(int));
    }

    /// <summary>A hash of the window's pixels as PrintWindow (PW_RENDERFULLCONTENT) draws them; null when it could not.</summary>
    public static ulong? Grab(nint window)
    {
        if (window == 0 || !GetClientRect(window, out var rect)) return null;
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0) return null;
        var screen = GetDC(0);
        var dc = CreateCompatibleDC(screen);
        var info = new BitmapInfoHeader { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
        var bitmap = CreateDIBSection(dc, ref info, 0, out var bits, 0, 0);
        try
        {
            if (bitmap == 0) return null;
            var old = SelectObject(dc, bitmap);
            var drawn = PrintWindow(window, dc, PwRenderFullContent);
            SelectObject(dc, old);
            if (!drawn) return null;
            var bytes = new byte[width * height * 4];
            Marshal.Copy(bits, bytes, 0, bytes.Length);
            var hash = 14695981039346656037UL;
            foreach (var word in MemoryMarshal.Cast<byte, ulong>(bytes)) hash = (hash ^ word) * 1099511628211UL;
            return hash;
        }
        finally
        {
            if (bitmap != 0) DeleteObject(bitmap);
            DeleteDC(dc);
            ReleaseDC(0, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
        // one empty colour entry: BITMAPINFO's bmiColors[1]
        public uint Colors;
    }

    [DllImport("atl.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AtlAxWinInit();

    [DllImport("atl.dll")]
    public static extern int AtlAxGetControl(nint window, [MarshalAs(UnmanagedType.IUnknown)] out object? control);

    [DllImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WTSIsChildSessionsEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(nint window);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandleW(string? module);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(nint window, nint dc, uint flags);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint dc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint dc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateDIBSection(nint dc, ref BitmapInfoHeader info, uint usage, out nint bits, nint section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint dc, nint item);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint item);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(nint dc);
}
#endif
