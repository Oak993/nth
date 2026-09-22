// P2, task packet T-M0-001: snapshot budget at production scale.
//
// QUESTION: on Avalonia 12.1.2 / .NET 10, with P1's two-pass glass (app-owned snapshot of the live content, then an
// SKSL lens over it) scaled to the real count of 22 glass sites, in a 1320x860 window, over a transcript that streams at
// the source's 45 ms flush cadence, with .interactive() pointer tracking live, does the frame fit the budget?
//
// PRE-REGISTERED PASS (from the packet, fixed before the first run and never changed):
//   median frame time <= 16.7 ms AND 99th percentile <= 33.0 ms, measured by an in-app frame timer (the interval
//   between consecutive executions of the scene's render operation on Avalonia's render thread, with the scene
//   invalidated on every UI frame), AND GPU time of the snapshot pass, measured separately with GL timer queries,
//   at or under 3.0 ms at the 99th percentile ("staying under about 3 ms").
//   Preconditions, all measured, or the run is INCONCLUSIVE (exit 2): the screen is live (a capture of our own window
//   finds our own marker), the render really ran on a GPU (lease GrContext non-null, GL_RENDERER names a hardware
//   adapter), the GPU timer returned valid results for >= 90% of frames, the 45 ms flushes ran (cadence median within
//   45..50 ms), and the pointer tracking was live (PointerMoved events reached the window, < 5% input misses).
//
// CONTROLS (each must be flagged by the same detectors that judge the subject):
//   --ctl stall     Thread.Sleep(30) inside the render operation: the frame timer must report median > 16.7 ms.
//   --ctl gpuheavy  a GPU-bound shader (tiny CPU cost) drawn over every snapshot surface inside the snapshot pass:
//                   the GPU timer must report snapshot p99 > 3.0 ms while the CPU time of the operation stays small.
//   --render software  Avalonia's CPU raster backend: the GPU-path detector must say False (no GrContext).
//   --config noglass   the same scene with no glass: the baseline the glass cost is measured against.
//
// Every window this probe touches is its own. Input is SendInput absolute pointer moves only (no buttons, no keys), to
// points where WindowFromPoint returns our own HWND, checked before every move. The cursor is restored at the end.
// Evidence: BitBlt of the screen DC (DWM-composed) over our own window's rectangle, from this process.
//
// Usage: p2 --api
//        p2 --out <dir> [--config packet|source|persite|noglass] [--adapter default|amd|nvidia] [--render gpu|software]
//                       [--ctl none|stall|gpuheavy] [--seconds 12] [--heavy 12]
// Exit 0 PASS, 1 FAIL, 2 INCONCLUSIVE, 3 control run (read its CONTROL line), 4 crash.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using SkiaSharp;

// ---- SOURCE FACTS (pinned Droppy Code 1.6.2, E:\Droppy\reference\droppy-code-v1.6.2) ---------------------------------
static class Src
{
    public const int W = 1320, H = 860;                   // WindowManager.swift:19  static let main = NSSize(width: 1320, height: 860)
    public const int FlushMs = 45;                        // ThreadRuntime.swift:3128 (case ..<4_096: 45) and :3138 flushWindow .milliseconds(45)
    public const int FlushTier = 4096;                    // ThreadRuntime.swift:3128: 45 ms only while the row is under 4 KB
    public const float SpacingChatRow = 2;                // ChatView.swift:1123   GlassEffectContainer(spacing: 2)
    public const float SpacingDiff = 2;                   // DiffInspector.swift:25 GlassEffectContainer(spacing: 2)
    public const float SpacingComposer = 8;               // ComposerView.swift:23  GlassEffectContainer(spacing: 8)
    public const float ChromeHPad = 14, ChromeTop = 12;   // Chrome.swift:31-32
    public const float CapsuleH = 32;                     // Chrome.swift:33-38 capsuleContentHeight 28 + 2 * capsuleVerticalPadding 2
    public const float RowSpacing = 10, TrailSpacing = 8; // ChatView.swift:1124, :1152
    public const float CardRadius = 16;                   // Chrome.swift:58 cardCornerRadius
    public const float ComposerRadius = 22;               // ComposerView.swift:1298
    public const float ComposerMaxW = 820;                // ComposerView.swift:52-54 maxWidth 820, padding h 20, bottom 14
    public const float RequestRadius = 20;                // RequestCards.swift:64
    public const float TabRadius = 12;                    // ThreadChangesTab.swift:15, FollowUpQueueTab.swift:47 (top corners)
    public const float PanelRadius = 22;                  // HydraPanel.swift:33, SubagentPanel.swift:265 (FloatingSidebarPanel.swift:35, UsageFloatingPanel.swift:25 use HydraPanel's)
    public const float PaletteRadius = 24;                // CommandPalette.swift:97
    public const float MinimapCardW = 300;                // TimelineMinimap.swift:356
    public const float TintAlpha = 0.30f;                 // Chrome.swift:180 Chrome.glassTint.opacity(0.3)
}

static class N
{
    public const int SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const uint GA_ROOT = 2, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const int SPI_GETWORKAREA = 0x30;
    public const uint INPUT_MOUSE = 0, MOUSEEVENTF_MOVE = 0x1, MOUSEEVENTF_ABSOLUTE = 0x8000, MOUSEEVENTF_VIRTUALDESK = 0x4000;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int L, T, R, B;
        public readonly int W => R - L; public readonly int H => B - T;
        public override readonly string ToString() => $"[{L},{T}]-[{R},{B}] {W}x{H}";
    }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
    }

    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint f);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] public static extern bool SystemParametersInfoW(int a, int b, out RECT r, int c);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, int rop);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bi, uint usage);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int size);
    [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint p);
    [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint p);

    public static RECT FrameBounds(IntPtr h)
    {
        if (DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out var r, Marshal.SizeOf<RECT>()) == 0) return r;
        GetWindowRect(h, out r);
        return r;
    }

    /// Screen DC BitBlt: the DWM-composed image of the rectangle, as top-down BGRA.
    public static byte[] Capture(RECT r)
    {
        IntPtr scr = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(scr), bmp = CreateCompatibleBitmap(scr, r.W, r.H);
        IntPtr old = SelectObject(mem, bmp);
        bool ok = BitBlt(mem, 0, 0, r.W, r.H, scr, r.L, r.T, SRCCOPY | CAPTUREBLT);
        SelectObject(mem, old);
        var bi = new BITMAPINFOHEADER { biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = r.W, biHeight = -r.H, biPlanes = 1, biBitCount = 32 };
        var buf = new byte[r.W * r.H * 4];
        int lines = GetDIBits(mem, bmp, 0, (uint)r.H, buf, ref bi, 0);
        DeleteObject(bmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, scr);
        if (!ok || lines != r.H) throw new InvalidOperationException($"capture failed bitblt={ok} lines={lines}");
        for (int i = 3; i < buf.Length; i += 4) buf[i] = 255;
        return buf;
    }

    public static uint MoveAbs(int x, int y)
    {
        int vx = GetSystemMetrics(76), vy = GetSystemMetrics(77), vw = GetSystemMetrics(78), vh = GetSystemMetrics(79);
        int nx = (int)Math.Floor((x - vx + 0.5) * 65536.0 / vw), ny = (int)Math.Floor((y - vy + 0.5) * 65536.0 / vh);
        var i = new[] { new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dx = nx, dy = ny, dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK } } };
        return SendInput(1, i, Marshal.SizeOf<INPUT>());
    }
}

static class Clock
{
    static readonly Stopwatch Sw = Stopwatch.StartNew();
    public static double Ms => Sw.Elapsed.TotalMilliseconds;
}

/// One glass site: shape, tint, interactivity and grouping. Layout is an ENGINEERING choice for the probe (the source
/// sizes most of these from content); shapes, radii, tints, .interactive() flags and container membership are sourced.
sealed record Site(string Name, SKRect R, float Radius, SKColor Tint, float TintA, bool Interactive, int Container, int PacketGroup, string Src);

