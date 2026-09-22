// P3, task packet T-M0-002: the acrylic window edge over live desktop content, and the layered-window trap.
//
// QUESTION: does an Avalonia 12.1.2 Window with TransparencyLevelHint = AcrylicBlur and
// ExtendClientAreaToDecorationsHint = true show what is behind it blurred and tinted, on the real composited
// Windows 11 desktop, while it keeps focus; and is that window free of WS_EX_LAYERED (the trap that made the
// old acrylic_window.ps1 captures byte-identical to a bare desktop grab)?
//
// Every window this probe touches is its own:
//   checker   a bright opaque checkerboard (the high-contrast background; the owner's wallpaper is never changed)
//   acrylic   the window under test, owned by checker so it stays above it when checker takes focus
//   ctl_*     control windows at the acrylic window's exact rectangle, one at a time
// Input is only SetForegroundWindow on our HWNDs, or, if Windows refuses that, SetCursorPos + SendInput click
// at a point that WindowFromPoint confirms is on our own HWND. The cursor is restored at the end.
// Captures are BitBlt from the screen DC (DWM-composed) over the acrylic window's DWM frame bounds.
//
// Usage: p3 --out <dir> [--composition default|winui|dcomp|redirection] [--mode avalonia|dwm-backdrop]
// Exit 0 = PASS (bare material blurred AND tinted), 3 = bare material blurred but not tinted while the same window with
// the app-drawn source scrim passes, 1 = FAIL, 2 = INCONCLUSIVE (screen not composited / focus not held), 4 = crash.
// Thresholds were fixed before the first run; run 2 (18:58Z) added only the scrim case, the interior hash and the
// scrim-matched tint-only control, not new thresholds.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;

static class Native
{
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_LAYERED = 0x00080000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOPMOST = 0x8,
        WS_EX_NOREDIRECTIONBITMAP = 0x00200000, WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000;
    public const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const int SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_SYSTEMBACKDROP_TYPE = 38;
    public const uint GA_ROOT = 2, LWA_ALPHA = 2;
    public const int SPI_GETWORKAREA = 0x30;

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; public int W => R - L; public int H => B - T; public override string ToString() => $"[{L},{T}]-[{R},{B}] {W}x{H}"; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint f);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
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
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int size);

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public MOUSEINPUT mi; }
    public const uint INPUT_MOUSE = 0, MOUSEEVENTF_LEFTDOWN = 0x2, MOUSEEVENTF_LEFTUP = 0x4;

    public static long ExStyle(IntPtr h) => GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();

    public static RECT FrameBounds(IntPtr h)
    {
        if (DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out var r, Marshal.SizeOf<RECT>()) == 0) return r;
        GetWindowRect(h, out r);
        return r;
    }

    /// Screen DC BitBlt: returns the DWM-composed desktop image of the rectangle, as top-down BGRA.
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
}

/// The checkerboard is drawn by a child control supplied through the window's template. A Render override on the
/// Window itself (first run, 18:57Z) left the window plain white under Avalonia 12.1.2 with no theme loaded.
sealed class CheckerControl : Control
{
    public const double Square = 96;                       // logical px
    static readonly IBrush White = new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
    static readonly IBrush Blue = new ImmutableSolidColorBrush(Color.FromRgb(0x00, 0x33, 0xFF));
    public override void Render(DrawingContext ctx)
    {
        var s = Bounds.Size;
        ctx.FillRectangle(Blue, new Rect(s));
        for (int y = 0; y * Square < s.Height; y++)
            for (int x = 0; x * Square < s.Width; x++)
                if (((x + y) & 1) == 0) ctx.FillRectangle(White, new Rect(x * Square, y * Square, Square, Square));
    }
}

/// Draws nothing, or flat fills stacked in order (the app-drawn scrim).
sealed class FillControl : Control
{
    public IBrush[]? Fill;
    public override void Render(DrawingContext ctx)
    {
        if (Fill == null) return;
        foreach (var b in Fill) ctx.FillRectangle(b, new Rect(Bounds.Size));
    }
}

sealed class CheckerWindow : Window
{
    public CheckerWindow() { Template = new FuncControlTemplate<Window>((_, _) => new CheckerControl()); }
}

sealed class ProbeWindow : Window
{
    readonly FillControl _child = new();
    public IBrush[]? Fill { get => _child.Fill; set => _child.Fill = value; }
    public ProbeWindow() { Template = new FuncControlTemplate<Window>((_, _) => _child); }
}

