// P4b, follow-up to P4 (task packet T-M0-003) under owner ruling OD-0013 ("the live-resize edge strip must be fixed").
//
// QUESTION: on P4's configuration (Avalonia 12.1.2, 1320x860 client, WindowDecorations.Full,
// ExtendClientAreaToDecorationsHint + title bar hint 44, TransparencyLevelHint [AcrylicBlur], P1's glass surface, the
// app-drawn scrim), which remedy removes the strip that the GROWING right and bottom edges show while the pointer moves
// (the window's own backdrop without the app layer, 3-5 px, 4-12% of P4's dense samples), and the one-frame clip on
// the SHRINKING edges, measured with P4's own dense edge sampler and P4's thresholds?
//
// How Avalonia 12.1.2 presents a resize (read from the shipped IL with ../ildump, see RESULT.md):
//   WindowImpl.AppWndProc WM_SIZE -> Resized -> WindowBase.HandleResized: ClientSize set, LayoutManager.ExecuteLayoutPass()
//   synchronously, CompositingRenderer.Resized (empty). The scene is recorded later (CompositingRenderer.UpdateCore, on the
//   next UI render tick, which also sets CompositionTarget.Size), and rendered on the "DwmRenderTimerLoop" thread, whose
//   ticks are WinUI ICompositor5.RequestCommitAsync completions. WinUiCompositedWindowRenderTarget.BeginDraw(sceneInfo)
//   then gives Avalonia's content SpriteVisual an ABSOLUTE size (WinUiCompositedWindow.ResizeIfNeeded -> IVisual.SetSize)
//   while the acrylic SpriteVisual and the root ContainerVisual are sized RELATIVE to the window
//   (RelativeSizeAdjustment 1,1). DWM applies the window's new size at once, the content visual's new size one or more
//   frames later: that is the strip. WM_PAINT runs CompositingRenderer.Paint -> MediaContext.ImmediateRenderRequested ->
//   SyncCommit(waitFullRender: true) (the UI thread waits for the render thread), but WM_PAINT is only dispatched when the
//   modal size loop goes idle. (Session 3: the loop goes idle right after every step; messages.csv shows WM_PAINT 0.13 ms
//   after WM_SIZE, median, in 407 of 408 steps, so the unremedied window already renders each size synchronously.)
//
// Cases (each on its own fresh subject window, all in one session; `base` is the unremedied control, three times):
//   base        no remedy.
//   sync        WM_SIZE, after Avalonia's own handling, inside the modal size loop: RedrawWindow(RDW_INVALIDATE |
//               RDW_UPDATENOW). That sends WM_PAINT at once, and Avalonia's WM_PAINT path records the new layout, commits
//               it and blocks the UI thread until the render thread has rendered the new size.
//   flush       WM_SIZE (after Avalonia): DwmFlush() only.
//   syncflush   sync, then DwmFlush() before WM_SIZE returns.
//   align       DwmFlush() in WM_SIZING (before the system resizes the window), then sync, then DwmFlush().
//   validrects  WM_NCCALCSIZE (after Avalonia's) returns WVR_VALIDRECTS with 1x1 source and destination rectangles,
//               plus syncflush.
//   overrender  in a size loop the client rectangle (WM_NCCALCSIZE) reaches 64 px past the window's right and bottom
//               edges; the app lays its content out in the visible part and fills the extra 64 px with its scrim, so a
//               newly exposed strip already holds scrim. Trimmed at WM_EXITSIZEMOVE.
//   hide        the scrim moves out of the app into the window's composition tree: a WinUI SpriteVisual with a colour
//               brush of the scrim, sized relative to the window, between the acrylic visual and Avalonia's content
//               visual; the app stops drawing its own scrim. A newly exposed strip then shows acrylic plus scrim.
//   stretch     Avalonia's content SpriteVisual is sized relative to the window (RelativeSizeAdjustment 1,1, Size 0,0)
//               instead of absolutely at render time, so DWM stretches the last presented frame (surface brush
//               Stretch = Fill) to the window's new size until the new size is presented.
//   stretchsync stretch plus syncflush.
//   stretchrdw  stretch plus sync, without DwmFlush (added after run 2, which showed two rare frames under `stretch`).
//   Added in session 3, after the wide diagnostic showed that the rare frames are DWM clipping the client area at the
//   previous client rectangle while the frame is already at the new size:
//   over2       overrender, with the window rectangle made smaller by the invisible resize border at WM_ENTERSIZEMOVE
//               and in every WM_SIZING (restored at WM_EXITSIZEMOVE), so the visible frame does not move.
//   overstretch over2 plus stretch, the stretched content visual's Size set to the client area beyond the window.
//   overstretchrdw, over2sync  the same plus sync (diagnostics).
//
// PRE-REGISTERED THRESHOLDS (fixed before the first recorded run; the development smoke runs are not evidence):
//   content-lag artefact (P4's dense criterion, unchanged): on a stable dense sample (DWM frame bounds identical before
//     and after the two line captures), the magenta marker on the right row or the bottom column is missing within
//     64 px of the frame edge, or sits more than 2 px from its at-rest calibrated offset.
//   phase of a sample: the phase in which its capture ENDED, by the driver's recorded step times; a moving phase keeps
//     its samples until 50 ms after its last pointer step. (P4 tagged the phase when a sample started; a smoke run showed
//     that this files a capture that overlaps the first shrink step under "hold". P4's tag is kept in the CSV.)
//   grow strip rate = artefact samples / stable samples while growing; shrink clip rate the same while shrinking.
//   "no strip while moving" (a remedy PASSES) = 0 artefacts in grow AND in shrink, over >= 200 stable grow samples and
//     >= 150 stable shrink samples, 0 in hold, hold2 and post, no pointer miss, AND every `base` case of the same
//     session shows >= 1 grow artefact (the control). If a `base` case shows none, the session is INCONCLUSIVE.
//   visible strip (appearance, for remedies that fill the strip instead of removing the lag), in a stable sample whose
//     right (bottom) marker sits more than 2 px inside its calibrated offset, over the band between the frame's first
//     inner pixel and the marker:
//     primary (neighbour): a band pixel differs, in some channel, by more than T_nb from the mean of the four scrim
//       pixels just inside the displaced marker (d = off+3 .. off+6). T_nb = max(12, 3 x the largest difference at rest
//       between the four scrim pixels nearest the edge and the mean of the four 7 px further in).
//     secondary (reference): a band pixel differs by more than T_app from the same screen pixel of a reference image
//       (the window enlarged past the drag extent, drawing only its scrim, at rest). T_app = max(24, 2 x the largest such
//       difference at rest 4..12 px inside the edge). Smoke runs showed the acrylic 22 levels lighter near the window's
//       edge than deep inside, which inflates this one; hence the neighbour test is primary.
//   scaled frame (the side effect of `stretch`): in a stable dense sample, the static cyan tick drawn at client x = 1260
//     across the dense right-edge row (60 px inside the rest edge, laid out from the left edge, which never moves in this
//     drag) is >= 1 px from its most common at-rest position. A second tick at client x = 660 on P4's spring row is read
//     by the row sampler as a cross-check. (A diagnostic run with a 20 ms render delay, --slowrender 20, showed base
//     strips in 79/80 grow samples and stretch 0 strips with the spring-row tick displaced in 54/82 grow rows: DWM shows
//     the stale frame scaled to the window.)
//   DWM animation (P7's detector, unchanged): >= 2 distinct intermediate bounding-box areas of the window's magenta in
//     1/4-scale captures of the monitor over 800 ms after WM_SYSCOMMAND SC_MAXIMIZE / SC_RESTORE / SC_MINIMIZE.
//     Control: the same window with DWMWA_TRANSITIONS_FORCEDISABLED must read not animated.
//   Snap Layouts flyout (P7's detector, unchanged): > 5% of the zone below the caption changes after a 2 s hover on the
//     declared maximise button, against the cursor parked in the content. Control: parked vs parked < 5%.
//   Snap by drag: after dragging the declared caption to the monitor's top edge and releasing, IsZoomed = true.
//
// Every window this probe touches is its own (checker, subject, ctl_inset). Input: SendInput absolute moves and left
// button events, only at points where WindowFromPoint's root is our subject HWND (and WM_NCHITTEST says what we expect);
// during drags the window holds capture and follows the cursor. The cursor is restored at the end.
//
//   rest geometry (added in session 3, a check on the remedies, not a threshold of P4): the marker offsets from the
//     client edges and the positions of the two static ticks, restored, maximised and restored again, must equal those
//     of the session's first `base` case ("SAME as base"). A remedy that scaled or shifted content at rest would differ.
//
// Usage: p4b --out <dir> [--cases a,b,c] [--reps 4] [--nosnap] [--snapfirst] [--snapcases a,b] [--nogeom] [--edge br|tl]
//            [--slowrender <ms>] [--idlegate <s>] [--cornercap]
// Exit 0 = at least one remedy passes and every control held; 1 = no remedy passes (controls held);
// 2 = INCONCLUSIVE; 4 = crash.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using SkiaSharp;

static class N
{
    public const int GWL_EXSTYLE = -20, GWL_STYLE = -16, GWLP_WNDPROC = -4;
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20, SWP_SHOWWINDOW = 0x40;
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const int SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000, COLORONCOLOR = 3;
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_TRANSITIONS_FORCEDISABLED = 3;
    public const uint GA_ROOT = 2;
    public const int SPI_GETWORKAREA = 0x30, SPI_GETANIMATION = 0x48, SPI_GETCLIENTAREAANIMATION = 0x1042, SPI_GETWINARRANGING = 0x82, SPI_GETDOCKMOVING = 0x90, SPI_GETSNAPSIZING = 0x8E;
    public const uint WM_NCHITTEST = 0x84, WM_SYSCOMMAND = 0x112;
    public const int SC_SIZE = 0xF000, SC_MOVE = 0xF010, SC_MINIMIZE = 0xF020, SC_MAXIMIZE = 0xF030, SC_RESTORE = 0xF120;
    public const uint RDW_INVALIDATE = 0x1, RDW_UPDATENOW = 0x100;
    public const int WVR_VALIDRECTS = 0x400;
    public const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
    public const uint INPUT_MOUSE = 0, MOUSEEVENTF_MOVE = 0x1, MOUSEEVENTF_LEFTDOWN = 0x2, MOUSEEVENTF_LEFTUP = 0x4,
        MOUSEEVENTF_ABSOLUTE = 0x8000, MOUSEEVENTF_VIRTUALDESK = 0x4000;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int L, T, R, B;
        public readonly int W => R - L; public readonly int H => B - T;
        public readonly bool Same(RECT o) => L == o.L && T == o.T && R == o.R && B == o.B;
        public override readonly string ToString() => $"[{L},{T}]-[{R},{B}] {W}x{H}";
    }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; public override readonly string ToString() => $"({X},{Y})"; }
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)] public struct NCCALCSIZE_PARAMS { public RECT r0, r1, r2; public IntPtr lppos; }
    [StructLayout(LayoutKind.Sequential)] public struct ANIMATIONINFO { public uint cbSize; public int iMinAnimate; }
    [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
    [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO li);
    [DllImport("kernel32.dll")] public static extern uint GetTickCount();
    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
    }
    public delegate IntPtr WndProc(IntPtr h, uint m, IntPtr w, IntPtr l);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll")] public static extern IntPtr CallWindowProcW(IntPtr prev, IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool RedrawWindow(IntPtr h, IntPtr rc, IntPtr rgn, uint flags);
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
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] public static extern bool SpiInt(int a, int b, out int v, int c);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] public static extern bool SpiAnim(int a, int b, ref ANIMATIONINFO v, int c);
    [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint f);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfoW(IntPtr m, ref MONITORINFO mi);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, int rop);
    [DllImport("gdi32.dll")] public static extern bool StretchBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, int sw, int sh, int rop);
    [DllImport("gdi32.dll")] public static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bi, uint usage);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmFlush();

    public static RECT FrameBounds(IntPtr h)
    {
        if (DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out var r, Marshal.SizeOf<RECT>()) == 0) return r;
        GetWindowRect(h, out r);
        return r;
    }

    public static RECT MonitorOf(IntPtr h)
    {
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfoW(MonitorFromWindow(h, 2), ref mi);
        return mi.rcMonitor;
    }

    /// Screen DC BitBlt: the DWM-composed desktop image of the rectangle, as top-down BGRA.
    public static byte[] Capture(RECT r) => Capture(r, r.W, r.H);

    /// Same, scaled to dw x dh (StretchBlt, COLORONCOLOR) when the size differs.
    public static byte[] Capture(RECT r, int dw, int dh)
    {
        IntPtr scr = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(scr), bmp = CreateCompatibleBitmap(scr, dw, dh);
        IntPtr old = SelectObject(mem, bmp);
        bool ok;
        if (dw == r.W && dh == r.H) ok = BitBlt(mem, 0, 0, r.W, r.H, scr, r.L, r.T, SRCCOPY | CAPTUREBLT);
        else { SetStretchBltMode(mem, COLORONCOLOR); ok = StretchBlt(mem, 0, 0, dw, dh, scr, r.L, r.T, r.W, r.H, SRCCOPY | CAPTUREBLT); }
        SelectObject(mem, old);
        var bi = new BITMAPINFOHEADER { biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = dw, biHeight = -dh, biPlanes = 1, biBitCount = 32 };
        var buf = new byte[dw * dh * 4];
        int lines = GetDIBits(mem, bmp, 0, (uint)dh, buf, ref bi, 0);
        DeleteObject(bmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, scr);
        if (!ok || lines != dh) throw new InvalidOperationException($"capture failed blt={ok} lines={lines}");
        for (int i = 3; i < buf.Length; i += 4) buf[i] = 255;
        return buf;
    }

    public static uint MoveAbs(int x, int y)
    {
        int vx = GetSystemMetrics(SM_XVIRTUALSCREEN), vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int vw = GetSystemMetrics(SM_CXVIRTUALSCREEN), vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        int nx = (int)Math.Floor((x - vx + 0.5) * 65536.0 / vw), ny = (int)Math.Floor((y - vy + 0.5) * 65536.0 / vh);
        var i = new[] { new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dx = nx, dy = ny, dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK } } };
        return SendInput(1, i, Marshal.SizeOf<INPUT>());
    }

    public static uint Button(bool down)
    {
        var i = new[] { new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP } } };
        return SendInput(1, i, Marshal.SizeOf<INPUT>());
    }

    public static IntPtr Root(POINT p) => GetAncestor(WindowFromPoint(p), GA_ROOT);

    public static int NcHit(IntPtr h, POINT p)
    {
        long lp = ((long)(ushort)(short)p.Y << 16) | (ushort)(short)p.X;
        return (int)SendMessageW(h, WM_NCHITTEST, IntPtr.Zero, new IntPtr(lp)).ToInt64();
    }
}

static class Clock
{
    static readonly Stopwatch Sw = Stopwatch.StartNew();
    public static double Ms => Sw.Elapsed.TotalMilliseconds;
}

/// P4's test spring (ENGINEERING choice for the probe, not a source constant): response 0.8 s, damping fraction 0.5,
/// integrated in 1 ms steps against wall-clock time; the target flips between two x positions every 700 ms.
sealed class Spring
{
    const double Response = 0.8, Damping = 0.5, Flip = 700, A = 120, B = 700;
    static readonly double K = Math.Pow(2 * Math.PI / Response, 2), C = 4 * Math.PI * Damping / Response;
    double _t0 = -1, _sim, _x = A, _v;
    public double Target(double t) => ((long)((t - _t0) / Flip) % 2 == 0) ? B : A;
    public double Advance(double now)
    {
        if (_t0 < 0) { _t0 = now; _sim = now; }
        while (_sim + 1 <= now)
        {
            _sim += 1;
            double a = -K * (_x - Target(_sim)) - C * _v;
            _v += a * 0.001; _x += _v * 0.001;
        }
        return _x;
    }
}

/// Owner-activity guard (added after run 2, where the owner moved the pointer during a drag and the probe still sent
/// its button press): the probe remembers where its own last input left the cursor; any other movement means the owner
/// is using the machine, and the probe stops sending input for the rest of the session.
static class Owner
{
    public static N.POINT Last;
    public static volatile bool Abort;
    public static string Why = "";
    public static void Mark() => N.GetCursorPos(out Last);
    public static bool Moved(out N.POINT now) { N.GetCursorPos(out now); return Math.Abs(now.X - Last.X) > 1 || Math.Abs(now.Y - Last.Y) > 1; }
    public static void Flag(string why) { if (!Abort) { Why = why; Abort = true; } }
    public static bool At(N.POINT want, out N.POINT now) { N.GetCursorPos(out now); return Math.Abs(now.X - want.X) <= 1 && Math.Abs(now.Y - want.Y) <= 1; }
}

/// Diagnostics shared with the render thread.
static class Diag
{
    public static volatile int SlowRenderMs;
    public static volatile bool InSizeLoop;
}

/// Per-case recorders (cleared at the start of every case; only one subject window exists at a time).
static class Rec
{
    public static ConcurrentQueue<(double ms, double x, double w, double h)> Ui = new();
    /// Render thread: time, spring x, layout size the scene was recorded at, device clip of the canvas, and the window's
    /// client size (GetClientRect) at that moment.
    public static ConcurrentQueue<(double ms, double x, double w, double h, int clipW, int clipH, int clientW, int clientH)> Render = new();
    public static ConcurrentQueue<(double ms, string what)> Msgs = new();
    public static bool LeaseOk, SurfaceOk, EffectOk;
    public static string EffectErr = "";
    public static void Clear() { Ui = new(); Render = new(); Msgs = new(); }
}

