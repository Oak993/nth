using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.App.Port.PcControl;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY: Tactus's native smoke (unit 15, P19; ED-NEW-pcctl-43; DESIGN.md 5.4). NATIVE: the real composition over the
// real Windows backends (UI Automation, WinEvents, capture, messages, the hooks, the Ctrl+Alt+Esc chord), for the native
// test classes only, which every regular run excludes by name. A test approver grants "this chat" to the named test apps
// only (Notepad, Calculator, Explorer, Paint and the smoke's own PowerShell windows); any other app gets no grant, and its
// ask runs out unanswered, so the engine refuses it. Each probe writes RESULT lines to the console and to
// %TEMP%\tactus-smoke\<probe>.log, which scripts/pc-control-smoke.ps1 collects. Run only alone, with focus, with the
// owner's go-ahead.
public sealed class PcSmokeRig : IDisposable
{
    /// <summary>The apps the test approver lets the engine use (exe names, lower case).</summary>
    public static IReadOnlySet<string> TestApps { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "notepad.exe", "calculatorapp.exe", "calc.exe", "applicationframehost.exe", "explorer.exe", "mspaint.exe", "powershell.exe",
    };

    public const string Thread = "tactus-smoke";

    private readonly IDisposable primary;
    private readonly IDisposable encoder;
    private readonly List<Process> started = [];
    private readonly string log;
    private readonly Lock gate = new();

    private readonly HashSet<string> approved;

    /// <param name="alsoApprove">More exe names the approver grants for this probe (the owner's own apps a probe reads).</param>
    public PcSmokeRig(string probe, PcSettings? settings = null, IEnumerable<string>? alsoApprove = null)
    {
        Settings = settings ?? PcSettings.Default with { Enabled = true };
        approved = new HashSet<string>(TestApps, StringComparer.OrdinalIgnoreCase);
        foreach (var exe in alsoApprove ?? []) approved.Add(exe);
        primary = PcCaptureSeams.Primary.Override(() => WgcWindowCapture.IsSupported ? WgcWindowCapture.Shared : null);
        encoder = PcCaptureSeams.Encoder.Override(() => JpegEncoder.Shared);
        Composition = new TactusComposition(new TactusHost
        {
            Settings = () => Settings,
            Backends = TactusDesktop.Backends,
            Hotkeys = input => new HotkeyThread(input),
            IdleMs = TactusDesktop.IdleMs,
            Surface = () => new PcSurface { Standing = (app, _) => approved.Contains(app.ExeName) ? PcApproval.Chat : null },
        });
        Parts = Composition.PartsFor(Thread) ?? throw new InvalidOperationException("Tactus did not build its parts.");
        Engine = new PcControlEngine(Parts, new PcCaller(Thread, () => true, Vision: true));
        var folder = Path.Combine(Path.GetTempPath(), "tactus-smoke");
        Directory.CreateDirectory(folder);
        log = Path.Combine(folder, probe + ".log");
        File.WriteAllText(log, "probe " + probe + " " + DateTime.Now.ToString("s", CultureInfo.InvariantCulture) + Environment.NewLine);
        Probe = probe;
    }

    public string Probe { get; }
    public PcSettings Settings { get; set; }
    public TactusComposition Composition { get; }
    public PcEngineParts Parts { get; }
    public PcControlEngine Engine { get; }

    /// <summary>One tool call with its arguments as an anonymous object or a dictionary; the result is logged.</summary>
    public async Task<PcResult> Call(string tool, object arguments, int timeoutSeconds = 60)
    {
        var args = JsonNode.Parse(JsonSerializer.Serialize(arguments)) as JsonObject ?? [];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        var watch = Stopwatch.StartNew();
        var result = await Engine.Call(tool, args, timeout.Token);
        Note(tool + " " + args.ToJsonString() + " -> " + (result.Error?.ToString() ?? result.Status.ToString()) + " in " + watch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + " ms");
        return result;
    }

    /// <summary>A RESULT line: what the probe measured or decided, for the ED entry.</summary>
    public void Result(string text) => Write("RESULT " + Probe + " " + text);

    /// <summary>A plain log line.</summary>
    public void Note(string text) => Write("  " + text);

    /// <summary>Starts a program and returns its new top-level window (by class or title), waiting up to 20 s.</summary>
    public async Task<nint> Launch(string file, string? arguments, Func<string, string, bool> isWindow)
    {
        var before = Windows().Select(w => w.Handle).ToHashSet();
        var process = Process.Start(new ProcessStartInfo(file, arguments ?? "") { UseShellExecute = true });
        if (process is not null) started.Add(process);
        var deadline = Environment.TickCount64 + 20_000;
        while (Environment.TickCount64 < deadline)
        {
            var match = Windows().FirstOrDefault(w => !before.Contains(w.Handle) && isWindow(w.Class, w.Title));
            if (match.Handle != 0)
            {
                Note("launched " + file + " -> " + PcWindowRef.Format(match.Handle) + " \"" + match.Title + "\" (" + match.Class + ")");
                return match.Handle;
            }
            await Task.Delay(200);
        }
        throw new TimeoutException(file + " opened no window in 20 s.");
    }