static class Layout
{
    static readonly SKColor GlassTint = new(0x1E, 0x1E, 0x20);   // AppTheme dark surface #1E1E20 (P1 Src.Tint)
    static readonly SKColor Warning = new(0xFF, 0x9F, 0x0A);     // ENGINEERING stand-in for Chrome.warning (theme-resolved)
    static readonly SKColor White = new(0xFF, 0xFF, 0xFF);

    public static readonly Site[] Sites = Build();

    static Site[] Build()
    {
        var s = new List<Site>();
        float y = Src.ChromeTop, h = Src.CapsuleH, x = Src.ChromeHPad;
        // Container 0: the chat chrome row (ChatView.swift:1123), every capsule and circle through ChromeGlassSurface,
        // i.e. Chrome.swift:180 .glassEffect(.regular.tint(Chrome.glassTint.opacity(0.3)).interactive()).
        Site Row(string n, float w, int pg)
        {
            var site = new Site(n, new SKRect(x, y, x + w, y + h), Math.Min(w, h) / 2, GlassTint, Src.TintAlpha, true, 0, pg, "Chrome.swift:180 via ChatView.swift:1123");
            x += w + Src.RowSpacing;
            return site;
        }
        s.Add(Row("A1 threads circle", 32, 0));
        s.Add(Row("A2 new thread circle", 32, 0));
        s.Add(Row("A3 hydra circle", 32, 0));
        s.Add(Row("A4 permission capsule", 116, 0));
        s.Add(Row("A5 branch capsule", 140, 0));
        s.Add(Row("A6 title capsule", 380, 0));
        float r0 = Src.W - Src.ChromeHPad;
        s.Add(new Site("A7 open|scripts|git capsule", new SKRect(r0 - 76 - Src.TrailSpacing - 136, y, r0 - 76 - Src.TrailSpacing, y + h), h / 2, GlassTint, Src.TintAlpha, true, 0, 0, "Chrome.swift:180 via ChatView.swift:1153"));
        s.Add(new Site("A8 terminal|changes capsule", new SKRect(r0 - 76, y, r0, y + h), h / 2, GlassTint, Src.TintAlpha, true, 0, 0, "Chrome.swift:180 via ChatView.swift:1164"));
        // Container 1: the diff inspector's header (DiffInspector.swift:25).
        float dy = 64;
        s.Add(new Site("B1 diff scope text menu", new SKRect(1000, dy, 1150, dy + h), h / 2, GlassTint, Src.TintAlpha, true, 1, 1, "Chrome.swift:180 via DiffInspector.swift:27"));
        s.Add(new Site("B2 diff more|hide capsule", new SKRect(r0 - 76, dy, r0, dy + h), h / 2, GlassTint, Src.TintAlpha, true, 1, 1, "Chrome.swift:180 via DiffInspector.swift:55"));
        // Container 2: the composer area (ComposerView.swift:23).
        float cl = (Src.W - Src.ComposerMaxW) / 2, cr = cl + Src.ComposerMaxW, cb = Src.H - 14, ct = cb - 110;
        s.Add(new Site("C1 composer surface", new SKRect(cl, ct, cr, cb), Src.ComposerRadius, GlassTint, 0f, false, 2, 2, "ComposerView.swift:1304 .glassEffect(.regular)"));
        s.Add(new Site("C2 thread changes tab", new SKRect(700, ct - 32, 900, ct + 4), Src.TabRadius, GlassTint, 0f, true, 2, 2, "ThreadChangesTab.swift:38 .regular.interactive()"));
        s.Add(new Site("C3 approval request card", new SKRect(cl, ct - 32 - 12 - 90, cr, ct - 32 - 12), Src.RequestRadius, Warning, 0.10f, false, 2, 2, "RequestCards.swift:64 tint(Chrome.warning.opacity(0.1))"));
        s.Add(new Site("C4 effort slider knob", new SKRect(960, ct + 72, 984, ct + 96), 12, White, 0.32f, true, 2, 2, "EffortSlider.swift:788 tint(white 0.32 dark).interactive(), Circle()"));
        // Standalone sites (no container): one pass each in the source.
        s.Add(new Site("S1 minimap preview card", new SKRect(990, 300, 990 + Src.MinimapCardW, 420), Src.CardRadius, GlassTint, 0f, false, -1, 1, "TimelineMinimap.swift:338"));
        s.Add(new Site("S2 floating sidebar panel", new SKRect(14, 120, 314, 680), Src.PanelRadius, GlassTint, 0f, false, -1, 0, "FloatingSidebarPanel.swift:90 (300x560, :17)"));
        s.Add(new Site("S3 usage floating panel", new SKRect(1000, 440, 1300, 590), Src.PanelRadius, GlassTint, 0f, false, -1, 1, "UsageFloatingPanel.swift:59"));
        s.Add(new Site("S4 hydra panel", new SKRect(340, 110, 640, 330), Src.PanelRadius, GlassTint, 0f, false, -1, 0, "HydraPanel.swift:105"));
        s.Add(new Site("S5 subagent panel", new SKRect(660, 110, 960, 330), Src.PanelRadius, GlassTint, 0f, false, -1, 0, "SubagentPanel.swift:339"));
        s.Add(new Site("S6 hydra progress bar", new SKRect(400, 350, 920, 382), Src.CardRadius, GlassTint, 0f, false, -1, 2, "HydraProgressBar.swift:576"));
        s.Add(new Site("S7 command palette", new SKRect(380, 150, 940, 470), Src.PaletteRadius, GlassTint, 0f, false, -1, 0, "CommandPalette.swift:97"));
        s.Add(new Site("S8 follow-up queue tab", new SKRect(360, ct - 32, 660, ct + 4), Src.TabRadius, GlassTint, 0f, false, -1, 2, "FollowUpQueueTab.swift:154 (maxWidth 560, :153)"));
        return s.ToArray();
    }

    /// Snapshot groups for a configuration: the sites sharing one backdrop snapshot.
    public static int[][] Groups(string config) => config switch
    {
        "packet" => Enumerable.Range(0, 3).Select(g => Sites.Select((s, i) => (s, i)).Where(t => t.s.PacketGroup == g).Select(t => t.i).ToArray()).ToArray(),
        "source" => Enumerable.Range(0, 3).Select(g => Sites.Select((s, i) => (s, i)).Where(t => t.s.Container == g).Select(t => t.i).ToArray())
                        .Concat(Sites.Select((s, i) => (s, i)).Where(t => t.s.Container < 0).Select(t => new[] { t.i })).ToArray(),
        "persite" => Sites.Select((_, i) => new[] { i }).ToArray(),
        "noglass" => Array.Empty<int[]>(),
        _ => throw new ArgumentException(config),
    };

    public const float SnapMargin = 32;   // P1 strength 26 x 1.10 chromatic offset = 28.6 px max displacement, rounded up
}

/// The live transcript: an append-only line store written on the UI thread and read on the render thread.
static class Transcript
{
    public sealed record Line(string Text, byte Kind);   // 0 prose, 1 header, 2 code
    public static readonly Line[] Lines = new Line[40000];
    static int _count;
    public static int Count => Volatile.Read(ref _count);
    public static void Push(Line l) { Lines[_count] = l; Volatile.Write(ref _count, _count + 1); }

    static readonly string[] Words = ("the runtime flushes deltas onto their rows every forty five milliseconds while the row is short " +
        "git status shows three files changed with fifty one insertions and thirty seven deletions in the composer view " +
        "a head shares the screen with every other head streaming at the same time and each flush re lays the panel " +
        "Worked for 3m 0s   git commit -c core.quotepath=false   +51 -37 swift build passed tests green").Split(' ', StringSplitOptions.RemoveEmptyEntries);
    static readonly Random Rng = new(2);
    static readonly StringBuilder Cur = new();
    static int _msgChars;
    public const int WrapChars = 96;

