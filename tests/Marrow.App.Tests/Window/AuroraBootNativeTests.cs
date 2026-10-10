using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Views.Composer;
using Marrow.App.Window;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Window.Native;

// T10: authentic native child and its own debug HWNDs. Never bootstrap a real provider or open the owner's app.
[Collection("WindowManagerNative")]
public sealed class AuroraBootNativeTests(WindowManagerNativeSession native)
{
    [Fact]
    [Trait("Decision", "ED-236")]
    public void B_warms_behind_the_cloaked_main_window_grows_to_its_DWM_frame_and_records_at_60_fps()
    {
        try
        {
            native.Run(subject =>
            {
                var cacheRoot = Path.Combine(Environment.GetEnvironmentVariable(WindowManagerNativeChildProtocol.DirectoryVariable)!, "aurora-boot-cache");
                GpuProgramCache.FolderOverride = cacheRoot;
                var frameTimeFailures = new List<string>();
                foreach (var reduce in new[] { false, true })
                {
                    var capture = new AuroraLoaderTelemetry();
                    var work = new AuroraBootTrace(); AuroraBootTrace.Current = work;
                    var reveals = 0;
                    Task? warm = null;
                    var (window, boot) = native.OnUi(() =>
                    {
                        subject.Main.Hide(); subject.Settings.Hide();
                        var manager = new WindowManager { MainContent = () => new Border { Background = new SolidColorBrush(Color.FromArgb(66, 0, 0, 0)) } };
                        var main = manager.PrepareMain(); main.Width = reduce ? 900 : 1320; main.Height = reduce ? 700 : 860;
                        main.ShowInTaskbar = false; native.Register(main);
                        FrameClock.Source.SetProcessDefault(new AvaloniaFrameSource(subject.Settings));
                        using var clock = SwiftClock.Provider.Override(TimeProvider.System);
                        var stage = BootStageWindow.Start(main, reduce, () => { reveals++; manager.ShowMain(); });
                        native.Register(stage.Window); stage.Stage.Telemetry = capture;
                        NthMotionWarmup.Register(); AuroraBootWarmup.Register(); // as Program.cs: the nth motion programs (ED-NEW-nth-motion) replaced the Aurora loaders
                        warm = GpuWarmup.WarmAsync(stage.Window);
                        return (main, stage);
                    });
                    try
                    {
                        N.Until(() => native.OnUi(() => boot.Stage.Elapsed >= 2.8), "the real B clock did not run", 20000);
                        Assert.Equal(0, reveals);
                        Assert.True(native.OnUi(() => boot.MainIsCloaked));
                        var handle = native.OnUi(() => window.TryGetPlatformHandle()!.Handle);
                        Assert.Equal(0, DwmGetWindowAttribute(handle, 14, out var cloaked, sizeof(int)));
                        Assert.NotEqual(0, cloaked); // physically absent, although ED-236 has shown the HWND for composition.
                        N.Until(() => warm!.IsCompleted, "the registered programs did not warm behind B", 30000);
                        warm!.GetAwaiter().GetResult();
                        var readyAt = native.OnUi(() => { boot.Ready(); return boot.Stage.Elapsed; });
                        N.Until(() => reveals == 1, "B did not hand over after readiness", 10000);
                        Assert.True(native.OnUi(() => boot.Stage.Elapsed >= readyAt));
                        Assert.False(native.OnUi(() => boot.MainIsCloaked));
                        Assert.True(native.OnUi(() => boot.WholeWindowOpacitySupported));
                        if (!reduce)
                        {
                            N.Until(() => native.OnUi(() => boot.Stage.Frame.Progress >= .2), "B did not begin its grow", 10000);
                            native.OnUi(() =>
                            {
                                Assert.InRange(boot.Stage.Frame.Progress, .2, .99);
                                AuroraBootNativeGeometry.AssertClip(handle, boot.Stage.Frame.Tile, boot.Window.Position, boot.Window.RenderScaling, boot.Stage.Frame.CornerRadius);
                            });
                            N.Until(() => native.OnUi(() => boot.Stage.Frame.Progress >= 1), "B did not finish its grow", 10000);
                            var (tile, origin, scale, visible) = native.OnUi(() => (boot.Stage.Frame.Tile, boot.Window.Position, boot.Window.RenderScaling,
                                WindowManagerNativePlacementGeometry.ReadVisibleBounds(handle).VisibleBounds!));
                            Assert.InRange(origin.X + tile.X * scale, visible.Left - 1, visible.Left + 1);
                            Assert.InRange(origin.Y + tile.Y * scale, visible.Top - 1, visible.Top + 1);
                            Assert.InRange(origin.X + tile.Right * scale, visible.Right - 1, visible.Right + 1);
                            Assert.InRange(origin.Y + tile.Bottom * scale, visible.Bottom - 1, visible.Bottom + 1);
                            Assert.Equal(8, native.OnUi(() => boot.Stage.Frame.CornerRadius));
                            Assert.Equal(38, native.OnUi(() => boot.Stage.Frame.TitleBand.Height));
                        }
                        N.Until(() => !native.OnUi(() => boot.Window.IsVisible), "B did not close after the glow", 10000);
                        var directory = Path.Combine(Environment.GetEnvironmentVariable(WindowManagerNativeChildProtocol.DirectoryVariable)!, "aurora-boot-capture");
                        Directory.CreateDirectory(directory);
                        var lines = new List<string> { "frame,interval_ms,handler_ms,painter_bytes,handler_bytes,start_ticks,end_ticks" };
                        for (var i = 0; i < capture.Count; i++) lines.Add(string.Create(CultureInfo.InvariantCulture, $"{i},{capture.IntervalMilliseconds(i):F6},{capture.DrawingMilliseconds(i):F6},{capture.AllocatedBytes(i)},{capture.HandlerAllocatedBytes(i)},{capture.StartTimestamp(i)},{capture.EndTimestamp(i)}"));
                        File.WriteAllLines(Path.Combine(directory, reduce ? "boot-reduce-motion.csv" : "boot-B.csv"), lines);
                        File.WriteAllText(Path.Combine(directory, reduce ? "boot-reduce-motion-info.json" : "boot-B-info.json"),
                            System.Text.Json.JsonSerializer.Serialize(new { readyAt, frames = capture.Count, frequency = AuroraBootTrace.Frequency, work = work.Snapshot(), warmSteps = GpuWarmup.Done.ToArray(), cacheState = GpuProgramCache.State, cacheFolder = GpuProgramCache.Folder, cacheHits = GpuProgramCache.Hits, cacheWrites = GpuProgramCache.Writes, cacheMisses = GpuProgramCache.Misses }));
                        if (!reduce)
                        {
                            var spans = work.Snapshot();
                            var drawThread = spans.First(e => e.Name == "Boot: compositor draw").ThreadId;
                            Assert.Contains(spans, e => e.Name == "worker: light-rotate");
                            Assert.Contains(spans, e => e.Name == "cache: hit" && e.ThreadId == drawThread);
                            Assert.DoesNotContain(spans, e => e.Name.StartsWith("worker: ", StringComparison.Ordinal) && e.ThreadId == drawThread);
                        }
                        // Keep both raw captures even if the cold first run misses the unchanged frame-time bounds.
                        try
                        {
                            Assert.True(capture.Count >= 180, $"Only {capture.Count} frames were captured.");
                            Assert.InRange(Enumerable.Range(1, capture.Count - 1).Average(capture.IntervalMilliseconds), 14, 20);
                        }
                        catch (Xunit.Sdk.XunitException error) { frameTimeFailures.Add($"ReduceMotion={reduce}: {error.Message}"); }
                        Assert.Equal(1, reveals);
                        Assert.Equal(1, native.OnUi(() => ((Control)window.Content!).Opacity));
                        Assert.Null(native.OnUi(() => ((Control)window.Content!).Clip));
                    }
                    finally
                    {
                        AuroraBootTrace.Current = null;
                        native.OnUi(() => { boot.Window.Close(); window.Close(); AuroraBootWarmup.Unregister(); NthMotionWarmup.Unregister(); });
                    }
                }
                GpuProgramCache.FolderOverride = null;
                Assert.True(frameTimeFailures.Count == 0, string.Join(Environment.NewLine, frameTimeFailures));
            }, factType: typeof(AuroraBootNativeTests));
        }
        finally
        {
            // Export authenticated child captures even when the unchanged cadence assertions fail.
            if (!WindowManagerNativeChildProtocol.IsChild)
            {
                var output = Environment.GetEnvironmentVariable("MARROW_AURORA_CAPTURE") ?? throw new InvalidOperationException("MARROW_AURORA_CAPTURE must name scratch evidence.");
                Directory.CreateDirectory(output);
                foreach (var run in Directory.EnumerateDirectories(Path.Combine(TestDataDirectory.Read()!, "WindowManagerNative")))
                {
                    var capture = Path.Combine(run, "aurora-boot-capture");
                    if (Directory.Exists(capture)) foreach (var file in Directory.EnumerateFiles(capture)) File.Copy(file, Path.Combine(output, Path.GetFileName(file)), true);
                }
            }
        }
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint h, int attribute, out int value, int size);
}