sealed class Img
{
    public string Name = "";
    public int W, H;
    public byte[] Bgra = Array.Empty<byte>();
    public string RawSha = "", InteriorSha = "", PngSha = "", PngFile = "";
    public long PngBytes;
}

sealed class Stats
{
    public double R, G, B, LumaMean, LumaStd, SqGrad, TwoValued;
    public double[] Luma = Array.Empty<double>();
    public double[] Blocks = Array.Empty<double>();
    public int IW, IH;
}

static class Program
{
    static string Out = "";
    static readonly StringBuilder LogBuf = new();
    static readonly List<string> Deviations = new();

    static void Log(string s)
    {
        var line = $"{DateTime.UtcNow:HH:mm:ss.fff}Z {s}";
        Console.WriteLine(line);
        LogBuf.AppendLine(line);
    }

    [STAThread]
    static int Main(string[] args)
    {
        string comp = "default", mode = "avalonia";
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--out") Out = args[++i];
            else if (args[i] == "--composition") comp = args[++i];
            else if (args[i] == "--mode") mode = args[++i];
        }
        if (Out == "") { Console.Error.WriteLine("--out required"); return 4; }
        Directory.CreateDirectory(Out);

        var builder = AppBuilder.Configure<Application>().UsePlatformDetect();
        if (comp != "default")
        {
            var m = comp switch
            {
                "winui" => Win32CompositionMode.WinUIComposition,
                "dcomp" => Win32CompositionMode.DirectComposition,
                "redirection" => Win32CompositionMode.RedirectionSurface,
                _ => throw new ArgumentException(comp),
            };
            builder = builder.With(new Win32PlatformOptions { CompositionMode = new[] { m } });
        }
        builder.SetupWithoutStarting();