    public static void Prefill(int lines)
    {
        while (Count < lines) Append(40);
        Flush();
        Publish();
    }

    /// Appends one flush worth of text (the deltas that arrived since the last flush). Returns the row length.
    public static int Append(int chars)
    {
        int added = 0;
        while (added < chars)
        {
            var w = Words[Rng.Next(Words.Length)];
            Cur.Append(w).Append(' ');
            added += w.Length + 1;
            if (Cur.Length >= WrapChars) { Push(new Line(Cur.ToString(), 0)); Cur.Clear(); }
        }
        _msgChars += added;
        // Keep every streaming row under the 4 KB tier so the source's 45 ms cadence applies throughout.
        if (_msgChars >= Src.FlushTier - 256)
        {
            Flush();
            Push(new Line("", 0));
            for (int i = 0; i < 6; i++) Push(new Line($"    let delay = Self.flushDelay(for: pendingFlushLength(), scrolling: false)  // {Rng.Next(1000)}", 2));
            Push(new Line("", 0));
            Push(new Line("Assistant", 1));
            _msgChars = 0;
        }
        Publish();
        return _msgChars;
    }
    static void Flush() { if (Cur.Length > 0) { Push(new Line(Cur.ToString(), 0)); Cur.Clear(); } }
    static string _streaming = "";
    /// The streaming row as of the last flush (a new string only when a flush changed it).
    public static string Streaming => _streaming;
    public static void Publish() => _streaming = Cur.ToString();
}

/// State handed from the UI thread to one render: immutable.
sealed class RenderState
{
    public int LineCount;
    public string Streaming = "";
    public float Scroll;          // extra downward offset that decays to 0 after each new line (auto-follow animation)
    public float[] Hover = Array.Empty<float>();
    public Point Pointer = new(-1, -1);
}

static class Rec
{
    public sealed class Frame { public long Index; public double T, CpuMs; public bool Gpu; }
    public static readonly ConcurrentQueue<Frame> Frames = new();
    public static readonly ConcurrentQueue<(long frame, int pass, double ms, bool disjoint)> GpuTimes = new();
    public static readonly ConcurrentQueue<double> UiFrames = new();
    public static readonly ConcurrentQueue<(double t, int rowLen)> Flushes = new();
    public static long PointerEvents, PointerEventsTitleBar;
    public static volatile bool LeaseOk, GrOk, EffectOk;
    public static string EffectErr = "", GlRenderer = "", GlVendor = "", GlVersion = "", GrBackend = "", TimerMode = "none", TimerNote = "";
    public static readonly List<string> Adapters = new();
    public static int AdapterChosen = -1;
}

/// GL timer queries (EXT_disjoint_timer_query) through the GL context behind Avalonia's Skia lease.
unsafe sealed class GpuTimer
{
    const uint GL_TIME_ELAPSED_EXT = 0x88BF, GL_QUERY_RESULT_EXT = 0x8866, GL_QUERY_RESULT_AVAILABLE_EXT = 0x8867, GL_GPU_DISJOINT_EXT = 0x8FBB;
    const uint GL_VENDOR = 0x1F00, GL_RENDERER = 0x1F01, GL_VERSION = 0x1F02, GL_EXTENSIONS = 0x1F03;
    public const int Passes = 3, Ring = 48;
    delegate* unmanaged<int, uint*, void> _gen;
    delegate* unmanaged<uint, uint, void> _begin;
    delegate* unmanaged<uint, void> _end;
    delegate* unmanaged<uint, uint, uint*, void> _getuiv;
    delegate* unmanaged<uint, uint, ulong*, void> _getui64v;
    delegate* unmanaged<uint, int*, void> _getiv;
    delegate* unmanaged<uint, IntPtr> _getString;
    readonly uint[] _ids = new uint[Passes * Ring];
    readonly long[] _frameOf = new long[Passes * Ring];
    readonly bool[] _pending = new bool[Passes * Ring];
    public bool Ok;
    public long Dropped;

    public static string Str(IntPtr p) => p == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(p) ?? "";

    public void Init(GlInterface gl)
    {
        _getString = (delegate* unmanaged<uint, IntPtr>)gl.GetProcAddress("glGetString");
        Rec.GlRenderer = Str(_getString(GL_RENDERER)); Rec.GlVendor = Str(_getString(GL_VENDOR)); Rec.GlVersion = Str(_getString(GL_VERSION));
        var ext = Str(_getString(GL_EXTENSIONS));
        bool has = ext.Contains("GL_EXT_disjoint_timer_query", StringComparison.Ordinal);
        _gen = (delegate* unmanaged<int, uint*, void>)gl.GetProcAddress("glGenQueriesEXT");
        _begin = (delegate* unmanaged<uint, uint, void>)gl.GetProcAddress("glBeginQueryEXT");
        _end = (delegate* unmanaged<uint, void>)gl.GetProcAddress("glEndQueryEXT");
        _getuiv = (delegate* unmanaged<uint, uint, uint*, void>)gl.GetProcAddress("glGetQueryObjectuivEXT");
        _getui64v = (delegate* unmanaged<uint, uint, ulong*, void>)gl.GetProcAddress("glGetQueryObjectui64vEXT");
        _getiv = (delegate* unmanaged<uint, int*, void>)gl.GetProcAddress("glGetIntegerv");
        Ok = has && _gen != null && _begin != null && _end != null && _getuiv != null && _getui64v != null && _getiv != null;
        Rec.TimerNote = $"GL_EXT_disjoint_timer_query in GL_EXTENSIONS={has}; entry points {(_gen != null && _begin != null && _end != null && _getuiv != null && _getui64v != null ? "resolved" : "missing")}";
        if (!Ok) return;
        fixed (uint* p = _ids) _gen(_ids.Length, p);
        Rec.TimerMode = "GL_TIME_ELAPSED_EXT queries";
    }

    public void Begin(long frame, int pass)
    {
        int slot = (int)(frame % Ring) * Passes + pass;
        if (_pending[slot]) { _pending[slot] = false; Dropped++; }
        _frameOf[slot] = frame;
        _begin(GL_TIME_ELAPSED_EXT, _ids[slot]);
    }
    public void End(int pass) { _end(GL_TIME_ELAPSED_EXT); _ = pass; }
    public void Arm(long frame, int pass) { _pending[(int)(frame % Ring) * Passes + pass] = true; }

    public void Poll()
    {
        int disjoint = 0;
        _getiv(GL_GPU_DISJOINT_EXT, &disjoint);
        for (int i = 0; i < _ids.Length; i++)
        {
            if (!_pending[i]) continue;
            uint avail = 0;
            _getuiv(_ids[i], GL_QUERY_RESULT_AVAILABLE_EXT, &avail);
            if (avail == 0) continue;
            ulong ns = 0;
            _getui64v(_ids[i], GL_QUERY_RESULT_EXT, &ns);
            _pending[i] = false;
            Rec.GpuTimes.Enqueue((_frameOf[i], i % Passes, ns / 1e6, disjoint != 0));
        }
    }
}

