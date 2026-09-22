// P4, task packet T-M0-003: acrylic plus extended client area plus live resize, all three at once.
//
// QUESTION: on Avalonia 12.1.2, when a 1320x860 frameless (extended client area) AcrylicBlur window that runs P1's
// glass surface and a spring animation is drag-resized through Windows' real modal size loop, (1) does any edge show a
// margin artefact (AvaloniaUI/Avalonia#8316: strips at the edges while resizing an extended-client-area window), and
// (2) does the spring keep animating, with no visible stall, snap or discontinuity across WM_ENTERSIZEMOVE and
// WM_EXITSIZEMOVE?
//
// Every window this probe touches is its own:
//   checker   an opaque blue/white checkerboard covering the whole drag extent (so the cursor only ever travels
//             over our own windows; the owner's wallpaper is never changed)
//   subject   the window under test, owned by checker so it stays above it
//   ctl_inset the margin-detector control: the same window, but its content deliberately stops 24 px short of the
//             right and bottom edges (a margin by construction). The detector MUST flag it.
// Input: SendInput absolute moves plus one left-button down/up pair, at a point where WM_NCHITTEST on our own HWND
// returns HTBOTTOMRIGHT and WindowFromPoint confirms our HWND. Every intermediate point lies on our window's own
// moving border over our own checker window. The cursor is restored at the end.
// Evidence: BitBlt of the screen DC (DWM-composed) from a sampler thread in this process, over our windows only.
//
// PRE-REGISTERED THRESHOLDS (fixed before the first run):
//   margin artefact : on a capture whose DWM frame bounds were identical immediately before and after the BitBlt,
//                     the magenta edge marker on any side is missing within 64 px of the frame edge, or sits more
//                     than 2 px away from its at-rest calibrated offset.
//   visible stall   : in the screen-row samples, the green spring bar's detected x is unchanged (or not found)
//                     for more than 100 ms.
//   visible snap    : between consecutive screen-row samples the bar moves more than 1.5x the largest step, or
//                     faster than 1.5x the largest speed, seen in the baseline phase.
//   frame stall     : UI-thread animation-frame interval, or render-thread frame interval, above 50 ms anywhere from
//                     the last frame before WM_ENTERSIZEMOVE to the first frame after WM_EXITSIZEMOVE.
// PASS = no margin artefact during and after the drag, AND no visible stall or snap, AND no frame stall, AND both
// controls (a deliberate 500 ms UI-thread block; the 24 px inset window) are flagged by the same detectors.
//
// Usage: p4 --out <dir> [--composition default|winui|dcomp] [--deco full|borderonly] [--level acrylic|none] [--dense] [--noextend]
// (--level none is a comparison run added after run 1: same window, no acrylic, to see whether the edge lag needs acrylic.)
// Exit 0 PASS, 1 FAIL, 2 INCONCLUSIVE (screen not live, pointer interference, no modal loop), 4 crash.

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
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using SkiaSharp;

static class N
{
    public const int GWL_EXSTYLE = -20, GWL_STYLE = -16;
    public const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const int SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const uint GA_ROOT = 2;
    public const int SPI_GETWORKAREA = 0x30;
    public const uint WM_NCHITTEST = 0x84;
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
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
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
    [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
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

    public static RECT FrameBounds(IntPtr h)
    {
        if (DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out var r, Marshal.SizeOf<RECT>()) == 0) return r;
        GetWindowRect(h, out r);
        return r;
    }

    /// Screen DC BitBlt: the DWM-composed desktop image of the rectangle, as top-down BGRA.
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
}

static class Clock
{
    static readonly Stopwatch Sw = Stopwatch.StartNew();
    public static double Ms => Sw.Elapsed.TotalMilliseconds;
}

/// The probe's test spring (ENGINEERING choice, not a source constant): SwiftUI-style response 0.8 s, damping
/// fraction 0.5, unit mass, integrated in fixed 1 ms steps against wall-clock time, so a stalled frame loop
/// resumes with a jump exactly as a real time-based animation would. The target flips between two x positions
/// every 700 ms, before the underdamped spring settles, so the bar is always moving.
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

static class Rec
{
    public static readonly ConcurrentQueue<(double ms, double x, double w, double h)> Ui = new();
    public static readonly ConcurrentQueue<(double ms, double x, double w, double h)> Render = new();
    public static readonly ConcurrentQueue<(double ms, string what)> Msgs = new();
    public static bool LeaseOk, SurfaceOk, EffectOk;
    public static string EffectErr = "";
}

static class Glass
{
    // P1's SKSL, unchanged: edge-concentrated displacement with per-channel offsets, specular rim, superellipse.
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

/// Client-area geometry, DIP (= physical px at the 96 DPI this probe runs at).
static class Geo
{
    public const double CardL = 48, CardT = 64, CardInset = 48;          // content card inside the acrylic margin
    public const double PanelT = 300, PanelW = 440, PanelH = 220;        // the spring-driven glass panel
    public const double BarW = 6;                                        // green spring bar at the panel centre
    public const double RowY = 410;                                      // screen-row sample through the panel
    public const float Marker = 3;                                       // magenta edge marker thickness
}

sealed class Scene : Control
{
    public double SpringX = 120;
    public double InsetRB;          // 0 for the subject; 24 for the ctl_inset control

    public override void Render(DrawingContext ctx) => ctx.Custom(new Op(new Rect(Bounds.Size), SpringX, InsetRB));

    sealed class Op : ICustomDrawOperation
    {
        readonly double _x, _inset;
        public Op(Rect b, double x, double inset) { Bounds = b; _x = x; _inset = inset; }
        public Rect Bounds { get; }
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            Rec.Render.Enqueue((Clock.Ms, _x, Bounds.Width, Bounds.Height));
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature is null) return;
            Rec.LeaseOk = true;
            using var lease = feature.Lease();
            var c = lease.SkCanvas;
            float W = (float)(Bounds.Width - _inset), H = (float)(Bounds.Height - _inset);