        int exit = 4;
        var cts = new CancellationTokenSource();
        Dispatcher.UIThread.Post(async () =>
        {
            try { exit = await Run(comp, mode); }
            catch (Exception e) { Log("CRASH " + e); exit = 4; }
            finally
            {
                File.WriteAllText(Path.Combine(Out, $"run_log_{comp}_{mode}.txt"), LogBuf.ToString());
                cts.Cancel();
            }
        });
        Dispatcher.UIThread.MainLoop(cts.Token);
        return exit;
    }

    static IntPtr Hwnd(Window w) => w.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

    static string ExDesc(long ex) =>
        $"0x{ex:X8} LAYERED={(ex & Native.WS_EX_LAYERED) != 0} NOREDIRECTIONBITMAP={(ex & Native.WS_EX_NOREDIRECTIONBITMAP) != 0} " +
        $"TRANSPARENT={(ex & Native.WS_EX_TRANSPARENT) != 0} TOPMOST={(ex & Native.WS_EX_TOPMOST) != 0}";

    static readonly List<(string when, long ex)> AcrylicExSamples = new();

    static async Task<string> Foreground(IntPtr target, Native.POINT clickAt, string label)
    {
        Native.SetForegroundWindow(target);
        await Task.Delay(300);
        if (Native.GetForegroundWindow() == target) return "SetForegroundWindow";
        var hit = Native.GetAncestor(Native.WindowFromPoint(clickAt), Native.GA_ROOT);
        if (hit != target)
        {
            Log($"{label}: SetForegroundWindow refused and point ({clickAt.X},{clickAt.Y}) is not on our HWND (hit 0x{hit.ToInt64():X}); no click sent");
            return "refused";
        }
        Native.SetCursorPos(clickAt.X, clickAt.Y);
        var inputs = new[]
        {
            new Native.INPUT { type = Native.INPUT_MOUSE, mi = new Native.MOUSEINPUT { dwFlags = Native.MOUSEEVENTF_LEFTDOWN } },
            new Native.INPUT { type = Native.INPUT_MOUSE, mi = new Native.MOUSEINPUT { dwFlags = Native.MOUSEEVENTF_LEFTUP } },
        };
        uint sent = Native.SendInput(2, inputs, Marshal.SizeOf<Native.INPUT>());
        await Task.Delay(300);
        return Native.GetForegroundWindow() == target ? $"SendInput click on own HWND (sent={sent})" : $"failed (sent={sent})";
    }

    static Img Grab(string name, Native.RECT r)
    {
        var img = new Img { Name = name, W = r.W, H = r.H, Bgra = Native.Capture(r) };
        img.RawSha = Convert.ToHexString(SHA256.HashData(img.Bgra)).ToLowerInvariant();
        var info = new SKImageInfo(img.W, img.H, SKColorType.Bgra8888, SKAlphaType.Opaque);
        byte[] png;
        var h = GCHandle.Alloc(img.Bgra, GCHandleType.Pinned);
        try
        {
            using var sk = SKImage.FromPixels(info, h.AddrOfPinnedObject(), img.W * 4);
            using var data = sk.Encode(SKEncodedImageFormat.Png, 100);
            png = data.ToArray();
            img.PngSha = Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant();
            img.PngFile = name + ".png";
            if (png.Length > 1_000_000)
            {
                // Evidence files must stay under 1 MB: keep a half-scale copy; hashes and metrics use full resolution.
                using var bmp = SKBitmap.FromImage(sk);
                using var half = bmp.Resize(new SKImageInfo(img.W / 2, img.H / 2, SKColorType.Bgra8888, SKAlphaType.Opaque), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                using var hi = SKImage.FromBitmap(half);
                using var hd = hi.Encode(SKEncodedImageFormat.Png, 100);
                png = hd.ToArray();
                img.PngFile = name + "_half.png";
            }
        }
        finally { h.Free(); }
        File.WriteAllBytes(Path.Combine(Out, img.PngFile), png);
        img.PngBytes = png.Length;
        // Interior hash: the metrics region only, so a change in the 1 px window border (focus colour) is told apart.
        using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            for (int y = TopInset; y < img.H - Inset; y++) sha.AppendData(img.Bgra, (y * img.W + Inset) * 4, (img.W - 2 * Inset) * 4);
            img.InteriorSha = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
        }
        Log($"CAPTURE {name,-26} rect={r} rawSha256={img.RawSha} interiorSha256={img.InteriorSha} pngSha256={img.PngSha} file={img.PngFile} ({img.PngBytes} B)");
        return img;
    }

    // Metrics region: the window's frame bounds minus 48 px each side and 96 px at the top (caption area).
    const int Inset = 48, TopInset = 96, Block = 16;

    static Stats Measure(Img img)
    {
        int x0 = Inset, y0 = TopInset, iw = img.W - 2 * Inset, ih = img.H - TopInset - Inset;
        var st = new Stats { IW = iw, IH = ih, Luma = new double[iw * ih] };
        double sr = 0, sg = 0, sb = 0, two = 0;
        for (int y = 0; y < ih; y++)
            for (int x = 0; x < iw; x++)
            {
                int o = ((y + y0) * img.W + (x + x0)) * 4;
                double b = img.Bgra[o], g = img.Bgra[o + 1], r = img.Bgra[o + 2];
                sr += r; sg += g; sb += b;
                st.Luma[y * iw + x] = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                bool white = r > 231 && g > 231 && b > 231;
                bool blue = r < 24 && Math.Abs(g - 0x33) < 24 && b > 231;
                if (white || blue) two++;
            }
        int n = iw * ih;
        st.R = sr / n; st.G = sg / n; st.B = sb / n; st.TwoValued = two / n;
        st.LumaMean = st.Luma.Average();
        st.LumaStd = Math.Sqrt(st.Luma.Select(v => (v - st.LumaMean) * (v - st.LumaMean)).Average());
        double gsum = 0; long gn = 0;
        for (int y = 0; y < ih - 1; y++)
            for (int x = 0; x < iw - 1; x++)
            {
                double c = st.Luma[y * iw + x], dx = st.Luma[y * iw + x + 1] - c, dy = st.Luma[(y + 1) * iw + x] - c;
                gsum += dx * dx + dy * dy; gn++;
            }
        st.SqGrad = gsum / gn;
        int bw = iw / Block, bh = ih / Block;
        st.Blocks = new double[bw * bh];
        for (int by = 0; by < bh; by++)
            for (int bx = 0; bx < bw; bx++)
            {
                double s = 0;
                for (int y = 0; y < Block; y++) for (int x = 0; x < Block; x++) s += st.Luma[(by * Block + y) * iw + bx * Block + x];
                st.Blocks[by * bw + bx] = s / (Block * Block);
            }
        return st;
    }

    static (double corr, double slope) Regress(double[] a, double[] b)
    {
        double ma = a.Average(), mb = b.Average(), sab = 0, saa = 0, sbb = 0;
        for (int i = 0; i < a.Length; i++) { double da = a[i] - ma, db = b[i] - mb; sab += da * db; saa += da * da; sbb += db * db; }
        double corr = (saa < 1e-9 || sbb < 1e-9) ? 0 : sab / Math.Sqrt(saa * sbb);
        double slope = sbb < 1e-9 ? 0 : sab / sbb;            // a regressed on b (b = control)
        return (corr, slope);
    }

    sealed record Verdict(string Name, double PixCorr, double BlockCorr, double Slope, double SqGradRatio, double SharpNorm,
        double MeanDelta, double LumaStd, bool ShaDiffers, bool SeesThrough, bool Blurred, bool Tinted)
    {
        public bool BlurredAndTinted => ShaDiffers && SeesThrough && Blurred && Tinted;
    }

    // Pre-registered thresholds (set before the first run):
    //   sees through : 16x16 block-mean luma correlation with the bare control >= 0.50
    //   blurred      : contrast-normalised sharpness (sqGrad ratio / slope^2) <= 0.25, evaluated only when it sees through
    //   tinted       : Euclidean distance of mean RGB from the control's mean RGB >= 8
    const double SeeThroughMin = 0.50, SharpMax = 0.25, TintMin = 8;

    static Verdict Compare(Img cap, Stats cs, Img ctl, Stats ks)
    {
        var (pc, _) = Regress(cs.Luma, ks.Luma);
        var (bc, slope) = Regress(cs.Blocks, ks.Blocks);
        double sqr = cs.SqGrad / ks.SqGrad;
        double norm = slope > 1e-3 ? sqr / (slope * slope) : double.PositiveInfinity;
        double md = Math.Sqrt((cs.R - ks.R) * (cs.R - ks.R) + (cs.G - ks.G) * (cs.G - ks.G) + (cs.B - ks.B) * (cs.B - ks.B));
        bool see = bc >= SeeThroughMin;
        var v = new Verdict(cap.Name, pc, bc, slope, sqr, norm, md, cs.LumaStd, cap.RawSha != ctl.RawSha, see, see && norm <= SharpMax, md >= TintMin);
        Log(string.Create(CultureInfo.InvariantCulture,
            $"METRIC {cap.Name,-24} meanRGB=({cs.R:F1},{cs.G:F1},{cs.B:F1}) lumaStd={cs.LumaStd:F2} pixCorr={pc:F4} blockCorr={bc:F4} slope={slope:F4} " +
            $"sqGrad={cs.SqGrad:F2} sqGradRatio={sqr:F5} sharpNorm={norm:F5} meanDelta={md:F2} shaDiffers={v.ShaDiffers} " +
            $"seesThrough={v.SeesThrough} blurred={v.Blurred} tinted={v.Tinted} => blurredAndTinted={v.BlurredAndTinted}"));
        return v;
    }

    // SOURCE FACT (pinned 1.6.2): the dark window backdrop is glass, then black at scrim(for: 0.5, isDark: true) = 0.26
    // (Chrome.swift:211-214, :256-258), then Chrome.glassTint (= theme surface #1E1E20, AppTheme.swift:114/:116/:185)
    // at 0.12 (Chrome.swift:264). The 0.5 backdrop setting is the scrim function's "stock" midpoint; whether it is the
    // shipped default setting is not established by this probe. Used only to test ADR-0002 deviation 1 (app-drawn tint).
    static IBrush[] SourceScrimDark() => new IBrush[]
    {
        new ImmutableSolidColorBrush(Color.FromArgb((byte)Math.Round(0.26 * 255), 0x00, 0x00, 0x00)),
        new ImmutableSolidColorBrush(Color.FromArgb((byte)Math.Round(0.12 * 255), 0x1E, 0x1E, 0x20)),
    };

    static ProbeWindow MakeProbeWindow(string title, WindowTransparencyLevel level, IBrush[]? fill = null) => new()
    {
        Title = title,
        TransparencyLevelHint = new[] { level },
        ExtendClientAreaToDecorationsHint = true,
        Background = Brushes.Transparent,
        Topmost = true,
        ShowInTaskbar = false,
        Fill = fill,
    };

    sealed class Shots
    {
        public Img? Focused, FocusedRepeat, Unfocused, Refocused;
        public bool FgFocused, FgAfter, FgUnfocused, FgRefocused;
        public readonly List<(string when, long ex)> Ex = new();
        public string Actual = "";
    }

    static Native.RECT CaptureRect;
    static bool HaveRect;

    static async Task<Shots> RunSubject(string name, ProbeWindow w, Window owner, IntPtr ownerHwnd, Native.RECT place,
        Native.POINT ownerSpot, Native.POINT cursor0, bool dwmBackdrop, bool forceLayered, bool full)
    {
        var s = new Shots();
        w.Show(owner);
        var h = Hwnd(w);
        Native.SetWindowPos(h, Native.HWND_TOPMOST, place.L, place.T, place.W, place.H, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        if (dwmBackdrop)
        {
            int t = 3; // DWMSBT_TRANSIENTWINDOW
            int hr = Native.DwmSetWindowAttribute(h, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref t, 4);
            Log($"{name}: DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_TRANSIENTWINDOW) hr=0x{hr:X8}");
        }
        if (forceLayered)
        {
            long e = Native.ExStyle(h);
            Native.SetWindowLongPtr(h, Native.GWL_EXSTYLE, new IntPtr(e | Native.WS_EX_LAYERED));
            bool ok = Native.SetLayeredWindowAttributes(h, 0, 255, Native.LWA_ALPHA);
            Log($"{name}: forced WS_EX_LAYERED + SetLayeredWindowAttributes(alpha 255) ok={ok}");
        }
        await Task.Delay(2000);
        s.Ex.Add(("after show", Native.ExStyle(h)));
        var frame = Native.FrameBounds(h);
        if (!HaveRect) { CaptureRect = frame; HaveRect = true; }
        Native.GetWindowRect(h, out var wr);
        s.Actual = w.ActualTransparencyLevel.ToString();
        Log($"{name}: hwnd=0x{h.ToInt64():X} dpi={Native.GetDpiForWindow(h)} GetWindowRect={wr} frame={frame} " +
            $"hint=[{string.Join(",", w.TransparencyLevelHint)}] ActualTransparencyLevel={w.ActualTransparencyLevel} " +
            $"IsExtendedIntoWindowDecorations={w.IsExtendedIntoWindowDecorations} ActualThemeVariant={w.ActualThemeVariant} ex={ExDesc(s.Ex[0].ex)}");
        if (frame.L != CaptureRect.L || frame.T != CaptureRect.T || frame.W != CaptureRect.W || frame.H != CaptureRect.H)
            Log($"{name}: NOTE frame differs from capture rect {CaptureRect}");
        var center = new Native.POINT { X = frame.L + frame.W / 2, Y = frame.T + frame.H / 2 };

        string how = await Foreground(h, center, $"{name} focus");
        Native.SetCursorPos(cursor0.X, cursor0.Y);
        await Task.Delay(1200);
        s.FgFocused = Native.GetForegroundWindow() == h;
        s.Ex.Add(("focused", Native.ExStyle(h)));
        Log($"{name}: focus via {how}; foreground==self {s.FgFocused}; IsActive={w.IsActive}");
        s.Focused = Grab(name + "_focused", CaptureRect);
        if (full)
        {
            await Task.Delay(400);
            s.FocusedRepeat = Grab(name + "_focused_repeat", CaptureRect);
        }
        s.FgAfter = Native.GetForegroundWindow() == h;
        Log($"{name}: foreground==self after focused capture(s) {s.FgAfter}");

        if (full)
        {
            how = await Foreground(ownerHwnd, ownerSpot, $"{name} unfocus (checker takes focus)");
            Native.SetCursorPos(cursor0.X, cursor0.Y);
            await Task.Delay(1200);
            s.FgUnfocused = Native.GetForegroundWindow() == ownerHwnd;
            s.Ex.Add(("unfocused", Native.ExStyle(h)));
            Log($"{name}: checker focused via {how}; foreground==checker {s.FgUnfocused}; self.IsActive={w.IsActive}");
            s.Unfocused = Grab(name + "_unfocused", CaptureRect);

            how = await Foreground(h, center, $"{name} refocus");
            Native.SetCursorPos(cursor0.X, cursor0.Y);
            await Task.Delay(1200);
            s.FgRefocused = Native.GetForegroundWindow() == h;
            s.Ex.Add(("refocused", Native.ExStyle(h)));
            Log($"{name}: refocus via {how}; foreground==self {s.FgRefocused}");
            s.Refocused = Grab(name + "_refocused", CaptureRect);
        }
        foreach (var e in s.Ex) Log($"ASSERT {name} GWL_EXSTYLE {e.when}: {ExDesc(e.ex)}");
        w.Close();
        await Task.Delay(800);
        return s;
    }

    static async Task<int> Run(string comp, string mode)
    {
        Native.GetCursorPos(out var cursor0);
        bool dwm = mode == "dwm-backdrop";
        Log($"P3 start composition={comp} mode={mode} cursor0=({cursor0.X},{cursor0.Y})");
        Log($"avalonia {typeof(Window).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");
        Log($"skiasharp {typeof(SKImage).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");
        Log($"os {Environment.OSVersion.VersionString}; MARROW_PORT_DATA_DIR={Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR")}");
        Native.SystemParametersInfoW(Native.SPI_GETWORKAREA, 0, out var work, 0);
        Log($"primary work area (physical) {work}");

        var windows = new List<Window>();
        int result = 1;
        try
        {
            // 1. The high-contrast background, our own window.
            var checker = new CheckerWindow { Title = "P3 checkerboard", Topmost = true, ShowInTaskbar = false, WindowDecorations = WindowDecorations.None, CanResize = false };
            windows.Add(checker);
            checker.Show();
            var ch = Hwnd(checker);
            int cw = Math.Min(1500, work.W - 40), chh = Math.Min(1000, work.H - 40);
            int cx = work.L + (work.W - cw) / 2, cy = work.T + (work.H - chh) / 2;
            Native.SetWindowPos(ch, Native.HWND_TOPMOST, cx, cy, cw, chh, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            Log($"checker hwnd=0x{ch.ToInt64():X} dpi={Native.GetDpiForWindow(ch)} ex={ExDesc(Native.ExStyle(ch))}");
            int aw = 960, ahh = 640;
            var place = new Native.RECT { L = cx + (cw - aw) / 2, T = cy + (chh - ahh) / 2 };
            place.R = place.L + aw; place.B = place.T + ahh;
            var checkerSpot = new Native.POINT { X = cx + 40, Y = cy + 40 };
            var subjectLevel = dwm ? WindowTransparencyLevel.Transparent : WindowTransparencyLevel.AcrylicBlur;
            string subjectLabel = dwm ? "dwmbackdrop" : "acrylic";

            // 2. The window under test, bare: exactly the packet's configuration.
            var bareW = MakeProbeWindow("P3 " + subjectLabel, subjectLevel);
            windows.Add(bareW);
            var bare = await RunSubject(subjectLabel, bareW, checker, ch, place, checkerSpot, cursor0, dwm, false, true);
            windows.Remove(bareW);

            // 3. The same window with the source's dark backdrop scrim drawn by the app over the material (ADR-0002 deviation 1).
            var scrimW = MakeProbeWindow("P3 " + subjectLabel + " + scrim", subjectLevel, SourceScrimDark());
            windows.Add(scrimW);
            var scrim = await RunSubject(subjectLabel + "_scrim", scrimW, checker, ch, place, checkerSpot, cursor0, dwm, false, true);
            windows.Remove(scrimW);

            // 4. Control: the same region with no window of ours over the checkerboard.
            await Task.Delay(400);
            var control = Grab("control_hidden", CaptureRect);
            await Task.Delay(400);
            var controlB = Grab("control_hidden_repeat", CaptureRect);

            // 5. Negative controls at the same rectangle; each must FAIL the blurred-and-tinted test.
            var ctl = new List<Shots>();
            foreach (var (name, level, fill, layered) in new (string, WindowTransparencyLevel, IBrush[]?, bool)[]
            {
                ("ctl_none_opaque", WindowTransparencyLevel.None, null, false),
                ("ctl_transparent_scrim_no_blur", WindowTransparencyLevel.Transparent, SourceScrimDark(), false),
                (dwm ? "ctl_layered_dwmbackdrop" : "ctl_layered_acrylic", subjectLevel, null, true),
            })
            {
                var w = MakeProbeWindow("P3 " + name, level, fill);
                windows.Add(w);
                ctl.Add(await RunSubject(name, w, checker, ch, place, checkerSpot, cursor0, dwm && layered, layered, false));
                windows.Remove(w);
            }

            // 6. Measurements.
            var ks = Measure(control);
            var ks2 = Measure(controlB);
            Log(string.Create(CultureInfo.InvariantCulture,
                $"CONTROL control_hidden meanRGB=({ks.R:F1},{ks.G:F1},{ks.B:F1}) lumaStd={ks.LumaStd:F2} twoValuedFraction={ks.TwoValued:F4} sqGrad={ks.SqGrad:F2} " +
                $"repeatIdentical={control.RawSha == controlB.RawSha} metricsInterior={ks.IW}x{ks.IH}"));
            bool screenLive = ks.LumaStd > 40 && ks.TwoValued > 0.90 && ks2.TwoValued > 0.90;
            Log($"screenLive (control shows our checkerboard, not a uniform or foreign image) = {screenLive}");

            Verdict V(Img? i) => Compare(i!, Measure(i!), control, ks);
            var bF = V(bare.Focused); var bF2 = V(bare.FocusedRepeat); var bU = V(bare.Unfocused); var bR = V(bare.Refocused);
            var sF = V(scrim.Focused); var sF2 = V(scrim.FocusedRepeat); var sU = V(scrim.Unfocused); var sR = V(scrim.Refocused);
            var cV = ctl.Select(c => V(c.Focused)).ToList();
            var nullV = Compare(controlB, ks2, control, ks);

            foreach (var (label, s) in new[] { (subjectLabel, bare), (subjectLabel + "_scrim", scrim) })
                Log($"FOCUS {label}: interior focused==repeat {s.Focused!.InteriorSha == s.FocusedRepeat!.InteriorSha}; " +
                    $"interior unfocused==focused {s.Unfocused!.InteriorSha == s.Focused.InteriorSha} (full frame {s.Unfocused.RawSha == s.Focused.RawSha}); " +
                    $"interior refocused==focused {s.Refocused!.InteriorSha == s.Focused.InteriorSha}");
            Log($"LAYERED CONTROL: {ctl[2].Focused!.Name} interior == {subjectLabel}_focused interior: {ctl[2].Focused!.InteriorSha == bare.Focused!.InteriorSha}");

            bool neverLayered = bare.Ex.Concat(scrim.Ex).All(s => (s.ex & Native.WS_EX_LAYERED) == 0);
            bool actualAcrylic = dwm || (bare.Actual == "AcrylicBlur" && scrim.Actual == "AcrylicBlur");
            bool focusHeld = bare.FgFocused && bare.FgAfter && scrim.FgFocused && scrim.FgAfter;
            bool negativesFail = !cV[0].BlurredAndTinted && !cV[1].BlurredAndTinted && !nullV.BlurredAndTinted;
            bool bareBlur = bF.ShaDiffers && bF.SeesThrough && bF.Blurred && bF2.SeesThrough && bF2.Blurred;
            bool barePass = neverLayered && actualAcrylic && bF.BlurredAndTinted && bF2.BlurredAndTinted && negativesFail;
            bool scrimPass = neverLayered && actualAcrylic && sF.BlurredAndTinted && sF2.BlurredAndTinted && negativesFail;

            Log($"SUMMARY neverLayered={neverLayered} actualAcrylic={actualAcrylic} screenLive={screenLive} focusHeld={focusHeld} negativeControlsFail={negativesFail} " +
                $"bare: blurred={bareBlur} tinted={bF.Tinted} blurredAndTinted={bF.BlurredAndTinted} unfocused={bU.BlurredAndTinted} refocused={bR.BlurredAndTinted} | " +
                $"scrim: blurredAndTinted={sF.BlurredAndTinted} unfocused={sU.BlurredAndTinted} refocused={sR.BlurredAndTinted}");

            if (!screenLive || !focusHeld) { Log("RESULT: INCONCLUSIVE"); result = 2; }
            else
            {
                Log($"RESULT bare {subjectLabel} (packet configuration): {(barePass ? "PASS" : "FAIL")}");
                Log($"RESULT {subjectLabel} + app-drawn source scrim (ADR-0002 deviation 1): {(scrimPass ? "PASS" : "FAIL")}");
                result = barePass ? 0 : (bareBlur && neverLayered && actualAcrylic && negativesFail && scrimPass) ? 3 : 1;
            }
        }
        finally
        {
            foreach (var w in windows.AsEnumerable().Reverse()) { try { w.Close(); } catch (Exception e) { Log("close: " + e.Message); } }
            Native.SetCursorPos(cursor0.X, cursor0.Y);
            Native.GetCursorPos(out var c1);
            Log($"windows closed; cursor restored to ({c1.X},{c1.Y}) (was ({cursor0.X},{cursor0.Y}))");
        }
        return result;
    }
}