    /// <summary>
    /// Marks borrow ways as proven for one exe, as importing NP-4's results would (R4 refuses every borrow until then). Only
    /// the smoke items that test R4 itself call it, and their RESULT line says so.
    /// </summary>
    public void Prove(string exe, params string[] methods)
    {
        var capabilities = (Composition.Assembly ?? throw new InvalidOperationException("Tactus is off.")).Capabilities;
        foreach (var method in methods) capabilities.Passed(PcBorrowProofs.Key(exe, method));
        Note("assumed proven for " + exe + ": " + string.Join(", ", methods));
    }

    /// <summary>The visible top-level windows of one exe (lower case, with ".exe"), top of the Z-order first.</summary>
    public static IReadOnlyList<(nint Handle, string Class, string Title)> WindowsOf(string exe) =>
        [.. Windows().Where(w => string.Equals(ExeOf(w.Handle), exe, StringComparison.OrdinalIgnoreCase) && w.Title.Length > 0)];

    /// <summary>The exe name of a window's process, or "" when it cannot be read.</summary>
    public static string ExeOf(nint window)
    {
        GetWindowThreadProcessId(window, out var id);
        try
        {
            using var process = Process.GetProcessById((int)id);
            return process.ProcessName.ToLowerInvariant() + ".exe";
        }
        catch (ArgumentException)
        {
            return "";
        }
        catch (InvalidOperationException)
        {
            return "";
        }
    }

    /// <summary>The process id of a window.</summary>
    public static int ProcessOf(nint window)
    {
        GetWindowThreadProcessId(window, out var id);
        return (int)id;
    }

    /// <summary>Puts one window exactly over another, without activating either (the covered case).</summary>
    public static void Cover(nint target, nint cover)
    {
        GetWindowRect(target, out var rect);
        SetWindowPos(cover, 0, rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, 0x0010 | 0x0040);
        SetWindowPos(target, cover, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    }

    /// <summary>The window's rect on screen.</summary>
    public static PcRect Bounds(nint window)
    {
        GetWindowRect(window, out var rect);
        return new PcRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    /// <summary>
    /// A WinForms window of the smoke's own, made by PowerShell, with a title and the given control lines (PowerShell that
    /// adds controls to $f; no double quotes). Notepad may open new files as tabs, so a second window is one of these.
    /// </summary>
    public Task<nint> Form(string title, string controls = "") =>
        Launch("powershell.exe", "-NoProfile -Command \"Add-Type -AssemblyName System.Windows.Forms; $f = New-Object System.Windows.Forms.Form; $f.Text = '"
            + title + "'; $f.Width = 640; $f.Height = 480; " + controls + " [void]$f.ShowDialog()\"", (_, text) => text == title);

    /// <summary>The visible top-level windows with their class and title.</summary>
    public static IReadOnlyList<(nint Handle, string Class, string Title)> Windows()
    {
        var found = new List<(nint, string, string)>();
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window)) return true;
            var name = new StringBuilder(256);
            var title = new StringBuilder(512);
            GetClassNameW(window, name, name.Capacity);
            GetWindowTextW(window, title, title.Capacity);
            found.Add((window, name.ToString(), title.ToString()));
            return true;
        }, 0);
        return found;
    }

    /// <summary>The foreground window, the focused control of the foreground thread and the cursor (NP-1's record).</summary>
    public static (nint Foreground, nint Focus, int X, int Y) Snapshot()
    {
        var foreground = GetForegroundWindow();
        var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        var focus = GetGUIThreadInfo(GetWindowThreadProcessId(foreground, out _), ref info) ? info.hwndFocus : 0;
        GetCursorPos(out var point);
        return (foreground, focus, point.X, point.Y);
    }

    public static string Describe((nint Foreground, nint Focus, int X, int Y) state) =>
        "fg=" + PcWindowRef.Format(state.Foreground) + " focus=" + PcWindowRef.Format(state.Focus) + " cursor=" + state.X.ToString(CultureInfo.InvariantCulture) + "," + state.Y.ToString(CultureInfo.InvariantCulture);

    /// <summary>Minimizes or restores a window without activating it (for the covered and minimized cases).</summary>
    public static void Show(nint window, bool minimized) => ShowWindow(window, minimized ? 7 : 4);

    /// <summary>Closes a window the probe opened (WM_CLOSE; an unsaved document's prompt stays for the owner).</summary>
    public static void Close(nint window) => PostMessageW(window, 0x0010, 0, 0);

    public void Dispose()
    {
        Engine.Dispose();
        Composition.Dispose();
        encoder.Dispose();
        primary.Dispose();
        foreach (var process in started) process.Dispose();
    }

    private void Write(string line)
    {
        Console.WriteLine(line);
        lock (gate) File.AppendAllText(log, line + Environment.NewLine);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public nint hwndActive;
        public nint hwndFocus;
        public nint hwndCapture;
        public nint hwndMenuOwner;
        public nint hwndMoveSize;
        public nint hwndCaret;
        public RECT rcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    private delegate bool EnumWindowsProc(nint window, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetClassNameW(nint window, StringBuilder name, int capacity);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetWindowTextW(nint window, StringBuilder text, int capacity);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint thread, ref GUITHREADINFO info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out RECT rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);
}
