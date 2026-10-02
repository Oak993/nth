// P14 probe (OD-0041, ED-236). P13 (E:\Droppy\work\probes\p13) found that an AcrylicBlur window shows its tint about
// 130-180 ms after Show and its blur about 330 ms after Show; for 150-200 ms it reads as tinted glass over SHARP content.
// This probe measures, on the real screen, ways of showing such a window over a high-contrast stripe pattern:
//   visible    : Window.Opacity 1 the whole time (the P13 case: the gap).
//   content0   : Opacity 1, but the window's root content at opacity 0 (only the backdrop can show).
//   window0    : Window.Opacity 0 from before Show; after a hold it is set to 1 (the proposed hand-over).
// Per sample (every ~3 ms from a background thread, BitBlt of the composed screen): mean luma and "edge" = mean absolute
// luma step between horizontally adjacent pixels of a stripe crop (the stripes are 6 px wide, black and white:
// about 255 when sharp, about 0 when blurred).
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Threading;

namespace P14;

public sealed class StripeBackdrop : Control
{
    public override void Render(DrawingContext context)
    {
        for (var x = 0; x < (int)Bounds.Width; x += 12)
        {
            context.FillRectangle(Brushes.White, new Rect(x, 0, 6, Bounds.Height));
            context.FillRectangle(Brushes.Black, new Rect(x + 6, 0, 6, Bounds.Height));
        }
    }
}

public sealed class ProbeApp : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        var life = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        life.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _ = Probe.RunAsync(life);
        base.OnFrameworkInitializationCompleted();
    }
}

public static class Probe
{
    private static readonly StringBuilder Log = new();
    private static readonly string Out = Environment.GetEnvironmentVariable("P14_OUT") ?? Path.Combine(AppContext.BaseDirectory, "out");

    private static void Say(string line) { Console.WriteLine(line); Log.AppendLine(line); }

    [STAThread]
    public static int Main(string[] args)
    {
        Directory.CreateDirectory(Out);
        var code = AppBuilder.Configure<ProbeApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
        File.WriteAllText(Path.Combine(Out, "p14-log.txt"), Log.ToString());
        return code;
    }

    private static Window Themeless(Window window)
    {
        window.Template = new FuncControlTemplate<Window>((owner, _) => new ContentPresenter { Content = owner.Content });
        return window;
    }

    public static async Task RunAsync(IClassicDesktopStyleApplicationLifetime life)
    {
        try
        {
            var backdrop = Themeless(new Window
            {
                Width = 1000, Height = 700, WindowDecorations = WindowDecorations.None, ShowInTaskbar = false, Topmost = true,
                WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(100, 100),
                Content = new StripeBackdrop(),
            });
            backdrop.Show();
            await Task.Delay(1500);
            Say($"scale={backdrop.RenderScaling} backdrop at {backdrop.Position}");
            var modes = Environment.GetEnvironmentVariable("P14_MODES")?.Split(',') ?? ["visible", "content0", "window0", "window0-borderless"];
            foreach (var mode in modes) await Experiment(mode);
            backdrop.Close();
        }
        catch (Exception e) { Say("FAILED " + e); }
        life.Shutdown();
    }

