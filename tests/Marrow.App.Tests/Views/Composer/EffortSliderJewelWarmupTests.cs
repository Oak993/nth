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
    [Trait("Decision", "ED-252")] // the chip calls WhenIdle as it is made, before it is in a window: it warms once it is in one, after its first frame
    public void A_host_given_before_it_is_in_a_window_warms_once_it_is() => headless.OnUiThread(() =>
    {
        var host = new Border { Width = 40, Height = 20 };
        GpuWarmup.WhenIdle(host);
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100, Content = host };
        window.Show();
        try
        {
            Assert.Null(ElementComposition.GetElementChildVisual(host)); // nothing at the attach
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();             // the first frame
            Dispatcher.UIThread.RunJobs();                               // then idle
            Assert.IsType<CompositionCustomVisual>(ElementComposition.GetElementChildVisual(host));
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
            for (var k = 0; k < GpuWarmup.Families - 1; k++) File.WriteAllBytes(Path.Combine(gpu, k + ".bin"), [1, 2, 3]);
            Assert.False(GpuWarmup.IsWarm);                  // fewer entries than the warm-up's families (ED-NEW-warmupsplit-1: a family's parts make its programs)
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
        using var cache = new GpuProgramCacheFolder();
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
        using var cache = new GpuProgramCacheFolder();
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
        using var cache = new GpuProgramCacheFolder();
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
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
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
            var asked = new List<TimeSpan>();
            var since = clock.Elapsed;
            GpuWarmup.WhenIdle(host); // its cap read on the manual clock (ED-NEW-handoverfix-4): a frame is 16 ms of it, however long the machine takes
            var seen = GpuWarmup.StepsAsked;
            ClockedFrames(clock, TimeSpan.FromSeconds(10), () =>
            {
                for (; seen < GpuWarmup.StepsAsked; seen++) asked.Add(clock.Elapsed);
                return GpuWarmup.StepsDone - done == GpuWarmup.Steps;
            });
            Assert.Equal(GpuWarmup.Steps, GpuWarmup.StepsDone - done);
            Assert.Equal(GpuWarmup.Steps, asked.Count);
            Assert.True((asked[0] - since).TotalMilliseconds >= 500, $"the first step {(asked[0] - since).TotalMilliseconds:F0} ms in, before the cap");
            // after the cap, one a frame, not a cap each: every gap a few 16 ms frames (the pass's 9 steps once had 450 ms for their
            // 8 gaps, 56 ms a gap; ED-NEW-warmupsplit-1 draws more, smaller steps, each gap held under 50 ms)
            var gaps = asked.Zip(asked.Skip(1), (a, b) => (b - a).TotalMilliseconds).ToArray();
            Assert.True(gaps.Max() < 50, $"a gap of {gaps.Max():F0} ms between steps after the cap: a cap each, not one a frame");
            Assert.All(GpuWarmup.Done.TakeLast(GpuWarmup.Steps), step => Assert.True(step.Forced)); // animating at the cap throughout
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
    [Trait("Decision", "ED-252")] // a new scaling warms again, in quiet frames like the fallback (also after WarmAsync: no screen is showing then)
    public void A_scaling_change_warms_again_in_quiet_frames_only() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        var cap = GpuWarmup.IdleCap;
        GpuWarmup.IdleCap = TimeSpan.FromSeconds(60);
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100 };
        window.Show();
        try
        {
            var task = GpuWarmup.WarmAsync(window);
            FramesFor(TimeSpan.FromSeconds(10), () => task.IsCompleted);
            Assert.True(task.IsCompletedSuccessfully);
            frames.RequestFrame(_ => { }); // animating
            var asked = GpuWarmup.StepsAsked;
            // the platform's report of a new scaling (headless windows do not change theirs): TopLevel raises ScalingChanged
            var report = (Action<double>)typeof(Avalonia.Platform.ITopLevelImpl).GetProperty("ScalingChanged")!.GetValue(window.PlatformImpl)!;
            report(1.5);
            FramesFor(TimeSpan.FromMilliseconds(600), () => false);
            Assert.True(asked == GpuWarmup.StepsAsked, $"{GpuWarmup.StepsAsked - asked} steps asked at once while animating");
            frames.Advance(TimeSpan.FromMilliseconds(16)); // quiet
            FramesFor(TimeSpan.FromSeconds(10), () => GpuWarmup.StepsAsked > asked);
            Assert.True(GpuWarmup.StepsAsked > asked, "no warm-up for the new scaling");
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
            var done = GpuWarmup.StepsDone;
            var task = GpuWarmup.WarmAsync(window, new SyncProgress<GpuWarmupStep>(reports.Add));
            // hurried: it steps at once, the idle run first, then the first run's own pass, all of ED-NEW-warmupsplit-1's smaller
            // parts within 5 s, well within the 60 s cap a run still waiting for idle frames would need
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
        var registered = GpuWarmup.ActivityCount;
        window.Show();
        try
        {
            runtime.Advance(TimeSpan.FromSeconds(0.32));
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Assert.True(GpuWarmup.Animating(frames), "precondition: the dots count while they move");
            Assert.Equal(registered + 1, GpuWarmup.ActivityCount);
            window.Content = null;
            Dispatcher.UIThread.RunJobs();
            Assert.False(GpuWarmup.Animating(frames), "the removed dots still hold the warm-up back");
            Assert.Equal(registered, GpuWarmup.ActivityCount); // nothing kept: the static list would hold every dot field ever shown
            window.Content = field;
            for (var k = 0; k < 20 && !GpuWarmup.Animating(frames); k++) // the compositor's frames: the dots start again on the render server
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
            }
            Assert.True(GpuWarmup.Animating(frames), "the dots back in the window are not seen");
            Assert.Equal(registered + 1, GpuWarmup.ActivityCount);
        }
        finally { window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs(); }
        Assert.Equal(registered, GpuWarmup.ActivityCount);
    });

    [Fact]
    [Trait("Decision", "ED-252")] // OD-0096: a change of the GPU the app draws with, or of its driver, shows the screen again, and nothing else does
    public void The_machine_is_the_adapter_the_app_draws_with_alone_with_its_driver()
    {
        var machine = GpuProgramCache.Machine();
        var adapters = GpuProgramCache.Adapters();
        Assert.False(string.IsNullOrEmpty(machine), "DXGI named no adapter drawn with");
        Assert.True(adapters is { Count: > 0 }, "DXGI named no graphics adapter");
        Assert.DoesNotContain(" | ", machine, StringComparison.Ordinal);                 // one adapter: another adapter's driver update changes nothing
        Assert.Equal(adapters![0], machine);                   // DXGI's first (an x64 process: Avalonia's ANGLE draws with adapter 0)
        Assert.Matches(@" (\d+\.\d+\.\d+\.\d+|no driver version)$", machine);
    }

    [Fact]
    [Trait("Decision", "ED-252")] // the adapter drawn with, picked as Avalonia 12.1.2's AngleWin32EglDisplay picks it
    public void The_adapter_drawn_with_is_picked_as_avalonia_picks_it()
    {
        Assert.Equal(0, GpuProgramCache.Drawn(["AMD Radeon(TM) Graphics", "NVIDIA GeForce RTX 4080"], arm64: false));     // DXGI's first
        Assert.Equal(0, GpuProgramCache.Drawn(["Qualcomm(R) Adreno(TM) X1-85 GPU", "Microsoft Basic Render Driver"], arm64: false));
        Assert.Equal(1, GpuProgramCache.Drawn(["Qualcomm(R) Adreno(TM) X1-85 GPU", "Microsoft Basic Render Driver"], arm64: true)); // ARM64: the Adreno blocklist
        Assert.Equal(0, GpuProgramCache.Drawn(["Qualcomm(R) Adreno(TM) X1-85 GPU"], arm64: true));                        // the only one
        Assert.Equal(0, GpuProgramCache.Drawn(["NVIDIA GeForce RTX 4080", "Qualcomm(R) Adreno(TM) X1-85 GPU"], arm64: true));
        Assert.Equal(0, GpuProgramCache.Drawn(["Qualcomm Adreno A", "Qualcomm ADRENO B"], arm64: true));                  // all Adreno: the first
        Assert.Equal(1, GpuProgramCache.Drawn(["Qualcomm Adreno A", null], arm64: true));                                  // no description: not an Adreno
    }

    [Fact]
    [Trait("Decision", "ED-252")] // the clean-up keeps a folder made on the machine's other adapter as it is now, and removes its old drivers'
    public void A_completed_warm_up_keeps_the_folders_of_the_machines_other_adapter_as_it_is_now()
    {
        var root = Path.Combine(Path.GetTempPath(), "jewel-clean-adapters-" + Guid.NewGuid().ToString("N"));
        var (source, adapters) = (GpuProgramCache.MachineSource, GpuProgramCache.AdaptersSource);
        const string gpu = "10de:2704:13bc196e:a1 NVIDIA GeForce RTX 4080 32.0.15.6094", igpu = "1002:13c0:d0001458:cb AMD Radeon(TM) Graphics 31.0.24033.1003";
        GpuProgramCache.MachineSource = () => gpu;              // the app draws with the RTX 4080
        GpuProgramCache.AdaptersSource = () => [igpu, gpu];
        GpuProgramCache.FolderOverride = root;
        string Folder(string name, string made)
        {
            var folder = Path.Combine(root, name);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "key.txt"), string.Join("\n", "ANGLE (Test)", "OpenGL ES 3.0.0", GpuProgramCache.Build, made, ""));
            File.WriteAllBytes(Path.Combine(folder, "a.bin"), [1, 2, 3]);
            return folder;
        }
        try
        {
            var current = Folder("aaaaaaaaaaaaaaaaaaaaaaaa", gpu);
            var otherAdapter = Folder("bbbbbbbbbbbbbbbbbbbbbbbb", igpu); // made when the app drew with the iGPU
            var otherOldDriver = Folder("cccccccccccccccccccccccc", "1002:13c0:d0001458:cb AMD Radeon(TM) Graphics 31.0.21000.1000");
            var oldDriver = Folder("dddddddddddddddddddddddd", "10de:2704:13bc196e:a1 NVIDIA GeForce RTX 4080 31.0.15.5222");
            GpuProgramCache.MarkWarm(current);
            Assert.True(Directory.Exists(current));
            Assert.True(Directory.Exists(otherAdapter), "the folder made on the machine's other adapter, as it is now, was removed");
            Assert.False(Directory.Exists(otherOldDriver), "the other adapter's folder of a driver it no longer has is kept");
            Assert.False(Directory.Exists(oldDriver), "the folder of a driver the adapter drawn with no longer has is kept");
        }
        finally
        {
            (GpuProgramCache.MachineSource, GpuProgramCache.AdaptersSource) = (source, adapters);
            GpuProgramCache.FolderOverride = null;
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Decision", "ED-252")] // the key names the adapter as the install read it, the marker as the warm-up's end read it: the app moved between them is not warm
    public void Is_warm_is_false_when_the_folders_key_names_another_adapter_than_the_marker()
    {
        var root = Path.Combine(Path.GetTempPath(), "jewel-warm-key-" + Guid.NewGuid().ToString("N"));
        var gpu = Path.Combine(root, "0123456789abcdef01234567");
        var source = GpuProgramCache.MachineSource;
        const string machine = "10de:2704:13bc196e:a1 NVIDIA GeForce RTX 4080 32.0.15.6094";
        GpuProgramCache.MachineSource = () => machine;
        GpuProgramCache.FolderOverride = root;
        void Key(string made) => File.WriteAllText(Path.Combine(gpu, "key.txt"), string.Join("\n", "ANGLE (Test)", "OpenGL ES 3.0.0", GpuProgramCache.Build, made, ""));
        try
        {
            Directory.CreateDirectory(gpu);
            for (var k = 0; k < GpuWarmup.Steps; k++) File.WriteAllBytes(Path.Combine(gpu, k + ".bin"), [1, 2, 3]);
            File.WriteAllLines(Path.Combine(root, "warm.txt"), [GpuProgramCache.Build, Path.GetFileName(gpu), machine]);
            // installed when the pass began on the iGPU (DXGI's adapter 0 then), marked when it ended on the RTX 4080 (its
            // adapter 0 now): the programs kept are the iGPU's
            Key("1002:13c0:d0001458:cb AMD Radeon(TM) Graphics 31.0.24033.1003");
            Assert.False(GpuWarmup.IsWarm, "warm over a folder of programs made on another adapter");
            Key(machine);
            Assert.True(GpuWarmup.IsWarm);
        }
        finally
        {
            GpuProgramCache.MachineSource = source;
            GpuProgramCache.FolderOverride = null;
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Decision", "ED-252")] // the callbacks are installed once per GPU context: again on a new one (a device lost, a display change)
    public void The_cache_is_installed_once_per_gpu_context()
    {
        var installer = GpuProgramCache.Installer;
        var installs = 0;
        GpuProgramCache.Installer = () => { installs++; return "installed (test)"; };
        try
        {
            GpuProgramCache.EnsureInstalled(0x7E570001);
            GpuProgramCache.EnsureInstalled(0x7E570001);
            Assert.Equal(1, installs);
            Assert.Equal("installed (test)", GpuProgramCache.State);
            GpuProgramCache.EnsureInstalled(0x7E570002);
            Assert.Equal(2, installs);                            // a new context: installed on it
            GpuProgramCache.EnsureInstalled(0x7E570002);
            Assert.Equal(2, installs);
        }
        finally
        {
            GpuProgramCache.Installer = () => "not tried";
            GpuProgramCache.EnsureInstalled(0);                   // the state before: no context, not tried
            GpuProgramCache.Installer = installer;
        }
    }

    [Fact]
    [Trait("Decision", "ED-252")] // the entries written count (a warm-up marks warm only when its steps went through the cache), a failed one does not
    public void A_cache_entry_written_is_counted_and_a_failed_one_is_not()
    {
        using var cache = new GpuProgramCacheFolder();
        var writes = GpuProgramCache.Writes;
        cache.Put([1, 2, 3]);
        Assert.Equal(writes + 1, GpuProgramCache.Writes);
        using (new FileStream(cache.FileFor(), FileMode.Open, FileAccess.Read, FileShare.Read)) // held by another instance: the move fails
            cache.Put([4, 5, 6]);
        Assert.Equal(writes + 1, GpuProgramCache.Writes);
    }

    [Fact]
    [Trait("Decision", "ED-252")] // a file written whole (an entry, a key) leaves no part beside it when its move fails, and says so
    public void A_file_replaced_whole_leaves_no_part_when_its_move_fails()
    {
        var folder = Path.Combine(Path.GetTempPath(), "jewel-whole-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "key.txt");
        try
        {
            File.WriteAllText(file, "old");
            using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read)) // another instance reading it
                Assert.ThrowsAny<Exception>(() => GpuProgramCache.ReplaceWhole(file, [1, 2, 3]));
            Assert.Empty(Directory.EnumerateFiles(folder, "*.part"));
            Assert.Equal("old", File.ReadAllText(file));
            GpuProgramCache.ReplaceWhole(file, "new"u8.ToArray());
            Assert.Equal("new", File.ReadAllText(file));
            Assert.Empty(Directory.EnumerateFiles(folder, "*.part"));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    [Trait("Decision", "ED-252")] // OD-0096: a new app build shows the screen again, and a release that patches only Skia or ANGLE is one
    public void The_build_names_the_skia_and_angle_libraries_the_programs_are_made_with()
    {
        var module = typeof(GpuProgramCache).Assembly.ManifestModule.ModuleVersionId.ToString("N");
        string[] natives = ["libSkiaSharp.dll 3.119.0.0 11264000", "av_libglesv2.dll 2.1.25000.0 5120000"];
        Assert.StartsWith(module + "-", GpuProgramCache.BuildOf(module, natives), StringComparison.Ordinal);
        Assert.Equal(GpuProgramCache.BuildOf(module, natives), GpuProgramCache.BuildOf(module, natives));
        Assert.NotEqual(GpuProgramCache.BuildOf(module, natives), GpuProgramCache.BuildOf(module, ["libSkiaSharp.dll 3.119.1.0 11264000", natives[1]]));
        Assert.NotEqual(GpuProgramCache.BuildOf(module, natives), GpuProgramCache.BuildOf(module, [natives[0], "av_libglesv2.dll 2.1.25000.0 5120512"]));
        Assert.NotEqual(GpuProgramCache.BuildOf(module, natives), GpuProgramCache.BuildOf(Guid.NewGuid().ToString("N"), natives));
        // this build's: the libraries as installed beside it (here under runtimes\<rid>\native), each found with its version
        // as its file says it ("no version" where it carries none: libSkiaSharp) and its size
        var installed = GpuProgramCache.NativeLibraries.Select(GpuProgramCache.Installed).ToList();
        Assert.All(installed, line => Assert.Matches(@"^\S+\.dll .+ \d+$", line));
        Assert.Equal(GpuProgramCache.BuildOf(module, installed), GpuProgramCache.Build);
    }

    [Fact]
    [Trait("Decision", "ED-252")] // a release that patches only Skia or ANGLE is a new build: each library is named by its own file's version and size
    public void Each_library_the_programs_are_made_with_is_named_by_its_files_version_and_size()
    {
        // found where the process loads it from, read here independently: beside the tests, or under runtimes\<rid>\native
        var rid = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
        var versions = 0;
        foreach (var name in GpuProgramCache.NativeLibraries)
        {
            var file = new[] { AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native") }
                .Select(folder => new FileInfo(Path.Combine(folder, name))).FirstOrDefault(found => found.Exists);
            Assert.True(file is not null, $"precondition: {name} is neither beside the tests nor under runtimes\\{rid}\\native");
            var installed = GpuProgramCache.Installed(name);
            Assert.StartsWith(name + " ", installed, StringComparison.Ordinal);
            Assert.EndsWith(" " + file!.Length, installed, StringComparison.Ordinal); // its size (libSkiaSharp: the only tell of a patch)
            if (System.Diagnostics.FileVersionInfo.GetVersionInfo(file.FullName).FileVersion is { } version)
            {
                versions++;
                Assert.Equal(name + " " + version + " " + file.Length, installed);  // and its version, as the file states it
            }
        }
        Assert.True(versions > 0, "precondition: no library carries a version (ANGLE's does)");
    }

    [Fact]
    [Trait("Decision", "ED-252")] // the clean-up keeps the folders made on any adapter the machine has: every one DXGI enumerates and describes is named
    public void Every_adapter_dxgi_enumerates_is_named_with_its_ids_description_and_driver()
    {
        // DXGI read through the runtime's COM interop (DxgiAdapterList), not the cache's own vtable reading: in DXGI's order,
        // each "vendor:device:subsystem:revision description driver" (ED-252), the driver as four 16-bit parts
        var dxgi = DxgiAdapterList.Read();
        Assert.True(dxgi.Count > 0, "precondition: DXGI enumerated no adapter");
        var expected = dxgi.Select(adapter => $"{adapter.Vendor:x4}:{adapter.Device:x4}:{adapter.SubSystem:x8}:{adapter.Revision:x2} {adapter.Description} "
            + (adapter.Driver is { } umd ? $"{(umd >> 48) & 0xFFFF}.{(umd >> 32) & 0xFFFF}.{(umd >> 16) & 0xFFFF}.{umd & 0xFFFF}" : "no driver version")).ToList();
        Assert.Equal(expected, GpuProgramCache.Adapters());
        if (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.Arm64)
            Assert.Equal(expected[0], GpuProgramCache.Machine()); // and the one drawn with: DXGI's first (Avalonia 12.1.2's pick off ARM64)
    }

    [Fact]
    [Trait("Decision", "ED-252")] // an Avalonia transition counts while it runs, and stops counting once it ended
    public void An_avalonia_transition_counts_as_animating_only_while_it_runs() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        var box = new Border { Width = 40, Height = 20, Background = Avalonia.Media.Brushes.Black };
        box.Transitions = [new Avalonia.Animation.DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromSeconds(0.1) }];
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100, Content = box };
        window.Show();
        try
        {
            Assert.NotNull(GpuWarmup.AvaloniaClockSubscribers()); // the pinned Avalonia's clock is read
            FramesFor(TimeSpan.FromMilliseconds(100), () => false);
            Assert.False(GpuWarmup.Animating(frames), "precondition: nothing animates");
            box.Opacity = 0.2;                                    // the transition starts
            FramesFor(TimeSpan.FromMilliseconds(30), () => false);
            Assert.True(box.Opacity > 0.2 && box.Opacity < 1, $"precondition: the transition runs (opacity {box.Opacity})");
            Assert.True(GpuWarmup.Animating(frames), "the warm-up saw nothing animating while an Avalonia transition runs");
            FramesFor(TimeSpan.FromMilliseconds(400), () => false);
            Assert.Equal(0.2, box.Opacity, 3);
            Assert.False(GpuWarmup.Animating(frames), "a transition that ended still holds the warm-up back");
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-252")] // a new GPU context first seen with a step waiting: the step compiles nothing on it and the run starts again from its first step
    public void A_new_gpu_context_seen_with_a_step_waiting_starts_the_run_again() => headless.OnUiThread(() =>
    {
        var source = GpuProgramCache.MachineSource;
        var adapter = "10de:2704:13bc196e:a1 NVIDIA GeForce RTX 4080 32.0.15.6094";
        GpuProgramCache.MachineSource = () => adapter;
        GpuWarmup.PretendGpu = true;
        GpuWarmup.PretendContext = 0x7E570001;
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100 };
        window.Show();
        try
        {
            var first = adapter;
            string? preparedFirst = null;
            var reports = new List<GpuWarmupStep>();
            var done = GpuWarmup.StepsDone;
            var task = GpuWarmup.WarmAsync(window, new SyncProgress<GpuWarmupStep>(step =>
            {
                reports.Add(step);
                if (reports.Count != 1) return;
                // reported before the second step is asked: the display moved to the iGPU (DXGI's adapter 0 now) and the
                // next frame draws on a new context, the second step waiting in it
                preparedFirst = GpuProgramCache.Prepared;
                adapter = "1002:13c0:d0001458:cb AMD Radeon(TM) Graphics 31.0.24033.1003";
                GpuWarmup.PretendContext = 0x7E570002;
            }));
            FramesFor(TimeSpan.FromSeconds(10), () => task.IsCompleted);
            Assert.True(task.IsCompletedSuccessfully, task.Exception?.InnerException?.Message);
            // the first step on the old context, then all four on the new one; the step waiting compiled nothing
            Assert.Equal(new[] { "noise" }.Concat(GpuWarmup.Programs), reports.Select(r => r.Program));
            Assert.Equal(1 + GpuWarmup.Steps, GpuWarmup.StepsDone - done);
            Assert.Equal(first, preparedFirst);
            Assert.Equal(adapter, GpuProgramCache.Prepared);     // the pass again read the adapter: the new context's key names it
        }
        finally { GpuWarmup.PretendGpu = false; GpuWarmup.PretendContext = 0; GpuProgramCache.MachineSource = source; window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-252")] // a step answered as the new GPU context's is done with: a later redraw of the primer, no step asked, compiles nothing
    public void A_step_answered_on_a_new_gpu_context_is_not_compiled_by_a_later_redraw() => headless.OnUiThread(() =>
    {
        var source = GpuProgramCache.MachineSource;
        using var holding = new ManualResetEventSlim(false);
        using var held = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        GpuProgramCache.MachineSource = () =>
        {
            // the restarted pass's read of the adapter (a pool thread) waits here, so no step is asked meanwhile
            if (holding.IsSet) { held.Set(); release.Wait(TimeSpan.FromSeconds(10)); }
            return "10de:2704:00000000:a1 Test GPU 1.2.3.4";
        };
        GpuWarmup.PretendGpu = true;
        GpuWarmup.PretendContext = 0x7E570001;
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100 };
        window.Show();
        try
        {
            var done = GpuWarmup.StepsDone;
            var reports = 0;
            var task = GpuWarmup.WarmAsync(window, new SyncProgress<GpuWarmupStep>(_ =>
            {
                if (reports++ != 0) return;
                holding.Set();                                    // the second step, asked next, is drawn on a new context
                GpuWarmup.PretendContext = 0x7E570002;
            }));
            FramesFor(TimeSpan.FromSeconds(10), () => held.IsSet);
            Assert.True(held.IsSet, "precondition: the run never started again on the new context");
            Assert.Equal(1, GpuWarmup.StepsDone - done);           // the noise step, on the old context
            // the window drawn whole on the new context, the primer with it, while the restart waits: the step it answered as
            // the new context's is not compiled now (outside the steps asked, and for the idle fallback outside its idle gate)
            window.Background = Avalonia.Media.Brushes.Gray;
            FramesFor(TimeSpan.FromMilliseconds(300), () => false);
            window.Background = Avalonia.Media.Brushes.DarkGray;
            FramesFor(TimeSpan.FromMilliseconds(300), () => false);
            Assert.True(GpuWarmup.StepsDone - done == 1, $"{GpuWarmup.StepsDone - done - 1} step(s) compiled in a redraw, none asked");
            holding.Reset();
            release.Set();
            FramesFor(TimeSpan.FromSeconds(10), () => task.IsCompleted);
            Assert.True(task.IsCompletedSuccessfully, task.Exception?.InnerException?.Message);
            Assert.Equal(1 + GpuWarmup.Steps, GpuWarmup.StepsDone - done);
        }
        finally { release.Set(); GpuWarmup.PretendGpu = false; GpuWarmup.PretendContext = 0; GpuProgramCache.MachineSource = source; window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-252")] // a pass cut short by a new GPU context marks nothing warm: the entries it saw were made on the old context
    public void A_pass_cut_short_by_a_new_gpu_context_marks_nothing_warm() => headless.OnUiThread(() =>
    {
        using var cache = new GpuProgramCacheFolder();
        var source = GpuProgramCache.MachineSource;
        GpuProgramCache.MachineSource = () => "10de:2704:00000000:a1 Test GPU 1.2.3.4";
        GpuWarmup.PretendGpu = true;
        GpuWarmup.PretendContext = 0x7E570001;
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100 };
        window.Show();
        try
        {
            var reports = 0;
            var task = GpuWarmup.WarmAsync(window, new SyncProgress<GpuWarmupStep>(_ =>
            {
                if (reports++ != 0) return;
                // the first step's link wrote as many entries as the warm-up has steps (its program's and others'), none
                // follow; and the second step, asked next, is drawn on a new context
                Interlocked.Add(ref GpuProgramCache.Writes, GpuWarmup.Steps);
                GpuWarmup.PretendContext = 0x7E570002;
            }));
            FramesFor(TimeSpan.FromSeconds(10), () => task.IsCompleted);
            Assert.True(task.IsCompletedSuccessfully, task.Exception?.InnerException?.Message);
            Assert.True(reports == 1 + GpuWarmup.Steps, $"precondition: the run started again ({reports} steps)");
            Assert.False(File.Exists(Path.Combine(cache.Root, "warm.txt")), "a pass cut short by a new GPU context marked the cache warm");
        }
        finally { GpuWarmup.PretendGpu = false; GpuWarmup.PretendContext = 0; GpuProgramCache.MachineSource = source; window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-252")] // a new GPU context with no step waiting warms again, in quiet frames like the fallback
    public void A_new_gpu_context_with_no_step_waiting_warms_again_in_quiet_frames() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        var cap = GpuWarmup.IdleCap;
        GpuWarmup.IdleCap = TimeSpan.FromSeconds(60);
        GpuWarmup.PretendGpu = true;
        GpuWarmup.PretendContext = 0x7E570001;
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100 };
        window.Show();
        try
        {
            var task = GpuWarmup.WarmAsync(window);
            FramesFor(TimeSpan.FromSeconds(10), () => task.IsCompleted);
            Assert.True(task.IsCompletedSuccessfully, task.Exception?.InnerException?.Message);
            frames.RequestFrame(_ => { });                       // animating
            var asked = GpuWarmup.StepsAsked;
            GpuWarmup.PretendContext = 0x7E570002;
            window.Background = Avalonia.Media.Brushes.Gray;     // the window drawn whole on the new context, the primer with it
            FramesFor(TimeSpan.FromMilliseconds(600), () => false);
            Assert.True(asked == GpuWarmup.StepsAsked, $"{GpuWarmup.StepsAsked - asked} steps asked at once while animating");
            frames.Advance(TimeSpan.FromMilliseconds(16));       // quiet
            FramesFor(TimeSpan.FromSeconds(10), () => GpuWarmup.StepsAsked > asked);
            Assert.True(GpuWarmup.StepsAsked > asked, "no warm-up on the new GPU context");
        }
        finally { GpuWarmup.IdleCap = cap; GpuWarmup.PretendGpu = false; GpuWarmup.PretendContext = 0; window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-252")] // after a give-up, single frames more than 100 ms apart are not a window drawing again: no resume
    public void After_a_give_up_single_frames_far_apart_do_not_resume_the_idle_warm_up() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        var (cap, timeout) = (GpuWarmup.IdleCap, GpuWarmup.StepTimeout);
        GpuWarmup.IdleCap = TimeSpan.FromMilliseconds(300);
        GpuWarmup.StepTimeout = TimeSpan.FromMilliseconds(300);
        var host = new Border { Width = 40, Height = 20 };
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100, Content = host };
        window.Show();
        try
        {
            GpuWarmup.WhenIdle(host);
            var asked = GpuWarmup.StepsAsked;
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();              // the window's first frame: the fallback starts
            Dispatcher.UIThread.RunJobs();
            Pump(TimeSpan.FromSeconds(2), () => false, render: false);   // then not drawn (minimised): its step is given up on
            Assert.True(GpuWarmup.StepsAsked == asked + 1, $"precondition: one step asked and given up on ({GpuWarmup.StepsAsked - asked} asked)");
            for (var k = 0; k < 3; k++)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();          // drawn in one frame (a flicker of the window)
                Dispatcher.UIThread.RunJobs();
                Pump(TimeSpan.FromMilliseconds(250), () => false, render: false); // and not again for 250 ms
            }
            Pump(TimeSpan.FromSeconds(1), () => GpuWarmup.StepsAsked > asked + 1, render: false);
            Assert.True(GpuWarmup.StepsAsked == asked + 1, $"{GpuWarmup.StepsAsked - asked - 1} steps asked on single frames 250 ms apart");
            Pump(TimeSpan.FromSeconds(5), () => GpuWarmup.StepsAsked > asked + 1, render: true);
            Assert.True(GpuWarmup.StepsAsked > asked + 1, "the warm-up never resumed once its window was drawn steadily");
        }
        finally { (GpuWarmup.IdleCap, GpuWarmup.StepTimeout) = (cap, timeout); window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-252")] // the chip's own run (WhenIdle) warms again when its window's scaling changes, idle-gated
    public void An_idle_warm_up_warms_again_when_its_hosts_window_changes_scaling() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        var cap = GpuWarmup.IdleCap;
        GpuWarmup.IdleCap = TimeSpan.FromSeconds(60);
        var host = new Border { Width = 40, Height = 20 };
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100, Content = host };
        window.Show();
        try
        {
            GpuWarmup.WhenIdle(host);
            var asked = GpuWarmup.StepsAsked;
            FramesFor(TimeSpan.FromSeconds(10), () => GpuWarmup.StepsAsked > asked);
            FramesFor(TimeSpan.FromMilliseconds(200), () => false); // the CPU renderer's answer ends the run
            Assert.True(GpuWarmup.StepsAsked == asked + 1, $"precondition: the chip's run asked one step ({GpuWarmup.StepsAsked - asked})");
            var report = (Action<double>)typeof(Avalonia.Platform.ITopLevelImpl).GetProperty("ScalingChanged")!.GetValue(window.PlatformImpl)!;
            report(1.5);
            FramesFor(TimeSpan.FromSeconds(10), () => GpuWarmup.StepsAsked > asked + 1);
            Assert.True(GpuWarmup.StepsAsked > asked + 1, "the chip's warm-up did not warm again for its window's new scaling");
        }
        finally { GpuWarmup.IdleCap = cap; window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-252")] // OD-0096's screen calling WarmAsync while a chip's idle run waits in the window: that run steps now too
    public void Warm_async_hurries_the_idle_warm_ups_going_in_its_window() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        var cap = GpuWarmup.IdleCap;
        GpuWarmup.IdleCap = TimeSpan.FromSeconds(60);
        GpuWarmup.PretendGpu = true;
        var host = new Border { Width = 40, Height = 20 };
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100, Content = host };
        window.Show();
        try
        {
            frames.RequestFrame(_ => { }); // animating throughout: the chip's run waits
            GpuWarmup.WhenIdle(host);
            var (asked, done) = (GpuWarmup.StepsAsked, GpuWarmup.StepsDone);
            FramesFor(TimeSpan.FromMilliseconds(500), () => false);
            Assert.Equal(asked, GpuWarmup.StepsAsked);
            var task = GpuWarmup.WarmAsync(window);
            // both runs step at once and end within 5 s (every ED-NEW-warmupsplit-1 part), well within the 60 s cap a run still
            // waiting for idle frames would need
            FramesFor(TimeSpan.FromSeconds(5), () => task.IsCompleted && GpuWarmup.StepsDone - done >= 2 * GpuWarmup.Steps);
            Assert.True(task.IsCompletedSuccessfully, task.Exception?.InnerException?.Message);
            Assert.True(GpuWarmup.StepsDone - done == 2 * GpuWarmup.Steps, $"{GpuWarmup.StepsDone - done} steps: the chip's run still waits for idle frames");
        }
        finally { GpuWarmup.IdleCap = cap; GpuWarmup.PretendGpu = false; window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-252")] // the steps done are kept, the last ones only (a chip's warm-up adds four, one for each composer made)
    public void The_record_of_steps_done_keeps_the_last_ones_only()
    {
        for (var k = 0; k < GpuWarmup.DoneKept + 10; k++)
            GpuWarmup.Record(new GpuWarmupStep(k % GpuWarmup.Steps, GpuWarmup.Steps, GpuWarmup.Programs[k % GpuWarmup.Steps], k, false));
        Assert.Equal(GpuWarmup.DoneKept, GpuWarmup.Done.Count);
        Assert.Equal(GpuWarmup.DoneKept + 9, GpuWarmup.Done.Last().Milliseconds);
    }

    [Fact]
    [Trait("Decision", "ED-252")] // the idle fallback reads Avalonia's clock: the app's window at rest, a thread open, leaves it without subscribers (else every warm-up waits for its cap)
    public void The_apps_window_at_rest_with_a_thread_open_holds_nothing_on_avalonias_clock() => headless.OnUiThread(() =>
    {
        // the chat's Hydra button opens its intro popover on first appearance (HydraButton.swift:93); a headless window has
        // no popup platform, so popovers are recorded (as RootViewDropRoutingTests does)
        using var popovers = Presentation.Presenter.Override(new RecordingPresenter());
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        using var harness = new AppModelHarness(withRuntime: false); // the real clock: the views' transitions end in time
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        using var cursor = Marrow.UI.Chrome.DragHandleView.CursorPlatform.Override(new ChatNoCursor());
        var model = new AppModel();
        model.NewThread(model.AddProject(new Project("Project", harness.NewFolder("project")).Url)); // selects it (AppModel.swift:623)
        var host = new Border();
        AppEnvironment.SetAppModel(host, model);
        host.Child = new Marrow.App.Views.RootView();
        var window = new Avalonia.Controls.Window { Width = 1000, Height = 720, Content = host };
        AppEnvironment.SetWindowLiveResize(host, new Marrow.App.Window.WindowLiveResize(window));
        window.Show();
        try
        {
            var chip = Assert.Single(LogicalQuery.SelfAndDescendants<ModelEffortButton>(window));
            Assert.True(GpuWarmup.IsScheduled(chip), "precondition: the composer's model chip has its idle warm-up");
            // the window's first frames, and any animation or transition they start, run out (the frame clock stepped with
            // the render timer)
            FramesFor(TimeSpan.FromSeconds(2), () => { frames.Advance(TimeSpan.FromMilliseconds(16)); return false; });
            var subscribers = GpuWarmup.AvaloniaClockSubscribers();
            Assert.NotNull(subscribers);
            Assert.True(subscribers!.Count == 0, $"{subscribers.Count} subscribers hold Avalonia's clock with the window at rest: the warm-up never sees a quiet frame");
            Assert.True(frames.Pending == 0, $"{frames.Pending} frame-clock requests still wait with the window at rest: {Requesters(frames)}");
            Assert.False(GpuWarmup.Animating(frames), "the window at rest counts as animating");
        }
        finally { window.Hide(); window.Content = null; Dispatcher.UIThread.RunJobs(); }
    });

    // who waits on a manual frame source (its requests' callbacks, by method and owner): a failure names what animates
    private static string Requesters(ManualFrameSource frames)
    {
        var waiting = (System.Collections.IEnumerable)typeof(ManualFrameSource)
            .GetField("waiting", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(frames)!;
        return string.Join("; ", waiting.Cast<object>().Select(request =>
        {
            var frame = (Delegate)request.GetType().GetProperty("Frame")!.GetValue(request)!;
            return frame.Method.DeclaringType?.FullName + "." + frame.Method.Name + " (" + frame.Target?.GetType().FullName + ")";
        }));
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

    // a frame every 16 ms of the manual clock (about 10 ms of the machine's, for the warm-up's timer turn between frames) until
    // done or the manual time is up: what is measured is the manual clock, never the machine's (ED-NEW-handoverfix-4)
    private static void ClockedFrames(ManualTimeProvider clock, TimeSpan span, Func<bool> done)
    {
        var until = clock.Elapsed + span;
        while (!done() && clock.Elapsed < until)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
            Dispatcher.UIThread.RunJobs();
            clock.Advance(TimeSpan.FromMilliseconds(16));
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