static class Glass
{
    // P1's SKSL, unchanged (as in P4): edge-concentrated displacement with per-channel offsets, specular rim, superellipse.
    const string Sksl = @"
uniform shader backdrop;
uniform float2 size;
uniform float radius;
uniform float band;
uniform float strength;
uniform float4 tint;
uniform float rimGain;
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
    return col;
}";
    static SKRuntimeEffect? _effect;
    public static SKRuntimeEffect? Effect()
    {
        if (_effect != null) return _effect;
        _effect = SKRuntimeEffect.CreateShader(Sksl, out var err);
        if (_effect == null) Rec.EffectErr = err ?? "unknown";
        return _effect;
    }
}

/// Client-area geometry, DIP (= physical px at the 96 DPI this probe runs at). As in P4.
static class Geo
{
    public const double CardL = 48, CardT = 64, CardInset = 48;
    public const double PanelT = 300, PanelW = 440, PanelH = 220;
    public const double BarW = 6;
    public const double RowY = 410;
    public const float Marker = 3;
    public const int OverMargin = 64;
    public const double TickX = 660;                                     // stretch readout tick (P4b), on the row sampler's row
    public const double Tick2X = 1260, DenseY = 200;                     // stretch readout tick on the dense right-edge row
}

sealed class Scene : Control
{
    public double SpringX = 120;
    public double InsetRB;              // 0 for the subject; 24 for the ctl_inset control
    public bool DrawScrim = true;       // false in `hide` (the scrim lives in the composition tree)
    public double OverMargin;           // 64 while `overrender` has extended the client area
    public bool RefMode;                // appearance reference: the scrim only
    public bool MagentaFill;            // animation and Snap checks: a static all-magenta window
    public IntPtr Hwnd;

    public override void Render(DrawingContext ctx) =>
        ctx.Custom(new Op(new Rect(Bounds.Size), SpringX, InsetRB, DrawScrim, OverMargin, RefMode, MagentaFill, Hwnd));

    sealed class Op : ICustomDrawOperation
    {
        readonly double _x, _inset, _over;
        readonly bool _scrim, _ref, _magenta;
        readonly IntPtr _hwnd;
        public Op(Rect b, double x, double inset, bool scrim, double over, bool refMode, bool magenta, IntPtr hwnd)
        { Bounds = b; _x = x; _inset = inset; _scrim = scrim; _over = over; _ref = refMode; _magenta = magenta; _hwnd = hwnd; }
        public Rect Bounds { get; }
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature is null) { Rec.Render.Enqueue((Clock.Ms, _x, Bounds.Width, Bounds.Height, -1, -1, -1, -1)); return; }
            Rec.LeaseOk = true;
            // Diagnostic knob (--slowrender): a render thread that needs longer than a frame while a size loop runs.
            if (Diag.SlowRenderMs > 0 && Diag.InSizeLoop && !_magenta && !_ref) Thread.Sleep(Diag.SlowRenderMs);
            using var lease = feature.Lease();
            var c = lease.SkCanvas;
            var clip = c.DeviceClipBounds;
            N.RECT cr = default;
            if (_hwnd != IntPtr.Zero) N.GetClientRect(_hwnd, out cr);
            Rec.Render.Enqueue((Clock.Ms, _x, Bounds.Width, Bounds.Height, clip.Width, clip.Height, cr.W, cr.H));

            float fullW = (float)(Bounds.Width - _inset), fullH = (float)(Bounds.Height - _inset);
            if (_magenta)
            {
                using var mg = new SKPaint { Color = new SKColor(255, 0, 255) };
                c.DrawRect(new SKRect(0, 0, fullW, fullH), mg);
                return;
            }
            // Content area: in `overrender` the client area reaches OverMargin past the visible edges; the app lays its
            // content out in the visible part and only its scrim fills the rest.
            float W = fullW - (float)_over, H = fullH - (float)_over;

            if (_scrim)
            {
                // Source dark scrim over the acrylic (P3: Avalonia's AcrylicBlur adds no tint; the app draws it).
                using (var p = new SKPaint { Color = new SKColor(0, 0, 0, (byte)Math.Round(0.26 * 255)) }) c.DrawRect(new SKRect(0, 0, fullW, fullH), p);
                using (var p = new SKPaint { Color = new SKColor(0x1E, 0x1E, 0x20, (byte)Math.Round(0.12 * 255)) }) c.DrawRect(new SKRect(0, 0, fullW, fullH), p);
            }
            if (_ref) return;

            var card = new SKRect((float)Geo.CardL, (float)Geo.CardT, W - (float)Geo.CardInset, H - (float)Geo.CardInset);
            if (card.Width > 10 && card.Height > 10)
            {
                c.Save();
                c.ClipRoundRect(new SKRoundRect(card, 16, 16), SKClipOperation.Intersect, true);
                c.Translate(card.Left, card.Top);
                Content(c, card.Width, card.Height);
                c.Restore();

                var panel = new SKRect((float)_x, (float)Geo.PanelT, (float)(_x + Geo.PanelW), (float)(Geo.PanelT + Geo.PanelH));
                var info = new SKImageInfo((int)panel.Width, (int)panel.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
                using var off = lease.GrContext is not null ? SKSurface.Create(lease.GrContext, false, info) : SKSurface.Create(info);
                var effect = Glass.Effect();
                if (off != null && effect != null)
                {
                    Rec.SurfaceOk = true; Rec.EffectOk = true;
                    var oc = off.Canvas;
                    oc.Clear(SKColors.Transparent);
                    oc.Save();
                    oc.Translate(card.Left - panel.Left, card.Top - panel.Top);
                    Content(oc, card.Width, card.Height);
                    oc.Restore();
                    using var img = off.Snapshot();
                    var u = new SKRuntimeEffectUniforms(effect)
                    {
                        ["size"] = new[] { panel.Width, panel.Height },
                        ["radius"] = 26f, ["band"] = 46f, ["strength"] = 26f, ["rimGain"] = 0.55f,
                        ["tint"] = new[] { 0x1E / 255f, 0x1E / 255f, 0x20 / 255f, 0.30f },
                    };
                    using var bs = img.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp);
                    var ch = new SKRuntimeEffectChildren(effect) { ["backdrop"] = bs };
                    using var shader = effect.ToShader(u, ch);
                    using var paint = new SKPaint { Shader = shader, IsAntialias = true };
                    c.Save();
                    c.Translate(panel.Left, panel.Top);
                    c.ClipRoundRect(new SKRoundRect(new SKRect(0, 0, panel.Width, panel.Height), 26, 26), SKClipOperation.Intersect, true);
                    c.DrawRect(new SKRect(0, 0, panel.Width, panel.Height), paint);
                    c.Restore();
                }
                using var gp = new SKPaint { Color = new SKColor(0, 255, 0) };
                float bx = (float)(_x + Geo.PanelW / 2 - Geo.BarW / 2);
                c.DrawRect(new SKRect(bx, panel.Top, bx + (float)Geo.BarW, panel.Bottom), gp);
                // Stretch readout (P4b): a static cyan tick at a fixed client x, on top of everything, crossed by the row
                // sampler. Laid out from the left edge, it only moves on screen if a frame is shown scaled.
                using var tp = new SKPaint { Color = new SKColor(0, 255, 255) };
                // Both ticks reach 190 px further down so that a top-left drag (whose rows cross ever lower client rows) still
                // crosses them.
                c.DrawRect(new SKRect((float)Geo.TickX, (float)(Geo.RowY - 10), (float)(Geo.TickX + 2), (float)(Geo.RowY + 190)), tp);
                // A second one inside the dense sampler's right-edge row (client y = 200), 60 px inside the rest edge,
                // where a scaled frame moves it by about one resize step.
                c.DrawRect(new SKRect((float)Geo.Tick2X, (float)(Geo.DenseY - 10), (float)(Geo.Tick2X + 2), (float)(Geo.DenseY + 190)), tp);
            }

            // The edge marker: 3 px pure magenta at the outermost pixels of what the app lays out.
            using var mp = new SKPaint { Color = new SKColor(255, 0, 255) };
            float m = Geo.Marker;
            c.DrawRect(new SKRect(0, 0, W, m), mp);
            c.DrawRect(new SKRect(0, H - m, W, H), mp);
            c.DrawRect(new SKRect(0, 0, m, H), mp);
            c.DrawRect(new SKRect(W - m, 0, W, H), mp);
        }

        static readonly SKFont Font = new(SKTypeface.Default, 22);
        static void Content(SKCanvas c, float w, float h)
        {
            c.Clear(new SKColor(0x0B, 0x0B, 0x0D));
            using (var p = new SKPaint { IsAntialias = true })
            {
                p.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(w, h),
                    new[] { new SKColor(0x0A, 0x2A, 0x4A), new SKColor(0x00, 0x8C, 0xC8), new SKColor(0xE8, 0xF4, 0xFF) },
                    new[] { 0f, 0.55f, 1f }, SKShaderTileMode.Clamp);
                c.DrawRect(new SKRect(0, h * 0.42f, w, h), p);
            }
            using (var p = new SKPaint { Color = SKColors.White, IsAntialias = true })
                for (int i = 0; i < 7; i++) c.DrawRect(new SKRect(0, 120 + i * 46, w, 120 + i * 46 + 6), p);
            using (var p = new SKPaint { Color = new SKColor(0xEC, 0xEC, 0xF0), IsAntialias = true })
                for (int i = 0; i < 16; i++) c.DrawText("Worked for 3m 0s   git commit -c core.quotepath=false   +51 -37", 70, 470 + i * 30, Font, p);
        }
    }
}

sealed class CheckerControl : Control
{
    static readonly IBrush White = new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
    static readonly IBrush Blue = new ImmutableSolidColorBrush(Color.FromRgb(0x00, 0x33, 0xFF));
    public override void Render(DrawingContext ctx)
    {
        var s = Bounds.Size; const double q = 96;
        ctx.FillRectangle(Blue, new Rect(s));
        for (int y = 0; y * q < s.Height; y++)
            for (int x = 0; x * q < s.Width; x++)
                if (((x + y) & 1) == 0) ctx.FillRectangle(White, new Rect(x * q, y * q, q, q));
    }
}

sealed class CheckerWindow : Window
{
    public CheckerWindow() { Template = new FuncControlTemplate<Window>((_, _) => new CheckerControl()); }
}

/// P4's subject window, plus P7's chrome hit-test declarations (a transparent 44 px caption band declared Caption and the
/// three traffic-light buttons at P7's source constants declared Close / MinButton / MaxButton) so that Snap can be
/// exercised. The declarations sit at the top-left; P4's measurement uses only the right and bottom edges.
sealed class SubjectWindow : Window
{
    public readonly Scene Scene = new();
    public readonly Border TitleBar = new() { Background = Brushes.Transparent, Height = 44, VerticalAlignment = VerticalAlignment.Top };
    public readonly Avalonia.Controls.Shapes.Ellipse CloseB = new(), MinB = new(), MaxB = new();
    readonly Canvas _lights = new() { Height = 44, VerticalAlignment = VerticalAlignment.Top };
    readonly Panel _root = new();
    readonly Spring _spring = new();
    public bool Animate = true;
    readonly bool _record;

    public SubjectWindow(string title, double inset, bool record)
    {
        _record = record;
        Title = title;
        Width = 1320; Height = 860;                                   // WindowManager.swift:19 (P1 Src.W/H)
        WindowDecorations = WindowDecorations.Full;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = 44;                      // P7's measured chrome configuration
        TransparencyLevelHint = new[] { WindowTransparencyLevel.AcrylicBlur };
        Background = Brushes.Transparent;
        CanResize = true;
        Topmost = true;
        ShowInTaskbar = false;
        Scene.InsetRB = inset;
        foreach (var (e, c, left) in new[] { (CloseB, Color.FromRgb(0xFF, 0x5F, 0x57), 18.0), (MinB, Color.FromRgb(0xFE, 0xBC, 0x2E), 41.0), (MaxB, Color.FromRgb(0x28, 0xC8, 0x40), 64.0) })
        {
            e.Width = 14; e.Height = 14; e.Fill = new ImmutableSolidColorBrush(c);
            Canvas.SetLeft(e, left); Canvas.SetTop(e, 16);
            _lights.Children.Add(e);
        }
        _root.Children.Add(Scene);
        _root.Children.Add(TitleBar);
        _root.Children.Add(_lights);
        Win32Properties.SetNonClientHitTestResult(TitleBar, Win32Properties.Win32HitTestValue.Caption);
        Win32Properties.SetNonClientHitTestResult(CloseB, Win32Properties.Win32HitTestValue.Close);
        Win32Properties.SetNonClientHitTestResult(MinB, Win32Properties.Win32HitTestValue.MinButton);
        Win32Properties.SetNonClientHitTestResult(MaxB, Win32Properties.Win32HitTestValue.MaxButton);
        Template = new FuncControlTemplate<Window>((_, _) => _root);
        if (record) Win32Properties.AddWndProcHookCallback(this, Hook);
    }

    public void Start() => RequestAnimationFrame(OnFrame);

    void OnFrame(TimeSpan ts)
    {
        if (!Animate) return;
        double now = Clock.Ms;
        double x = _spring.Advance(now);
        if (_record) Rec.Ui.Enqueue((now, x, Bounds.Width, Bounds.Height));
        Scene.SpringX = x;
        Scene.InvalidateVisual();
        RequestAnimationFrame(OnFrame);
    }

    static IntPtr Hook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        string? s = msg switch
        {
            0x0231 => "WM_ENTERSIZEMOVE",
            0x0232 => "WM_EXITSIZEMOVE",
            0x0214 => $"WM_SIZING edge={wParam.ToInt64()}",
            0x0005 => $"WM_SIZE {lParam.ToInt64() & 0xFFFF}x{(lParam.ToInt64() >> 16) & 0xFFFF}",
            0x000F => "WM_PAINT",
            0x00A1 => $"WM_NCLBUTTONDOWN ht={wParam.ToInt64()}",
            0x0112 => $"WM_SYSCOMMAND 0x{wParam.ToInt64() & 0xFFF0:X4}",
            0x0215 => "WM_CAPTURECHANGED",
            _ => null,
        };
        if (s != null) Rec.Msgs.Enqueue((Clock.Ms, s));
        return IntPtr.Zero;
    }
}

/// The remedy flags of a case.
sealed record Remedy(bool Sync, bool FlushBefore, bool FlushAfter, bool ValidRects, bool OverRender, bool Hide, bool Stretch, bool Over2 = false)
{
    public static Remedy For(string c) => c switch
    {
        "base" => new(false, false, false, false, false, false, false),
        "sync" => new(true, false, false, false, false, false, false),
        "flush" => new(false, false, true, false, false, false, false),
        "syncflush" => new(true, false, true, false, false, false, false),
        "align" => new(true, true, true, false, false, false, false),
        "validrects" => new(true, false, true, true, false, false, false),
        "overrender" => new(false, false, false, false, true, false, false),
        "hide" => new(false, false, false, false, false, true, false),
        "stretch" => new(false, false, false, false, false, false, true),
        "stretchsync" => new(true, false, true, false, false, false, true),
        "stretchrdw" => new(true, false, false, false, false, false, true),   // added after run 2: stretch + sync, no DwmFlush
        // Added in session 3 after the wide diagnostic showed the second lag type (DWM still clipping the client area at
        // the previous client rectangle): during an SC_SIZE loop the client rectangle reaches 64 px past the window's
        // right and bottom edges, and the window rectangle is made smaller by the invisible resize border so the visible
        // frame stays exactly where it was (over2); combined with stretch (overstretch).
        "over2" => new(false, false, false, false, false, false, false, true),
        "overstretch" => new(false, false, false, false, false, false, true, true),
        "overstretchrdw" => new(true, false, false, false, false, false, true, true),
        "over2sync" => new(true, false, false, false, false, false, false, true),
        _ => throw new ArgumentException("unknown case " + c),
    };
}

/// A window-procedure subclass wrapped around Avalonia's own (SetWindowLongPtr GWLP_WNDPROC + CallWindowProc), so the
/// remedy runs both before and after Avalonia handles a message. Win32Properties' hook only runs before.
sealed class Subclass
{
    static readonly List<Subclass> Alive = new();                  // every delegate stays alive for the process lifetime
    readonly IntPtr _h, _orig;
    readonly N.WndProc _proc;
    readonly Remedy _r;
    readonly Scene _scene;
    int _lastSysCmd;
    public bool InLoop, SizeLoop, Extended;
    public bool Over2On;                 // over2: the loop-time client extension with the window rectangle compensated
    public int ShrinkR, ShrinkB;         // over2: the invisible resize border on the right and bottom (window rect - frame)
    public int OffX, OffY;               // overstretch: the stretched visual's Size offset while over2 is on
    public readonly ConcurrentQueue<(double ms, string kind, double dur, int w, int h)> Hooks = new();