    private static async Task Experiment(string mode)
    {
        var scrim = new Border { Background = new SolidColorBrush(Color.FromArgb(0x61, 0, 0, 0)), Child = new TextBlock { Text = "P14", Foreground = Brushes.White } };
        var subject = Themeless(new Window
        {
            Width = 600, Height = 400, ShowInTaskbar = false, Topmost = true, Title = "P14 subject",
            WindowDecorations = mode.EndsWith("borderless") ? WindowDecorations.None : WindowDecorations.Full,
            WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(300, 250),
            Background = Brushes.Transparent, TransparencyLevelHint = [WindowTransparencyLevel.AcrylicBlur],
            ShowActivated = false,
            Content = scrim,
        });
        if (mode == "content0") scrim.Opacity = 0;
        if (mode.StartsWith("window0")) subject.Opacity = 0;
        var hwnd = subject.TryGetPlatformHandle()!.Handle;
        Say($"  handle before Show: 0x{hwnd:X}");
        if (mode.StartsWith("cloak")) { var one = 1; Say($"  cloak hr=0x{DwmSetWindowAttribute(hwnd, 13, ref one, 4):X}"); }
        if (mode == "layered0")
        {
            SetWindowLongPtr(hwnd, -20, GetWindowLongPtr(hwnd, -20) | 0x80000);
            Say($"  layered alpha0 ok={SetLayeredWindowAttributes(hwnd, 0, 0, 2)}");
        }
        if (mode == "offscreen") subject.Position = new PixelPoint(-20000, -20000);
        var scale = subject.RenderScaling;
        // crop: 120 x 60 physical px at the middle of the subject's client area
        var cx = (int)(300 + 600 * scale / 2) - 60;
        var cy = (int)(250 + 400 * scale / 2) - 30;
        var samples = new List<(double T, double Mean, double Edge)>();
        var watch = Stopwatch.StartNew();
        var stop = false;
        var sampler = new Thread(() =>
        {
            while (!Volatile.Read(ref stop))
            {
                var (mean, edge) = Measure(cx, cy, 120, 60);
                lock (samples) samples.Add((watch.Elapsed.TotalMilliseconds, mean, edge));
                Thread.Sleep(3);
            }
        }) { IsBackground = true };
        sampler.Start();
        await Task.Delay(300);
        var showAt = watch.Elapsed.TotalMilliseconds;
        subject.Show();
        double revealAt = -1;
        if (mode.StartsWith("window0") || mode.StartsWith("cloak") || mode is "layered0" or "offscreen")
        {
            await Task.Delay(mode.StartsWith("cloak:") ? int.Parse(mode[6..]) : 1200); // the hold under the stage
            revealAt = watch.Elapsed.TotalMilliseconds;
            if (mode.StartsWith("cloak")) { var zero = 0; DwmSetWindowAttribute(hwnd, 13, ref zero, 4); }
            else if (mode == "layered0") { SetLayeredWindowAttributes(hwnd, 0, 255, 2); SetWindowLongPtr(hwnd, -20, GetWindowLongPtr(hwnd, -20) & ~0x80000L); }
            else if (mode == "offscreen") subject.Position = new PixelPoint(300, 250);
            else subject.Opacity = 1;
            await Task.Delay(700);
        }
        else await Task.Delay(1400);
        Volatile.Write(ref stop, true);
        sampler.Join();
        var layered = (GetWindowLongPtr(subject.TryGetPlatformHandle()!.Handle, -20) & 0x80000) != 0;
        Say($"== {mode}: ActualTransparencyLevel={subject.ActualTransparencyLevel} WS_EX_LAYERED={layered} showAt={showAt:F0}ms revealAt={revealAt:F0}ms");
        var lastKey = "";
        var lastPrint = -1000.0;
        (double T, double Mean, double Edge)[] all;
        lock (samples) all = samples.ToArray();
        foreach (var s in all)
        {
            var key = (s.Edge > 25 ? "SHARP" : s.Edge > 3 ? "mid" : "BLUR") + "/" + (s.Mean < 100 ? "dark" : s.Mean < 140 ? "grey" : "light");
            if (key != lastKey || s.T - lastPrint >= 250)
            {
                var marker = revealAt > 0 && s.T >= revealAt && lastPrint < revealAt ? "   <- reveal" : "";
                Say($"  t={s.T - showAt,7:F0}ms (since Show) mean={s.Mean,6:F1} edge={s.Edge,6:F1} {key}{marker}");
                lastKey = key; lastPrint = s.T;
            }
        }
        var baseline = all.TakeWhile(s => s.T < showAt - 20).ToArray();
        var baseMean = baseline.Average(s => s.Mean); var baseEdge = baseline.Average(s => s.Edge);
        var from = revealAt > 0 ? revealAt : showAt;
        var firstChange = all.Where(s => s.T >= from && (Math.Abs(s.Mean - baseMean) > 8 || Math.Abs(s.Edge - baseEdge) > 3)).Cast<(double T, double Mean, double Edge)?>().FirstOrDefault();
        var firstBlur = all.Where(s => s.T >= from && s.Edge < 3).Cast<(double T, double Mean, double Edge)?>().FirstOrDefault();
        var tintedSharp = all.Count(s => s.T >= from && s.Edge >= 3 && Math.Abs(s.Mean - baseMean) > 8);
        Say($"  backdrop mean={baseMean:F1} edge={baseEdge:F1}; first change at +{(firstChange is null ? double.NaN : firstChange.Value.T - from):F0}ms, first blurred frame at +{(firstBlur is null ? double.NaN : firstBlur.Value.T - from):F0}ms (from {(revealAt > 0 ? "reveal" : "Show")}); samples tinted-but-sharp from there: {tintedSharp} of {all.Count(s => s.T >= from)} samples");
        var held = all.Where(s => s.T >= showAt && s.T < (revealAt > 0 ? revealAt : showAt + 1400)).ToArray();
        if (held.Length > 0)
            Say($"  while shown{(revealAt > 0 ? " at window opacity 0" : "")}: mean {held.Min(s => s.Mean):F1}..{held.Max(s => s.Mean):F1}, edge {held.Min(s => s.Edge):F1}..{held.Max(s => s.Edge):F1}");
        subject.Close();
        await Task.Delay(400);
    }

    private static (double Mean, double Edge) Measure(int x, int y, int w, int h)
    {
        var screen = GetDC(IntPtr.Zero);
        var mem = CreateCompatibleDC(screen);
        var info = new BITMAPINFO { Size = 40, Width = w, Height = -h, Planes = 1, BitCount = 32 };
        var bmp = CreateDIBSection(mem, ref info, 0, out var bits, IntPtr.Zero, 0);
        var old = SelectObject(mem, bmp);
        BitBlt(mem, 0, 0, w, h, screen, x, y, 0x00CC0020 | 0x40000000);
        var buffer = new byte[w * h * 4];
        Marshal.Copy(bits, buffer, 0, buffer.Length);
        SelectObject(mem, old); DeleteObject(bmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen);
        double sum = 0, steps = 0; var count = 0; var stepCount = 0;
        for (var j = 0; j < h; j += 3)
        {
            var previous = -1.0;
            for (var i = 0; i < w; i++)
            {
                var o = (j * w + i) * 4;
                var luma = 0.2126 * buffer[o + 2] + 0.7152 * buffer[o + 1] + 0.0722 * buffer[o];
                sum += luma; count++;
                if (previous >= 0) { steps += Math.Abs(luma - previous); stepCount++; }
                previous = luma;
            }
        }
        return (sum / count, steps / stepCount);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO { public int Size, Width, Height; public short Planes, BitCount; public int Compression, SizeImage, XPels, YPels, ClrUsed, ClrImportant; }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern long SetWindowLongPtr(IntPtr h, int i, long v);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern long GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFO info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, int rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
}