            // Source dark scrim over the acrylic (P3: Avalonia's AcrylicBlur adds no tint; the app draws it).
            using (var p = new SKPaint { Color = new SKColor(0, 0, 0, (byte)Math.Round(0.26 * 255)) }) c.DrawRect(new SKRect(0, 0, W, H), p);
            using (var p = new SKPaint { Color = new SKColor(0x1E, 0x1E, 0x20, (byte)Math.Round(0.12 * 255)) }) c.DrawRect(new SKRect(0, 0, W, H), p);

            // Live app content card (P1's backdrop content), then the glass panel over it.
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
                // The spring's visible readout: an opaque pure-green bar at the panel centre.
                using var gp = new SKPaint { Color = new SKColor(0, 255, 0) };
                float bx = (float)(_x + Geo.PanelW / 2 - Geo.BarW / 2);
                c.DrawRect(new SKRect(bx, panel.Top, bx + (float)Geo.BarW, panel.Bottom), gp);
            }

            // The edge marker: 3 px pure magenta at the outermost pixels of what the app draws.
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

sealed class SubjectWindow : Window
{
    public readonly Scene Scene = new();
    readonly Spring _spring = new();
    public bool Animate = true;
    readonly bool _record;

    public SubjectWindow(string title, WindowDecorations deco, double inset, bool record, WindowTransparencyLevel level)
    {
        _record = record;
        Title = title;
        Width = 1320; Height = 860;                                   // WindowManager.swift:19 (P1 Src.W/H)
        WindowDecorations = deco;
        ExtendClientAreaToDecorationsHint = Program.Extend;
        if (Program.Extend) ExtendClientAreaTitleBarHeightHint = 44;                      // P7's measured chrome configuration
        TransparencyLevelHint = new[] { level };
        Background = Brushes.Transparent;
        CanResize = true;
        Topmost = true;
        ShowInTaskbar = false;
        Scene.InsetRB = inset;
        Template = new FuncControlTemplate<Window>((_, _) => Scene);
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
            0x00A1 => $"WM_NCLBUTTONDOWN ht={wParam.ToInt64()}",
            0x0112 => $"WM_SYSCOMMAND 0x{wParam.ToInt64() & 0xFFF0:X4}",
            0x0215 => "WM_CAPTURECHANGED",
            _ => null,
        };
        if (s != null) Rec.Msgs.Enqueue((Clock.Ms, s));
        return IntPtr.Zero;
    }
}

/// One edge measurement of a capture.
sealed record Edge(string Side, int[] Offsets, bool Missing, int MaxDev, string BandRgb);

sealed class Cap
{
    public double T0, T1;
    public N.RECT Before, After;
    public bool Stable => Before.Same(After);
    public string Phase = "";
    public Edge[] Edges = Array.Empty<Edge>();
    public bool Artefact;
    public int WorstDev;
    public byte[]? Raw;                  // kept only for the captures chosen as evidence
}

/// Dense-mode edge sample (added after runs 1 to 5, post hoc): one screen row across the right edge and one screen
/// column across the bottom edge, so the edge-lag rate can be counted over hundreds of samples instead of ~15.
sealed class DenseSample { public double T0, T1; public string Phase = ""; public bool Stable; public int Right, Bottom; public N.RECT Frame; public string EdgeRgb = ""; }