static class Shaders
{
    // P1's SKSL (porting/probes/p1-glass/Program.cs), extended with the .interactive() response: a highlight that
    // follows the pointer, scaled by the site's hover amount. The lens, the chromatic offsets, the tint and the rim
    // are P1's unchanged.
    public const string Glass = @"
uniform shader backdrop;
uniform float2 size;
uniform float radius;
uniform float band;
uniform float strength;
uniform float4 tint;
uniform float rimGain;
uniform float2 pointer;
uniform float hover;
float sdSquircle(float2 p, float2 b, float r) {
    float2 q = abs(p) - b + r;
    float n = 4.0;
    float2 qc = max(q, 0.0);
    float outside = pow(pow(qc.x, n) + pow(qc.y, n), 1.0 / n);
    return outside + min(max(q.x, q.y), 0.0) - r;
}
half4 main(float2 xy) {
    float2 c = size * 0.5;
    float d = -sdSquircle(xy - c, c, radius);
    float t = clamp(1.0 - d / band, 0.0, 1.0);
    float lens = pow(t, 2.2);
    float e = 1.0;
    float dx = (-sdSquircle(xy + float2(e,0) - c, c, radius)) - (-sdSquircle(xy - float2(e,0) - c, c, radius));
    float dy = (-sdSquircle(xy + float2(0,e) - c, c, radius)) - (-sdSquircle(xy - float2(0,e) - c, c, radius));
    float2 n2 = normalize(float2(dx, dy) + float2(1e-6));
    float disp = lens * strength;
    half4 r = backdrop.eval(xy - n2 * disp * 1.10);
    half4 g = backdrop.eval(xy - n2 * disp * 1.00);
    half4 b = backdrop.eval(xy - n2 * disp * 0.90);
    half4 col = half4(r.r, g.g, b.b, 1.0);
    col.rgb = mix(col.rgb, half3(tint.rgb), half(tint.a));
    float rim = exp(-pow(d - 1.5, 2.0) / 6.0);
    float light = pow(clamp(-n2.x * 0.7 - n2.y * 0.7, 0.0, 1.0), 1.5);
    col.rgb += half3(half(rim * light * rimGain));
    float2 pd = xy - pointer;
    col.rgb += half3(half(hover * 0.22 * exp(-dot(pd, pd) / 3200.0)));
    return col;
}";

    // Control only: GPU-bound, CPU-trivial. Its output alpha is at most 1/255, so the snapshot stays visually the same.
    public const string Heavy = @"
uniform float seed;
half4 main(float2 xy) {
    float a = seed;
    for (int i = 0; i < 256; i++) {
        a += sin(xy.x * 0.0131 * float(i) + cos(xy.y * 0.0173 * float(i) + a));
    }
    return half4(0.0, 0.0, 0.0, half(clamp(fract(a) * 0.004, 0.0, 0.004)));
}";

    static SKRuntimeEffect? _glass, _heavy;
    public static SKRuntimeEffect? GlassFx() { if (_glass == null) { _glass = SKRuntimeEffect.CreateShader(Glass, out var e); if (_glass == null) Rec.EffectErr = e ?? "?"; } return _glass; }
    public static SKRuntimeEffect? HeavyFx() { if (_heavy == null) { _heavy = SKRuntimeEffect.CreateShader(Heavy, out var e); if (_heavy == null) Rec.EffectErr += " heavy:" + (e ?? "?"); } return _heavy; }
}

/// Everything the render thread owns: blob cache, snapshot surfaces, the timer.
static class RenderSide
{
    public static string Config = "packet", Ctl = "none";
    public static int HeavyPasses = 12;
    public static bool NoTimer;
    public static int[][] Groups = Array.Empty<int[]>();
    public static SKRectI[] GroupRects = Array.Empty<SKRectI>();
    static SKSurface?[] _surfaces = Array.Empty<SKSurface?>();
    static GRContext? _surfacesFor;
    static readonly SKTextBlob?[] Blobs = new SKTextBlob?[Transcript.Lines.Length];
    static SKTextBlob? _streamBlob; static string _streamText = "\u0001";
    static readonly SKFont Prose = new(SKTypeface.Default, 14), Code = new(SKTypeface.FromFamilyName("Consolas") ?? SKTypeface.Default, 13),
        Header = new(SKTypeface.FromFamilyName(SKTypeface.Default.FamilyName, SKFontStyle.Bold) ?? SKTypeface.Default, 14);
    static readonly SKPaint ProsePaint = new() { Color = new SKColor(0xEC, 0xEC, 0xF0), IsAntialias = true },
        CodePaint = new() { Color = new SKColor(0x9C, 0xDC, 0xFE), IsAntialias = true },
        HeaderPaint = new() { Color = new SKColor(0x0A, 0x84, 0xFF), IsAntialias = true },
        CodeBg = new() { Color = new SKColor(0x2A, 0x2A, 0x30) },
        Scrim1 = new() { Color = new SKColor(0, 0, 0, 66) },                 // WindowBackdrop.scrim dark stock 0.26 (Chrome.swift:212)
        Scrim2 = new() { Color = new SKColor(0x1E, 0x1E, 0x20, 31) },        // theme tint 0.12 over it (P3/P4 recipe)
        DiffAdd = new() { Color = new SKColor(0x30, 0xD1, 0x58, 60) }, DiffDel = new() { Color = new SKColor(0xFF, 0x45, 0x3A, 60) },
        Marker = new() { Color = new SKColor(255, 0, 255) }, MarkerG = new() { Color = new SKColor(0, 255, 0) };
    public const float LineH = 20, TextL = 250, Anchor = 700;
    static readonly GpuTimer Timer = new();
    static bool _timerInit;
    static long _frame;

    static SKTextBlob? Blob(int i)
    {
        var b = Blobs[i];
        if (b != null) return b;
        var l = Transcript.Lines[i];
        if (l.Text.Length == 0) return null;
        b = SKTextBlob.Create(l.Text, l.Kind == 2 ? Code : l.Kind == 1 ? Header : Prose);
        Blobs[i] = b;
        return b;
    }

    /// The live content: scrim, a static diff column, and the transcript at its current scroll. Drawn once onto the
    /// window and once more into every snapshot surface (P1's app-owned re-render), culled to `clip`.
    static void Backdrop(SKCanvas c, RenderState s, SKRect clip)
    {
        c.DrawRect(clip, Scrim1);
        c.DrawRect(clip, Scrim2);
        for (int i = 0; i < 38; i++)
        {
            float y = 110 + i * 20;
            if (y + 20 < clip.Top || y > clip.Bottom) continue;
            if (i % 5 == 1) c.DrawRect(new SKRect(1080, y, Src.W, y + 18), DiffAdd);
            else if (i % 7 == 3) c.DrawRect(new SKRect(1080, y, Src.W, y + 18), DiffDel);
        }
        // Transcript: the streaming row sits on the anchor line; complete rows stack above it.
        float baseY = Anchor + s.Scroll;
        int n = s.LineCount;
        for (int k = 0; ; k++)
        {
            float y = baseY - k * LineH;       // baseline of this row
            if (y < clip.Top - LineH) break;
            if (y - LineH > clip.Bottom) continue;
            if (k == 0)
            {
                if (!ReferenceEquals(_streamText, s.Streaming)) { _streamBlob?.Dispose(); _streamBlob = s.Streaming.Length > 0 ? SKTextBlob.Create(s.Streaming, Prose) : null; _streamText = s.Streaming; }
                if (_streamBlob != null) c.DrawText(_streamBlob, TextL, y, ProsePaint);
                continue;
            }
            int idx = n - k;
            if (idx < 0) break;
            var line = Transcript.Lines[idx];
            if (line.Kind == 2) c.DrawRect(new SKRect(TextL - 8, y - 15, TextL + 812, y + 5), CodeBg);
            var b = Blob(idx);
            if (b != null) c.DrawText(b, TextL, y, line.Kind == 2 ? CodePaint : line.Kind == 1 ? HeaderPaint : ProsePaint);
        }
    }