    public Subclass(IntPtr h, Remedy r, Scene scene)
    {
        _h = h; _r = r; _scene = scene;
        _proc = Proc;
        _orig = N.SetWindowLongPtr(h, N.GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_proc));
        lock (Alive) Alive.Add(this);
    }

    IntPtr Orig(IntPtr h, uint m, IntPtr w, IntPtr l) => N.CallWindowProcW(_orig, h, m, w, l);

    void Time(string kind, Action a)
    {
        double t0 = Clock.Ms;
        a();
        double t1 = Clock.Ms;
        N.GetClientRect(_h, out var cr);
        Hooks.Enqueue((t0, kind, t1 - t0, cr.W, cr.H));
    }

    void FrameChanged(string why) => Time(why, () => N.SetWindowPos(_h, IntPtr.Zero, 0, 0, 0, 0,
        N.SWP_NOMOVE | N.SWP_NOSIZE | N.SWP_NOZORDER | N.SWP_NOACTIVATE | N.SWP_FRAMECHANGED));

    IntPtr Proc(IntPtr h, uint m, IntPtr w, IntPtr l)
    {
        switch (m)
        {
            case 0x0112:                                                 // WM_SYSCOMMAND: SC_SIZE starts a size loop
                _lastSysCmd = (int)(w.ToInt64() & 0xFFF0);
                return Orig(h, m, w, l);
            case 0x0231:                                                 // WM_ENTERSIZEMOVE
                {
                    var res = Orig(h, m, w, l);
                    InLoop = true; SizeLoop = _lastSysCmd == N.SC_SIZE;
                    Diag.InSizeLoop = SizeLoop;
                    if (_r.OverRender && SizeLoop)
                    {
                        // One UI-thread handler changes both, so no scene can be recorded in between.
                        Extended = true; _scene.OverMargin = Geo.OverMargin; _scene.InvalidateVisual();
                        FrameChanged("extend");
                    }
                    if (_r.Over2 && SizeLoop)
                    {
                        // over2: the client rectangle reaches Geo.OverMargin past the window rectangle's right and bottom
                        // (WM_NCCALCSIZE below), so DWM, which then shows the whole window rectangle, would show the
                        // invisible resize border too; the window rectangle is therefore made smaller by that border, in
                        // the same SetWindowPos, so the visible frame does not move. The scene lays its content out up to
                        // the visible edge and fills the rest of the client area with its scrim.
                        N.GetWindowRect(h, out var wr);
                        var fb = N.FrameBounds(h);
                        ShrinkR = wr.R - fb.R; ShrinkB = wr.B - fb.B;
                        Extended = true; Over2On = true;
                        _scene.OverMargin = Geo.OverMargin - ShrinkR; _scene.InvalidateVisual();
                        Time("over2_enter", () => N.SetWindowPos(h, IntPtr.Zero, 0, 0, wr.W - ShrinkR, wr.H - ShrinkB,
                            N.SWP_NOMOVE | N.SWP_NOZORDER | N.SWP_NOACTIVATE | N.SWP_FRAMECHANGED));
                        if (_r.Stretch)
                        {
                            // overstretch: the stretched content visual is sized as the root (the visible part of the
                            // client area) plus the part of the client area beyond the window rectangle.
                            N.GetClientRect(h, out var cx);
                            var o = new N.POINT(); N.ClientToScreen(h, ref o);
                            N.GetWindowRect(h, out var wr2);
                            OffX = cx.W - (Math.Min(o.X + cx.W, wr2.R) - o.X);
                            OffY = cx.H - (Math.Min(o.Y + cx.H, wr2.B) - o.Y);
                            Time("over2_offset", () => Comp.SetStretchOffset(OffX, OffY));
                        }
                    }
                    return res;
                }
            case 0x0232:                                                 // WM_EXITSIZEMOVE
                {
                    var res = Orig(h, m, w, l);
                    InLoop = false; SizeLoop = false;
                    Diag.InSizeLoop = false;
                    if (Over2On)
                    {
                        // over2: back to Avalonia's own client rectangle and the full window rectangle, in one call.
                        Over2On = false; Extended = false; _scene.OverMargin = 0; _scene.InvalidateVisual();
                        N.GetWindowRect(h, out var wr);
                        Time("over2_exit", () => N.SetWindowPos(h, IntPtr.Zero, 0, 0, wr.W + ShrinkR, wr.H + ShrinkB,
                            N.SWP_NOMOVE | N.SWP_NOZORDER | N.SWP_NOACTIVATE | N.SWP_FRAMECHANGED));
                        if (_r.Stretch) Time("over2_offset0", () => Comp.SetStretchOffset(0, 0));
                    }
                    else if (Extended)
                    {
                        Extended = false; _scene.OverMargin = 0; _scene.InvalidateVisual();
                        FrameChanged("trim");
                    }
                    return res;
                }
            case 0x0214:                                                 // WM_SIZING: before the system resizes the window
                if (Over2On)
                {
                    // over2: the loop proposes rectangles from the window rectangle it started with; keep the right and
                    // bottom edges one invisible border short, as set at WM_ENTERSIZEMOVE.
                    var rc = Marshal.PtrToStructure<N.RECT>(l);
                    rc.R -= ShrinkR; rc.B -= ShrinkB;
                    Marshal.StructureToPtr(rc, l, false);
                }
                if (_r.FlushBefore && SizeLoop) Time("flush_before", () => N.DwmFlush());
                return Orig(h, m, w, l);
            case 0x0083:                                                 // WM_NCCALCSIZE
                {
                    var res = Orig(h, m, w, l);
                    if (w != IntPtr.Zero && (_r.ValidRects || Extended))
                    {
                        var p = Marshal.PtrToStructure<N.NCCALCSIZE_PARAMS>(l);
                        if (Extended) { p.r0.R += Geo.OverMargin; p.r0.B += Geo.OverMargin; }
                        if (_r.ValidRects)
                        {
                            var one = new N.RECT { L = p.r0.L, T = p.r0.T, R = p.r0.L + 1, B = p.r0.T + 1 };
                            p.r1 = one; p.r2 = one;
                            res = new IntPtr(N.WVR_VALIDRECTS);
                        }
                        Marshal.StructureToPtr(p, l, false);
                    }
                    return res;
                }
            case 0x0005:                                                 // WM_SIZE: Avalonia has done its layout pass
                {
                    var res = Orig(h, m, w, l);
                    if (SizeLoop)
                    {
                        if (_r.Sync) Time("sync", () => N.RedrawWindow(h, IntPtr.Zero, IntPtr.Zero, N.RDW_INVALIDATE | N.RDW_UPDATENOW));
                        if (_r.FlushAfter) Time("flush_after", () => N.DwmFlush());
                    }
                    return res;
                }
        }
        return Orig(h, m, w, l);
    }
}

/// Reaches Avalonia.Win32's WinUI composition objects of one window by reflection (probe only; internal, version-pinned).
/// Path: Window.PlatformImpl (WindowImpl) ._glSurface (WinUiCompositedWindowSurface) ._window (WinUiCompositedWindow),
/// whose fields hold the ICompositionTarget, the acrylic IVisual `_blur`, Avalonia's content IVisual `_visual` and its
/// ICompositionSurfaceBrush `_surfaceBrush`.
static class Comp
{
    const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    static readonly Assembly W32 = typeof(Win32PlatformOptions).Assembly;
    static Type T(string n) => W32.GetType("Avalonia.Win32.WinRT." + n, true)!;
    static readonly Type MicroCom = Type.GetType("MicroCom.Runtime.MicroComRuntime, MicroCom.Runtime", true)!;
    static readonly MethodInfo QIDef = MicroCom.GetMethods(BindingFlags.Public | BindingFlags.Static)
        .First(m => m.Name == "QueryInterface" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);

    static object Get(object o, string f)
    {
        for (var t = o.GetType(); t != null; t = t.BaseType)
        {
            var fi = t.GetField(f, All);
            if (fi != null) return fi.GetValue(o) ?? throw new InvalidOperationException($"{t.Name}.{f} is null");
        }
        throw new MissingFieldException(o.GetType().FullName, f);
    }

    static void Set(object o, string f, object v)
    {
        for (var t = o.GetType(); t != null; t = t.BaseType)
        {
            var fi = t.GetField(f, All);
            if (fi != null) { fi.SetValue(o, v); return; }
        }
        throw new MissingFieldException(o.GetType().FullName, f);
    }

    static object GetProp(object o, string p)
    {
        for (var t = o.GetType(); t != null; t = t.BaseType)
        {
            var pi = t.GetProperty(p, All);
            if (pi != null) return pi.GetValue(o) ?? throw new InvalidOperationException($"{t.Name}.{p} is null");
        }
        throw new MissingMemberException(o.GetType().FullName, p);
    }

    static object QI(object o, string iface) => QIDef.MakeGenericMethod(T(iface)).Invoke(null, new[] { o })!;
    static object? Call(object o, string iface, string m, params object?[] a) => T(iface).GetMethod(m)!.Invoke(o, a);

    public static (object cw, object compositor, object sync, string path) Objects(Window w)
    {
        var impl = GetProp(w, "PlatformImpl");
        var surf = Get(impl, "_glSurface");
        var cw = Get(surf, "_window");
        var shared = Get(cw, "_shared");
        var compositor = GetProp(shared, "Compositor");
        var sync = GetProp(shared, "SyncRoot");
        return (cw, compositor, sync, $"{impl.GetType().Name}._glSurface={surf.GetType().Name} ._window={cw.GetType().Name}");
    }

    public static string SurfaceType(Window w)
    {
        try { return Get(GetProp(w, "PlatformImpl"), "_glSurface").GetType().FullName ?? "?"; }
        catch (Exception e) { return "unavailable: " + e.Message; }
    }

    static string Vis(object visual)
    {
        var size = (Vector2)Call(visual, "IVisual", "get_Size")!;
        var rel = (Vector2)Call(QI(visual, "IVisual2"), "IVisual2", "get_RelativeSizeAdjustment")!;
        return FormattableString.Invariant($"Size=({size.X:F0},{size.Y:F0}) RelativeSizeAdjustment=({rel.X:F2},{rel.Y:F2})");
    }

    public static string Describe(Window w)
    {
        var (cw, _, sync, path) = Objects(w);
        lock (sync)
            return $"{path}; content _visual {Vis(Get(cw, "_visual"))}; acrylic _blur {Vis(Get(cw, "_blur"))}; " +
                   $"surface brush Stretch={Call(Get(cw, "_surfaceBrush"), "ICompositionSurfaceBrush", "get_Stretch")}";
    }

    /// `hide`: a scrim-coloured SpriteVisual sized relative to the window, directly above the acrylic visual.
    public static unsafe string ApplyHide(Window w, byte a, byte r, byte g, byte b)
    {
        var (cw, compositor, sync, _) = Objects(w);
        lock (sync)
        {
            // Not CreateColorBrushWithColor: Avalonia's MicroCom declaration passes the colour by pointer, while the WinRT ABI
            // passes Windows.UI.Color by value (a first attempt produced a colour made of pointer bits). CreateColorBrush()
            // plus ICompositionColorBrush.SetColor(value) matches the ABI.
            var raw = Call(compositor, "ICompositor", "CreateColorBrush")!;
            IntPtr ptr = (IntPtr)Pointer.Unbox(raw);
            var proxyDef = MicroCom.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "CreateProxyFor" && m.IsGenericMethodDefinition && m.GetParameters()[0].ParameterType == typeof(IntPtr));
            var brush = proxyDef.MakeGenericMethod(T("ICompositionColorBrush")).Invoke(null, new object[] { ptr, true })!;
            var color = T("WinRTColor").GetMethod("FromArgb")!.Invoke(null, new object[] { a, r, g, b })!;
            Call(brush, "ICompositionColorBrush", "SetColor", color);
            string readBack = Call(brush, "ICompositionColorBrush", "get_Color")?.ToString() ?? "?";
            var sprite = Call(compositor, "ICompositor", "CreateSpriteVisual")!;
            Call(sprite, "ISpriteVisual", "SetBrush", QI(brush, "ICompositionBrush"));
            var visual = QI(sprite, "IVisual");
            Call(QI(sprite, "IVisual2"), "IVisual2", "SetRelativeSizeAdjustment", new Vector2(1, 1));
            var root = Call(Get(cw, "_target"), "ICompositionTarget", "get_Root")!;
            var children = Call(QI(root, "IContainerVisual"), "IContainerVisual", "get_Children")!;
            Call(children, "IVisualCollection", "InsertAbove", visual, Get(cw, "_blur"));
            int count = (int)Call(children, "IVisualCollection", "get_Count")!;
            return $"hide: scrim SpriteVisual ARGB({a},{r},{g},{b}) (brush reads back {readBack}) inserted above the acrylic visual; root children now {count}; scrim {Vis(visual)}";
        }
    }

    /// The stretched content visual of the current subject and its lock (session 3, for `overstretch`).
    public static object? StretchVisual, StretchSync;

    /// `overstretch` (session 3): WinUI sizes a visual as parent size x RelativeSizeAdjustment + Size, so a Size of
    /// (x, y) makes the stretched content visual that much larger than the composition root. The root is the part of the
    /// client area inside the window rectangle, so while over2 extends the client area past the window this keeps the
    /// content visual equal to the client area (the surface Avalonia renders) instead of squeezing it into the root.
    public static void SetStretchOffset(float x, float y)
    {
        if (StretchVisual == null || StretchSync == null) return;
        lock (StretchSync) Call(StretchVisual, "IVisual", "SetSize", new Vector2(x, y));
    }

    /// `stretch`: Avalonia's content visual sized relative to the window; ResizeIfNeeded is pointed at a detached dummy.
    public static string ApplyStretch(Window w)
    {
        var (cw, compositor, sync, _) = Objects(w);
        lock (sync)
        {
            var visual = Get(cw, "_visual");
            StretchVisual = visual; StretchSync = sync;
            string before = Vis(visual);
            Call(visual, "IVisual", "SetSize", new Vector2(0, 0));
            Call(QI(visual, "IVisual2"), "IVisual2", "SetRelativeSizeAdjustment", new Vector2(1, 1));
            var dummy = QI(Call(compositor, "ICompositor", "CreateSpriteVisual")!, "IVisual");
            Set(cw, "_visual", dummy);
            var sb = Get(cw, "_surfaceBrush");
            object stretchBefore = Call(sb, "ICompositionSurfaceBrush", "get_Stretch")!;
            Call(sb, "ICompositionSurfaceBrush", "SetStretch", Enum.ToObject(T("CompositionStretch"), 1));
            return $"stretch: content visual before {before}, after {Vis(visual)}; surface brush Stretch {stretchBefore} -> " +
                   $"{Call(sb, "ICompositionSurfaceBrush", "get_Stretch")}; ResizeIfNeeded now sizes a detached dummy visual";
        }
    }
}

sealed class DenseSample
{
    public double T0, T1; public string Phase = "", Label = ""; public int Rep; public bool Stable;   // Phase: P4's tag at sample start; Label: by capture time
    public int Right = int.MinValue, Bottom = int.MinValue, Tick2 = -1; public N.RECT Frame; public N.POINT Cursor;
    public byte[] RSlice = Array.Empty<byte>(), BSlice = Array.Empty<byte>();   // RGB for d = -6..64 from the frame edge inward
    // Diagnostic added in session 3 after 3a (not part of P4's criterion, which scans d = -6..64): the first magenta pixel
    // scanning from 24 px OUTSIDE the frame edge (d = -24..64), the length of that magenta run, and the RGB of the 18 px
    // outside P4's window (d = -24..-7). It tells a marker that sits beyond the frame edge from one that is not drawn.
    public int RightWide = int.MinValue, BottomWide = int.MinValue, RightRun, BottomRun;
    public byte[] ROut = Array.Empty<byte>(), BOut = Array.Empty<byte>();
}

sealed class RowSample
{
    public double T0, T1; public int X, Tick = -1, RightMarker = int.MinValue; public string Phase = "", Label = ""; public int Rep;
    public bool Stable; public N.RECT Frame;
}

sealed class Tally
{
    public int N, Art, Visible, VisibleRef, Missing, Scaled, MaxScale, TickMissing;
    public readonly Dictionary<string, int> Hist = new();
    public string Rate => N == 0 ? "0/0" : $"{Art}/{N} ({(100.0 * Art / N).ToString("F1", CultureInfo.InvariantCulture)}%)";
}

sealed class CaseResult
{
    public string Name = "";
    public int Idx;
    public bool Inconclusive; public string Why = "";
    public readonly Dictionary<string, Tally> Phases = new();
    public int PointerMisses, Drags, ModalLoops;
    public double TApp, RestNoise, TNb, NbNoise;
    public string Stretched = "";
    public N.POINT DragStart;
    public string Frames = "", Hooks = "", Steps = "", Lag = "", Composition = "", Surface = "";
    public bool? AnimMax, AnimRestore, AnimMin, AnimRestore2, Flyout, DragSnap, AnimControlDisabled;
    public string SnapDetail = "";
    public string? GeomKey;             // rest geometry (restored, maximised, restored again): markers and ticks vs the client rect
    public Tally P(string phase) => Phases.TryGetValue(phase, out var t) ? t : new Tally();
    public bool Pass =>
        !Inconclusive && P("grow").N >= 200 && P("shrink").N >= 150 && P("grow").Art == 0 && P("shrink").Art == 0 &&
        P("hold").Art == 0 && P("hold2").Art == 0 && P("post").Art == 0 && PointerMisses == 0;
}

