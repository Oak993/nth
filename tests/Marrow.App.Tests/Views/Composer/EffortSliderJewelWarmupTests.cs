using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

// OD-0090 / ED-252 (lead's ruling 2026-10-03, the owner's zero-stutter rule): the knob's programs are primed on the render
// thread before the effort popover can be opened, after the window's first frame at idle priority, never at attach.
// (The GPU compile itself is measured natively: EffortSliderNativeFrameTests.The_knob_warm_up_*.)
[Collection("Headless")]
public sealed class EffortSliderJewelWarmupTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-252")] // the primer is added only after the window's first frame, at idle: never at attach, never in startup's path
    public void The_warm_up_waits_for_the_windows_first_frame_and_idle() => headless.OnUiThread(() =>
    {
        var host = new Border { Width = 40, Height = 20 };
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100, Content = host };
        window.Show();
        try
        {
            GpuWarmup.WhenIdle(host);
            Assert.True(GpuWarmup.IsScheduled(host));
            Assert.Null(ElementComposition.GetElementChildVisual(host)); // nothing at the call
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
            Assert.Null(ElementComposition.GetElementChildVisual(host)); // nor before a frame was drawn
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();             // the first frame
            Dispatcher.UIThread.RunJobs();                               // then idle
            var primer = Assert.IsType<CompositionCustomVisual>(ElementComposition.GetElementChildVisual(host));
            Assert.Equal(new Vector(1, 1), primer.Size);                 // a pixel, drawing nothing on screen
            GpuWarmup.WhenIdle(host);                                   // again: nothing new
            Assert.Same(primer, ElementComposition.GetElementChildVisual(host));
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-252")] // the composer's model chip schedules it (it is what opens the effort popover)
    public void The_model_chip_schedules_the_warm_up() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness(withRuntime: false);
        var model = new AppModel();
        foreach (var provider in SwiftEnum<ProviderKind>.AllCases)
            if (!provider.IsAPIKeyBased) model.Settings.SetBinaryPath(Path.Combine(harness.Scratch, "missing.exe"), provider);
        var chip = new ModelEffortButton(new ChatThread(Guid.NewGuid(), ProviderKind.Claude, "opus", null, RuntimeMode.Supervised), false);
        Assert.True(GpuWarmup.IsScheduled(chip));
    });

    [Fact]
    [Trait("Decision", "ED-252")] // a warm-up step draws nothing where the knob will be drawn later, and leaves the knob's surfaces made
    public void Warm_up_steps_build_the_knobs_own_surfaces_and_draw_nothing_visible()
    {
        var renderer = JewelKnobRenderer.For(null, 96);
        for (var step = 0; step < GpuWarmup.Steps; step++) renderer.Warm(step);
        using var bitmap = new SKBitmap(new SKImageInfo(48, 48, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        var u = new JewelUniforms();
        new JewelOrb(JewelMaterial.For(EffortBrand.Claude)).Frame(0, 0, 0, Math.PI, false, 96, 2, true, u);
        var before = GC.GetAllocatedBytesForCurrentThread();
        renderer.Draw(canvas, u.Values, new SKRect(0, 0, 48, 48), 1); // the first real draw: nothing left to make
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(255, bitmap.GetPixel(24, 24).Alpha);
    }

    [Fact]
    [Trait("Decision", "ED-252")] // something animates while a frame-clock request waits, or a registered activity answers true
    public void The_warm_up_sees_animation_in_waiting_frame_requests_and_registered_activities() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        Assert.False(GpuWarmup.Animating());
        frames.RequestFrame(_ => { });
        Assert.True(GpuWarmup.Animating());
        frames.Advance(TimeSpan.FromMilliseconds(16));
        Assert.False(GpuWarmup.Animating());
        var spinning = true;
        using (GpuWarmup.AddActivity(() => spinning))
        {
            Assert.True(GpuWarmup.Animating());
            spinning = false;
            Assert.False(GpuWarmup.Animating());
            spinning = true;
        }
        Assert.False(GpuWarmup.Animating()); // removed
    });

    [Fact]
    [Trait("Decision", "ED-252")] // the idle fallback asks for no step while something animates, and steps once it stops
    public void The_idle_warm_up_waits_while_something_animates() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        var cap = GpuWarmup.IdleCap;
        GpuWarmup.IdleCap = TimeSpan.FromSeconds(60); // well past this test's frames: the cap must not be what ends the wait here
        var host = new Border { Width = 40, Height = 20 };
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100, Content = host };
        window.Show();
        try
        {
            frames.RequestFrame(_ => { }); // an animation: its next frame waits
            GpuWarmup.WhenIdle(host);
            var asked = GpuWarmup.StepsAsked;
            FramesFor(TimeSpan.FromMilliseconds(600), () => false);
            Assert.True(asked == GpuWarmup.StepsAsked, $"{GpuWarmup.StepsAsked - asked} steps asked while animating"); // all animating: no step
            frames.Advance(TimeSpan.FromMilliseconds(16)); // the animation ends
            FramesFor(TimeSpan.FromSeconds(10), () => GpuWarmup.StepsAsked > asked);
            Assert.True(asked + 1 == GpuWarmup.StepsAsked, $"{GpuWarmup.StepsAsked - asked} steps asked once quiet"); // the first step (the CPU renderer then ends it: nothing to compile)
        }
        finally { GpuWarmup.IdleCap = cap; window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-252")] // an animation that never stops does not keep the popover unready: after the cap it steps anyway
    public void The_idle_warm_up_steps_anyway_after_its_cap() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        var cap = GpuWarmup.IdleCap;
        GpuWarmup.IdleCap = TimeSpan.FromSeconds(1);
        var host = new Border { Width = 40, Height = 20 };
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100, Content = host };
        window.Show();
        try
        {
            frames.RequestFrame(_ => { }); // animating throughout
            var since = DateTime.UtcNow;
            GpuWarmup.WhenIdle(host);
            var asked = GpuWarmup.StepsAsked;
            FramesFor(TimeSpan.FromSeconds(20), () => GpuWarmup.StepsAsked > asked);
            var waited = (DateTime.UtcNow - since).TotalMilliseconds;
            Assert.True(asked + 1 == GpuWarmup.StepsAsked, $"{GpuWarmup.StepsAsked - asked} steps asked at the cap");
            Assert.True(waited >= 1000, $"stepped after {waited:F0} ms, before the cap ran out");
            Assert.True(GpuWarmup.Animating()); // still animating: forced
        }
        finally { GpuWarmup.IdleCap = cap; window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-252")] // WarmAsync (OD-0096's first-run screen awaits it): completes, at once on the CPU renderer, no step reported
    public void Warm_async_completes_on_the_cpu_renderer_with_no_steps() => headless.OnUiThread(() =>
    {
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100 };
        window.Show();
        try
        {
            var reports = new List<GpuWarmupStep>();
            var asked = GpuWarmup.StepsAsked;
            var task = GpuWarmup.WarmAsync(window, new SyncProgress<GpuWarmupStep>(reports.Add));
            FramesFor(TimeSpan.FromSeconds(10), () => task.IsCompleted);
            Assert.True(task.IsCompletedSuccessfully);
            Assert.Empty(reports);                         // nothing compiled: no step to report
            Assert.Equal(asked + 1, GpuWarmup.StepsAsked); // the first step asked, answered by the CPU renderer
            var again = GpuWarmup.WarmAsync(window);       // repeatable: the same answer
            FramesFor(TimeSpan.FromSeconds(10), () => again.IsCompleted);
            Assert.True(again.IsCompletedSuccessfully);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-252")] // IsWarm (OD-0096): this build's warm-up completed on this machine as it is now, its programs kept
    public void Is_warm_only_for_this_build_on_this_machine_with_an_entry_for_each_step()
    {
        var root = Path.Combine(Path.GetTempPath(), "jewel-warm-" + Guid.NewGuid().ToString("N"));
        var gpu = Path.Combine(root, "0123456789abcdef01234567");
        var source = GpuProgramCache.MachineSource;
        var machine = "10de:2704:00000000:a1 Test GPU 32.0.15.6094";
        GpuProgramCache.MachineSource = () => machine;
        GpuProgramCache.FolderOverride = root;
        void Key(string build, string? made) =>
            File.WriteAllText(Path.Combine(gpu, "key.txt"), "ANGLE (Test GPU)\nOpenGL ES 3.0.0\n" + build + "\n" + (made is null ? "" : made + "\n"));
        void Marker(params string[] lines) => File.WriteAllLines(Path.Combine(root, "warm.txt"), lines);
        try
        {
            Assert.False(GpuWarmup.IsWarm);                  // no folder
            Directory.CreateDirectory(gpu);
            Key(GpuProgramCache.Build, machine);
            GpuProgramCache.MarkWarm(gpu);
            Assert.False(GpuWarmup.IsWarm);                  // marked, but no program in it
            for (var k = 0; k < GpuWarmup.Steps - 1; k++) File.WriteAllBytes(Path.Combine(gpu, k + ".bin"), [1, 2, 3]);
            Assert.False(GpuWarmup.IsWarm);                  // fewer entries than the warm-up's steps
            File.WriteAllBytes(Path.Combine(gpu, "last.bin"), [1, 2, 3]);
            Assert.True(GpuWarmup.IsWarm);
            Assert.Equal([GpuProgramCache.Build, Path.GetFileName(gpu), machine], File.ReadAllLines(Path.Combine(root, "warm.txt")));

            machine = "10de:2704:00000000:a1 Test GPU 32.0.15.7000";
            Assert.False(GpuWarmup.IsWarm);                  // a driver update
            Key(GpuProgramCache.Build, machine);
            Assert.False(GpuWarmup.IsWarm);                  // the folder made again on it, but no warm-up completed since
            GpuProgramCache.MarkWarm(gpu);
            Assert.True(GpuWarmup.IsWarm);
            Key(GpuProgramCache.Build, null);
            Assert.False(GpuWarmup.IsWarm);                  // a key that names no machine
            Key("another build", machine);
            Assert.False(GpuWarmup.IsWarm);                  // a key of another build
            Key(GpuProgramCache.Build, machine);
            Assert.True(GpuWarmup.IsWarm);

            Marker("another build", Path.GetFileName(gpu), machine);
            Assert.False(GpuWarmup.IsWarm);                  // another build's marker
            Marker(GpuProgramCache.Build);
            Assert.False(GpuWarmup.IsWarm);                  // a short marker (read, not thrown)
            Marker(GpuProgramCache.Build, Path.GetFileName(gpu));
            Assert.False(GpuWarmup.IsWarm);                  // a marker with no machine
            Marker(GpuProgramCache.Build, Path.Combine("..", Path.GetFileName(root), Path.GetFileName(gpu)), machine);
            Assert.False(GpuWarmup.IsWarm);                  // a marker naming a path, not a folder of the cache
            GpuProgramCache.MarkWarm(Path.Combine(root, "fedcba9876543210fedcba98"));
            Assert.False(GpuWarmup.IsWarm);                  // another GPU's folder, not there
            GpuProgramCache.MarkWarm(gpu);
            Assert.True(GpuWarmup.IsWarm);
            GpuProgramCache.MachineSource = () => null;
            Assert.False(GpuWarmup.IsWarm);                  // the machine cannot be read: not proven
        }
        finally
        {
            GpuProgramCache.MachineSource = source;
            GpuProgramCache.FolderOverride = null;
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Decision", "ED-252")] // the machine IsWarm compares: this PC's graphics adapters and drivers, read the same way each time
    public void The_machine_names_its_graphics_adapters_and_their_drivers()
    {
        var machine = GpuProgramCache.Machine();
        Assert.False(string.IsNullOrEmpty(machine), "DXGI named no graphics adapter");
        Assert.Equal(machine, GpuProgramCache.Machine());
        foreach (var adapter in machine!.Split(" | "))
            Assert.Matches(@"^[0-9a-f]{4}:[0-9a-f]{4}:[0-9a-f]{8}:[0-9a-f]{2} \S.* (\d+\.\d+\.\d+\.\d+|no driver version)$", adapter);
        // the driver's version read (IDXGIAdapter::CheckInterfaceSupport, as ANGLE reads it): a driver update changes it
        Assert.Contains(machine.Split(" | "), adapter => System.Text.RegularExpressions.Regex.IsMatch(adapter, @" \d+\.\d+\.\d+\.\d+$"));
    }

    [Fact]
    [Trait("Decision", "ED-252")] // a completed warm-up leaves this build's folders made on this machine (another adapter's too), nothing older
    public void A_completed_warm_up_keeps_only_this_builds_folders_made_on_this_machine()
    {
        var root = Path.Combine(Path.GetTempPath(), "jewel-clean-" + Guid.NewGuid().ToString("N"));
        var source = GpuProgramCache.MachineSource;
        const string machine = "10de:2704:00000000:a1 Test GPU 32.0.15.6094 | 8086:a780:00000000:04 Test iGPU 31.0.101.5186";
        GpuProgramCache.MachineSource = () => machine;
        GpuProgramCache.FolderOverride = root;
        string Folder(string name, string? key)
        {
            var folder = Path.Combine(root, name);
            Directory.CreateDirectory(folder);
            if (key is not null) File.WriteAllText(Path.Combine(folder, "key.txt"), key);
            File.WriteAllBytes(Path.Combine(folder, "a.bin"), [1, 2, 3]);
            return folder;
        }
        try
        {
            static string Key(string renderer, string build, string made) => string.Join("\n", renderer, "OpenGL ES 3.0.0", build, made, "");
            var current = Folder("aaaaaaaaaaaaaaaaaaaaaaaa", Key("ANGLE (Test GPU)", GpuProgramCache.Build, machine));
            var otherAdapter = Folder("bbbbbbbbbbbbbbbbbbbbbbbb", Key("ANGLE (Test iGPU)", GpuProgramCache.Build, machine));
            var oldDriver = Folder("cccccccccccccccccccccccc", Key("ANGLE (Test GPU)", GpuProgramCache.Build, "10de:2704:00000000:a1 Test GPU 31.0.15.5222"));
            var oldBuild = Folder("dddddddddddddddddddddddd", Key("ANGLE (Test GPU)", "an earlier build", machine));
            var beingMade = Folder("eeeeeeeeeeeeeeeeeeeeeeee", null); // another instance installing: no key yet
            GpuProgramCache.MarkWarm(current);
            Assert.True(Directory.Exists(current));
            Assert.True(Directory.Exists(otherAdapter), "this build's folder for the machine's other adapter was removed");
            Assert.True(Directory.Exists(beingMade), "a folder still being made was removed");
            Assert.False(Directory.Exists(oldDriver), "the folder of a driver this machine no longer has is kept");
            Assert.False(Directory.Exists(oldBuild), "an earlier build's folder is kept");
        }
        finally
        {
            GpuProgramCache.MachineSource = source;
            GpuProgramCache.FolderOverride = null;
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Decision", "ED-252")] // ANGLE asks for an entry's size, then hands a buffer of that size: it gets the entry
    public void A_cache_entry_is_given_to_a_buffer_of_exactly_its_size()
    {
        using var cache = new CacheFolder();
        var entry = new byte[] { 7, 6, 5, 4, 3, 2, 1, 0, 9, 8 };
        File.WriteAllBytes(cache.FileFor(), entry);
        var hits = GpuProgramCache.Hits;
        Assert.Equal(entry.Length, cache.Take(buffer: false, 0));                // the size asked
        Assert.Equal(entry.Length, cache.Take(buffer: true, entry.Length - 1)); // too small: the size again, nothing copied
        Assert.Equal(hits, GpuProgramCache.Hits);
        Assert.Equal(entry.Length, cache.Take(buffer: true, entry.Length));     // exactly its size: copied
        Assert.Equal(entry, cache.Taken(entry.Length));
        Assert.Equal(hits + 1, GpuProgramCache.Hits);
    }

    [Fact]
    [Trait("Decision", "ED-252")] // a kept entry is replaced whole: another instance reading the old one never sees the new one written into it
    public void A_cache_entry_is_replaced_whole_never_written_in_place()
    {
        using var cache = new CacheFolder();
        var file = cache.FileFor();
        File.WriteAllBytes(file, [1, 1, 1, 1]);
        using (var reader = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            cache.Put([2, 2, 2, 2, 2, 2]);
            var seen = new byte[8];
            Assert.Equal(4, reader.Read(seen, 0, seen.Length));
            Assert.Equal([1, 1, 1, 1], seen[..4]);                     // the reader's entry is the old one, whole
        }
        Assert.Empty(Directory.EnumerateFiles(cache.Folder, "*.part"));
    }

    [Fact]
    [Trait("Decision", "ED-252")] // the cache is marked warm when the warm-up's steps all went through the cache, and only then
    public void A_completed_warm_up_marks_the_cache_warm_only_when_its_entries_cover_its_steps() => headless.OnUiThread(() =>
    {
        using var cache = new CacheFolder();
        var source = GpuProgramCache.MachineSource;
        GpuProgramCache.MachineSource = () => "10de:2704:00000000:a1 Test GPU 1.2.3.4";
        GpuWarmup.PretendGpu = true;
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100 };
        window.Show();
        try
        {
            var marker = Path.Combine(cache.Root, "warm.txt");
            var reports = new List<GpuWarmupStep>();
            var task = GpuWarmup.WarmAsync(window, new SyncProgress<GpuWarmupStep>(step => { reports.Add(step); Interlocked.Increment(ref GpuProgramCache.Writes); }));
            FramesFor(TimeSpan.FromSeconds(10), () => task.IsCompleted);
            Assert.True(task.IsCompletedSuccessfully, task.Exception?.InnerException?.Message);
            Assert.Equal(GpuWarmup.Programs, reports.Select(r => r.Program));
            Assert.All(reports, r => Assert.False(r.Forced));
            Assert.Equal(Path.GetFileName(cache.Folder), File.ReadAllLines(marker)[1]); // each step wrote an entry: warm

            File.Delete(marker);
            var again = GpuWarmup.WarmAsync(window, new SyncProgress<GpuWarmupStep>(step => reports.Add(step)));
            FramesFor(TimeSpan.FromSeconds(10), () => again.IsCompleted);
            Assert.True(again.IsCompletedSuccessfully, again.Exception?.InnerException?.Message);
            Assert.Equal(2 * GpuWarmup.Steps, reports.Count);
            Assert.False(File.Exists(marker));                   // nothing went through the cache: not proven warm
        }
        finally { GpuWarmup.PretendGpu = false; GpuProgramCache.MachineSource = source; window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-252")] // the cap counts from the warm-up's start: once it ran out, the steps follow one a frame, not a cap each
    public void The_idle_warm_up_counts_its_cap_from_its_start() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        var cap = GpuWarmup.IdleCap;
        GpuWarmup.IdleCap = TimeSpan.FromMilliseconds(500);
        GpuWarmup.PretendGpu = true;
        var host = new Border { Width = 40, Height = 20 };
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100, Content = host };
        window.Show();
        try
        {
            frames.RequestFrame(_ => { }); // animating throughout
            var done = GpuWarmup.StepsDone;
            var asked = new List<DateTime>();
            var since = DateTime.UtcNow;
            GpuWarmup.WhenIdle(host);
            var seen = GpuWarmup.StepsAsked;
            FramesFor(TimeSpan.FromSeconds(10), () =>
            {
                for (; seen < GpuWarmup.StepsAsked; seen++) asked.Add(DateTime.UtcNow);
                return GpuWarmup.StepsDone - done == GpuWarmup.Steps;
            });
            Assert.Equal(GpuWarmup.Steps, GpuWarmup.StepsDone - done);
            Assert.Equal(GpuWarmup.Steps, asked.Count);
            Assert.True((asked[0] - since).TotalMilliseconds >= 500, $"the first step {(asked[0] - since).TotalMilliseconds:F0} ms in, before the cap");
            var rest = (asked[^1] - asked[0]).TotalMilliseconds;
            Assert.True(rest < 450, $"the last three steps took {rest:F0} ms: a cap each, not one a frame");
            Assert.All(GpuWarmup.Done.TakeLast(GpuWarmup.Steps), step => Assert.True(step.Forced));
        }
        finally { GpuWarmup.IdleCap = cap; GpuWarmup.PretendGpu = false; window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-252")] // a quiet frame is two frames in a row with nothing animating: one calm frame between two moving ones is not
    public void The_idle_warm_up_waits_for_two_calm_frames_in_a_row() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        var cap = GpuWarmup.IdleCap;
        GpuWarmup.IdleCap = TimeSpan.FromSeconds(60);
        var host = new Border { Width = 40, Height = 20 };
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100, Content = host };
        window.Show();
        var checks = 0;
        var flicker = GpuWarmup.AddActivity(() => ++checks % 2 == 1); // moving, calm, moving, calm ...
        try
        {
            GpuWarmup.WhenIdle(host);
            var asked = GpuWarmup.StepsAsked;
            FramesFor(TimeSpan.FromSeconds(1.5), () => false);
            Assert.True(checks >= 6, $"precondition: the warm-up checked {checks} frames");
            Assert.True(asked == GpuWarmup.StepsAsked, $"{GpuWarmup.StepsAsked - asked} steps asked after a single calm frame");
            flicker.Dispose();
            FramesFor(TimeSpan.FromSeconds(10), () => GpuWarmup.StepsAsked > asked);
            Assert.True(GpuWarmup.StepsAsked > asked, "no step once calm");
        }
        finally { flicker.Dispose(); GpuWarmup.IdleCap = cap; window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-252")] // while it waits for quiet frames the idle fallback leaves the UI thread free: no pass of the dispatcher runs on
    public void The_idle_warm_up_leaves_the_dispatcher_free_between_its_checks() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        var cap = GpuWarmup.IdleCap;
        GpuWarmup.IdleCap = TimeSpan.FromSeconds(3);
        var host = new Border { Width = 40, Height = 20 };
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100, Content = host };
        window.Show();
        try
        {
            frames.RequestFrame(_ => { }); // animating: it keeps checking frame after frame
            GpuWarmup.WhenIdle(host);
            var asked = GpuWarmup.StepsAsked;
            var longest = TimeSpan.Zero;
            var since = DateTime.UtcNow;
            while (DateTime.UtcNow - since < TimeSpan.FromSeconds(1.5))
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var pass = System.Diagnostics.Stopwatch.StartNew();
                Dispatcher.UIThread.RunJobs();
                if (pass.Elapsed > longest) longest = pass.Elapsed;
                Thread.Sleep(10);
            }
            Assert.Equal(asked, GpuWarmup.StepsAsked); // still waiting (animating, the cap not reached)
            Assert.True(longest < TimeSpan.FromMilliseconds(250), $"one pass of the dispatcher's jobs took {longest.TotalMilliseconds:F0} ms");
        }
        finally { GpuWarmup.IdleCap = cap; window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-252")] // a window that closes before the step: WarmAsync fails at once and asks the closed window nothing
    public void Warm_async_on_a_window_that_closed_asks_no_step() => headless.OnUiThread(() =>
    {
        var timeout = GpuWarmup.StepTimeout;
        GpuWarmup.StepTimeout = TimeSpan.FromSeconds(1);
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100 };
        window.Show();
        try
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            var asked = GpuWarmup.StepsAsked;
            var task = GpuWarmup.WarmAsync(window);
            window.Close();
            FramesFor(TimeSpan.FromSeconds(5), () => task.IsCompleted);
            Assert.True(task.IsFaulted, "WarmAsync did not fail on a closed window");
            Assert.IsType<InvalidOperationException>(task.Exception!.InnerException);
            Assert.Equal(asked, GpuWarmup.StepsAsked);
        }
        finally { GpuWarmup.StepTimeout = timeout; window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-252")] // a window never shown has nothing to draw a step in: WarmAsync fails, it does not report prepared
    public void Warm_async_on_a_window_never_shown_fails() => headless.OnUiThread(() =>
    {
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100 };
        var task = GpuWarmup.WarmAsync(window);
        Assert.True(task.IsFaulted);
        Assert.IsType<InvalidOperationException>(task.Exception!.InnerException);
    });

    [Fact]
    [Trait("Decision", "ED-252")] // after a give-up, one frame (a restore's last frame, a flicker) is not a window drawn again: it steps once drawn steadily
    public void After_a_give_up_the_idle_warm_up_steps_only_once_its_host_is_drawn_steadily() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        var (cap, timeout) = (GpuWarmup.IdleCap, GpuWarmup.StepTimeout);
        GpuWarmup.IdleCap = TimeSpan.FromMilliseconds(300);
        GpuWarmup.StepTimeout = TimeSpan.FromMilliseconds(300);
        var host = new Border { Width = 40, Height = 20 };
        var holder = new Border { IsVisible = false, Child = host };
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100, Content = holder };
        window.Show();
        try
        {
            GpuWarmup.WhenIdle(host);
            var asked = GpuWarmup.StepsAsked;
            Pump(TimeSpan.FromSeconds(2), () => false, render: true);   // the host hidden: its step is given up on
            Assert.True(GpuWarmup.StepsAsked == asked + 1, $"precondition: one step asked and given up on ({GpuWarmup.StepsAsked - asked} asked)");
            holder.IsVisible = true;                                         // drawn in one frame
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            holder.IsVisible = false;                                        // and hidden again
            Dispatcher.UIThread.RunJobs();
            Pump(TimeSpan.FromSeconds(2), () => false, render: true);
            Assert.True(GpuWarmup.StepsAsked == asked + 1, $"{GpuWarmup.StepsAsked - asked - 1} steps asked on a single frame");
            holder.IsVisible = true;
            Pump(TimeSpan.FromSeconds(5), () => GpuWarmup.StepsAsked > asked + 1, render: true);
            Assert.True(GpuWarmup.StepsAsked > asked + 1, "the warm-up never resumed once its host was drawn steadily");
        }
        finally { (GpuWarmup.IdleCap, GpuWarmup.StepTimeout) = (cap, timeout); window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-252")] // a step given up on is withdrawn: drawn later (the window shown again) it compiles nothing then
    public void A_step_given_up_on_compiles_nothing_when_drawn_later() => headless.OnUiThread(() =>
    {
        var timeout = GpuWarmup.StepTimeout;
        GpuWarmup.StepTimeout = TimeSpan.FromMilliseconds(300);
        GpuWarmup.PretendGpu = true;
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100 };
        window.Show();
        try
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            var (asked, done) = (GpuWarmup.StepsAsked, GpuWarmup.StepsDone);
            var task = GpuWarmup.WarmAsync(window);
            Pump(TimeSpan.FromSeconds(5), () => task.IsCompleted, render: false); // not drawn
            Assert.IsType<TimeoutException>(task.Exception?.InnerException);
            Assert.Equal(asked + 1, GpuWarmup.StepsAsked);
            Pump(TimeSpan.FromMilliseconds(500), () => false, render: true);       // drawn again
            Assert.Equal(done, GpuWarmup.StepsDone);                               // its compile not made in that frame
            Assert.Equal(asked + 1, GpuWarmup.StepsAsked);                         // and WarmAsync's caller decides: no step of its own
        }
        finally { GpuWarmup.StepTimeout = timeout; GpuWarmup.PretendGpu = false; window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-252")] // OD-0096's screen calling WarmAsync while the idle fallback waits: it steps now and reports each step
    public void Warm_async_hurries_an_idle_warm_up_already_running() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        var cap = GpuWarmup.IdleCap;
        GpuWarmup.IdleCap = TimeSpan.FromSeconds(60);
        GpuWarmup.PretendGpu = true;
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100 };
        window.Show();
        try
        {
            frames.RequestFrame(_ => { }); // animating throughout: the idle fallback waits
            GpuWarmup.WhenIdle(window);
            var asked = GpuWarmup.StepsAsked;
            FramesFor(TimeSpan.FromMilliseconds(500), () => false);
            Assert.Equal(asked, GpuWarmup.StepsAsked);
            var reports = new List<GpuWarmupStep>();
            var task = GpuWarmup.WarmAsync(window, new SyncProgress<GpuWarmupStep>(reports.Add));
            FramesFor(TimeSpan.FromSeconds(5), () => task.IsCompleted);
            Assert.True(task.IsCompletedSuccessfully, task.Exception?.InnerException?.Message);
            Assert.Equal(GpuWarmup.Programs, reports.Select(r => r.Program));
            Assert.All(reports, r => Assert.False(r.Forced));
        }
        finally { GpuWarmup.IdleCap = cap; GpuWarmup.PretendGpu = false; window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-252")] // the working dots count only while they are in a window: once removed, nothing stays registered
    public void The_working_dots_stop_counting_once_removed() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var executor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var field = new DotFieldControl(new SwiftColor(1), true, false);
        var window = new Avalonia.Controls.Window { Width = 32, Height = 24, Content = field };
        window.Show();
        try
        {
            runtime.Advance(TimeSpan.FromSeconds(0.32));
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Assert.True(GpuWarmup.Animating(frames), "precondition: the dots count while they move");
            window.Content = null;
            Dispatcher.UIThread.RunJobs();
            Assert.False(GpuWarmup.Animating(frames), "the removed dots still hold the warm-up back");
            window.Content = field;
            for (var k = 0; k < 20 && !GpuWarmup.Animating(frames); k++) // the compositor's frames: the dots start again on the render server
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
            }
            Assert.True(GpuWarmup.Animating(frames), "the dots back in the window are not seen");
        }
        finally { window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    // a cache folder of its own, with the cache's private entry points (ANGLE's callbacks) at hand
    private sealed class CacheFolder : IDisposable
    {
        private static readonly System.Reflection.FieldInfo folderField = typeof(GpuProgramCache).GetField("folder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        private readonly object? saved = folderField.GetValue(null);
        private readonly nint key = System.Runtime.InteropServices.Marshal.AllocHGlobal(8), value = System.Runtime.InteropServices.Marshal.AllocHGlobal(64);
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "jewel-cache-" + Guid.NewGuid().ToString("N"));
        public string Folder => Path.Combine(Root, "0123456789abcdef01234567");

        public CacheFolder()
        {
            Directory.CreateDirectory(Folder);
            System.Runtime.InteropServices.Marshal.Copy(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, 0, key, 8);
            System.Runtime.InteropServices.Marshal.Copy(new byte[64], 0, value, 64);
            folderField.SetValue(null, Folder);
            GpuProgramCache.FolderOverride = Root;
        }

        private static object? Call(string name, params object[] args) =>
            typeof(GpuProgramCache).GetMethod(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, args);

        public string FileFor() => (string)Call("FileFor", key, (nint)8)!;
        public nint Take(bool buffer, int size) => (nint)Call("Take", key, (nint)8, buffer ? value : (nint)0, (nint)size)!;
        public byte[] Taken(int count) { var bytes = new byte[count]; System.Runtime.InteropServices.Marshal.Copy(value, bytes, 0, count); return bytes; }

        public void Put(byte[] bytes)
        {
            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, value, bytes.Length);
            Call("Put", key, (nint)8, value, (nint)bytes.Length);
        }

        public void Dispose()
        {
            folderField.SetValue(null, saved);
            GpuProgramCache.FolderOverride = null;
            System.Runtime.InteropServices.Marshal.FreeHGlobal(key);
            System.Runtime.InteropServices.Marshal.FreeHGlobal(value);
            Directory.Delete(Root, recursive: true);
        }
    }

    // frames (or none) about every 10 ms until done or the time is up, the dispatcher's jobs run between
    private static void Pump(TimeSpan span, Func<bool> done, bool render)
    {
        var since = DateTime.UtcNow;
        while (!done() && DateTime.UtcNow - since < span)
        {
            if (render) AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
            Dispatcher.UIThread.RunJobs();
        }
    }

    // frames about every 10 ms (the warm-up's checks take a timer's turn between frames) until done or the time is up
    private static void FramesFor(TimeSpan span, Func<bool> done)
    {
        var since = DateTime.UtcNow;
        while (!done() && DateTime.UtcNow - since < span)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
            Dispatcher.UIThread.RunJobs();
        }
    }
}