    public static void Render(ImmediateDrawingContext context, RenderState s)
    {
        double t0 = Clock.Ms;
        long frame = ++_frame;
        var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (feature is null) return;
        Rec.LeaseOk = true;
        using var lease = feature.Lease();
        var c = lease.SkCanvas;
        var gr = lease.GrContext;
        bool gpu = gr != null;
        if (gpu) { Rec.GrOk = true; Rec.GrBackend = gr!.Backend.ToString(); }
        if (gpu && !_timerInit)
        {
            _timerInit = true;
            using var api = lease.TryLeasePlatformGraphicsApi();
            if (api?.Context is IGlContext gl) Timer.Init(gl.GlInterface);
            else Rec.TimerNote = $"platform graphics context is {api?.Context?.GetType().FullName ?? "null"}, not IGlContext";
        }
        bool timed = gpu && Timer.Ok && !NoTimer;

        void Mark(bool begin, int pass)
        {
            if (!timed) return;
            gr!.Flush();                                     // hand Skia's recorded work for the pass to GL before the query edge
            using var api = lease.TryLeasePlatformGraphicsApi();
            if (begin) Timer.Begin(frame, pass); else { Timer.End(pass); Timer.Arm(frame, pass); }
        }

        if (timed) { gr!.Flush(); using var api = lease.TryLeasePlatformGraphicsApi(); Timer.Poll(); }

        // Pass 0: the window's own content.
        Mark(true, 0);
        var full = new SKRect(0, 0, Src.W, Src.H);
        Backdrop(c, s, full);
        c.DrawRect(new SKRect(4, Src.H - 16, 16, Src.H - 4), Marker);    // screen-live marker (never under glass)
        c.DrawRect(new SKRect(16, Src.H - 16, 28, Src.H - 4), MarkerG);
        Mark(false, 0);

        var fx = Shaders.GlassFx();
        if (Groups.Length > 0 && fx != null)
        {
            Rec.EffectOk = true;
            if (!ReferenceEquals(_surfacesFor, gr) || _surfaces.Length != Groups.Length)
            {
                foreach (var old in _surfaces) old?.Dispose();
                _surfaces = new SKSurface?[Groups.Length];
                _surfacesFor = gr;
            }
            // Pass 1: the snapshot pass. Every group re-renders the live content under it into its own app-owned surface.
            Mark(true, 1);
            var images = new SKImage?[Groups.Length];
            for (int g = 0; g < Groups.Length; g++)
            {
                var r = GroupRects[g];
                var surf = _surfaces[g];
                if (surf == null)
                {
                    var info = new SKImageInfo(r.Width, r.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
                    surf = gpu ? SKSurface.Create(gr!, false, info) : SKSurface.Create(info);
                    _surfaces[g] = surf;
                }
                if (surf == null) continue;
                var oc = surf.Canvas;
                oc.Clear(new SKColor(0x0B, 0x0B, 0x0D));
                oc.Save();
                oc.Translate(-r.Left, -r.Top);
                Backdrop(oc, s, new SKRect(r.Left, r.Top, r.Right, r.Bottom));
                oc.Restore();
                if (Ctl == "gpuheavy" && Shaders.HeavyFx() is { } hx)
                {
                    for (int k = 0; k < HeavyPasses; k++)
                    {
                        using var hs = hx.ToShader(new SKRuntimeEffectUniforms(hx) { ["seed"] = (float)(k + frame % 7) });
                        using var hp = new SKPaint { Shader = hs };
                        oc.DrawRect(new SKRect(0, 0, r.Width, r.Height), hp);
                    }
                }
                images[g] = surf.Snapshot();
            }
            Mark(false, 1);

            // Pass 2: the lens, one SKSL draw per site, sampling its group's snapshot.
            Mark(true, 2);
            for (int g = 0; g < Groups.Length; g++)
            {
                var img = images[g];
                if (img == null) continue;
                var gr0 = GroupRects[g];
                foreach (int si in Groups[g])
                {
                    var site = Layout.Sites[si];
                    var R = site.R;
                    float hv = si < s.Hover.Length ? s.Hover[si] : 0;
                    var u = new SKRuntimeEffectUniforms(fx)
                    {
                        ["size"] = new[] { R.Width, R.Height },
                        ["radius"] = site.Radius,
                        ["band"] = Math.Min(46f, Math.Min(R.Width, R.Height) / 2),
                        ["strength"] = 26f,
                        ["rimGain"] = 0.55f,
                        ["tint"] = new[] { site.Tint.Red / 255f, site.Tint.Green / 255f, site.Tint.Blue / 255f, site.TintA },
                        ["pointer"] = new[] { (float)s.Pointer.X - R.Left, (float)s.Pointer.Y - R.Top },
                        ["hover"] = hv,
                    };
                    using var bs = img.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, SKMatrix.CreateTranslation(gr0.Left - R.Left, gr0.Top - R.Top));
                    var ch = new SKRuntimeEffectChildren(fx) { ["backdrop"] = bs };
                    using var shader = fx.ToShader(u, ch);
                    using var paint = new SKPaint { Shader = shader, IsAntialias = true };
                    c.Save();
                    c.Translate(R.Left, R.Top);
                    if (site.Interactive && hv > 0)
                    {
                        float k = 1 + 0.04f * hv;                // .interactive() press/hover swell (ENGINEERING stand-in)
                        c.Scale(k, k, R.Width / 2, R.Height / 2);
                    }
                    c.ClipRoundRect(new SKRoundRect(new SKRect(0, 0, R.Width, R.Height), site.Radius, site.Radius), SKClipOperation.Intersect, true);
                    c.DrawRect(new SKRect(0, 0, R.Width, R.Height), paint);
                    c.Restore();
                }
            }
            Mark(false, 2);
            foreach (var im in images) im?.Dispose();
        }

        if (Ctl == "stall") Thread.Sleep(30);
        if (timed) { gr!.Flush(); }
        Rec.Frames.Enqueue(new Rec.Frame { Index = frame, T = t0, CpuMs = Clock.Ms - t0, Gpu = gpu });
    }

    public static long DroppedQueries => Timer.Dropped;
}

sealed class Scene : Control
{
    public RenderState State = new();
    public override void Render(DrawingContext ctx) => ctx.Custom(new Op(new Rect(0, 0, Src.W, Src.H), State));

    sealed class Op : ICustomDrawOperation
    {
        readonly RenderState _s;
        public Op(Rect b, RenderState s) { Bounds = b; _s = s; }
        public Rect Bounds { get; }
        public bool HitTest(Point p) => Bounds.Contains(p);          // the whole scene takes the pointer, as the app's content does
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }
        public void Render(ImmediateDrawingContext context) => RenderSide.Render(context, _s);
    }
}

sealed class ProbeWindow : Window
{
    public readonly Scene Scene = new();
    readonly float[] _hover = new float[Layout.Sites.Length];
    Point _pointer = new(-1, -1);
    float _scroll;
    int _lastCount;
    double _lastT;
    public volatile bool Running = true;
    public bool Measuring;

    public ProbeWindow()
    {
        Title = "P2 snapshot budget";
        Width = Src.W; Height = Src.H;                                  // WindowManager.swift:19
        WindowDecorations = WindowDecorations.Full;                     // P7's measured chrome configuration
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = 44;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.AcrylicBlur };   // P3/P4: the owner's acrylic edge
        Background = Brushes.Transparent;
        CanResize = false;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Template = new FuncControlTemplate<Window>((_, _) => Scene);
        PointerMoved += (_, e) => { _pointer = e.GetPosition(this); Interlocked.Increment(ref Rec.PointerEvents); if (_pointer.Y < 44) Interlocked.Increment(ref Rec.PointerEventsTitleBar); };
        PointerExited += (_, _) => _pointer = new Point(-1, -1);
    }

    public void Start() { _lastT = Clock.Ms; _lastCount = Transcript.Count; RequestAnimationFrame(OnFrame); }

    void OnFrame(TimeSpan _)
    {
        if (!Running) return;
        double now = Clock.Ms, dt = Math.Max(0.1, now - _lastT);
        _lastT = now;
        if (Measuring) Rec.UiFrames.Enqueue(now);
        int count = Transcript.Count;
        if (count != _lastCount) { _scroll += (count - _lastCount) * RenderSide.LineH; _lastCount = count; }
        _scroll *= (float)Math.Exp(-dt / 60.0);                          // auto-follow scroll, tau 60 ms (ENGINEERING)
        if (_scroll < 0.05f) _scroll = 0;
        float a = (float)(1 - Math.Exp(-dt / 80.0));                      // hover response, tau 80 ms (ENGINEERING)
        for (int i = 0; i < _hover.Length; i++)
        {
            var site = Layout.Sites[i];
            float target = site.Interactive && site.R.Contains((float)_pointer.X, (float)_pointer.Y) ? 1 : 0;
            _hover[i] += (target - _hover[i]) * a;
        }
        Scene.State = new RenderState { LineCount = count, Streaming = Transcript.Streaming, Scroll = _scroll, Hover = (float[])_hover.Clone(), Pointer = _pointer };
        Scene.InvalidateVisual();
        RequestAnimationFrame(OnFrame);
    }
}