static class Program
{
    static string Out = "";
    static readonly StringBuilder LogBuf = new();
    static readonly object LogLock = new();
    static int Reps = 4;
    static bool DoSnap = true;
    static bool TopLeft;          // --edge tl: drag the top-left corner instead (added after the first recorded session)
    static int IdleGateS = 60;    // --idlegate <s>: required input idle time before a session starts
    static bool CornerCap;        // --cornercap: diagnostic, images the right-edge band of artefact frames (not P4's sampler)
    static bool SnapFirstOnly;    // --snapfirst: DWM animation and Snap checks only on the first case of each name (added in session 3)
    static HashSet<string>? SnapCases; // --snapcases a,b: as --snapfirst, and only for these case names (added in session 3)
    static bool DoGeom = true;    // --nogeom: skip the rest-geometry check (added in session 3)
    static N.RECT CheckerRect;    // saved animation frames are cropped to our own checker window (added in session 3)
    static readonly string[] DefaultCases = { "base", "sync", "syncflush", "flush", "align", "validrects", "base", "overrender", "hide", "stretch", "stretchsync", "base" };

    static void Log(string s)
    {
        var line = $"{DateTime.UtcNow:HH:mm:ss.fff}Z [{Clock.Ms,9:F1}] {s}";
        lock (LogLock) { Console.WriteLine(line); LogBuf.AppendLine(line); }
    }
    static string F(double v, string f = "F1") => v.ToString(f, CultureInfo.InvariantCulture);

