using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// REVIEW (adversarial, ED-252 warm-up v2, ffd1e29f4): each test states what the warm-up or the cache must do and fails on
// the head reviewed.
[Collection("Headless")]
public sealed class EffortSliderJewelWarmupReviewTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-252")] // the composer's working dots animate on the render server: the warm-up must see them
    public void REVIEW_the_composers_working_dots_count_as_animating_for_the_warm_up() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var executor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var field = new DotFieldControl(new SwiftColor(1), true, false); // the working dots (no sweep: they loop)
        var window = new Avalonia.Controls.Window { Width = 32, Height = 24, Background = Brushes.Black, Content = field };
        window.Show();
        try
        {
            runtime.Advance(TimeSpan.FromSeconds(0.32));
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using (window.CaptureRenderedFrame()) { }
            Assert.True(field.CompositionHandler?.IsAnimating, "precondition: the dots animate on the compositor");
            // GpuWarmup's own contract (AddActivity: "an animation the frame clock cannot see"), but nothing registers them,
            // so the idle fallback compiles in the dots' frames
            Assert.True(GpuWarmup.Animating(frames), "the warm-up saw nothing animating while the composer's dots pulse");
        }
        finally { window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    [Trait("Decision", "ED-252")] // a host the warm-up served must be collectable once it leaves its window
    public void REVIEW_a_detached_host_is_not_kept_alive_by_its_warm_up() => headless.OnUiThread(() =>
    {
        var panel = new StackPanel();
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100, Content = panel };
        window.Show();
        try
        {
            var weak = WarmAndDetach(panel);
            for (var k = 0; k < 3; k++)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            }
            // Driver.Run subscribes top.ScalingChanged with a lambda holding the host: the window keeps every model chip
            // it ever showed (one per composer) and re-runs a warm-up for each on every scaling change
            Assert.False(weak.IsAlive, "the detached host is still reachable after the GC");
        }
        finally { window.Close(); }
    });

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference WarmAndDetach(StackPanel panel)
    {
        var host = new Border { Width = 40, Height = 20 };
        panel.Children.Add(host);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        var asked = GpuWarmup.StepsAsked;
        GpuWarmup.WhenIdle(host);
        var since = DateTime.UtcNow;
        while (GpuWarmup.StepsAsked == asked && DateTime.UtcNow - since < TimeSpan.FromSeconds(10))
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(GpuWarmup.StepsAsked > asked, "precondition: the warm-up ran its first step");
        for (var k = 0; k < 5; k++) { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); } // the CPU answer ends it
        panel.Children.Remove(host);
        return new WeakReference(host);
    }

    [Fact]
    [Trait("Decision", "ED-252")] // an entry that cannot be moved over its file (held open) leaves no half-written file behind
    public void REVIEW_a_failed_cache_write_leaves_no_part_file()
    {
        var folderField = typeof(GpuProgramCache).GetField("folder", BindingFlags.NonPublic | BindingFlags.Static)!;
        var put = typeof(GpuProgramCache).GetMethod("Put", BindingFlags.NonPublic | BindingFlags.Static)!;
        var fileFor = typeof(GpuProgramCache).GetMethod("FileFor", BindingFlags.NonPublic | BindingFlags.Static)!;
        var saved = folderField.GetValue(null);
        var folder = Path.Combine(Path.GetTempPath(), "jewel-cache-review-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var key = Marshal.AllocHGlobal(8);
        var value = Marshal.AllocHGlobal(16);
        try
        {
            Marshal.Copy(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, 0, key, 8);
            Marshal.Copy(new byte[16], 0, value, 16);
            folderField.SetValue(null, folder);
            var file = (string)fileFor.Invoke(null, [key, (nint)8])!;
            File.WriteAllBytes(file, [9]);
            using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read)) // another instance reading it (ReadAllBytes' sharing)
                put.Invoke(null, [key, (nint)8, value, (nint)16]);
            Assert.Empty(Directory.EnumerateFiles(folder, "*.part")); // left behind for good: nothing ever cleans the folder
        }
        finally
        {
            folderField.SetValue(null, saved);
            Marshal.FreeHGlobal(key); Marshal.FreeHGlobal(value);
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    [Trait("Decision", "ED-252")] // OD-0096: the prepared state is invalid on "a new GPU or driver, or a new app build"
    public void REVIEW_is_warm_is_false_when_the_marker_was_made_on_a_gpu_or_driver_this_machine_does_not_have()
    {
        var root = Path.Combine(Path.GetTempPath(), "jewel-warm-review-" + Guid.NewGuid().ToString("N"));
        var gpu = Path.Combine(root, "0123456789abcdef01234567");
        GpuProgramCache.FolderOverride = root;
        try
        {
            Directory.CreateDirectory(gpu);
            // the folder GpuProgramCache.Install writes, for a GPU and driver that are not this machine's (after a driver
            // update the old driver's folder stays, full of programs, and warm.txt still names it)
            File.WriteAllText(Path.Combine(gpu, "key.txt"),
                "ANGLE (Review, No Such GPU Direct3D11 vs_5_0 ps_5_0, D3D11-0.0.0.1)\nOpenGL ES 3.0.0 (ANGLE review)\n" + GpuProgramCache.Build + "\n");
            File.WriteAllBytes(Path.Combine(gpu, "a.bin"), [1, 2, 3]);
            GpuProgramCache.MarkWarm(gpu);
            // IsWarm reads only the build and "a folder holding programs": the first-run screen would be skipped and the
            // four compiles land in the composer's idle fallback (the forced-cap hitches) after every driver update
            Assert.False(GpuWarmup.IsWarm, "warm for a GPU and driver this machine does not have");
        }
        finally
        {
            GpuProgramCache.FolderOverride = null;
            Directory.Delete(root, recursive: true);
        }
    }
}