static class Program
{
    static string Out = "";
    static readonly StringBuilder LogBuf = new();
    static void Log(string s)
    {
        var line = $"{DateTime.UtcNow:HH:mm:ss.fff}Z [{Clock.Ms,9:F1}] {s}";
        Console.WriteLine(line);
        lock (LogBuf) LogBuf.AppendLine(line);
    }
    static string F(double v, string f = "F2") => v.ToString(f, CultureInfo.InvariantCulture);
    static double Pct(IReadOnlyList<double> sorted, double p) => sorted.Count == 0 ? double.NaN : sorted[Math.Clamp((int)Math.Ceiling(p / 100.0 * sorted.Count) - 1, 0, sorted.Count - 1)];
    static string Stats(IEnumerable<double> v, out double med, out double p99, out double max)
    {
        var s = v.OrderBy(x => x).ToList();
        med = Pct(s, 50); p99 = Pct(s, 99); max = s.Count > 0 ? s[^1] : double.NaN;
        return $"n={s.Count} median={F(med)} p95={F(Pct(s, 95))} p99={F(p99)} max={F(max)}";
    }

    static string Config = "packet", Adapter = "default", RenderMode = "gpu", Ctl = "none";
    static int Seconds = 12;

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Contains("--api")) return Api();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out": Out = args[++i]; break;
                case "--config": Config = args[++i]; break;
                case "--adapter": Adapter = args[++i]; break;
                case "--render": RenderMode = args[++i]; break;
                case "--ctl": Ctl = args[++i]; break;
                case "--seconds": Seconds = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--heavy": RenderSide.HeavyPasses = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--notimer": RenderSide.NoTimer = true; break;
            }
        }
        if (Out == "") { Console.Error.WriteLine("--out required"); return 4; }
        Directory.CreateDirectory(Out);
        RenderSide.Config = Config; RenderSide.Ctl = Ctl;

        var opts = new Win32PlatformOptions();
        if (RenderMode == "software") opts.RenderingMode = new[] { Win32RenderingMode.Software };
        if (Adapter != "default")
        {
            opts.GraphicsAdapterSelectionCallback = list =>
            {
                int pick = -1;
                for (int i = 0; i < list.Count; i++)
                {
                    var l = list[i].DeviceLuid ?? Array.Empty<byte>();
                    string luid = l.Length == 8 ? $"luid_0x{BitConverter.ToUInt32(l, 4):X8}_0x{BitConverter.ToUInt32(l, 0):X8}" : "luid_?";
                    lock (Rec.Adapters) Rec.Adapters.Add($"[{i}] {list[i].Description} {luid}");
                    if (pick < 0 && (list[i].Description ?? "").Contains(Adapter, StringComparison.OrdinalIgnoreCase)) pick = i;
                }
                Rec.AdapterChosen = pick;
                return pick < 0 ? 0 : pick;
            };
        }
        AppBuilder.Configure<Application>().UsePlatformDetect().With(opts).SetupWithoutStarting();

        int exit = 4;
        var cts = new CancellationTokenSource();
        Dispatcher.UIThread.Post(async () =>
        {
            try { exit = await Run(); }
            catch (Exception e) { Log("CRASH " + e); exit = 4; }
            finally
            {
                lock (LogBuf) File.WriteAllText(Path.Combine(Out, "run_log.txt"), LogBuf.ToString());
                cts.Cancel();
            }
        });
        Dispatcher.UIThread.MainLoop(cts.Token);
        return exit;
    }

    static int Api()
    {
        foreach (var asm in new[] { typeof(Win32PlatformOptions).Assembly, typeof(IGlContext).Assembly, typeof(ISkiaSharpApiLease).Assembly, typeof(IPlatformGraphics).Assembly })
            foreach (var t in asm.GetExportedTypes())
            {
                if (!(t.Name is "Win32PlatformOptions" or "Win32RenderingMode" or "Win32CompositionMode" or "PlatformGraphicsDeviceAdapterDescription" or "ISkiaSharpApiLease" or "ISkiaSharpPlatformGraphicsApiLease" or "IGlContext")) continue;
                Console.WriteLine($"== {t.FullName} ({asm.GetName().Name} {asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion})");
                foreach (var m in t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Where(m => m.MemberType is MemberTypes.Property or MemberTypes.Field))
                    Console.WriteLine($"   {m.MemberType} {m}");
            }
        return 0;
    }

    static IntPtr Hwnd(Window w) => w.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

    static void SavePng(byte[] buf, int w, int h, string name, double scale)
    {
        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var bmp = new SKBitmap(info);
        Marshal.Copy(buf, 0, bmp.GetPixels(), buf.Length);
        using var outBmp = bmp.Resize(new SKImageInfo((int)(w * scale), (int)(h * scale), SKColorType.Bgra8888, SKAlphaType.Opaque), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        using var img = SKImage.FromBitmap(outBmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(Out, name), data.ToArray());
        Log($"PNG {name} {w}x{h} scale={F(scale)} bytes={data.Size}");
    }

    static async Task<int> Run()
    {
        N.GetCursorPos(out var cursor0);
        Log($"P2 start config={Config} adapter={Adapter} render={RenderMode} ctl={Ctl} notimer={RenderSide.NoTimer} seconds={Seconds} heavyPasses={RenderSide.HeavyPasses} cursor0=({cursor0.X},{cursor0.Y})");
        Log($"avalonia {typeof(Window).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");
        Log($"skiasharp {typeof(SKImage).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");
        Log($"os {Environment.OSVersion.VersionString}; pid={Environment.ProcessId}; MARROW_PORT_DATA_DIR={Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR")}");
        N.SystemParametersInfoW(N.SPI_GETWORKAREA, 0, out var work, 0);
        Log($"primary work area {work}; virtual screen ({N.GetSystemMetrics(76)},{N.GetSystemMetrics(77)}) {N.GetSystemMetrics(78)}x{N.GetSystemMetrics(79)}");

        var groups = Layout.Groups(Config);
        RenderSide.Groups = groups;
        RenderSide.GroupRects = groups.Select(g =>
        {
            var u = Layout.Sites[g[0]].R;
            foreach (var i in g) u = SKRect.Union(u, Layout.Sites[i].R);
            u.Inflate(Layout.SnapMargin, Layout.SnapMargin);
            u.Intersect(new SKRect(0, 0, Src.W, Src.H));
            return SKRectI.Round(u);
        }).ToArray();
        Log($"sites={Layout.Sites.Length} interactive={Layout.Sites.Count(s => s.Interactive)} snapshotGroups={groups.Length} snapshotPixels={RenderSide.GroupRects.Sum(r => (long)r.Width * r.Height)} (window {Src.W * Src.H})");
        for (int g = 0; g < groups.Length; g++)
            Log($"  group {g} rect={RenderSide.GroupRects[g]} sites={string.Join(",", groups[g].Select(i => Layout.Sites[i].Name.Split(' ')[0]))}");
        foreach (var s in Layout.Sites) Log($"  site {s.Name} rect=[{s.R.Left},{s.R.Top},{s.R.Right},{s.R.Bottom}] r={s.Radius} tintA={s.TintA} interactive={s.Interactive} container={s.Container} packetGroup={s.PacketGroup} src={s.Src}");

        Transcript.Prefill(80);
        ProbeWindow? win = null;
        int result = 2;
        var stopFlush = new CancellationTokenSource();
        var stopPointer = new CancellationTokenSource();
        N.timeBeginPeriod(1);
        try
        {
            win = new ProbeWindow();
            win.Show();
            var h = Hwnd(win);
            N.GetWindowRect(h, out var wr0);
            N.SetWindowPos(h, N.HWND_TOPMOST, work.L + 120, work.T + 60, wr0.W, wr0.H, N.SWP_NOACTIVATE | N.SWP_SHOWWINDOW);
            win.Start();

            // The 45 ms flush: deltas arrive continuously and are flushed onto the row at the source's pacing
            // (ThreadRuntime.swift:3087-3095 and flushDelay :3126-3136; the row stays under 4 KB, so 45 ms).
            var flushThread = new Thread(() =>
            {
                double next = Clock.Ms + Src.FlushMs;
                while (!stopFlush.IsCancellationRequested)
                {
                    while (Clock.Ms < next - 1.5) Thread.Sleep(1);
                    while (Clock.Ms < next) Thread.SpinWait(50);
                    next += Src.FlushMs;
                    Dispatcher.UIThread.Post(() =>
                    {
                        int len = Transcript.Append(30);
                        if (win.Measuring) Rec.Flushes.Enqueue((Clock.Ms, len));
                    }, DispatcherPriority.Input);
                }
            }) { IsBackground = true, Name = "flush45" };
            flushThread.Start();

            await Task.Delay(2500);                                          // warm-up: shader compile, surfaces, first frames
            var f0 = N.FrameBounds(h);
            N.GetClientRect(h, out var cr);
            var co = new N.POINT(); N.ClientToScreen(h, ref co);
            uint dpi = N.GetDpiForWindow(h);
            double scale = dpi / 96.0;
            Log($"window hwnd=0x{h.ToInt64():X} dpi={dpi} frame={f0} client={cr.W}x{cr.H} at ({co.X},{co.Y}) ActualTransparencyLevel={win.ActualTransparencyLevel} IsExtendedIntoWindowDecorations={win.IsExtendedIntoWindowDecorations}");
            Log($"render path: lease={Rec.LeaseOk} GrContext={(Rec.GrOk ? "non-null backend=" + Rec.GrBackend : "null (CPU raster)")} effect={Rec.EffectOk} {Rec.EffectErr}");
            Log($"GL_VENDOR='{Rec.GlVendor}' GL_RENDERER='{Rec.GlRenderer}' GL_VERSION='{Rec.GlVersion}'");
            Log($"gpu timer: mode={Rec.TimerMode}; {Rec.TimerNote}");
            lock (Rec.Adapters) Log($"adapter selection callback: {(Adapter == "default" ? "not installed (Avalonia default)" : $"offered {Rec.Adapters.Count}: {string.Join(" | ", Rec.Adapters)}; chose index {Rec.AdapterChosen} for '{Adapter}'")}");

            // Pointer driver: a path over the interactive sites (the chrome row, the changes tab, the effort knob).
            Point[] path =
            {
                new(20, 28), new(1300, 28), new(1300, 80), new(1010, 80), new(800, 724), new(980, 820), new(700, 724), new(20, 28),
            };
            var misses = 0; var moves = 0; var interfered = false; var targetMisses = 0;
            var pointerThread = new Thread(() =>
            {
                double len = 0; var seg = new double[path.Length - 1];
                for (int i = 0; i < seg.Length; i++) { seg[i] = Math.Sqrt(Math.Pow(path[i + 1].X - path[i].X, 2) + Math.Pow(path[i + 1].Y - path[i].Y, 2)); len += seg[i]; }
                const double speed = 1.4;                                    // px per ms, one loop in about 4.5 s
                double t0 = Clock.Ms; N.POINT? last = null;
                while (!stopPointer.IsCancellationRequested)
                {
                    double d = ((Clock.Ms - t0) * speed) % len;
                    int k = 0; while (k < seg.Length - 1 && d > seg[k]) { d -= seg[k]; k++; }
                    double f = seg[k] > 0 ? Math.Min(1, d / seg[k]) : 0;
                    double cx = path[k].X + (path[k + 1].X - path[k].X) * f, cy = path[k].Y + (path[k + 1].Y - path[k].Y) * f;
                    var p = new N.POINT { X = co.X + (int)Math.Round(cx * scale), Y = co.Y + (int)Math.Round(cy * scale) };
                    N.GetCursorPos(out var now);
                    if (last is { } lp && (Math.Abs(now.X - lp.X) > 2 || Math.Abs(now.Y - lp.Y) > 2)) { interfered = true; Log($"POINTER interference: cursor at ({now.X},{now.Y}), expected ({lp.X},{lp.Y}); stopping the driver"); break; }
                    if (N.GetAncestor(N.WindowFromPoint(p), N.GA_ROOT) != h) { targetMisses++; Thread.Sleep(8); continue; }
                    N.MoveAbs(p.X, p.Y);
                    moves++;
                    Thread.Sleep(1);
                    N.GetCursorPos(out var after);
                    if (Math.Abs(after.X - p.X) > 1 || Math.Abs(after.Y - p.Y) > 1) misses++;
                    last = after;
                    Thread.Sleep(7);
                }
            }) { IsBackground = true, Name = "pointer" };

            // Measurement.
            while (Rec.Frames.TryDequeue(out _)) { }
            while (Rec.GpuTimes.TryDequeue(out _)) { }
            while (Rec.UiFrames.TryDequeue(out _)) { }
            Interlocked.Exchange(ref Rec.PointerEvents, 0); Interlocked.Exchange(ref Rec.PointerEventsTitleBar, 0);
            pointerThread.Start();
            await Task.Delay(400);
            win.Measuring = true;
            double m0 = Clock.Ms;
            byte[]? shot = null; N.RECT shotRect = default; bool shotStable = false;
            await Task.Delay(Seconds * 1000 / 2);
            await Task.Run(() =>
            {
                var a = N.FrameBounds(h);
                shot = N.Capture(a);
                shotRect = a;
                var b = N.FrameBounds(h);
                shotStable = a.L == b.L && a.T == b.T && a.R == b.R && a.B == b.B;
            });
            await Task.Delay(Seconds * 1000 - Seconds * 1000 / 2);
            win.Measuring = false;
            double m1 = Clock.Ms;
            stopPointer.Cancel();
            pointerThread.Join(1000);
            stopFlush.Cancel();
            long pointerEvents = Interlocked.Read(ref Rec.PointerEvents);
            await Task.Delay(300);                                          // let the last GPU query results land
            win.Running = false;

            // ---- screen-live check ----
            bool live = false;
            if (shot != null)
            {
                int W = shotRect.W, H = shotRect.H;
                int mx = co.X - shotRect.L, my = co.Y - shotRect.T;
                int mag = 0, grn = 0;
                for (int y = (int)((Src.H - 14) * scale) + my; y < (int)((Src.H - 6) * scale) + my; y++)
                    for (int x = (int)(6 * scale) + mx; x < (int)(26 * scale) + mx; x++)
                    {
                        if (x < 0 || y < 0 || x >= W || y >= H) continue;
                        int o = (y * W + x) * 4;
                        if (shot[o + 2] > 220 && shot[o + 1] < 40 && shot[o] > 220) mag++;
                        if (shot[o + 1] > 220 && shot[o + 2] < 40 && shot[o] < 40) grn++;
                    }
                var distinct = new HashSet<int>();
                for (int i = 0; i < shot.Length; i += 4 * 97) distinct.Add(shot[i] | shot[i + 1] << 8 | shot[i + 2] << 16);
                live = mag >= 20 && grn >= 20 && distinct.Count > 50;
                Log($"SCREEN capture {shotRect} stable={shotStable} markerMagentaPx={mag} markerGreenPx={grn} distinctColours(sampled)={distinct.Count} => live={live}");
                SavePng(shot, W, H, "window_mid_run.png", 0.5);
            }

            // ---- numbers ----
            var frames = Rec.Frames.ToArray().Where(f => f.T >= m0 && f.T <= m1).OrderBy(f => f.Index).ToList();
            var intervals = new List<double>();
            for (int i = 1; i < frames.Count; i++) intervals.Add(frames[i].T - frames[i - 1].T);
            var gpuT = Rec.GpuTimes.ToArray();
            var inWin = new HashSet<long>(frames.Select(f => f.Index));
            var byPass = Enumerable.Range(0, 3).Select(p => gpuT.Where(g => g.pass == p && inWin.Contains(g.frame) && !g.disjoint).Select(g => g.ms).ToList()).ToArray();
            int disjoint = gpuT.Count(g => g.disjoint && inWin.Contains(g.frame));
            var ui = Rec.UiFrames.ToArray(); var uiInt = new List<double>(); for (int i = 1; i < ui.Length; i++) uiInt.Add(ui[i] - ui[i - 1]);
            var fl = Rec.Flushes.ToArray(); var flInt = new List<double>(); for (int i = 1; i < fl.Length; i++) flInt.Add(fl[i].t - fl[i - 1].t);

            string sFrame = Stats(intervals, out var fMed, out var fP99, out var fMax);
            string sCpu = Stats(frames.Select(f => f.CpuMs), out var cMed, out var cP99, out _);
            string sG0 = Stats(byPass[0], out var g0Med, out _, out _);
            string sG1 = Stats(byPass[1], out var g1Med, out var g1P99, out var g1Max);
            string sG2 = Stats(byPass[2], out var g2Med, out var g2P99, out _);
            string sUi = Stats(uiInt, out _, out _, out _);
            string sFl = Stats(flInt, out var flMed, out _, out _);
            Log($"FRAMES render-op interval (frame time): {sFrame}; over {F(m1 - m0, "F0")} ms; frames>16.7ms={intervals.Count(x => x > 16.7)} frames>33ms={intervals.Count(x => x > 33)}");
            Log($"CPU    render-op duration: {sCpu}");
            Log($"GPU    pass0 main content: {sG0}");
            Log($"GPU    pass1 SNAPSHOT:     {sG1}");
            Log($"GPU    pass2 glass lens:   {sG2}");
            Log($"GPU    total (medians): {F(g0Med + g1Med + g2Med)} ms; disjoint results={disjoint}; dropped queries={RenderSide.DroppedQueries}");
            Log($"UI     animation-frame interval: {sUi}");
            Log($"FLUSH  45 ms cadence interval: {sFl}; flushes={fl.Length}; max row length={(fl.Length > 0 ? fl.Max(x => x.rowLen) : 0)} (tier {Src.FlushTier}); transcript lines={Transcript.Count}");
            Log($"POINTER moves={moves} readback misses={misses} target-not-ours skips={targetMisses} interference={interfered} PointerMoved events={pointerEvents} (of which in the 44 px title-bar band: {Interlocked.Read(ref Rec.PointerEventsTitleBar)})");

            using (var sw = new StreamWriter(Path.Combine(Out, "frames.csv")))
            {
                sw.WriteLine("frame,t_ms,interval_ms,cpu_ms,gpu_main_ms,gpu_snapshot_ms,gpu_glass_ms");
                var g = gpuT.Where(x => !x.disjoint).GroupBy(x => x.frame).ToDictionary(k => k.Key, k => k.ToDictionary(z => z.pass, z => z.ms));
                for (int i = 0; i < frames.Count; i++)
                {
                    var fr = frames[i];
                    g.TryGetValue(fr.Index, out var d);
                    string P(int p) => d != null && d.TryGetValue(p, out var v) ? F(v, "F3") : "";
                    sw.WriteLine($"{fr.Index},{F(fr.T, "F3")},{(i > 0 ? F(fr.T - frames[i - 1].T, "F3") : "")},{F(fr.CpuMs, "F3")},{P(0)},{P(1)},{P(2)}");
                }
            }

            // ---- verdict ----
            bool gpuPath = frames.Count > 0 && frames.All(f => f.Gpu) && Rec.GlRenderer.Length > 0
                && !Rec.GlRenderer.Contains("SwiftShader", StringComparison.OrdinalIgnoreCase) && !Rec.GlRenderer.Contains("Basic Render", StringComparison.OrdinalIgnoreCase)
                && !Rec.GlRenderer.Contains("WARP", StringComparison.OrdinalIgnoreCase);
            bool glass = Config != "noglass";
            double timerCoverage = frames.Count == 0 ? 0 : (double)byPass[0].Count / frames.Count;
            bool timerOk = gpuPath && timerCoverage >= 0.90 && (!glass || byPass[1].Count >= 0.90 * frames.Count);
            bool flushOk = flMed >= 44.0 && flMed <= 50.0 && fl.Length > 0;
            bool pointerOk = !interfered && pointerEvents > 100 && moves > 0 && (misses + targetMisses) < 0.05 * moves;
            bool frameOk = fMed <= 16.7 && fP99 <= 33.0;
            bool snapOk = !glass || (g1P99 <= 3.0);
            Log($"CHECKS screenLive={live} gpuPath={gpuPath} timerCoverage={F(timerCoverage)} timerOk={timerOk} flushOk={flushOk} pointerOk={pointerOk} | frameMedian={F(fMed)}<=16.7 frameP99={F(fP99)}<=33 -> {frameOk} | snapshotGpuP99={F(g1P99)}<=3.0 -> {snapOk}");
            if (Ctl != "none" || RenderMode == "software")
            {
                string expect = Ctl switch
                {
                    "stall" => $"stall control must be flagged by the frame timer: frameMedian {F(fMed)} > 16.7 => flagged={fMed > 16.7}",
                    "gpuheavy" => $"gpuheavy control must be flagged by the GPU timer: snapshotGpuP99 {F(g1P99)} > 3.0 => flagged={g1P99 > 3.0}; CPU op median {F(cMed)} ms (must stay small relative to the GPU increase)",
                    _ => $"software control must fail the GPU-path check: gpuPath={gpuPath} => flagged={!gpuPath}",
                };
                Log("CONTROL " + expect);
                result = 3;
            }
            else if (RenderSide.NoTimer)
            {
                Log($"RESULT characterisation (--notimer: no GPU queries, frame timer only): frame budget {(frameOk ? "met" : "missed")}");
                result = frameOk ? 0 : 1;
            }
            else if (!live || !gpuPath || !timerOk || !flushOk || !pointerOk)
            {
                Log("RESULT INCONCLUSIVE (a precondition failed; see CHECKS)");
                result = 2;
            }
            else if (Config == "noglass")
            {
                Log($"RESULT baseline (noglass, not a verdict run): frame budget {(frameOk ? "met" : "missed")}");
                result = frameOk ? 0 : 1;
            }
            else
            {
                bool pass = frameOk && snapOk;
                Log($"RESULT {(pass ? "PASS" : "FAIL")} config={Config} adapter='{Rec.GlRenderer}'");
                result = pass ? 0 : 1;
            }
            _ = cP99; _ = g0Med; _ = g2P99; _ = g1Max; _ = fMax; _ = g2Med;
        }
        finally
        {
            stopPointer.Cancel(); stopFlush.Cancel();
            if (win != null) { win.Running = false; win.Close(); }
            N.timeEndPeriod(1);
            N.SetCursorPos(cursor0.X, cursor0.Y);
            N.GetCursorPos(out var c1);
            Log($"window closed; cursor restored to ({c1.X},{c1.Y}) (was ({cursor0.X},{cursor0.Y}))");
        }
        return result;
    }
}