    [STAThread]
    static int Main(string[] args)
    {
        var cases = DefaultCases.ToList();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--out") Out = args[++i];
            else if (args[i] == "--cases") cases = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
            else if (args[i] == "--reps") Reps = int.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (args[i] == "--nosnap") DoSnap = false;
            else if (args[i] == "--slowrender") Diag.SlowRenderMs = int.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (args[i] == "--edge") TopLeft = args[++i] == "tl";
            else if (args[i] == "--idlegate") IdleGateS = int.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (args[i] == "--cornercap") CornerCap = true;
            else if (args[i] == "--snapfirst") SnapFirstOnly = true;
            else if (args[i] == "--snapcases") { SnapFirstOnly = true; SnapCases = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(); }
            else if (args[i] == "--nogeom") DoGeom = false;
        }
        if (Out == "") { Console.Error.WriteLine("--out required"); return 4; }
        foreach (var c in cases) Remedy.For(c);                       // validate names before anything opens
        Directory.CreateDirectory(Out);
        AppBuilder.Configure<Application>().UsePlatformDetect().SetupWithoutStarting();
        int exit = 4;
        var cts = new CancellationTokenSource();
        Dispatcher.UIThread.Post(async () =>
        {
            try { exit = await Session(cases); }
            catch (Exception e) { Log("CRASH " + e); exit = 4; }
            finally
            {
                lock (LogLock) File.WriteAllText(Path.Combine(Out, "run_log.txt"), LogBuf.ToString());
                cts.Cancel();
            }
        });
        Dispatcher.UIThread.MainLoop(cts.Token);
        return exit;
    }

    static IntPtr Hwnd(Window w) => w.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
    static bool Magenta(byte[] b, int o) => b[o + 2] > 200 && b[o + 1] < 60 && b[o] > 200;          // P4's marker test
    static bool Green(byte[] b, int o) => b[o + 1] > 200 && b[o + 2] < 80 && b[o] < 80;
    static bool StrictMagenta(byte[] b, int o) => b[o + 2] > 235 && b[o + 1] < 25 && b[o] > 235;   // P7's animation test

    sealed record Edge(string Side, int[] Offsets, bool Missing, int MaxDev);

    /// P4's at-rest marker locator: offset 0 = marker on the frame's outermost pixel; positive = inside.
    static Edge[] Edges(byte[] buf, N.RECT u, N.RECT f)
    {
        int Px(int sx, int sy) => ((sy - u.T) * u.W + (sx - u.L)) * 4;
        bool In(int sx, int sy) => sx >= u.L && sx < u.R && sy >= u.T && sy < u.B;
        double[] fr = { 0.25, 0.40, 0.55, 0.70, 0.85 };
        var res = new List<Edge>();
        foreach (var side in new[] { "left", "top", "right", "bottom" })
        {
            var offs = new int[fr.Length];
            bool missing = false;
            for (int k = 0; k < fr.Length; k++)
            {
                int found = int.MinValue;
                for (int d = -6; d <= 64; d++)
                {
                    int sx, sy;
                    switch (side)
                    {
                        case "left": sx = f.L + d; sy = f.T + (int)(f.H * fr[k]); break;
                        case "right": sx = f.R - 1 - d; sy = f.T + (int)(f.H * fr[k]); break;
                        case "top": sx = f.L + (int)(f.W * fr[k]); sy = f.T + d; break;
                        default: sx = f.L + (int)(f.W * fr[k]); sy = f.B - 1 - d; break;
                    }
                    if (!In(sx, sy)) continue;
                    if (Magenta(buf, Px(sx, sy))) { found = d; break; }
                }
                if (found == int.MinValue) missing = true;
                offs[k] = found;
            }
            res.Add(new Edge(side, offs, missing, 0));
        }
        return res.ToArray();
    }

    static Edge[] Judge(Edge[] e, Dictionary<string, int> calib, out bool artefact)
    {
        artefact = false;
        var o = new Edge[e.Length];
        for (int i = 0; i < e.Length; i++)
        {
            int dev = 0;
            foreach (var v in e[i].Offsets) dev = Math.Max(dev, v == int.MinValue ? 999 : Math.Abs(v - calib[e[i].Side]));
            o[i] = e[i] with { MaxDev = dev };
            if (e[i].Missing || dev > 2) artefact = true;
        }
        return o;
    }

    static string EdgeStr(Edge[] e) => string.Join(" ", e.Select(x =>
        $"{x.Side}=[{string.Join(",", x.Offsets.Select(v => v == int.MinValue ? "none" : v.ToString(CultureInfo.InvariantCulture)))}]dev{x.MaxDev}"));

    static void SavePng(string dir, byte[] buf, int bw, int bh, N.RECT crop, string name, double scale = 1.0)
    {
        // buf covers (0,0)-(bw,bh); crop is in buffer coordinates.
        crop.L = Math.Max(crop.L, 0); crop.T = Math.Max(crop.T, 0); crop.R = Math.Min(crop.R, bw); crop.B = Math.Min(crop.B, bh);
        var info = new SKImageInfo(crop.W, crop.H, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var bmp = new SKBitmap(info);
        var px = new byte[crop.W * crop.H * 4];
        for (int y = 0; y < crop.H; y++) Buffer.BlockCopy(buf, ((y + crop.T) * bw + crop.L) * 4, px, y * crop.W * 4, crop.W * 4);
        Marshal.Copy(px, 0, bmp.GetPixels(), px.Length);
        SKBitmap outBmp = bmp;
        if (scale != 1.0) outBmp = bmp.Resize(new SKImageInfo(Math.Max(1, (int)(crop.W * scale)), Math.Max(1, (int)(crop.H * scale)), SKColorType.Bgra8888, SKAlphaType.Opaque), new SKSamplingOptions(SKFilterMode.Nearest));
        using var img = SKImage.FromBitmap(outBmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(dir, name), data.ToArray());
        Log($"PNG {Path.GetFileName(dir)}/{name} {crop.W}x{crop.H} scale={F(scale, "F2")} bytes={data.Size}");
        if (!ReferenceEquals(outBmp, bmp)) outBmp.Dispose();
    }

    static volatile string Phase = "pre";
    static volatile int CurRep;
    static volatile bool SamplerStop;

    // Drag plan (screen px), P4's: grow +280,+180 over 1.2 s (60 steps), hold 1.0 s, shrink to +130,+90 over 0.8 s
    // (40 steps), hold 0.3 s, release, 1.5 s after.
    const int GrowX = 280, GrowY = 180, BackX = 130, BackY = 90;

    static async Task<int> Session(List<string> cases)
    {
        N.GetCursorPos(out var cursor0);
        Log($"P4b start cases=[{string.Join(",", cases)}] reps={Reps} snap={DoSnap} snapFirstOnly={SnapFirstOnly} geom={DoGeom} edge={(TopLeft ? "top-left" : "bottom-right")} slowrender={Diag.SlowRenderMs}ms cursor0=({cursor0.X},{cursor0.Y})");
        Log($"avalonia {typeof(Window).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");
        Log($"avalonia.win32 {typeof(Win32PlatformOptions).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");
        Log($"skiasharp {typeof(SKImage).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");
        Log($"os {Environment.OSVersion.VersionString}; MARROW_PORT_DATA_DIR={Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR")}");
        N.SystemParametersInfoW(N.SPI_GETWORKAREA, 0, out var work, 0);
        var ai = new N.ANIMATIONINFO { cbSize = (uint)Marshal.SizeOf<N.ANIMATIONINFO>() };
        N.SpiAnim(N.SPI_GETANIMATION, (int)ai.cbSize, ref ai, 0);
        N.SpiInt(N.SPI_GETCLIENTAREAANIMATION, 0, out int caa, 0);
        N.SpiInt(N.SPI_GETWINARRANGING, 0, out int arr, 0);
        N.SpiInt(N.SPI_GETDOCKMOVING, 0, out int dock, 0);
        N.SpiInt(N.SPI_GETSNAPSIZING, 0, out int snapSz, 0);
        Log($"primary work area {work}; virtual screen ({N.GetSystemMetrics(76)},{N.GetSystemMetrics(77)}) {N.GetSystemMetrics(78)}x{N.GetSystemMetrics(79)}; " +
            $"SPI_GETANIMATION iMinAnimate={ai.iMinAnimate} SPI_GETCLIENTAREAANIMATION={caa} SPI_GETWINARRANGING={arr} SPI_GETDOCKMOVING={dock} SPI_GETSNAPSIZING={snapSz}");

        // Idle gate (added after run 2): start only when nobody has used the keyboard or mouse for IdleGateS seconds.
        {
            var li = new N.LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<N.LASTINPUTINFO>() };
            N.GetLastInputInfo(ref li);
            double idle = unchecked(N.GetTickCount() - li.dwTime) / 1000.0;
            Log($"input idle {F(idle)} s (gate {IdleGateS} s)");
            if (idle < IdleGateS) { Log("RESULT: INCONCLUSIVE (the owner is using the machine; no window opened, no input sent)"); return 2; }
        }
        Owner.Mark();
        var windows = new List<Window>();
        var results = new List<CaseResult>();
        int exit = 2;
        try
        {
            // 1. Checkerboard covering the full drag extent (P4's).
            var checker = new CheckerWindow { Title = "P4b checkerboard", Topmost = true, ShowInTaskbar = false, WindowDecorations = WindowDecorations.None, CanResize = false };
            windows.Add(checker);
            checker.Show();
            var chk = new N.RECT { L = work.L + 100, T = work.T + 40, R = Math.Min(work.R - 100, work.L + 2100), B = work.B - 40 };
            N.SetWindowPos(Hwnd(checker), N.HWND_TOPMOST, chk.L, chk.T, chk.W, chk.H, N.SWP_NOACTIVATE | N.SWP_SHOWWINDOW);
            Log($"checker rect {chk}");
            CheckerRect = chk;
            await Task.Delay(800);

            // Screen-live check (P4's): a patch of our checker far from any subject must be the plain two-valued checkerboard.
            var live = new N.RECT { L = chk.L + 8, T = chk.B - 88, R = chk.L + 88, B = chk.B - 8 };
            var liveBuf = N.Capture(live);
            int two = 0, tot = 0;
            for (int o = 0; o < liveBuf.Length; o += 4)
            {
                tot++;
                byte bb = liveBuf[o], gg = liveBuf[o + 1], rr = liveBuf[o + 2];
                if ((rr > 231 && gg > 231 && bb > 231) || (rr < 24 && Math.Abs(gg - 0x33) < 24 && bb > 231)) two++;
            }
            double twoFrac = (double)two / tot;
            Log($"screenLive: checker patch two-valued fraction={F(twoFrac, "F4")} => {twoFrac > 0.9}");
            if (twoFrac <= 0.9) { Log("RESULT: INCONCLUSIVE (screen not live: locked, off or covered)"); return 2; }

            // 2. The marker detector's control (P4's ctl_inset): content stopping 24 px short must be flagged right/bottom only.
            bool insetFlagged = await InsetControl(checker, chk, windows);

            // 3. The cases.
            for (int i = 0; i < cases.Count; i++)
            {
                if (Owner.Abort) { Log($"SESSION STOPPED before case {i:D2} {cases[i]}: {Owner.Why}"); break; }
                if (Owner.Moved(out var now)) { Owner.Flag($"before case {i:D2}: cursor at {now}, the probe's last input left it at {Owner.Last}"); Log($"SESSION STOPPED: {Owner.Why}"); break; }
                bool snapThis = DoSnap && (!SnapFirstOnly || i == cases.IndexOf(cases[i])) && (SnapCases == null || SnapCases.Contains(cases[i]));
                bool animControl = snapThis && cases[i] == "base" && i == (SnapFirstOnly ? cases.IndexOf("base") : cases.LastIndexOf("base"));
                var r = await RunCase(i, cases[i], Remedy.For(cases[i]), checker, chk, windows, animControl, snapThis);
                results.Add(r);
                Log($"CASE {r.Idx:D2} {r.Name} DONE: {Summary(r)}");
                await Task.Delay(500);
            }

            // 4. Session verdict.
            Log("==== SUMMARY ====");
            foreach (var r in results) Log($"CASE {r.Idx:D2} {r.Name,-11} {Summary(r)}");
            var bases = results.Where(r => r.Name == "base").ToList();
            bool controlHeld = bases.Count > 0 && bases.All(b => !b.Inconclusive && b.P("grow").Art > 0);
            bool anyInconclusive = results.Any(r => r.Inconclusive) || Owner.Abort || results.Count < cases.Count;
            var animCtl = results.FirstOrDefault(r => r.AnimControlDisabled.HasValue);
            bool animControlHeld = !DoSnap || (animCtl != null && animCtl.AnimControlDisabled == false);
            if (DoGeom)
            {
                // Rest geometry: every case must show the same marker offsets and tick positions as the first base case
                // (restored, maximised, restored again). A remedy that scaled or shifted the content at rest would differ.
                var g0 = results.FirstOrDefault(r => r.Name == "base" && r.GeomKey != null)?.GeomKey;
                foreach (var r in results.Where(r => r.GeomKey != null))
                    Log($"GEOM CASE {r.Idx:D2} {r.Name,-11} {(g0 == null ? "no base reference" : (r.GeomKey == g0 ? "SAME as base" : "DIFFERS from base"))}: {r.GeomKey}");
            }
            Log($"CONTROLS marker detector (ctl_inset) flagged={insetFlagged}; every base case showed a grow strip={controlHeld} " +
                $"({string.Join(", ", bases.Select(b => $"#{b.Idx}: {b.P("grow").Rate}"))}); animation detector control (transitions force-disabled reads not animated)={(DoSnap ? (animCtl?.AnimControlDisabled == false).ToString() : "not run")}");
            var passing = results.Where(r => r.Name != "base" && r.Pass).Select(r => r.Name).Distinct().ToList();
            Log($"REMEDIES passing 'no strip while moving' (P4 detector, grow and shrink 0, n>=200/150): [{string.Join(", ", passing)}]");
            if (!insetFlagged || !controlHeld || anyInconclusive || !animControlHeld)
            {
                Log($"RESULT: INCONCLUSIVE (insetFlagged={insetFlagged} baseControlHeld={controlHeld} anyCaseInconclusive={anyInconclusive} animControlHeld={animControlHeld})");
                exit = 2;
            }
            else
            {
                exit = passing.Count > 0 ? 0 : 1;
                Log($"RESULT: {(exit == 0 ? "a remedy passes" : "no remedy passes")}");
            }
        }
        finally
        {
            foreach (var w in windows.AsEnumerable().Reverse()) { try { w.Close(); } catch (Exception e) { Log("close: " + e.Message); } }
            if (Owner.Abort)
            {
                // The owner has the mouse: leave the cursor where they put it.
                N.GetCursorPos(out var cNow);
                Log($"windows closed; cursor NOT restored because someone else is moving it (now ({cNow.X},{cNow.Y}), was ({cursor0.X},{cursor0.Y}) at start): {Owner.Why}");
            }
            else
            {
                N.SetCursorPos(cursor0.X, cursor0.Y);
                N.GetCursorPos(out var c1);
                Log($"windows closed; cursor restored to ({c1.X},{c1.Y}) (was ({cursor0.X},{cursor0.Y}))");
            }
        }
        return exit;
    }

    static string Summary(CaseResult r)
    {
        if (r.Inconclusive) return "INCONCLUSIVE: " + r.Why;
        string T(string p) { var t = r.P(p); return $"{p} {t.Rate} vis{t.Visible}/{t.VisibleRef} miss{t.Missing} scaled{t.Scaled}(max{t.MaxScale})"; }
        return $"{(r.Name == "base" ? "CONTROL" : (r.Pass ? "PASS" : "FAIL"))} | {T("grow")} | {T("shrink")} | hold {r.P("hold").Rate} hold2 {r.P("hold2").Rate} post {r.P("post").Rate} | " +
               $"Tnb={F(r.TNb, "F0")} Tapp={F(r.TApp, "F0")} | stretch-tick {r.Stretched} | {r.Frames} | hooks {r.Hooks} | {r.Steps} | lag {r.Lag} | anim max/restore/min/restore={r.AnimMax}/{r.AnimRestore}/{r.AnimMin}/{r.AnimRestore2} " +
               $"flyout={r.Flyout} dragSnap={r.DragSnap}{(r.AnimControlDisabled.HasValue ? $" animControl(disabled->animated)={r.AnimControlDisabled}" : "")} | pointerMisses={r.PointerMisses}";
    }

    static async Task<bool> InsetControl(CheckerWindow checker, N.RECT chk, List<Window> windows)
    {
        var ctl = new SubjectWindow("P4b ctl_inset", 24, false);
        windows.Add(ctl);
        ctl.Show(checker);
        var hc = Hwnd(ctl);
        N.GetWindowRect(hc, out var wr0);
        N.SetWindowPos(hc, N.HWND_TOPMOST, chk.L + 100, chk.T + 60, wr0.W, wr0.H, N.SWP_NOACTIVATE | N.SWP_SHOWWINDOW);
        ctl.Start();
        await Task.Delay(2000);
        var fc = N.FrameBounds(hc);
        var u = new N.RECT { L = fc.L - 16, T = fc.T - 16, R = fc.R + 16, B = fc.B + 16 };
        var buf = N.Capture(u);
        var fc2 = N.FrameBounds(hc);
        var calib = new Dictionary<string, int> { ["left"] = 1, ["top"] = 1, ["right"] = 1, ["bottom"] = 1 };
        var e = Judge(Edges(buf, u, fc), calib, out bool art);
        bool flagged = art && e.First(x => x.Side == "right").MaxDev > 2 && e.First(x => x.Side == "bottom").MaxDev > 2
                       && e.First(x => x.Side == "left").MaxDev <= 2 && e.First(x => x.Side == "top").MaxDev <= 2;
        Log($"CTL_INSET frame={fc} stable={fc.Same(fc2)} edges {EdgeStr(e)} artefact={art} => flagged right/bottom only={flagged} (calibration taken as P4's measured 1 on every side)");
        SavePng(Out, buf, u.W, u.H, new N.RECT { L = u.W - 216, T = u.H - 216, R = u.W, B = u.H }, "ctl_inset_corner_br.png");
        ctl.Animate = false;
        ctl.Close(); windows.Remove(ctl);
        await Task.Delay(500);
        return flagged;
    }

    static async Task<CaseResult> RunCase(int idx, string name, Remedy rm, CheckerWindow checker, N.RECT chk, List<Window> windows, bool animControl, bool snap)
    {
        var R = new CaseResult { Name = name, Idx = idx };
        string dir = Path.Combine(Out, $"{idx:D2}_{name}");
        Directory.CreateDirectory(dir);
        Rec.Clear();
        Comp.StretchVisual = null; Comp.StretchSync = null;
        Log($"=== CASE {idx:D2} {name}: {rm}");

        var sub = new SubjectWindow($"P4b {name}", 0, true);
        sub.Scene.DrawScrim = !rm.Hide;
        windows.Add(sub);
        sub.Show(checker);
        var h = Hwnd(sub);
        sub.Scene.Hwnd = h;
        N.GetWindowRect(h, out var wr0);
        int x0 = TopLeft ? chk.L + 400 : chk.L + 100, y0 = TopLeft ? chk.T + 300 : chk.T + 60;
        N.SetWindowPos(h, N.HWND_TOPMOST, x0, y0, wr0.W, wr0.H, N.SWP_NOACTIVATE | N.SWP_SHOWWINDOW);
        var sc = new Subclass(h, rm, sub.Scene);
        sub.Start();
        await Task.Delay(1500);
        R.Surface = Comp.SurfaceType(sub);
        try
        {
            if (rm.Stretch) R.Composition = Comp.ApplyStretch(sub);
            else if (rm.Hide)
            {
                // The app's two scrim layers (black 66/255, then #1E1E20 at 31/255) as one straight-alpha colour:
                // alpha = 1 - (1 - 66/255)(1 - 31/255) = 89/255; colour = (31/255 * (30,30,32)) / alpha = (10,10,11).
                R.Composition = Comp.ApplyHide(sub, 89, 10, 10, 11);
            }
            else R.Composition = "none";
        }
        catch (Exception e) { R.Composition = "FAILED: " + e; R.Inconclusive = true; R.Why = "composition change failed"; }
        try { R.Composition += " | " + Comp.Describe(sub); }
        catch (Exception e) { R.Composition += " | describe failed: " + e.Message; }
        Log($"{name}: surface {R.Surface}; composition {R.Composition}");
        await Task.Delay(800);

        var f0 = N.FrameBounds(h);
        N.GetClientRect(h, out var cr0);
        var co = new N.POINT(); N.ClientToScreen(h, ref co);
        Log($"{name}: hwnd=0x{h.ToInt64():X} dpi={N.GetDpiForWindow(h)} frame={f0} client={cr0.W}x{cr0.H} at ({co.X},{co.Y}) " +
            $"style=0x{N.GetWindowLongPtr(h, N.GWL_STYLE).ToInt64():X8} ex=0x{N.GetWindowLongPtr(h, N.GWL_EXSTYLE).ToInt64():X8} " +
            $"ActualTransparencyLevel={sub.ActualTransparencyLevel} IsExtendedIntoWindowDecorations={sub.IsExtendedIntoWindowDecorations} " +
            $"glass: lease={Rec.LeaseOk} surface={Rec.SurfaceOk} effect={Rec.EffectOk} {Rec.EffectErr}");
        if (R.Inconclusive) { sub.Animate = false; sub.Close(); windows.Remove(sub); return R; }

        var u = new N.RECT { L = f0.L - 16, T = f0.T - 16, R = f0.R + GrowX + 24, B = f0.B + GrowY + 24 };
        int rowY = co.Y + (int)Geo.RowY;
        var rowRect = new N.RECT { L = f0.L, T = rowY, R = f0.R + GrowX + 8, B = rowY + 1 };
        var dRight = new N.RECT { L = f0.R - 80, T = f0.T + 200, R = f0.R + GrowX + 30, B = f0.T + 201 };
        var dBottom = new N.RECT { L = f0.L + 200, T = f0.B - 80, R = f0.L + 201, B = f0.B + GrowY + 30 };
        if (TopLeft)
        {
            // The top-left corner moves; the right and bottom edges stay, and the lag shows there (the content is anchored
            // at the moving top-left). The lines reach far enough left for the dense-row tick, which moves with the left edge.
            u = new N.RECT { L = f0.L - GrowX - 24, T = f0.T - GrowY - 24, R = f0.R + 16, B = f0.B + 16 };
            rowRect = new N.RECT { L = f0.L - GrowX - 8, T = rowY, R = f0.R + 8, B = rowY + 1 };
            dRight = new N.RECT { L = f0.R - 80 - GrowX, T = f0.T + 200, R = f0.R + 30, B = f0.T + 201 };
            dBottom = new N.RECT { L = f0.L + 200, T = f0.B - 80, R = f0.L + 201, B = f0.B + 30 };
        }

        // Calibration at rest (P4's): every marker at one offset per side.
        var restBuf = N.Capture(u);
        var restEdges = Edges(restBuf, u, f0);
        var calib = new Dictionary<string, int>();
        bool calibOk = true;
        foreach (var e in restEdges)
        {
            if (e.Missing || e.Offsets.Distinct().Count() != 1) calibOk = false;
            calib[e.Side] = e.Missing ? 0 : e.Offsets.OrderBy(v => v).ElementAt(e.Offsets.Length / 2);
        }
        Log($"{name}: CALIB {string.Join(" ", calib.Select(kv => $"{kv.Key}={kv.Value}"))} consistent={calibOk} rest {EdgeStr(restEdges)}");
        SavePng(dir, restBuf, u.W, u.H, new N.RECT { L = f0.R - 200 - u.L, T = f0.B - 200 - u.T, R = f0.R + 16 - u.L, B = f0.B + 16 - u.T }, "rest_corner_br.png");
        if (!calibOk) { R.Inconclusive = true; R.Why = "markers not calibrated at rest"; sub.Animate = false; sub.Close(); windows.Remove(sub); return R; }

        // Appearance reference: the same window drawing only its scrim, enlarged past the whole drag extent.
        var restR = N.Capture(dRight); var restB = N.Capture(dBottom);
        sub.Scene.RefMode = true; sub.Scene.InvalidateVisual();
        if (TopLeft) N.SetWindowPos(h, N.HWND_TOPMOST, x0 - GrowX - 40, y0 - GrowY - 40, wr0.W + GrowX + 40, wr0.H + GrowY + 40, N.SWP_NOACTIVATE);
        else N.SetWindowPos(h, N.HWND_TOPMOST, x0, y0, wr0.W + GrowX + 40, wr0.H + GrowY + 40, N.SWP_NOACTIVATE);
        await Task.Delay(900);
        var refR = N.Capture(dRight); var refB = N.Capture(dBottom);
        var fRef = N.FrameBounds(h);
        sub.Scene.RefMode = false; sub.Scene.InvalidateVisual();
        N.SetWindowPos(h, N.HWND_TOPMOST, x0, y0, wr0.W, wr0.H, N.SWP_NOACTIVATE);
        await Task.Delay(900);
        // T_app from the rest noise: scrim pixels 4..12 px inside the rest edge against the reference at the same pixel.
        int noise = 0;
        for (int d = calib["right"] + 3; d <= calib["right"] + 11; d++)
        {
            int sx = f0.R - 1 - d, ix = (sx - dRight.L) * 4;
            for (int ch = 0; ch < 3; ch++) noise = Math.Max(noise, Math.Abs(restR[ix + ch] - refR[ix + ch]));
        }
        for (int d = calib["bottom"] + 3; d <= calib["bottom"] + 11; d++)
        {
            int sy = f0.B - 1 - d, iy = (sy - dBottom.T) * 4;
            for (int ch = 0; ch < 3; ch++) noise = Math.Max(noise, Math.Abs(restB[iy + ch] - refB[iy + ch]));
        }
        R.RestNoise = noise;
        R.TApp = Math.Max(24, 2 * noise);
        // T_nb from the rest image: the four scrim pixels nearest the edge (d = cal+3 .. cal+6) against the mean of the four
        // 7 px further in (d = cal+10 .. cal+13), the same geometry as band-versus-inner in a strip sample.
        int nbNoise = 0;
        foreach (var (line, rest, isRight) in new[] { ("right", restR, true), ("bottom", restB, false) })
        {
            int cal = calib[line];
            double[] m = new double[3];
            for (int d = cal + 10; d <= cal + 13; d++)
            {
                int o = isRight ? (f0.R - 1 - d - dRight.L) * 4 : (f0.B - 1 - d - dBottom.T) * 4;
                m[0] += rest[o + 2] / 4.0; m[1] += rest[o + 1] / 4.0; m[2] += rest[o] / 4.0;
            }
            for (int d = cal + 3; d <= cal + 6; d++)
            {
                int o = isRight ? (f0.R - 1 - d - dRight.L) * 4 : (f0.B - 1 - d - dBottom.T) * 4;
                nbNoise = Math.Max(nbNoise, (int)Math.Round(Math.Max(Math.Abs(rest[o + 2] - m[0]), Math.Max(Math.Abs(rest[o + 1] - m[1]), Math.Abs(rest[o] - m[2])))));
            }
        }
        R.NbNoise = nbNoise;
        R.TNb = Math.Max(12, 3 * nbNoise);
        Log($"{name}: appearance reference taken at frame={fRef}; reference rest noise={noise} => T_app={F(R.TApp, "F0")}; neighbour rest noise={nbNoise} => T_nb={F(R.TNb, "F0")}");
        {
            var lines = new List<string> { "line,screenPos,dFromRestEdge,restR,restG,restB,refR,refG,refB" };
            for (int x = dRight.L; x < dRight.R; x++) { int o = (x - dRight.L) * 4; lines.Add($"right,{x},{f0.R - 1 - x},{restR[o + 2]},{restR[o + 1]},{restR[o]},{refR[o + 2]},{refR[o + 1]},{refR[o]}"); }
            for (int y = dBottom.T; y < dBottom.B; y++) { int o = (y - dBottom.T) * 4; lines.Add($"bottom,{y},{f0.B - 1 - y},{restB[o + 2]},{restB[o + 1]},{restB[o]},{refB[o + 2]},{refB[o + 1]},{refB[o]}"); }
            File.WriteAllLines(Path.Combine(dir, "appearance_lines.csv"), lines);
        }

        // Sampler (P4's dense mode: a row through the spring bar, then one row across the right edge and one column across
        // the bottom edge, as fast as it goes), tagged with phase and repetition.
        var rows = new List<RowSample>();
        var dense = new List<DenseSample>();
        int cornerN = 0, cornerArt = 0, cornerSaved = 0, cornerNormalSaved = 0;
        var cornerLog = new List<string>();
        SamplerStop = false; Phase = "pre"; CurRep = 0;
        var sampler = new Thread(() =>
        {
            while (!SamplerStop)
            {
                string ph = Phase; int rep = CurRep;
                var rs = new RowSample { T0 = Clock.Ms, Phase = ph, Rep = rep };
                var fa = N.FrameBounds(h);
                var row = N.Capture(rowRect);
                rs.Frame = N.FrameBounds(h);
                rs.Stable = fa.Same(rs.Frame);
                rs.T1 = Clock.Ms;
                for (int d = -6; d <= 64; d++)
                {
                    int sx = rs.Frame.R - 1 - d;
                    if (sx < rowRect.L || sx >= rowRect.R) continue;
                    if (Magenta(row, (sx - rowRect.L) * 4)) { rs.RightMarker = d; break; }
                }
                int first = -1, last = -1;
                for (int x = 0; x < rowRect.W; x++) if (Green(row, x * 4)) { if (first < 0) first = x; last = x; }
                rs.X = first < 0 ? -1 : (first + last) / 2;
                int tf = -1, tl = -1;
                for (int x = 0; x < rowRect.W; x++) { int o = x * 4; if (row[o + 2] < 60 && row[o + 1] > 200 && row[o] > 200) { if (tf < 0) tf = x; tl = x; } }
                rs.Tick = tf < 0 ? -1 : (tf + tl) / 2;
                lock (rows) rows.Add(rs);
                if (CornerCap && ph is "grow" or "hold" or "shrink" or "hold2" or "post")
                {
                    // Diagnostic: the whole right-edge band of the window, 60 px inside to 20 px outside, full height.
                    var fb0 = N.FrameBounds(h);
                    var reg = new N.RECT { L = fb0.R - 60, T = fb0.T, R = fb0.R + 20, B = fb0.B + 20 };
                    var img = N.Capture(reg);
                    var fb1 = N.FrameBounds(h);
                    double tc = Clock.Ms;
                    if (fb0.Same(fb1))
                    {
                        int cal = calib["right"];
                        int RowOff(int yy) { for (int d = -6; d <= 58; d++) { int ix = fb1.R - 1 - d - reg.L; if (ix >= 0 && ix < reg.W && Magenta(img, (yy * reg.W + ix) * 4)) return d; } return int.MinValue; }
                        int ry = f0.T + 200 - reg.T;
                        int off = ry >= 0 && ry < reg.H ? RowOff(ry) : int.MinValue;
                        bool art = off == int.MinValue || Math.Abs(off - cal) > 2;
                        Interlocked.Increment(ref cornerN);
                        if (art)
                        {
                            Interlocked.Increment(ref cornerArt);
                            int ok = 0, disp = 0, miss = 0, firstBad = -1, lastBad = -1;
                            for (int yy = 6; yy < fb1.H - 6; yy++)
                            {
                                int o = RowOff(yy);
                                if (o == int.MinValue) miss++; else if (Math.Abs(o - cal) > 2) disp++; else { ok++; continue; }
                                if (firstBad < 0) firstBad = yy; lastBad = yy;
                            }
                            string tag = $"corner_rep{rep}_{ph}_{F(tc, "F0")}ms_off{(off == int.MinValue ? "none" : off.ToString(CultureInfo.InvariantCulture))}";
                            lock (cornerLog) cornerLog.Add($"{tag} frame={fb1} rows ok={ok} displaced={disp} missing={miss} badRows={firstBad}..{lastBad} at dense row: " +
                                string.Join(" ", Enumerable.Range(-2, 10).Select(d => { int ix = fb1.R - 1 - d - reg.L; int o4 = (ry * reg.W + ix) * 4; return $"d{d}=({img[o4 + 2]},{img[o4 + 1]},{img[o4]})"; })));
                            if (Interlocked.Increment(ref cornerSaved) <= 10) SavePng(dir, img, reg.W, reg.H, new N.RECT { L = 0, T = 0, R = reg.W, B = reg.H }, tag + ".png");
                        }
                        else if (ph == "grow" && Interlocked.Increment(ref cornerNormalSaved) == 40)
                            SavePng(dir, img, reg.W, reg.H, new N.RECT { L = 0, T = 0, R = reg.W, B = reg.H }, $"corner_rep{rep}_{ph}_{F(tc, "F0")}ms_normal.png");
                    }
                    Thread.Sleep(1);
                    continue;
                }
                if (ph is "grow" or "hold" or "shrink" or "hold2" or "post")
                {
                    var ds = new DenseSample { T0 = Clock.Ms, Phase = Phase, Rep = rep };
                    N.GetCursorPos(out ds.Cursor);
                    var fb0 = N.FrameBounds(h);
                    var rb = N.Capture(dRight);
                    var bb2 = N.Capture(dBottom);
                    ds.Frame = N.FrameBounds(h);
                    ds.T1 = Clock.Ms;
                    ds.Stable = fb0.Same(ds.Frame);
                    ds.RSlice = new byte[71 * 3]; ds.BSlice = new byte[71 * 3];
                    {
                        int t2f = -1, t2l = -1;
                        for (int x = 0; x < dRight.W; x++) { int o = x * 4; if (rb[o + 2] < 60 && rb[o + 1] > 200 && rb[o] > 200) { if (t2f < 0) t2f = x; t2l = x; } }
                        ds.Tick2 = t2f < 0 ? -1 : dRight.L + (t2f + t2l) / 2;
                    }
                    for (int d = -6; d <= 64; d++)
                    {
                        int sx = ds.Frame.R - 1 - d;
                        if (sx >= dRight.L && sx < dRight.R)
                        {
                            int o = (sx - dRight.L) * 4, k = (d + 6) * 3;
                            ds.RSlice[k] = rb[o + 2]; ds.RSlice[k + 1] = rb[o + 1]; ds.RSlice[k + 2] = rb[o];
                            if (ds.Right == int.MinValue && Magenta(rb, o)) ds.Right = d;
                        }
                        int sy = ds.Frame.B - 1 - d;
                        if (sy >= dBottom.T && sy < dBottom.B)
                        {
                            int o = (sy - dBottom.T) * 4, k = (d + 6) * 3;
                            ds.BSlice[k] = bb2[o + 2]; ds.BSlice[k + 1] = bb2[o + 1]; ds.BSlice[k + 2] = bb2[o];
                            if (ds.Bottom == int.MinValue && Magenta(bb2, o)) ds.Bottom = d;
                        }
                    }
                    // Wide diagnostic scan (session 3, after 3a): from d = -24, and the 18 px outside P4's window.
                    ds.ROut = new byte[18 * 3]; ds.BOut = new byte[18 * 3];
                    for (int d = -24; d <= 64; d++)
                    {
                        int sx = ds.Frame.R - 1 - d;
                        bool rIn = sx >= dRight.L && sx < dRight.R;
                        if (rIn && d <= -7) { int o = (sx - dRight.L) * 4, k = (d + 24) * 3; ds.ROut[k] = rb[o + 2]; ds.ROut[k + 1] = rb[o + 1]; ds.ROut[k + 2] = rb[o]; }
                        if (rIn && ds.RightWide == int.MinValue && Magenta(rb, (sx - dRight.L) * 4))
                        {
                            ds.RightWide = d;
                            for (int e = d; e <= 64; e++) { int x2 = ds.Frame.R - 1 - e; if (x2 >= dRight.L && x2 < dRight.R && Magenta(rb, (x2 - dRight.L) * 4)) ds.RightRun++; else break; }
                        }
                        int sy = ds.Frame.B - 1 - d;
                        bool bIn = sy >= dBottom.T && sy < dBottom.B;
                        if (bIn && d <= -7) { int o = (sy - dBottom.T) * 4, k = (d + 24) * 3; ds.BOut[k] = bb2[o + 2]; ds.BOut[k + 1] = bb2[o + 1]; ds.BOut[k + 2] = bb2[o]; }
                        if (bIn && ds.BottomWide == int.MinValue && Magenta(bb2, (sy - dBottom.T) * 4))
                        {
                            ds.BottomWide = d;
                            for (int e = d; e <= 64; e++) { int y2 = ds.Frame.B - 1 - e; if (y2 >= dBottom.T && y2 < dBottom.B && Magenta(bb2, (y2 - dBottom.T) * 4)) ds.BottomRun++; else break; }
                        }
                    }
                    lock (dense) dense.Add(ds);
                }
                Thread.Sleep(1);
            }
        }) { IsBackground = true, Name = "sampler" };
        sampler.Start();

        // Drag start point: HTBOTTOMRIGHT on our own HWND, confirmed by WindowFromPoint (P4's scan).
        var dragTimes = new List<(int rep, double down, double grow, double hold, double shrink, double hold2, double up, double reset)>();
        var inputLog = new ConcurrentQueue<string>();
        int pointerMiss = 0;
        bool buttonDown = false;
        try
        {
            for (int rep = 1; rep <= Reps; rep++)
            {
                var f1 = N.FrameBounds(h);
                N.POINT start = default; bool haveStart = false;
                var map = new StringBuilder();
                for (int d = -4; d <= 6 && !haveStart; d++)
                {
                    // Bottom-right: from 4 px inside the frame's last pixel outward (P4). Top-left: from 6 px outside the
                    // frame's first pixel inward (the extended client area puts the top resize band inside the frame).
                    var p = TopLeft ? new N.POINT { X = f1.L - 6 + (d + 4), Y = f1.T - 6 + (d + 4) } : new N.POINT { X = f1.R - 1 + d, Y = f1.B - 1 + d };
                    int ht = N.NcHit(h, p);
                    var root = N.Root(p);
                    map.Append(CultureInfo.InvariantCulture, $"({p.X},{p.Y})ht={ht}{(root == h ? "" : "!foreign")} ");
                    if (TopLeft ? (ht == 13 && root == h) : (ht == 17 && root == h && d >= 0)) { start = p; haveStart = true; }
                }
                if (rep == 1) Log($"{name}: NCHITTEST diagonal scan: {map}");
                if (!haveStart) { R.Inconclusive = true; R.Why = $"no {(TopLeft ? "HTTOPLEFT" : "HTBOTTOMRIGHT")} point on our HWND"; Log($"{name}: {R.Why}; no input sent"); break; }
                int sgn = TopLeft ? -1 : 1;
                var endGrow = new N.POINT { X = start.X + sgn * GrowX, Y = start.Y + sgn * GrowY };
                var endBack = new N.POINT { X = start.X + sgn * BackX, Y = start.Y + sgn * BackY };
                if (endGrow.X >= chk.R || endGrow.Y >= chk.B || endGrow.X <= chk.L || endGrow.Y <= chk.T) { R.Inconclusive = true; R.Why = "planned point outside our checker"; break; }
                if (rep == 1) R.DragStart = start;
                CurRep = rep;
                void Check(N.POINT want)
                {
                    N.GetCursorPos(out var c);
                    if (Math.Abs(c.X - want.X) > 1 || Math.Abs(c.Y - want.Y) > 1) { Interlocked.Increment(ref pointerMiss); inputLog.Enqueue($"{F(Clock.Ms)} rep{rep} cursor ({c.X},{c.Y}) != planned ({want.X},{want.Y})"); }
                }
                // Returns false (and stops) at the first cursor position that is not the planned one: someone else moved the
                // pointer. The caller then releases the button at once.
                bool Glide(N.POINT a, N.POINT b, int steps, int totalMs)
                {
                    double t0 = Clock.Ms;
                    for (int i = 1; i <= steps; i++)
                    {
                        var p = new N.POINT { X = a.X + (b.X - a.X) * i / steps, Y = a.Y + (b.Y - a.Y) * i / steps };
                        N.MoveAbs(p.X, p.Y);
                        double due = t0 + totalMs * i / (double)steps;
                        while (Clock.Ms < due) Thread.Sleep(1);
                        if (!Owner.At(p, out var c)) { Check(p); Owner.Flag($"rep{rep}: cursor {c} != planned {p} during a glide"); return false; }
                    }
                    return true;
                }
                double tDown = 0, tGrow = 0, tHold = 0, tShrink = 0, tHold2 = 0, tUp = 0;
                var driver = Task.Run(() =>
                {
                    try
                    {
                        if (Owner.Moved(out var before)) { Owner.Flag($"rep{rep}: cursor at {before}, but the probe's last input left it at {Owner.Last}"); return; }
                        N.MoveAbs(start.X, start.Y);
                        Thread.Sleep(120);
                        // Never press unless the cursor is exactly where the probe put it, over our own resize corner.
                        if (!Owner.At(start, out var c0) || N.Root(c0) != h || N.NcHit(h, c0) != (TopLeft ? 13 : 17))
                        { Check(start); Owner.Flag($"rep{rep}: cursor {c0} not at the planned start {start} on our resize corner; no button sent"); return; }
                        tDown = Clock.Ms; N.Button(true); buttonDown = true;
                        Thread.Sleep(200);
                        Phase = "grow"; tGrow = Clock.Ms; if (!Glide(start, endGrow, 60, 1200)) return;
                        Phase = "hold"; tHold = Clock.Ms; Thread.Sleep(1000);
                        if (!Owner.At(endGrow, out var ch)) { Check(endGrow); Owner.Flag($"rep{rep}: cursor {ch} != {endGrow} while holding"); return; }
                        Phase = "shrink"; tShrink = Clock.Ms; if (!Glide(endGrow, endBack, 40, 800)) return;
                        Phase = "hold2"; tHold2 = Clock.Ms; Thread.Sleep(300);
                        tUp = Clock.Ms; N.Button(false); buttonDown = false;
                        Phase = "post";
                    }
                    finally
                    {
                        if (buttonDown) { N.Button(false); buttonDown = false; Phase = "post"; }
                        Owner.Mark();
                    }
                });
                await driver;
                if (Owner.Abort)
                {
                    R.Inconclusive = true; R.Why = "external pointer activity: " + Owner.Why;
                    Log($"{name}: STOPPED, {R.Why}");
                    await Task.Delay(300);
                    break;
                }
                await Task.Delay(1500);
                double tReset = Clock.Ms; Phase = "reset";
                dragTimes.Add((rep, tDown, tGrow, tHold, tShrink, tHold2, tUp, tReset));
                R.Drags++;
                N.SetWindowPos(h, N.HWND_TOPMOST, x0, y0, wr0.W, wr0.H, N.SWP_NOACTIVATE);
                await Task.Delay(800);
            }
        }
        finally
        {
            if (buttonDown) N.Button(false);
            SamplerStop = true;
            sampler.Join();
            Phase = "done";
        }
        R.PointerMisses = pointerMiss;
        foreach (var s in inputLog) Log($"{name}: POINTER {s}");
        if (CornerCap)
        {
            Log($"{name}: CORNERCAP (diagnostic, not P4's sampler) stable samples={cornerN} with the dense-row marker missing or displaced={cornerArt}");
            foreach (var s in cornerLog) Log($"{name}: CORNERCAP {s}");
        }
        foreach (var t in dragTimes) Log($"{name}: rep{t.rep} down {F(t.down)} grow {F(t.grow)} hold {F(t.hold)} shrink {F(t.shrink)} hold2 {F(t.hold2)} up {F(t.up)} reset {F(t.reset)}");

        // ---- analysis ----
        var msgs = Rec.Msgs.ToArray();
        var ui = Rec.Ui.ToArray();
        var rf = Rec.Render.ToArray();
        var hooks = sc.Hooks.ToArray();
        var enters = msgs.Where(m => m.what == "WM_ENTERSIZEMOVE").Select(m => m.ms).ToArray();
        var exits = msgs.Where(m => m.what == "WM_EXITSIZEMOVE").Select(m => m.ms).ToArray();
        var loops = new List<(double a, double b)>();
        foreach (var t in dragTimes)
        {
            var en = enters.Where(e => e >= t.down - 50 && e <= t.up).DefaultIfEmpty(double.NaN).First();
            var ex = exits.Where(e => e >= t.up - 50 && e <= t.up + 1000).DefaultIfEmpty(double.NaN).First();
            if (!double.IsNaN(en) && !double.IsNaN(ex)) loops.Add((en, ex));
        }
        R.ModalLoops = loops.Count;
        if (loops.Count != dragTimes.Count && !R.Inconclusive) { R.Inconclusive = true; R.Why = $"modal loops {loops.Count} != drags {dragTimes.Count}"; }

        // Dense tallies with P4's thresholds, plus the appearance test.
        int cR = calib["right"], cB = calib["bottom"];
        // Appearance of the band between the frame's first inner pixel (d = calibrated offset) and a marker that sits more
        // than 2 px inside it. Primary: against the scrim just inside the displaced marker (d = off+3 .. off+6, mean), i.e.
        // "does the strip look like the window next to it". Secondary: against the reference image at the same pixel.
        (bool nb, bool rf, int maxNb, int maxRef, string band, string inner) Band(DenseSample d, bool right)
        {
            int off = right ? d.Right : d.Bottom, cal = right ? cR : cB;
            if (off == int.MinValue || off - cal <= 2) return (false, false, 0, 0, "", "");
            var slice = right ? d.RSlice : d.BSlice;
            var refBuf = right ? refR : refB;
            double mr = 0, mg = 0, mb = 0; int n = 0;
            for (int k = off + 3; k <= off + 6 && k <= 64; k++) { int si = (k + 6) * 3; mr += slice[si]; mg += slice[si + 1]; mb += slice[si + 2]; n++; }
            if (n > 0) { mr /= n; mg /= n; mb /= n; }
            int maxNb = 0, maxRef = 0;
            var band = new StringBuilder();
            for (int k = cal; k < off; k++)
            {
                int si = (k + 6) * 3;
                band.Append(CultureInfo.InvariantCulture, $"({slice[si]},{slice[si + 1]},{slice[si + 2]})");
                maxNb = Math.Max(maxNb, (int)Math.Round(Math.Max(Math.Abs(slice[si] - mr), Math.Max(Math.Abs(slice[si + 1] - mg), Math.Abs(slice[si + 2] - mb)))));
                int s = right ? d.Frame.R - 1 - k : d.Frame.B - 1 - k;
                int ri = right ? (s - dRight.L) * 4 : (s - dBottom.T) * 4;
                if (ri >= 0 && ri + 2 < refBuf.Length)
                    maxRef = Math.Max(maxRef, Math.Max(Math.Abs(slice[si] - refBuf[ri + 2]), Math.Max(Math.Abs(slice[si + 1] - refBuf[ri + 1]), Math.Abs(slice[si + 2] - refBuf[ri]))));
            }
            return (maxNb > R.TNb, maxRef > R.TApp, maxNb, maxRef, band.ToString(), FormattableString.Invariant($"({mr:F0},{mg:F0},{mb:F0})"));
        }
        var artRows = new List<string> { "t0,rep,phaseByTime,side,offset,calibrated,bandRGB,innerMeanRGB,maxDiffNeighbour,maxDiffReference,visibleNeighbour,visibleReference,wideOffset,wideRun,tick2X,outsideRGB_dMinus24toMinus7,sliceRGB_dMinus6to10" };
        bool Bad(int v, int cal) => v == int.MinValue || Math.Abs(v - cal) > 2;
        string Dv(int v, int cal) => v == int.MinValue ? "none" : (v - cal).ToString(CultureInfo.InvariantCulture);
                // Phase by capture time (P4 tagged the phase at sample start, which mislabels samples that straddle a boundary):
        // a sample belongs to the phase in which its capture ENDED; a moving phase keeps its samples for 50 ms after the
        // last pointer step (the lag being measured outlives the step); a sample overlapping a later step is moving.
        const double Settle = 50;
        foreach (var d in dense)
        {
            var dt = dragTimes.FirstOrDefault(t => t.rep == d.Rep);
            if (dt.rep == 0) { d.Label = "unknown"; continue; }
            d.Label = d.T1 < dt.grow ? "pre"
                : d.T1 < dt.hold + Settle ? "grow"
                : d.T1 < dt.shrink ? "hold"
                : d.T1 < dt.hold2 + Settle ? "shrink"
                : d.T1 < dt.up ? "hold2"
                : d.T1 < dt.reset - 20 ? "post" : "reset";
        }
        // Rest position of the dense row's cyan tick (laid out from the left edge, which does not move): the most common
        // position over the at-rest phases.
        // Positions are taken relative to the frame's left edge, which moves only in a top-left drag.
        var t2g = dense.Where(d => d.Stable && d.Tick2 >= 0 && d.Label is "hold" or "hold2" or "post").GroupBy(d => d.Tick2 - d.Frame.L).OrderByDescending(g => g.Count()).ToList();
        int tick2Rest = t2g.Count > 0 ? t2g[0].Key : int.MinValue;
        Log($"{name}: dense-row tick rest position frame.L+{tick2Rest} ({(t2g.Count > 0 ? t2g[0].Count() : 0)} at-rest samples; others {string.Join(" ", t2g.Skip(1).Select(g => $"{g.Key}:{g.Count()}"))})");
        foreach (var d in dense.Where(d => d.Stable && d.Label is "grow" or "hold" or "shrink" or "hold2" or "post"))
        {
            if (!R.Phases.TryGetValue(d.Label, out var t)) R.Phases[d.Label] = t = new Tally();
            t.N++;
            if (d.Tick2 < 0) t.TickMissing++;
            else if (tick2Rest != int.MinValue && Math.Abs(d.Tick2 - d.Frame.L - tick2Rest) >= 1) { t.Scaled++; t.MaxScale = Math.Max(t.MaxScale, Math.Abs(d.Tick2 - d.Frame.L - tick2Rest)); }
            bool art = Bad(d.Right, cR) || Bad(d.Bottom, cB);
            if (art)
            {
                t.Art++;
                string key = $"r{Dv(d.Right, cR)}/b{Dv(d.Bottom, cB)}";
                t.Hist[key] = t.Hist.GetValueOrDefault(key) + 1;
            }
            var bR = Band(d, true); var bB = Band(d, false);
            if (bR.nb || bB.nb) t.Visible++;
            if (bR.rf || bB.rf) t.VisibleRef++;
            foreach (var (side, b, off, cal) in new[] { ("right", bR, d.Right, cR), ("bottom", bB, d.Bottom, cB) })
                if (off == int.MinValue || Math.Abs(off - cal) > 2)
                {
                    bool isR = side == "right";
                    int wide = isR ? d.RightWide : d.BottomWide, run = isR ? d.RightRun : d.BottomRun;
                    var outs = isR ? d.ROut : d.BOut; var sl = isR ? d.RSlice : d.BSlice;
                    string Rgb(byte[] a, int from, int n) { var s = new StringBuilder(); for (int q = from; q < from + n && q * 3 + 2 < a.Length; q++) s.Append(CultureInfo.InvariantCulture, $"({a[q * 3]},{a[q * 3 + 1]},{a[q * 3 + 2]})"); return s.ToString(); }
                    artRows.Add($"{F(d.T0, "F2")},{d.Rep},{d.Label},{side},{(off == int.MinValue ? "none" : off.ToString(CultureInfo.InvariantCulture))},{cal},\"{b.band}\",\"{b.inner}\",{b.maxNb},{b.maxRef},{b.nb},{b.rf}," +
                                $"{(wide == int.MinValue ? "none" : wide.ToString(CultureInfo.InvariantCulture))},{run},{d.Tick2},\"{Rgb(outs, 0, 18)}\",\"{Rgb(sl, 0, 17)}\"");
                }
            if (d.Right == int.MinValue || d.Bottom == int.MinValue) t.Missing++;
        }
        foreach (var (ph, t) in R.Phases)
            Log($"{name}: DENSE {ph}: stable={t.N} artefact={t.Rate} visibleStrip(neighbour>{F(R.TNb, "F0")})={t.Visible} visibleStrip(reference>{F(R.TApp, "F0")})={t.VisibleRef} markerMissing={t.Missing} scaledFrame(tick moved)={t.Scaled} (max {t.MaxScale}px, tick not found {t.TickMissing}); deviations {string.Join(" ", t.Hist.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}:{kv.Value}"))}");
        // Wide diagnostic (session 3, after 3a): where the marker is in samples whose P4 scan found none.
        {
            var miss = dense.Where(d => d.Stable && d.Label is "grow" or "hold" or "shrink" or "hold2" or "post" && (d.Right == int.MinValue || d.Bottom == int.MinValue)).ToList();
            if (miss.Count > 0)
                Log($"{name}: WIDE marker-missing samples={miss.Count}: " + string.Join(" ", miss.Select(d =>
                    $"[{d.Label} r{(d.Right == int.MinValue ? $"none->wide{(d.RightWide == int.MinValue ? "none" : d.RightWide.ToString(CultureInfo.InvariantCulture))}x{d.RightRun}" : d.Right.ToString(CultureInfo.InvariantCulture))}" +
                    $" b{(d.Bottom == int.MinValue ? $"none->wide{(d.BottomWide == int.MinValue ? "none" : d.BottomWide.ToString(CultureInfo.InvariantCulture))}x{d.BottomRun}" : d.Bottom.ToString(CultureInfo.InvariantCulture))} tick2 {d.Tick2 - d.Frame.L}]")));
        }
        File.WriteAllLines(Path.Combine(dir, "artefacts.csv"), artRows);

        // Stretch readout: where the static cyan tick is on screen (row sampler), labelled by capture time as above.
        foreach (var rs in rows)
        {
            var dt = dragTimes.FirstOrDefault(t => t.rep == rs.Rep);
            rs.Label = dt.rep == 0 ? (rs.Rep == 0 ? "pre" : "unknown") : rs.T1 < dt.grow ? "pre" : rs.T1 < dt.hold + Settle ? "grow" : rs.T1 < dt.shrink ? "hold"
                : rs.T1 < dt.hold2 + Settle ? "shrink" : rs.T1 < dt.up ? "hold2" : rs.T1 < dt.reset - 20 ? "post" : "reset";
        }
        // Tick position relative to the frame's left edge (constant in a bottom-right drag, so every row counts there; in a
        // top-left drag only stable rows, whose frame is known, count).
        int Rel(RowSample r) => r.Tick + rowRect.L - r.Frame.L;
        var restTicks = rows.Where(r => r.Label == "pre" && r.Tick >= 0 && r.Stable).GroupBy(Rel).OrderByDescending(g => g.Count()).ToList();
        int tick0 = restTicks.Count > 0 ? restTicks[0].Key : int.MinValue;
        var tickParts = new List<string>();
        // "pre" added in session 3 (overstretch): it holds the moment WM_ENTERSIZEMOVE changes the client area.
        foreach (var ph in new[] { "pre", "grow", "hold", "shrink", "post" })
        {
            // The row also crosses the right edge at client y = 410, so a stable row says both whether a frame was shown
            // scaled (tick) and whether the right edge lagged (P4's marker criterion on this row).
            var all = rows.Where(r => r.Label == ph && (!TopLeft || r.Stable)).ToList();
            var sel = all.Where(r => r.Stable).ToList();
            int found = all.Count(r => r.Tick >= 0), moved = all.Count(r => r.Tick >= 0 && Math.Abs(Rel(r) - tick0) >= 1);
            int maxD = all.Where(r => r.Tick >= 0).Select(r => Math.Abs(Rel(r) - tick0)).DefaultIfEmpty(0).Max();
            int edge = sel.Count(r => r.RightMarker == int.MinValue || Math.Abs(r.RightMarker - cR) > 2);
            int both = sel.Count(r => (r.RightMarker == int.MinValue || Math.Abs(r.RightMarker - cR) > 2) && r.Tick >= 0 && Math.Abs(Rel(r) - tick0) >= 1);
            tickParts.Add($"{ph} rows={all.Count} tickDisplaced={moved} (max {maxD}px, not found {all.Count - found}); stable rows={sel.Count} rowEdgeArtefact={edge} both={both}");
        }
        R.Stretched = $"tick0={tick0} ({(restTicks.Count > 0 ? restTicks[0].Count() : 0)} rest rows); " + string.Join("; ", tickParts);
        Log($"{name}: STRETCH-TICK (a static tick at client x={Geo.TickX}; displaced = a frame shown scaled): {R.Stretched}");
        File.WriteAllLines(Path.Combine(dir, "row_samples.csv"), new[] { "t0,t1,rep,phaseByTime,stable,frame,barX,tickX,rightMarker" }.Concat(rows.Where(r => r.Label is "grow" or "hold" or "shrink" or "hold2").Select(r =>
            $"{F(r.T0, "F2")},{F(r.T1, "F2")},{r.Rep},{r.Label},{r.Stable},\"{r.Frame}\",{r.X},{r.Tick},{(r.RightMarker == int.MinValue ? "none" : r.RightMarker.ToString(CultureInfo.InvariantCulture))}")));
        Log($"{name}: DENSE unstable samples skipped={dense.Count(d => !d.Stable)}; sample time median={F(dense.Select(d => d.T1 - d.T0).DefaultIfEmpty(0).OrderBy(v => v).ElementAt(dense.Count / 2), "F2")}ms");

        // Frame intervals inside the modal loops (UI thread and render thread), with the boundary frames.
        (double med, double p95, double max, int n) Gaps(double[] ts)
        {
            var all = new List<double>();
            foreach (var (a, b) in loops)
            {
                var sel = ts.Where(t => t >= a && t <= b).ToList();
                var before = ts.Where(t => t < a).DefaultIfEmpty(double.NaN).Max();
                var after = ts.Where(t => t > b).DefaultIfEmpty(double.NaN).Min();
                if (!double.IsNaN(before)) sel.Insert(0, before);
                if (!double.IsNaN(after)) sel.Add(after);
                all.AddRange(sel.Zip(sel.Skip(1), (x, y) => y - x));
            }
            if (all.Count == 0) return (double.NaN, double.NaN, double.NaN, 0);
            all.Sort();
            return (all[all.Count / 2], all[(int)(all.Count * 0.95)], all[^1], all.Count);
        }
        var gu = Gaps(ui.Select(x => x.ms).ToArray());
        var gr = Gaps(rf.Select(x => x.ms).ToArray());
        R.Frames = $"ui frames med/p95/max {F(gu.med, "F1")}/{F(gu.p95, "F1")}/{F(gu.max, "F1")}ms (n={gu.n}) render {F(gr.med, "F1")}/{F(gr.p95, "F1")}/{F(gr.max, "F1")}ms (n={gr.n})";
        string HookStat(string kind)
        {
            var v = hooks.Where(x => x.kind == kind).Select(x => x.dur).OrderBy(x => x).ToList();
            return v.Count == 0 ? "" : $"{kind} n={v.Count} med={F(v[v.Count / 2], "F2")} p95={F(v[(int)(v.Count * 0.95)], "F2")} max={F(v[^1], "F2")}ms";
        }
        R.Hooks = string.Join("; ", new[] { "sync", "flush_before", "flush_after", "extend", "trim", "over2_enter", "over2_exit", "over2_offset", "over2_offset0" }.Select(HookStat).Where(s => s != ""));
        if (R.Hooks == "") R.Hooks = "none";
        if (rm.Over2) R.Hooks += $"; over2 invisible border right={sc.ShrinkR} bottom={sc.ShrinkB}, scene margin {Geo.OverMargin - sc.ShrinkR}" + (rm.Stretch ? $", stretched visual offset {sc.OffX}x{sc.OffY}" : "");
        // Resize steps per drag and the pointer lag (how far the frame edge trails the cursor's displacement while growing).
        var sizes = msgs.Where(m => m.what.StartsWith("WM_SIZE ", StringComparison.Ordinal)).Select(m => m.ms).ToArray();
        var sizings = msgs.Where(m => m.what.StartsWith("WM_SIZING", StringComparison.Ordinal)).Select(m => m.ms).ToArray();
        var perDrag = loops.Select(l => (sz: sizes.Count(t => t >= l.a && t <= l.b), sg: sizings.Count(t => t >= l.a && t <= l.b))).ToList();
        R.Steps = $"per drag WM_SIZING={string.Join("/", perDrag.Select(p => p.sg))} WM_SIZE={string.Join("/", perDrag.Select(p => p.sz))}";
        var lags = new List<int>();
        foreach (var d in dense.Where(d => d.Stable && d.Label == "grow"))
        {
            // Start point x = f0.R - 1 + k (k small); the frame edge follows the cursor's displacement.
            int lag = TopLeft ? (R.DragStart.X - d.Cursor.X) - (f0.L - d.Frame.L) : (d.Cursor.X - R.DragStart.X) - (d.Frame.R - f0.R);
            lags.Add(lag);
        }
        lags.Sort();
        R.Lag = lags.Count == 0 ? "n/a" : $"grow cursor-to-edge px med={lags[lags.Count / 2]} p95={lags[(int)(lags.Count * 0.95)]} max={lags[^1]}";
        // Render-thread record: how often the scene was rendered at a size different from the window's client size.
        int renders = 0, stale = 0;
        foreach (var (a, b) in loops)
            foreach (var x in rf.Where(x => x.ms >= a && x.ms <= b && x.clientW > 0))
            {
                renders++;
                if (Math.Abs(x.w - x.clientW) > 0.5 || Math.Abs(x.h - x.clientH) > 0.5) stale++;
            }
        Log($"{name}: FRAMES {R.Frames}; HOOKS {R.Hooks}; STEPS {R.Steps}; LAG {R.Lag}; render passes in the loops={renders}, recorded at a size other than the client size at render time={stale}");

        // Evidence: CSVs and kymographs.
        WriteCsvs(dir, dense, msgs, ui, rf, hooks, loops);
        Kymograph(dir, "kymo_right.png", dense.Where(d => d.Stable && d.Rep <= 2 && d.Label is "grow" or "hold" or "shrink").ToList(), true, cR);
        Kymograph(dir, "kymo_bottom.png", dense.Where(d => d.Stable && d.Rep <= 2 && d.Label is "grow" or "hold" or "shrink").ToList(), false, cB);

        // ---- Rest geometry (session 3): no input, SC_MAXIMIZE / SC_RESTORE posted to our own window ----
        if (DoGeom && !R.Inconclusive)
        {
            try { R.GeomKey = await RestGeometry(name, h, x0, y0, wr0); }
            catch (Exception e) { Log($"{name}: GEOM failed: {e}"); R.GeomKey = "failed: " + e.Message; }
        }

        // ---- DWM animations and Snap, with the remedy still installed ----
        if (snap && !R.Inconclusive)
        {
            try { await SnapChecks(R, sub, h, x0, y0, wr0, co, dir, animControl); }
            catch (Exception e) { Log($"{name}: SNAP checks failed: {e}"); R.SnapDetail = "failed: " + e.Message; }
        }

        sub.Animate = false;
        sub.Close(); windows.Remove(sub);
        await Task.Delay(400);
        return R;
    }

    static void WriteCsvs(string dir, List<DenseSample> dense, (double ms, string what)[] msgs,
        (double ms, double x, double w, double h)[] ui,
        (double ms, double x, double w, double h, int clipW, int clipH, int clientW, int clientH)[] rf,
        (double ms, string kind, double dur, int w, int h)[] hooks, List<(double a, double b)> loops)
    {
        // Per-frame CSVs cover the first drag's modal loop only (the logged statistics cover all drags).
        bool InLoops(double t) => loops.Count > 0 && t >= loops[0].a - 50 && t <= loops[0].b + 50;
        string W(int v) => v == int.MinValue ? "none" : v.ToString(CultureInfo.InvariantCulture);
        File.WriteAllLines(Path.Combine(dir, "dense_edges.csv"), new[] { "t0,t1,rep,phaseByTime,phaseP4Tag,stable,rightOffset,bottomOffset,frame,cursorX,cursorY,tick2X,rightWide,rightRun,bottomWide,bottomRun" }.Concat(dense.Select(d =>
            $"{F(d.T0, "F2")},{F(d.T1, "F2")},{d.Rep},{d.Label},{d.Phase},{d.Stable},{W(d.Right)},{W(d.Bottom)},\"{d.Frame}\",{d.Cursor.X},{d.Cursor.Y},{d.Tick2},{W(d.RightWide)},{d.RightRun},{W(d.BottomWide)},{d.BottomRun}")));
        File.WriteAllLines(Path.Combine(dir, "messages.csv"), new[] { "ms,message" }.Concat(msgs.Select(m => $"{F(m.ms, "F2")},{m.what}")));
        File.WriteAllLines(Path.Combine(dir, "hooks.csv"), new[] { "ms,kind,durationMs,clientW,clientH" }.Concat(hooks.Select(x => $"{F(x.ms, "F2")},{x.kind},{F(x.dur, "F3")},{x.w},{x.h}")));
        File.WriteAllLines(Path.Combine(dir, "ui_frames.csv"), new[] { "ms,springX,clientW,clientH" }.Concat(ui.Where(r => InLoops(r.ms)).Select(r => $"{F(r.ms, "F2")},{F(r.x, "F1")},{r.w},{r.h}")));
        File.WriteAllLines(Path.Combine(dir, "render_frames.csv"), new[] { "ms,springX,layoutW,layoutH,clipW,clipH,clientW,clientH" }.Concat(rf.Where(r => InLoops(r.ms)).Select(r => $"{F(r.ms, "F2")},{F(r.x, "F1")},{r.w},{r.h},{r.clipW},{r.clipH},{r.clientW},{r.clientH}")));
    }

    /// One image row per stable sample: the 71 px across the right edge (or the bottom edge) from d = 64 (inside, left of
    /// the image) to d = -6 (outside, right), each scaled x3, rows x2. A red tick in the left margin marks a sample that
    /// P4's criterion counts as an artefact; a dark grey tick a hold sample; phase changes get a thin white line.
    static void Kymograph(string dir, string name, List<DenseSample> ds, bool right, int cal)
    {
        if (ds.Count == 0) return;
        const int SX = 3, SY = 2, Margin = 8;
        int w = Margin + 71 * SX, hgt = ds.Count * SY;
        var buf = new byte[w * hgt * 4];
        string prev = "";
        for (int i = 0; i < ds.Count; i++)
        {
            var d = ds[i];
            var slice = right ? d.RSlice : d.BSlice;
            int off = right ? d.Right : d.Bottom;
            bool art = off == int.MinValue || Math.Abs(off - cal) > 2;
            for (int yy = 0; yy < SY; yy++)
            {
                int y = i * SY + yy;
                for (int x = 0; x < Margin; x++)
                {
                    int o = (y * w + x) * 4;
                    byte r = 40, g = 40, b = 40;
                    if (art) { r = 255; g = 0; b = 0; }
                    else if (d.Label == "hold") { r = 90; g = 90; b = 90; }
                    if (d.Label != prev && yy == 0) { r = 255; g = 255; b = 255; }
                    buf[o] = b; buf[o + 1] = g; buf[o + 2] = r; buf[o + 3] = 255;
                }
                for (int k = 0; k < 71; k++)
                {
                    int dd = 64 - k;                                  // image left = inside
                    int si = (dd + 6) * 3;
                    for (int xs = 0; xs < SX; xs++)
                    {
                        int o = (y * w + Margin + k * SX + xs) * 4;
                        buf[o] = slice[si + 2]; buf[o + 1] = slice[si + 1]; buf[o + 2] = slice[si]; buf[o + 3] = 255;
                    }
                }
            }
            prev = d.Label;
        }
        SavePng(dir, buf, w, hgt, new N.RECT { L = 0, T = 0, R = w, B = hgt }, name);
    }

    // ---------------- Rest geometry (added in session 3) ----------------

    /// Where the app's content sits on screen at rest, relative to the window's client rectangle: the first magenta marker
    /// pixel counted inward from each client edge (left and right on the dense row at client y = 200, top and bottom on a
    /// column at client x = 200), and the centres of the two static cyan ticks (drawn at client x = 660 and x = 1260) as x
    /// relative to the client's left edge. Measured restored, maximised (SC_MAXIMIZE posted to our own window, no input)
    /// and restored again. A remedy that scaled or shifted the content at rest gives numbers different from `base`.
    /// Only numbers are kept: a maximised window's lines cross the whole monitor.
    static async Task<string> RestGeometry(string name, IntPtr h, int x0, int y0, N.RECT wr0)
    {
        var parts = new List<string> { MeasureGeom(name, h, "restored") };
        N.PostMessageW(h, N.WM_SYSCOMMAND, new IntPtr(N.SC_MAXIMIZE), IntPtr.Zero);
        await Task.Delay(1200);
        parts.Add(MeasureGeom(name, h, "maximized"));
        N.PostMessageW(h, N.WM_SYSCOMMAND, new IntPtr(N.SC_RESTORE), IntPtr.Zero);
        await Task.Delay(1200);
        parts.Add(MeasureGeom(name, h, "restoredAgain"));
        N.SetWindowPos(h, N.HWND_TOPMOST, x0, y0, wr0.W, wr0.H, N.SWP_NOACTIVATE);
        await Task.Delay(600);
        return string.Join(" | ", parts);
    }

    static string MeasureGeom(string name, IntPtr h, string state)
    {
        N.GetClientRect(h, out var cr);
        var o = new N.POINT(); N.ClientToScreen(h, ref o);
        var f0 = N.FrameBounds(h);
        var row = new N.RECT { L = o.X, T = o.Y + (int)Geo.DenseY, R = o.X + cr.W, B = o.Y + (int)Geo.DenseY + 1 };
        var row2 = new N.RECT { L = o.X, T = o.Y + 450, R = o.X + cr.W, B = o.Y + 451 };
        var col = new N.RECT { L = o.X + 200, T = o.Y, R = o.X + 201, B = o.Y + cr.H };
        var br = N.Capture(row); var br2 = N.Capture(row2); var bc = N.Capture(col);
        var f1 = N.FrameBounds(h);
        int ml = -1, mr = -1, mt = -1, mb = -1;
        for (int d = 0; d < 64 && d < row.W; d++) if (Magenta(br, d * 4)) { ml = d; break; }
        for (int d = 0; d < 64 && d < row.W; d++) if (Magenta(br, (row.W - 1 - d) * 4)) { mr = d; break; }
        for (int d = 0; d < 64 && d < col.H; d++) if (Magenta(bc, d * 4)) { mt = d; break; }
        for (int d = 0; d < 64 && d < col.H; d++) if (Magenta(bc, (col.H - 1 - d) * 4)) { mb = d; break; }
        static int Tick(byte[] b, int w)
        {
            int tf = -1, tl = -1;
            for (int x = 0; x < w; x++) { int q = x * 4; if (b[q + 2] < 60 && b[q + 1] > 200 && b[q] > 200) { if (tf < 0) tf = x; tl = x; } }
            return tf < 0 ? -1 : (tf + tl) / 2;
        }
        int t1 = Tick(br2, row2.W), t2 = Tick(br, row.W);
        Log($"{name}: GEOM {state}: IsZoomed={N.IsZoomed(h)} client {cr.W}x{cr.H} at ({o.X},{o.Y}) frame {f0}{(f0.Same(f1) ? "" : " (frame moved during the capture)")} " +
            $"marker offsets from the client edges L{ml} T{mt} R{mr} B{mb}; tick drawn at x=660 found at {t1}, tick drawn at x=1260 found at {t2}");
        return $"{state}: client {cr.W}x{cr.H} markers L{ml} T{mt} R{mr} B{mb} ticks {t1}/{t2}";
    }

    // ---------------- DWM animations and Snap ----------------

    static (long area, byte[] buf, int w, int h) SampleMonitor(N.RECT mon)
    {
        int w = mon.W / 4, h = mon.H / 4;
        var buf = N.Capture(mon, w, h);
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (StrictMagenta(buf, (y * w + x) * 4)) { if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; }
        long area = maxX < 0 ? 0 : (long)(maxX - minX + 1) * (maxY - minY + 1);
        return (area, buf, w, h);
    }

    /// P7's detector: act, then sample the monitor (1/4 scale) for 800 ms; count distinct magenta bounding-box areas
    /// strictly between the start and end states (8% margins). Two or more = animated.
    static (bool animated, int inter, long pre, long post, int frames, double frameMs) Animate(string dir, string label, N.RECT mon, Action act, bool png)
    {
        var pre = SampleMonitor(mon);
        var sw = Stopwatch.StartNew();
        act();
        var frames = new List<(double t, long area)>();
        (double t, long area, byte[]? buf, int w, int h) keep = (0, 0, null, 0, 0);
        var all = new List<(double t, long area, byte[] buf, int w, int h)>();
        while (sw.ElapsedMilliseconds < 800)
        {
            var s = SampleMonitor(mon);
            frames.Add((sw.Elapsed.TotalMilliseconds, s.area));
            all.Add((sw.Elapsed.TotalMilliseconds, s.area, s.buf, s.w, s.h));
        }
        long post = frames[^1].area;
        long lo = Math.Min(pre.area, post), hi = Math.Max(pre.area, post), margin = (long)((hi - lo) * 0.08);
        var mids = all.Where(f => f.area > lo + margin && f.area < hi - margin).ToList();
        int inter = mids.Select(f => f.area).Distinct().Count();
        double fms = frames.Count > 1 ? (frames[^1].t - frames[0].t) / (frames.Count - 1) : 0;
        Log($"ANIM {label}: frames={frames.Count} meanFrameMs={F(fms)} preArea={pre.area} postArea={post} distinctIntermediateAreas={inter} => animated={inter >= 2}; series: " +
            string.Join(" ", frames.Select(f => $"{F(f.t, "F0")}:{f.area}")));
        if (png && mids.Count > 0)
        {
            long target = (lo + hi) / 2;
            var best = mids.OrderBy(f => Math.Abs(f.area - target)).First();
            keep = (best.t, best.area, best.buf, best.w, best.h);
            // Cropped to our own checker window (session 3): the monitor capture also holds whatever else is on screen.
            var crop = new N.RECT { L = (CheckerRect.L - mon.L) / 4, T = (CheckerRect.T - mon.T) / 4, R = (CheckerRect.R - mon.L) / 4, B = (CheckerRect.B - mon.T) / 4 };
            SavePng(dir, keep.buf!, keep.w, keep.h, crop, $"anim_{label}_mid_{F(keep.t, "F0")}ms.png", 0.5);
        }
        return (inter >= 2, inter, pre.area, post, frames.Count, fms);
    }

    static double Changed(byte[] a, byte[] b)
    {
        long d = 0;
        for (int o = 0; o < a.Length; o += 4) if (Math.Abs(a[o] - b[o]) + Math.Abs(a[o + 1] - b[o + 1]) + Math.Abs(a[o + 2] - b[o + 2]) > 48) d++;
        return d / (double)(a.Length / 4);
    }

    static bool MoveGuarded(IntPtr h, N.POINT p, string what)
    {
        if (Owner.Abort) return false;
        if (Owner.Moved(out var now)) { Owner.Flag($"{what}: cursor at {now}, the probe's last input left it at {Owner.Last}"); Log($"GUARD refused {what}: {Owner.Why}"); return false; }
        if (N.Root(p) != h) { Log($"GUARD refused {what}: {p} is not over our HWND; no input sent"); return false; }
        N.MoveAbs(p.X, p.Y);
        Thread.Sleep(15);
        Owner.Mark();
        return true;
    }

    static async Task SnapChecks(CaseResult R, SubjectWindow sub, IntPtr h, int x0, int y0, N.RECT wr0, N.POINT co, string dir, bool animControl)
    {
        string name = R.Name;
        bool png = name is "base" or "stretch" or "stretchsync" or "hide";
        sub.Scene.MagentaFill = true; sub.Scene.InvalidateVisual();
        await Task.Delay(600);
        var mon = N.MonitorOf(h);
        IntPtr SC(int c) => new(c);

        // DWM animations: maximise, restore, minimise, restore (the commands the hooked chrome of P7 posts).
        var aMax = await Task.Run(() => Animate(dir, name + "_maximize", mon, () => N.PostMessageW(h, N.WM_SYSCOMMAND, SC(N.SC_MAXIMIZE), IntPtr.Zero), png));
        bool zoomed = N.IsZoomed(h);
        await Task.Delay(300);
        var aRes = await Task.Run(() => Animate(dir, name + "_restore", mon, () => N.PostMessageW(h, N.WM_SYSCOMMAND, SC(N.SC_RESTORE), IntPtr.Zero), false));
        bool restored = !N.IsZoomed(h);
        await Task.Delay(300);
        var aMin = await Task.Run(() => Animate(dir, name + "_minimize", mon, () => N.PostMessageW(h, N.WM_SYSCOMMAND, SC(N.SC_MINIMIZE), IntPtr.Zero), false));
        bool iconic = N.IsIconic(h);
        await Task.Delay(300);
        var aRes2 = await Task.Run(() => Animate(dir, name + "_restore_from_min", mon, () => N.PostMessageW(h, N.WM_SYSCOMMAND, SC(N.SC_RESTORE), IntPtr.Zero), false));
        bool restored2 = !N.IsIconic(h);
        R.AnimMax = aMax.animated && zoomed; R.AnimRestore = aRes.animated && restored; R.AnimMin = aMin.animated && iconic; R.AnimRestore2 = aRes2.animated && restored2;
        Log($"{name}: ANIM max={aMax.animated} (IsZoomed={zoomed}) restore={aRes.animated} (restored={restored}) min={aMin.animated} (IsIconic={iconic}) restoreFromMin={aRes2.animated} (restored={restored2})");
        N.SetWindowPos(h, N.HWND_TOPMOST, x0, y0, wr0.W, wr0.H, N.SWP_NOACTIVATE);
        await Task.Delay(600);

        if (animControl)
        {
            int on = 1;
            int hr = N.DwmSetWindowAttribute(h, N.DWMWA_TRANSITIONS_FORCEDISABLED, ref on, 4);
            var c = await Task.Run(() => Animate(dir, name + "_CONTROL_transitions_disabled_maximize", mon, () => N.PostMessageW(h, N.WM_SYSCOMMAND, SC(N.SC_MAXIMIZE), IntPtr.Zero), false));
            bool cz = N.IsZoomed(h);
            await Task.Delay(300);
            N.PostMessageW(h, N.WM_SYSCOMMAND, SC(N.SC_RESTORE), IntPtr.Zero);
            await Task.Delay(700);
            int off = 0;
            N.DwmSetWindowAttribute(h, N.DWMWA_TRANSITIONS_FORCEDISABLED, ref off, 4);
            R.AnimControlDisabled = c.animated;
            Log($"{name}: ANIM CONTROL DWMWA_TRANSITIONS_FORCEDISABLED (hr=0x{hr:X8}) maximise animated={c.animated} (must be False) IsZoomed={cz}");
            N.SetWindowPos(h, N.HWND_TOPMOST, x0, y0, wr0.W, wr0.H, N.SWP_NOACTIVATE);
            await Task.Delay(600);
        }

        // Snap Layouts flyout over the declared maximise button (P7's detector).
        var f = N.FrameBounds(h);
        var content = new N.POINT { X = co.X + 600, Y = co.Y + 500 };
        var maxBtn = new N.POINT { X = co.X + 71, Y = co.Y + 23 };
        var zone = new N.RECT { L = f.L, T = f.T + 50, R = Math.Min(f.R, maxBtn.X + 420), B = Math.Min(f.B, f.T + 340) };
        var flyout = await Task.Run<(bool ok, double df, double dfCtl, double dfPost, int hit, bool fg)>(() =>
        {
            if (N.GetForegroundWindow() != h)
            {
                N.SetForegroundWindow(h);
                Thread.Sleep(300);
                if (N.GetForegroundWindow() != h && MoveGuarded(h, content, "foreground click"))
                {
                    Thread.Sleep(80);
                    if (Owner.At(content, out var cc) && N.Root(cc) == h) { N.Button(true); N.Button(false); Owner.Mark(); Thread.Sleep(400); }
                    else Owner.Flag($"foreground click: cursor {cc} != {content}; no button sent");
                }
            }
            bool fg = N.GetForegroundWindow() == h;
            if (!MoveGuarded(h, content, "flyout park")) return (false, double.NaN, double.NaN, double.NaN, 0, fg);
            Thread.Sleep(900);
            var pre = N.Capture(zone);
            Thread.Sleep(1200);
            var ctl = N.Capture(zone);
            int hit = N.NcHit(h, maxBtn);
            if (!MoveGuarded(h, new N.POINT { X = maxBtn.X - 3, Y = maxBtn.Y - 2 }, "flyout hover")) return (false, double.NaN, Changed(pre, ctl), double.NaN, hit, fg);
            Thread.Sleep(60);
            if (!MoveGuarded(h, maxBtn, "flyout hover 2") || !MoveGuarded(h, new N.POINT { X = maxBtn.X + 1, Y = maxBtn.Y }, "flyout hover 3") || !MoveGuarded(h, maxBtn, "flyout hover 4"))
                return (false, double.NaN, Changed(pre, ctl), double.NaN, hit, fg);
            Thread.Sleep(2000);
            var hover = N.Capture(zone);
            if (png) SavePng(dir, hover, zone.W, zone.H, new N.RECT { L = 0, T = 0, R = zone.W, B = zone.H }, "snap_flyout_hover.png", 0.5);
            MoveGuarded(h, content, "flyout dismiss park");
            Thread.Sleep(1500);
            var post = N.Capture(zone);
            return (Changed(pre, hover) > 0.05, Changed(pre, hover), Changed(pre, ctl), Changed(pre, post), hit, fg);
        });
        R.Flyout = flyout.ok;
        Log($"{name}: SNAP flyout: foreground={flyout.fg} NCHITTEST(max button)={flyout.hit} zone={zone} changed hover={F(flyout.df, "F4")} " +
            $"control parked-vs-parked={F(flyout.dfCtl, "F4")} after dismissal={F(flyout.dfPost, "F4")} => flyout={flyout.ok}");

        // Snap by drag: the declared caption to the monitor's top edge, away from the top-centre snap bar.
        var cap = new N.POINT { X = co.X + 300, Y = co.Y + 22 };
        var drag = await Task.Run<(bool ok, string detail)>(() =>
        {
            if (Owner.Abort) return (false, "skipped: " + Owner.Why);
            int ht = N.NcHit(h, cap);
            if (ht != 2 || N.Root(cap) != h) return (false, $"caption point {cap} ht={ht} root ours={N.Root(cap) == h}; no input sent");
            if (!MoveGuarded(h, cap, "snap drag start")) return (false, "guard refused: " + Owner.Why);
            Thread.Sleep(120);
            if (!Owner.At(cap, out var c0) || N.Root(c0) != h || N.NcHit(h, c0) != 2) { Owner.Flag($"snap drag: cursor {c0} != {cap} or not our caption; no button sent"); return (false, Owner.Why); }
            N.Button(true);
            try
            {
                Thread.Sleep(150);
                int top = mon.T;
                for (int i = 1; i <= 24; i++)
                {
                    var p = new N.POINT { X = cap.X, Y = cap.Y + (top - cap.Y) * i / 24 };
                    N.MoveAbs(p.X, p.Y); Thread.Sleep(20);
                    if (!Owner.At(p, out var ci)) { Owner.Flag($"snap drag: cursor {ci} != planned {p}"); break; }
                }
                if (!Owner.Abort) Thread.Sleep(700);
            }
            finally { N.Button(false); Owner.Mark(); }
            if (Owner.Abort) return (false, "aborted: " + Owner.Why);
            Thread.Sleep(900);
            return (N.IsZoomed(h), $"caption {cap} ht=HTCAPTION dragged to y={mon.T}; IsZoomed={N.IsZoomed(h)} frame={N.FrameBounds(h)}");
        });
        R.DragSnap = drag.ok;
        Log($"{name}: SNAP drag-to-top: {drag.detail} => snapped={drag.ok}");
        if (N.IsZoomed(h)) { N.PostMessageW(h, N.WM_SYSCOMMAND, SC(N.SC_RESTORE), IntPtr.Zero); await Task.Delay(700); }
        N.SetWindowPos(h, N.HWND_TOPMOST, x0, y0, wr0.W, wr0.H, N.SWP_NOACTIVATE);
        await Task.Delay(500);
        R.SnapDetail = $"flyout {F(flyout.df, "F4")} ctl {F(flyout.dfCtl, "F4")}; drag {drag.detail}";
        sub.Scene.MagentaFill = false; sub.Scene.InvalidateVisual();
    }
}
