// Isolated Windows renderer probe. Reuses the public scratch-data/credential and screen-capture fixtures;
// it does not start the installed app, bootstrap providers, alter monitor mode, or use headless drawing.
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.App.Window;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using HostWindow = Avalonia.Controls.Window;

internal static class Program
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static ExecutionContext context = null!;
    private static string output = "";
    private static HostWindow window = null!, backdrop = null!;
    private static WindowManager manager = null!;
    private static readonly ManualFrameSource frames = new();
    private static readonly ManualTimeProvider time = new();
    private static readonly List<object> samples = [];
    private static PixelPoint origin;
    private static PixelSize size;
    private static int result;

    [STAThread]
    public static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows() || !Environment.UserInteractive)
        { Console.Error.WriteLine("Needs an interactive Windows 11 x64 desktop with native WinUI composition."); return 2; }
        output = Path.Combine(Path.GetTempPath(), "Marrow-design-gaps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        // Preserve runtime/OS inputs, then remove inherited application/provider credentials and settings.
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "SystemRoot", "WINDIR", "ComSpec", "PATH", "PATHEXT", "SystemDrive", "NUMBER_OF_PROCESSORS",
          "PROCESSOR_ARCHITECTURE", "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT(x86)", "DOTNET_HOST_PATH" };
        foreach (string name in Environment.GetEnvironmentVariables().Keys)
            if (!keep.Contains(name)) Environment.SetEnvironmentVariable(name, null);
        foreach (var (name, folder) in new[] { ("USERPROFILE", "profile"), ("HOME", "profile"), ("LOCALAPPDATA", "local"), ("APPDATA", "roaming"), ("TEMP", "temp"), ("TMP", "temp") })
        {
            var path = Path.Combine(output, folder); Directory.CreateDirectory(path);
            Environment.SetEnvironmentVariable(name, path);
        }
        Environment.SetEnvironmentVariable("MARROW_PORT_DATA_DIR", Path.Combine(output, "data"));
        Environment.SetEnvironmentVariable("MARROW_DEV_DATA_ROOT", Path.Combine(output, "dev"));
        Console.WriteLine("Native capture artifacts: " + output);
        return Run(args.Contains("--light"));
    }

    private static int Run(bool light)
    {
        AppBuilder.Configure<Application>().UsePlatformDetect().WithSwiftFonts().WithPortControlThemes().SetupWithoutStarting();
        MainActor.Bind(AvaloniaMainExecutor.Instance);
        using var harness = new AppModelHarness(withRuntime: false);
        FrameClock.Source.SetProcessDefault(frames);
        SwiftClock.Provider.SetProcessDefault(time);
        manager = WindowManager.Shared;
        context = ExecutionContext.Capture()!;
        using var stop = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try { await Drive(light); }
            catch (Exception error) { Console.Error.WriteLine(error); result = 1; }
            finally
            {
                await Ui(() =>
                {
                    window?.Hide(); // stop and restore the retained Settings animations first
                    window?.PlatformImpl?.Dispose();
                    backdrop?.Close();
                    File.WriteAllText(Path.Combine(output, "samples.json"), JsonSerializer.Serialize(samples, new JsonSerializerOptions { WriteIndented = true }));
                    stop.Cancel();
                    return true;
                });
            }
        });
        try { Dispatcher.UIThread.MainLoop(stop.Token); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        Console.WriteLine("CAPTURED: inspect native pixels and live frames against the locked source before accepting parity.");
        return result;
    }

    private static async Task Drive(bool light)
    {
        await Ui(() =>
        {
            manager.ShowSettings(SettingsPage.Appearance);
            window = (HostWindow)typeof(WindowManager).GetField("settingsWindow", Members)!.GetValue(manager)!;
            var screen = window.Screens.ScreenFromWindow(window) ?? throw new InvalidOperationException("No screen.");
            var scale = screen.Scaling;
            var work = screen.WorkingArea;
            if (work.Width < 1100 * scale || work.Height < 760 * scale) throw new InvalidOperationException("Probe needs a work area fitting 1100 x 760 DIPs.");
            window.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
            window.Topmost = true;
            window.ShowInTaskbar = false;
            window.Position = new PixelPoint(work.X + (int)(150 * scale), work.Y + (int)(70 * scale));
            backdrop = new HostWindow
            {
                WindowDecorations = WindowDecorations.None, ShowInTaskbar = false, ShowActivated = false,
                Topmost = true, Width = 1080, Height = 740, Position = new PixelPoint(work.X, work.Y),
                Content = new Stripes(),
                Template = new FuncControlTemplate<HostWindow>((owner, _) => new ContentPresenter { Content = owner.Content }),
            };
            backdrop.Show();
            window.UpdateLayout();
            if (EnvironmentValues.GetAccessibilityReduceMotion(window)) throw new InvalidOperationException("Animation effects must be enabled for the motion capture; run Reduce Motion separately.");
            origin = window.PointToScreen(default);
            size = PixelSize.FromSize(window.ClientSize, window.RenderScaling);
            return true;
        });
        await Task.Delay(350); // draw while cloaked; no UI-thread sleep
        Capture("hidden", 0);
        var revealed = false;
        for (var i = 0; i < 60; i++)
        {
            revealed = await Ui(() =>
            {
                frames.Advance(TimeSpan.Zero);
                var rise = typeof(WindowManager).GetProperty("SettingsOpening", Members)!.GetValue(manager)!;
                return !(bool)rise.GetType().GetProperty("Waiting")!.GetValue(rise)!;
            });
            await Task.Delay(100);
            if (revealed) break;
        }
        if (!revealed) throw new InvalidOperationException("The cloak never released.");
        // Prove the exact backend is in use, rather than mistaking Window.Opacity for native blur alpha.
        var graph = await Ui(() => NativeState());
        if (graph is null) throw new InvalidOperationException("RR-37: pinned WinUI native graph is unavailable.");
        Capture("open", 0);
        double previous = 0;
        for (var i = 1; i <= 16; i++)
        {
            var t = Math.Min(i / 60.0, .260);
            await Step(t - previous); previous = t;
            Capture("open", t);
        }
        await Ui(() => { window.Close(); return true; });
        Capture("close", 0);
        previous = 0;
        for (var i = 1; i <= 10; i++)
        {
            var t = Math.Min(i / 60.0, .160);
            await Step(t - previous); previous = t;
            Capture("close", t);
        }
        await Ui(() =>
        {
            if (window.IsVisible) throw new InvalidOperationException("Settings remained visible past 160 ms.");
            FrameClock.Source.SetProcessDefault(new AvaloniaFrameSource(window));
            SwiftClock.Provider.SetProcessDefault(TimeProvider.System);
            return true;
        });
        // Real-time capture: no held pose, no UI thread delay. Save/compress only after the sequence.
        var live = new List<(string Phase, double Seconds, ScreenGrab Image)>();
        await Ui(() => { manager.ShowSettings(); return true; });
        await RecordLive("live-open", 40, live);
        await Ui(() => { window.Close(); return true; });
        await RecordLive("live-close", 12, live);
        for (var i = 0; i < live.Count; i++)
        {
            var item = live[i];
            item.Image.SavePng(Path.Combine(output, $"{item.Phase}-{i:00}-{item.Seconds * 1000:0000}ms.png"));
        }
    }

    private static async Task RecordLive(string phase, int count, List<(string, double, ScreenGrab)> images)
    {
        var timer = Stopwatch.StartNew();
        for (var i = 0; i < count; i++)
        {
            var remaining = i / 60.0 - timer.Elapsed.TotalSeconds;
            if (remaining > 0) await Task.Delay(TimeSpan.FromSeconds(remaining));
            DwmFlush();
            images.Add((phase, timer.Elapsed.TotalSeconds, ScreenGrab.Of(origin.X - 20, origin.Y - 20, size.Width + 40, size.Height + 40)));
        }
    }
    private static async Task Step(double by)
    {
        await Ui(() => { var span = TimeSpan.FromSeconds(by); time.Advance(span); frames.Advance(span); return true; });
        await Task.Delay(100);
    }
    private static void Capture(string phase, double seconds)
    {
        DwmFlush();
        var image = ScreenGrab.Of(origin.X - 20, origin.Y - 20, size.Width + 40, size.Height + 40);
        image.SavePng(Path.Combine(output, $"{phase}-{seconds * 1000:0000}ms.png"));
        var stripe = ScreenGrab.Of(origin.X + 30, origin.Y + size.Height - 18, 120, 8);
        samples.Add(new { phase, seconds, contrast = stripe.StripeContrast(), mean = stripe.MeanLuma(), native = Ui(NativeState).GetAwaiter().GetResult() });
    }
    private static object? NativeState()
    {
        var surface = window.PlatformImpl!.GetType().GetField("_glSurface", Members)?.GetValue(window.PlatformImpl);
        if (surface?.GetType().FullName != "Avalonia.Win32.WinRT.Composition.WinUiCompositedWindowSurface") return null;
        var native = surface.GetType().GetField("_window", Members)?.GetValue(surface);
        if (native is null) return null;
        var shared = native.GetType().GetField("_shared", Members)!.GetValue(native)!;
        var sync = shared.GetType().GetProperty("SyncRoot")!.GetValue(shared)!;
        lock (sync)
        {
            var blur = native.GetType().GetField("_blur", Members)!.GetValue(native)!;
            var target = native.GetType().GetField("_target", Members)!.GetValue(native)!;
            var targetContract = target.GetType().GetInterfaces().Single(t => t.FullName == "Avalonia.Win32.WinRT.ICompositionTarget");
            var root = targetContract.GetProperty("Root")!.GetValue(target)!;
            try
            {
                var visual = root.GetType().GetInterfaces().Single(t => t.FullName == "Avalonia.Win32.WinRT.IVisual");
                return new { visible = window.IsVisible, blurOpacity = visual.GetProperty("Opacity")!.GetValue(blur), rootOpacity = visual.GetProperty("Opacity")!.GetValue(root), scale = visual.GetProperty("Scale")!.GetValue(root)?.ToString(), centre = visual.GetProperty("CenterPoint")!.GetValue(root)?.ToString(), window.RenderScaling };
            }
            finally { ((IDisposable)root).Dispose(); }
        }
    }
    private static async Task<T> Ui<T>(Func<T> action) => await Dispatcher.UIThread.InvokeAsync(() =>
    {
        T value = default!;
        ExecutionContext.Run(context.CreateCopy(), _ => value = action(), null);
        return value;
    });
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    private sealed class Stripes : Control
    {
        public override void Render(DrawingContext drawing)
        {
            var step = 6 / (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
            for (var x = 0.0; x < Bounds.Width; x += step * 2)
            {
                drawing.FillRectangle(Brushes.Black, new Rect(x, 0, step, Bounds.Height));
                drawing.FillRectangle(Brushes.White, new Rect(x + step, 0, step, Bounds.Height));
            }
        }
    }
}