sealed class RowSample { public double T0, T1; public int X; public string Phase = ""; public int FrameW; }

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
    static string F(double v, string f = "F1") => v.ToString(f, CultureInfo.InvariantCulture);

    [STAThread]
    static int Main(string[] args)
    {
        string comp = "default", deco = "full";
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--out") Out = args[++i];
            else if (args[i] == "--composition") comp = args[++i];
            else if (args[i] == "--deco") deco = args[++i];
            else if (args[i] == "--dense") Dense = true;
            else if (args[i] == "--noextend") Extend = false;
            else if (args[i] == "--level") Level = args[++i] == "none" ? WindowTransparencyLevel.None : WindowTransparencyLevel.AcrylicBlur;
        }
        if (Out == "") { Console.Error.WriteLine("--out required"); return 4; }
        Directory.CreateDirectory(Out);
        var builder = AppBuilder.Configure<Application>().UsePlatformDetect();
        if (comp != "default")
        {
            var m = comp switch { "winui" => Win32CompositionMode.WinUIComposition, "dcomp" => Win32CompositionMode.DirectComposition, _ => throw new ArgumentException(comp) };
            builder = builder.With(new Win32PlatformOptions { CompositionMode = new[] { m } });
        }
        builder.SetupWithoutStarting();
        int exit = 4;
        var cts = new CancellationTokenSource();
        Dispatcher.UIThread.Post(async () =>
        {
            try { exit = await Run(comp, deco == "borderonly" ? WindowDecorations.BorderOnly : WindowDecorations.Full); }
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

    static IntPtr Hwnd(Window w) => w.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

    static bool Magenta(byte[] b, int o) => b[o + 2] > 200 && b[o + 1] < 60 && b[o] > 200;
    static bool Green(byte[] b, int o) => b[o + 1] > 200 && b[o + 2] < 80 && b[o] < 80;

    /// Locates the magenta marker on each side relative to the frame bounds. Offset 0 = marker on the frame's
    /// outermost pixel; positive = inside by that many px; negative = outside the frame.
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
            double br = 0, bg = 0, bb = 0; int bn = 0;
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
                    int o = Px(sx, sy);
                    if (Magenta(buf, o)) { found = d; break; }
                    if (d >= 0) { br += buf[o + 2]; bg += buf[o + 1]; bb += buf[o]; bn++; }
                }
                if (found == int.MinValue) missing = true;
                offs[k] = found;
            }
            res.Add(new Edge(side, offs, missing, 0, bn > 0 ? $"({F(br / bn, "F0")},{F(bg / bn, "F0")},{F(bb / bn, "F0")})" : "-"));
        }
        return res.ToArray();
    }

    static Edge[] Judge(Edge[] e, Dictionary<string, int> calib, out bool artefact, out int worst)
    {
        artefact = false; worst = 0;
        var o = new Edge[e.Length];
        for (int i = 0; i < e.Length; i++)
        {
            int dev = 0;
            foreach (var v in e[i].Offsets) dev = Math.Max(dev, v == int.MinValue ? 999 : Math.Abs(v - calib[e[i].Side]));
            o[i] = e[i] with { MaxDev = dev };
            if (e[i].Missing || dev > 2) artefact = true;
            worst = Math.Max(worst, dev);
        }
        return o;
    }

    static string EdgeStr(Edge[] e) => string.Join(" ", e.Select(x =>
        $"{x.Side}=[{string.Join(",", x.Offsets.Select(v => v == int.MinValue ? "none" : v.ToString(CultureInfo.InvariantCulture)))}]dev{x.MaxDev}" + (x.MaxDev > 2 ? $"band{x.BandRgb}" : "")));

    static void SavePng(byte[] buf, N.RECT u, N.RECT crop, string name, double scale)
    {
        crop.L = Math.Max(crop.L, u.L); crop.T = Math.Max(crop.T, u.T); crop.R = Math.Min(crop.R, u.R); crop.B = Math.Min(crop.B, u.B);
        var info = new SKImageInfo(crop.W, crop.H, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var bmp = new SKBitmap(info);
        var px = new byte[crop.W * crop.H * 4];
        for (int y = 0; y < crop.H; y++) Buffer.BlockCopy(buf, ((y + crop.T - u.T) * u.W + (crop.L - u.L)) * 4, px, y * crop.W * 4, crop.W * 4);
        Marshal.Copy(px, 0, bmp.GetPixels(), px.Length);
        SKBitmap outBmp = bmp;
        if (scale != 1.0) outBmp = bmp.Resize(new SKImageInfo((int)(crop.W * scale), (int)(crop.H * scale), SKColorType.Bgra8888, SKAlphaType.Opaque), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        using var img = SKImage.FromBitmap(outBmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(Out, name), data.ToArray());
        Log($"PNG {name} crop={crop} scale={F(scale, "F2")} bytes={data.Size}");
        if (!ReferenceEquals(outBmp, bmp)) outBmp.Dispose();
    }

    static volatile string Phase = "pre";

    static async Task<int> Run(string comp, WindowDecorations deco)
    {
        N.GetCursorPos(out var cursor0);
        Log($"P4 start composition={comp} deco={deco} level={Level} dense={Dense} extend={Extend} cursor0=({cursor0.X},{cursor0.Y})");
        Log($"avalonia {typeof(Window).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");
        Log($"skiasharp {typeof(SKImage).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");
        Log($"os {Environment.OSVersion.VersionString}; MARROW_PORT_DATA_DIR={Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR")}");
        N.SystemParametersInfoW(N.SPI_GETWORKAREA, 0, out var work, 0);
        Log($"primary work area {work}; virtual screen ({N.GetSystemMetrics(76)},{N.GetSystemMetrics(77)}) {N.GetSystemMetrics(78)}x{N.GetSystemMetrics(79)}");

        var windows = new List<Window>();
        bool buttonDown = false;
        int result = 2;
        try
        {
            // 1. Checkerboard covering the full drag extent.
            var checker = new CheckerWindow { Title = "P4 checkerboard", Topmost = true, ShowInTaskbar = false, WindowDecorations = WindowDecorations.None, CanResize = false };
            windows.Add(checker);
            checker.Show();
            var chk = new N.RECT { L = work.L + 100, T = work.T + 40, R = Math.Min(work.R - 100, work.L + 2100), B = work.B - 40 };
            N.SetWindowPos(Hwnd(checker), N.HWND_TOPMOST, chk.L, chk.T, chk.W, chk.H, N.SWP_NOACTIVATE | N.SWP_SHOWWINDOW);
            Log($"checker rect {chk}");

            // 2. Subject window.
            var sub = new SubjectWindow("P4 subject", deco, 0, true, Level);
            windows.Add(sub);
            sub.Show(checker);
            var h = Hwnd(sub);
            N.GetWindowRect(h, out var wr0);
            N.SetWindowPos(h, N.HWND_TOPMOST, chk.L + 100, chk.T + 60, wr0.W, wr0.H, N.SWP_NOACTIVATE | N.SWP_SHOWWINDOW);
            sub.Start();
            await Task.Delay(2500);
            var f0 = N.FrameBounds(h);
            N.GetWindowRect(h, out var wr);
            N.GetClientRect(h, out var cr);
            var co = new N.POINT(); N.ClientToScreen(h, ref co);
            Log($"subject hwnd=0x{h.ToInt64():X} dpi={N.GetDpiForWindow(h)} GetWindowRect={wr} frame={f0} client={cr.W}x{cr.H} at ({co.X},{co.Y}) " +
                $"style=0x{N.GetWindowLongPtr(h, N.GWL_STYLE).ToInt64():X8} ex=0x{N.GetWindowLongPtr(h, N.GWL_EXSTYLE).ToInt64():X8} " +
                $"ActualTransparencyLevel={sub.ActualTransparencyLevel} IsExtendedIntoWindowDecorations={sub.IsExtendedIntoWindowDecorations} " +
                $"Bounds={sub.Bounds.Width}x{sub.Bounds.Height} ActualThemeVariant={sub.ActualThemeVariant}");
            Log($"glass: lease={Rec.LeaseOk} surface={Rec.SurfaceOk} effect={Rec.EffectOk} {Rec.EffectErr}");

            // Drag plan (screen px): grow +280,+180 over 1.2 s, hold 1.0 s, shrink back to +130,+90 over 0.8 s, hold 0.3 s.
            const int GrowX = 280, GrowY = 180, BackX = 130, BackY = 90;
            var u = new N.RECT { L = f0.L - 16, T = f0.T - 16, R = f0.R + GrowX + 24, B = f0.B + GrowY + 24 };
            Log($"capture union {u}; row y = client top + {Geo.RowY}");
            int rowY = co.Y + (int)Geo.RowY;
            var rowRect = new N.RECT { L = f0.L, T = rowY, R = f0.R + GrowX + 8, B = rowY + 1 };
            var dRight = new N.RECT { L = f0.R - 80, T = f0.T + 200, R = f0.R + GrowX + 30, B = f0.T + 201 };
            var dBottom = new N.RECT { L = f0.L + 200, T = f0.B - 80, R = f0.L + 201, B = f0.B + GrowY + 30 };
            var dense = new List<DenseSample>();

            // 3. At-rest calibration of the marker offsets (two stable captures).
            var restBuf = N.Capture(u);
            var fAfterRest = N.FrameBounds(h);
            var restEdges = Edges(restBuf, u, f0);
            Log($"REST edges (raw offsets) {string.Join(" ", restEdges.Select(e => $"{e.Side}=[{string.Join(",", e.Offsets)}]"))} stable={f0.Same(fAfterRest)}");
            var calib = new Dictionary<string, int>();
            bool calibOk = true;
            foreach (var e in restEdges)
            {
                if (e.Missing || e.Offsets.Distinct().Count() != 1) calibOk = false;
                calib[e.Side] = e.Missing ? 0 : e.Offsets.OrderBy(v => v).ElementAt(e.Offsets.Length / 2);
            }
            Log($"CALIB {string.Join(" ", calib.Select(kv => $"{kv.Key}={kv.Value}"))} consistent={calibOk}");
            // Screen-live check: a patch of our checker far from the subject (run 1 sampled next to the subject, inside
            // its DWM drop shadow, and so read darkened squares) must be the plain checkerboard.
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
            bool screenLive = twoFrac > 0.9 && calibOk;
            Log($"screenLive: checker strip two-valued fraction={F(twoFrac, "F4")} markers found on every side={calibOk} => {screenLive}");
            SavePng(restBuf, u, new N.RECT { L = f0.L - 8, T = f0.T - 8, R = f0.R + 8, B = f0.B + 8 }, "rest_window_half.png", 0.5);
            SavePng(restBuf, u, new N.RECT { L = f0.R - 200, T = f0.B - 200, R = f0.R + 16, B = f0.B + 16 }, "rest_corner_br.png", 1.0);

            // 4. Start of the sampler thread.
            var rows = new List<RowSample>();
            var caps = new List<Cap>();
            volatile_stop = false;
            var reps = new Dictionary<string, Cap>();
            Cap? worst = null;
            var sampler = new Thread(() =>
            {
                double lastCap = 0;
                while (!volatile_stop)
                {
                    var rs = new RowSample { T0 = Clock.Ms, Phase = Phase };
                    var fb = N.FrameBounds(h);
                    rs.FrameW = fb.W;
                    var row = N.Capture(rowRect);
                    rs.T1 = Clock.Ms;
                    int first = -1, last = -1;
                    for (int x = 0; x < rowRect.W; x++) if (Green(row, x * 4)) { if (first < 0) first = x; last = x; }
                    rs.X = first < 0 ? -1 : (first + last) / 2;
                    lock (rows) rows.Add(rs);
                    // Full captures every 120 ms in EVERY phase, so row-sampling cadence is the same in the baseline
                    // as during the drag (the snap test compares step sizes against the baseline).
                    if (Dense && Phase is "grow" or "hold" or "shrink" or "hold2" or "post")
                    {
                        var ds = new DenseSample { T0 = Clock.Ms, Phase = Phase };
                        var fb0 = N.FrameBounds(h);
                        var rb = N.Capture(dRight);
                        var bb2 = N.Capture(dBottom);
                        ds.Frame = N.FrameBounds(h);
                        ds.T1 = Clock.Ms;
                        ds.Stable = fb0.Same(ds.Frame);
                        ds.Right = ds.Bottom = int.MinValue;
                        { int ex = ds.Frame.R - 3 - dRight.L; if (ex >= 0 && ex < dRight.W) ds.EdgeRgb = $"({rb[ex * 4 + 2]},{rb[ex * 4 + 1]},{rb[ex * 4]})"; }
                        for (int d = -6; d <= 64; d++)
                        {
                            int sx = ds.Frame.R - 1 - d;
                            if (sx < dRight.L || sx >= dRight.R) continue;
                            if (Magenta(rb, (sx - dRight.L) * 4)) { ds.Right = d; break; }
                        }
                        for (int d = -6; d <= 64; d++)
                        {
                            int sy = ds.Frame.B - 1 - d;
                            if (sy < dBottom.T || sy >= dBottom.B) continue;
                            if (Magenta(bb2, (sy - dBottom.T) * 4)) { ds.Bottom = d; break; }
                        }
                        dense.Add(ds);
                        Thread.Sleep(1);
                        continue;
                    }
                    if (Clock.Ms - lastCap >= 120)
                    {
                        lastCap = Clock.Ms;
                        var c = new Cap { T0 = Clock.Ms, Phase = Phase, Before = N.FrameBounds(h) };
                        var buf = N.Capture(u);
                        c.After = N.FrameBounds(h);
                        c.T1 = Clock.Ms;
                        c.Edges = Judge(Edges(buf, u, c.After), calib, out c.Artefact, out c.WorstDev);
                        bool dragPhase = c.Phase is "grow" or "hold" or "shrink" or "hold2" or "post";
                        if (c.Stable && dragPhase)
                        {
                            // Evidence: the capture nearest the middle of each phase is replaced as the phase goes on,
                            // simplest rule: keep the 4th stable capture of the phase; plus the worst-deviation capture.
                            int nth = caps.Count(k => k.Phase == c.Phase && k.Stable);
                            if (nth == 3 && c.Phase is "grow" or "hold" or "shrink") { c.Raw = buf; reps[c.Phase] = c; }
                            if (c.WorstDev > 2 && (worst == null || c.WorstDev > worst.WorstDev))
                            {
                                if (worst != null && !reps.ContainsValue(worst)) worst.Raw = null;
                                worst = c; c.Raw = buf;
                            }
                        }
                        lock (caps) caps.Add(c);
                    }
                    Thread.Sleep(1);
                }
            }) { IsBackground = true, Name = "sampler" };

            // 5. Baseline (sampler rows only), then the stall control (500 ms UI-thread block), then recovery.
            await Task.Delay(400);                                 // let the UI thread settle after the rest PNG encodes
            Phase = "baseline";
            double tBase0 = Clock.Ms;
            sampler.Start();
            await Task.Delay(2200);
            double tBase1 = Clock.Ms;
            Phase = "ctl_stall";
            await Task.Delay(300);
            double tCtl0 = Clock.Ms;
            Thread.Sleep(500);                                     // the control: the UI thread does nothing for 500 ms
            double tCtl1 = Clock.Ms;
            await Task.Delay(700);
            Phase = "pre";
            Log($"phases: baseline {F(tBase0)}..{F(tBase1)}; ctl_stall block {F(tCtl0)}..{F(tCtl1)}");

            // 6. Drag start point: HTBOTTOMRIGHT on our own HWND, confirmed by WindowFromPoint.
            var f1 = N.FrameBounds(h);
            N.POINT start = default; bool haveStart = false;
            var map = new StringBuilder();
            for (int d = -4; d <= 6 && !haveStart; d++)
            {
                var p = new N.POINT { X = f1.R - 1 + d, Y = f1.B - 1 + d };
                long lp = ((long)(ushort)p.Y << 16) | (ushort)p.X;
                int ht = (int)N.SendMessageW(h, N.WM_NCHITTEST, IntPtr.Zero, new IntPtr(lp)).ToInt64();
                var root = N.GetAncestor(N.WindowFromPoint(p), N.GA_ROOT);
                map.Append($"({p.X},{p.Y})ht={ht}{(root == h ? "" : "!foreign")} ");
                if (ht == 17 && root == h && d >= 0) { start = p; haveStart = true; }
            }
            Log($"NCHITTEST diagonal scan from the frame's bottom-right pixel: {map}");
            if (!haveStart) { Log("no HTBOTTOMRIGHT point on our HWND; no input sent"); Log("RESULT: INCONCLUSIVE"); return 2; }
            Log($"drag start point ({start.X},{start.Y}) HTBOTTOMRIGHT on our HWND");
            var endGrow = new N.POINT { X = start.X + GrowX, Y = start.Y + GrowY };
            var endBack = new N.POINT { X = start.X + BackX, Y = start.Y + BackY };
            foreach (var p in new[] { endGrow, endBack })
                if (p.X >= chk.R || p.Y >= chk.B) { Log($"planned point ({p.X},{p.Y}) outside our checker; no input sent"); return 2; }

            // 7. The drag, from a driver thread (the UI thread is inside the modal loop while it runs).
            var inputLog = new List<string>();
            int pointerMiss = 0;
            double tDown = 0, tGrow0 = 0, tHold0 = 0, tShrink0 = 0, tHold2 = 0, tUp = 0;
            void Check(N.POINT want)
            {
                N.GetCursorPos(out var c);
                if (Math.Abs(c.X - want.X) > 1 || Math.Abs(c.Y - want.Y) > 1) { pointerMiss++; lock (inputLog) inputLog.Add($"{F(Clock.Ms)} cursor ({c.X},{c.Y}) != planned ({want.X},{want.Y})"); }
            }
            void Glide(N.POINT a, N.POINT b, int steps, int totalMs)
            {
                double t0 = Clock.Ms;
                for (int i = 1; i <= steps; i++)
                {
                    var p = new N.POINT { X = a.X + (b.X - a.X) * i / steps, Y = a.Y + (b.Y - a.Y) * i / steps };
                    N.MoveAbs(p.X, p.Y);
                    double due = t0 + totalMs * i / (double)steps;
                    while (Clock.Ms < due) Thread.Sleep(1);
                    Check(p);
                }
            }
            var driver = Task.Run(() =>
            {
                N.MoveAbs(start.X, start.Y);
                Thread.Sleep(120);
                Check(start);
                tDown = Clock.Ms; N.Button(true); buttonDown = true;
                Thread.Sleep(200);
                Phase = "grow"; tGrow0 = Clock.Ms; Glide(start, endGrow, 60, 1200);
                Phase = "hold"; tHold0 = Clock.Ms; Thread.Sleep(1000); Check(endGrow);
                Phase = "shrink"; tShrink0 = Clock.Ms; Glide(endGrow, endBack, 40, 800);
                Phase = "hold2"; tHold2 = Clock.Ms; Thread.Sleep(300);
                tUp = Clock.Ms; N.Button(false); buttonDown = false;
                Phase = "post";
            });
            await driver;
            await Task.Delay(1500);
            volatile_stop = true;
            sampler.Join();
            Phase = "done";
            foreach (var s in inputLog) Log("POINTER " + s);
            Log($"drag times: down {F(tDown)} grow {F(tGrow0)} hold {F(tHold0)} shrink {F(tShrink0)} hold2 {F(tHold2)} up {F(tUp)}; pointerMisses={pointerMiss}");

            // 8. Settled after-drag capture.
            var fEnd = N.FrameBounds(h);
            var endBuf = N.Capture(u);
            var endEdges = Judge(Edges(endBuf, u, fEnd), calib, out bool endArt, out _);
            Log($"AFTER frame={fEnd} edges {EdgeStr(endEdges)} artefact={endArt}");
            SavePng(endBuf, u, new N.RECT { L = fEnd.L - 8, T = fEnd.T - 8, R = fEnd.R + 8, B = fEnd.B + 8 }, "after_window_half.png", 0.5);
            SavePng(endBuf, u, new N.RECT { L = fEnd.R - 200, T = fEnd.B - 200, R = fEnd.R + 16, B = fEnd.B + 16 }, "after_corner_br.png", 1.0);

            sub.Animate = false;
            var msgs = Rec.Msgs.ToArray();
            var ui = Rec.Ui.ToArray();
            var rf = Rec.Render.ToArray();
            sub.Close(); windows.Remove(sub);
            await Task.Delay(600);

            // 9. Control: the inset-margin window at rest, same position and size. The detector must flag right and bottom.
            var ctl = new SubjectWindow("P4 ctl_inset", deco, 24, false, Level);
            windows.Add(ctl);
            ctl.Show(checker);
            var hc = Hwnd(ctl);
            N.SetWindowPos(hc, N.HWND_TOPMOST, chk.L + 100, chk.T + 60, wr0.W, wr0.H, N.SWP_NOACTIVATE | N.SWP_SHOWWINDOW);
            ctl.Start();
            await Task.Delay(2000);
            var fc = N.FrameBounds(hc);
            var ctlBuf = N.Capture(u);
            var fc2 = N.FrameBounds(hc);
            var ctlEdges = Judge(Edges(ctlBuf, u, fc), calib, out bool ctlArt, out _);
            Log($"CTL_INSET frame={fc} stable={fc.Same(fc2)} edges {EdgeStr(ctlEdges)} artefact={ctlArt}");
            SavePng(ctlBuf, u, new N.RECT { L = fc.R - 200, T = fc.B - 200, R = fc.R + 16, B = fc.B + 16 }, "ctl_inset_corner_br.png", 1.0);
            bool ctlInsetFlagged = ctlArt && ctlEdges.First(e => e.Side == "right").MaxDev > 2 && ctlEdges.First(e => e.Side == "bottom").MaxDev > 2
                                   && ctlEdges.First(e => e.Side == "left").MaxDev <= 2 && ctlEdges.First(e => e.Side == "top").MaxDev <= 2;
            ctl.Animate = false;
            ctl.Close(); windows.Remove(ctl);

            // 10. Analysis.
            foreach (var m in msgs.Where(m => !m.what.StartsWith("WM_SIZ", StringComparison.Ordinal))) Log($"MSG {F(m.ms)} {m.what}");
            int nSizing = msgs.Count(m => m.what.StartsWith("WM_SIZING", StringComparison.Ordinal)), nSize = msgs.Count(m => m.what.StartsWith("WM_SIZE ", StringComparison.Ordinal));
            var enter = msgs.Where(m => m.what == "WM_ENTERSIZEMOVE").Select(m => m.ms).ToArray();
            var exitm = msgs.Where(m => m.what == "WM_EXITSIZEMOVE").Select(m => m.ms).ToArray();
            Log($"modal loop: WM_ENTERSIZEMOVE x{enter.Length} WM_EXITSIZEMOVE x{exitm.Length} WM_SIZING x{nSizing} WM_SIZE x{nSize}");
            bool modal = enter.Length == 1 && exitm.Length == 1 && nSizing > 20;
            double tE = enter.FirstOrDefault(), tX = exitm.FirstOrDefault();

            File.WriteAllLines(Path.Combine(Out, "ui_frames.csv"), new[] { "ms,springX,clientW,clientH" }.Concat(ui.Select(r => $"{F(r.ms, "F2")},{F(r.x, "F2")},{r.w},{r.h}")));
            File.WriteAllLines(Path.Combine(Out, "render_frames.csv"), new[] { "ms,springX,w,h" }.Concat(rf.Select(r => $"{F(r.ms, "F2")},{F(r.x, "F2")},{r.w},{r.h}")));
            File.WriteAllLines(Path.Combine(Out, "row_samples.csv"), new[] { "t0,t1,phase,barX,frameW" }.Concat(rows.Select(r => $"{F(r.T0, "F2")},{F(r.T1, "F2")},{r.Phase},{r.X},{r.FrameW}")));
            File.WriteAllLines(Path.Combine(Out, "messages.csv"), new[] { "ms,message" }.Concat(msgs.Select(m => $"{F(m.ms, "F2")},{m.what}")));
            File.WriteAllLines(Path.Combine(Out, "edge_captures.csv"), new[] { "t0,t1,phase,stable,artefact,worstDev,frame,edges" }.Concat(caps.Select(c => $"{F(c.T0, "F2")},{F(c.T1, "F2")},{c.Phase},{c.Stable},{c.Artefact},{c.WorstDev},\"{c.After}\",\"{EdgeStr(c.Edges)}\"")));

            // Frame intervals.
            (double max, double median, int n, double at) Gaps(double[] ts, double a, double b)
            {
                var sel = ts.Where(t => t >= a && t <= b).ToList();
                var before = ts.Where(t => t < a).DefaultIfEmpty(double.NaN).Max();
                var after = ts.Where(t => t > b).DefaultIfEmpty(double.NaN).Min();
                if (!double.IsNaN(before)) sel.Insert(0, before);
                if (!double.IsNaN(after)) sel.Add(after);
                if (sel.Count < 2) return (double.PositiveInfinity, double.NaN, sel.Count, double.NaN);
                var d = sel.Zip(sel.Skip(1), (x, y) => (gap: y - x, at: x)).ToList();
                var mx = d.OrderByDescending(v => v.gap).First();
                var sorted = d.Select(v => v.gap).OrderBy(v => v).ToList();
                return (mx.gap, sorted[sorted.Count / 2], sel.Count, mx.at);
            }
            var uiT = ui.Select(r => r.ms).ToArray();
            var rT = rf.Select(r => r.ms).ToArray();
            string G((double max, double median, int n, double at) g) => $"frames={g.n} medianInterval={F(g.median, "F2")}ms maxInterval={F(g.max, "F1")}ms (starting at {F(g.at)})";
            var uiBase = Gaps(uiT, tBase0, tBase1); var rBase = Gaps(rT, tBase0, tBase1);
            var uiCtl = Gaps(uiT, tCtl0 - 50, tCtl1 + 50); var rCtl = Gaps(rT, tCtl0 - 50, tCtl1 + 50);
            (double max, double median, int n, double at) uiModal = modal ? Gaps(uiT, tE, tX) : (double.PositiveInfinity, double.NaN, 0, double.NaN);
            (double max, double median, int n, double at) rModal = modal ? Gaps(rT, tE, tX) : (double.PositiveInfinity, double.NaN, 0, double.NaN);
            Log($"FRAMES ui baseline: {G(uiBase)}");
            Log($"FRAMES render baseline: {G(rBase)}");
            Log($"FRAMES ui ctl_stall: {G(uiCtl)}");
            Log($"FRAMES render ctl_stall: {G(rCtl)}");
            Log($"FRAMES ui modal [ENTER {F(tE)} .. EXIT {F(tX)}] incl. boundary frames: {G(uiModal)}");
            Log($"FRAMES render modal incl. boundary frames: {G(rModal)}");
            foreach (var (name, a, b) in new[] { ("grow", tGrow0, tHold0), ("hold", tHold0, tShrink0), ("shrink", tShrink0, tHold2), ("hold2", tHold2, tUp) })
                Log($"FRAMES phase {name}: ui {G(Gaps(uiT, a, b))} | render {G(Gaps(rT, a, b))}");
            // Boundary crossing: intervals that contain the ENTER / EXIT timestamps.
            string Cross(double[] ts, double t) { var b = ts.Where(v => v <= t).DefaultIfEmpty(double.NaN).Max(); var a = ts.Where(v => v > t).DefaultIfEmpty(double.NaN).Min(); return $"{F(a - b)}ms"; }
            if (modal) Log($"BOUNDARY interval containing WM_ENTERSIZEMOVE: ui {Cross(uiT, tE)} render {Cross(rT, tE)}; containing WM_EXITSIZEMOVE: ui {Cross(uiT, tX)} render {Cross(rT, tX)}");

            // Visible (screen-row) analysis.
            (double longest, double at, double maxStep, double maxSpeed, int n, int notFound) Vis(double a, double b)
            {
                var s = rows.Where(r => r.T1 >= a && r.T0 <= b).OrderBy(r => r.T0).ToList();
                double longest = 0, at = double.NaN, maxStep = 0, maxSpeed = 0; int nf = 0;
                int i = 0;
                while (i < s.Count)
                {
                    int j = i;
                    while (j + 1 < s.Count && s[j + 1].X == s[i].X) j++;
                    double dur = (j + 1 < s.Count ? s[j + 1].T0 : s[j].T1) - s[i].T0;
                    if (dur > longest) { longest = dur; at = s[i].T0; }
                    i = j + 1;
                }
                for (int k = 1; k < s.Count; k++)
                {
                    if (s[k].X < 0) nf++;
                    if (s[k].X < 0 || s[k - 1].X < 0 || s[k].X == s[k - 1].X) continue;
                    double step = Math.Abs(s[k].X - s[k - 1].X), dt = s[k].T0 - s[k - 1].T0;
                    maxStep = Math.Max(maxStep, step);
                    if (dt > 0) maxSpeed = Math.Max(maxSpeed, step / dt);
                }
                return (longest, at, maxStep, maxSpeed, s.Count, nf);
            }
            string V((double longest, double at, double maxStep, double maxSpeed, int n, int notFound) v) =>
                $"samples={v.n} notFound={v.notFound} longestUnchanged={F(v.longest)}ms (from {F(v.at)}) maxStep={F(v.maxStep, "F0")}px maxSpeed={F(v.maxSpeed, "F2")}px/ms";
            var sp = rows.Select(r => r.T1 - r.T0).OrderBy(v => v).ToList();
            Log($"ROW SAMPLER capture time median={F(sp[sp.Count / 2], "F2")}ms p95={F(sp[(int)(sp.Count * 0.95)], "F2")}ms max={F(sp[^1], "F2")}ms n={sp.Count}");
            var vBase = Vis(tBase0, tBase1);
            var vCtl = Vis(tCtl0 - 50, tCtl1 + 50);
            (double longest, double at, double maxStep, double maxSpeed, int n, int notFound) vModal = modal ? Vis(tE - 100, tX + 100) : (double.PositiveInfinity, 0.0, 0.0, 0.0, 0, 0);
            Log($"VISIBLE baseline: {V(vBase)}");
            Log($"VISIBLE ctl_stall: {V(vCtl)}");
            Log($"VISIBLE modal (ENTER-100 .. EXIT+100): {V(vModal)}");
            foreach (var (name, a, b) in new[] { ("grow", tGrow0, tHold0), ("hold", tHold0, tShrink0), ("shrink", tShrink0, tHold2), ("hold2", tHold2, tUp), ("post", tUp, tUp + 1400) })
                Log($"VISIBLE phase {name}: {V(Vis(a, b))}");

            // Edge captures.
            foreach (var c in caps) Log($"EDGE {F(c.T0)}..{F(c.T1)} {c.Phase,-6} stable={c.Stable} frame={c.After} artefact={c.Artefact} {EdgeStr(c.Edges)}");
            var dragCaps = caps.Where(c => c.Stable && c.Phase is "grow" or "hold" or "shrink" or "hold2" or "post").ToList();
            int nArt = dragCaps.Count(c => c.Artefact);
            Log($"EDGE SUMMARY stable captures during/after drag={dragCaps.Count} (unstable skipped={caps.Count(c => !c.Stable)}) with artefact={nArt}; " +
                $"per phase: {string.Join(" ", dragCaps.GroupBy(c => c.Phase).Select(g => $"{g.Key}:{g.Count(c => c.Artefact)}/{g.Count()}"))}");
            foreach (var side in new[] { "left", "top", "right", "bottom" })
                Log($"EDGE side {side}: artefact in {dragCaps.Count(c => c.Edges.First(e => e.Side == side).MaxDev > 2 || c.Edges.First(e => e.Side == side).Missing)} captures, max dev {dragCaps.Select(c => c.Edges.First(e => e.Side == side).MaxDev).DefaultIfEmpty(0).Max()}");
            foreach (var c in caps.Where(c => c.Raw != null))
            {
                string tag = (c == worst ? "worst_" : "") + $"{c.Phase}_{(int)c.T0}";
                SavePng(c.Raw!, u, new N.RECT { L = c.After.L - 8, T = c.After.T - 8, R = c.After.R + 8, B = c.After.B + 8 }, $"drag_{tag}_window_half.png", 0.5);
                SavePng(c.Raw!, u, new N.RECT { L = c.After.R - 200, T = c.After.B - 200, R = c.After.R + 16, B = c.After.B + 16 }, $"drag_{tag}_corner_br.png", 1.0);
            }

            if (Dense)
            {
                File.WriteAllLines(Path.Combine(Out, "dense_edges.csv"), new[] { "t0,t1,phase,stable,rightOffset,bottomOffset,frame,rightEdgeRgbAt3px" }.Concat(dense.Select(d =>
                    $"{F(d.T0, "F2")},{F(d.T1, "F2")},{d.Phase},{d.Stable},{(d.Right == int.MinValue ? "none" : d.Right.ToString(CultureInfo.InvariantCulture))},{(d.Bottom == int.MinValue ? "none" : d.Bottom.ToString(CultureInfo.InvariantCulture))},\"{d.Frame}\",\"{d.EdgeRgb}\"")));
                var dt = dense.Select(d => d.T1 - d.T0).OrderBy(v => v).ToList();
                if (dt.Count > 0) Log($"DENSE sample time median={F(dt[dt.Count / 2], "F2")}ms max={F(dt[^1], "F2")}ms n={dt.Count}");
                string Dv(int v, int cal) => v == int.MinValue ? "none" : (v - cal).ToString(CultureInfo.InvariantCulture);
                foreach (var g in dense.Where(d => d.Stable).GroupBy(d => d.Phase))
                {
                    var l = g.ToList();
                    bool Bad(int v, int cal) => v == int.MinValue || Math.Abs(v - cal) > 2;
                    int nr = l.Count(d => Bad(d.Right, calib["right"])), nb = l.Count(d => Bad(d.Bottom, calib["bottom"]));
                    int any = l.Count(d => Bad(d.Right, calib["right"]) || Bad(d.Bottom, calib["bottom"]));
                    var hist = l.Where(d => Bad(d.Right, calib["right"]) || Bad(d.Bottom, calib["bottom"]))
                                .GroupBy(d => $"r{Dv(d.Right, calib["right"])}/b{Dv(d.Bottom, calib["bottom"])}").OrderByDescending(x => x.Count()).Select(x => $"{x.Key}:{x.Count()}");
                    Log($"DENSE phase {g.Key}: stable samples={l.Count} artefact(any)={any} ({F(100.0 * any / Math.Max(1, l.Count), "F1")}%) right={nr} bottom={nb}; deviations {string.Join(" ", hist)}");
                }
                Log($"DENSE unstable samples skipped={dense.Count(d => !d.Stable)}");
            }

            // 11. Verdict.
            const double StallMs = 100, FrameMs = 50;
            bool visStall = vModal.longest > StallMs || vModal.notFound > 0;
            bool visSnap = vModal.maxStep > 1.5 * vBase.maxStep || vModal.maxSpeed > 1.5 * vBase.maxSpeed;
            bool frameStall = uiModal.max > FrameMs || rModal.max > FrameMs;
            bool marginArt = nArt > 0 || endArt;
            bool baselineClean = vBase.longest <= StallMs && uiBase.max <= FrameMs && rBase.max <= FrameMs && vBase.notFound == 0;
            bool ctlStallFlagged = vCtl.longest > StallMs && uiCtl.max > FrameMs;
            Log($"CONTROLS stall control flagged={ctlStallFlagged} (visible longest {F(vCtl.longest)}ms, ui max {F(uiCtl.max)}ms, render max {F(rCtl.max)}ms); inset control flagged={ctlInsetFlagged}");
            Log($"SUMMARY screenLive={screenLive} modalLoop={modal} pointerMisses={pointerMiss} baselineClean={baselineClean} | marginArtefact={marginArt} ({nArt}/{dragCaps.Count} stable drag captures, after={endArt}) | " +
                $"visibleStall={visStall} visibleSnap={visSnap} frameStall={frameStall} (ui max {F(uiModal.max)}ms, render max {F(rModal.max)}ms)");
            if (!screenLive || !modal || pointerMiss > 0 || !baselineClean || !ctlStallFlagged || !ctlInsetFlagged)
            {
                Log("RESULT: INCONCLUSIVE (a precondition or a control did not hold)");
                result = 2;
            }
            else
            {
                bool pass = !marginArt && !visStall && !visSnap && !frameStall;
                Log($"RESULT margin: {(marginArt ? "FAIL" : "PASS")}; animation held across the modal size loop: {(visStall || visSnap || frameStall ? "FAIL" : "PASS")}; P4: {(pass ? "PASS" : "FAIL")}");
                result = pass ? 0 : 1;
            }
        }
        finally
        {
            if (buttonDown) N.Button(false);
            foreach (var w in windows.AsEnumerable().Reverse()) { try { w.Close(); } catch (Exception e) { Log("close: " + e.Message); } }
            N.SetCursorPos(cursor0.X, cursor0.Y);
            N.GetCursorPos(out var c1);
            Log($"windows closed; cursor restored to ({c1.X},{c1.Y}) (was ({cursor0.X},{cursor0.Y}))");
        }
        return result;
    }

    static volatile bool volatile_stop;
    static WindowTransparencyLevel Level = WindowTransparencyLevel.AcrylicBlur;
    static bool Dense;
    public static bool Extend = true;
}
