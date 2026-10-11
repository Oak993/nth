using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Marrow.App.Port.FirstRun;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// ED-NEW-warmupsplit-1: a warm-up step fits one 60 Hz frame on a cached launch, so the families that made several programs a
// step are drawn in parts, one GPU draw (site) a part, with nothing left out. ED-NEW-warmupsplit-2: the idle fallback asks no
// step right after input. ED-NEW-warmupsplit-3: a step is labelled forced only when something animates as the cap lets it
// through. (The < 1000/60 ms bound on a cached launch is the native On_a_second_launch_the_knob_programs_come_from_the_cache.)
[Collection("Headless")]
public sealed class GpuWarmupSplitTests(HeadlessSession headless)
{
    private static readonly GpuWarmContext Cpu = new(null, 1);

    [Fact]
    [Trait("Decision", "ED-NEW-warmupsplit-1")]
    public void Every_part_of_a_split_family_draws_exactly_one_gpu_draw_of_its_frame_and_together_they_draw_them_all() => headless.OnUiThread(() =>
    {
        (string Family, Func<GpuProgram, bool> Of, int Sites)[] families =
        [
            ("light-rotate", p => p.Family == "light-rotate", GpuWarmup.LightSites(GpuWarmup.LightKind.Rotate)),
            ("light-moving", p => p.Family == "light-moving", GpuWarmup.LightSites(GpuWarmup.LightKind.Moving)),
            ("light-aura", p => p.Family == "light-aura", GpuWarmup.LightSites(GpuWarmup.LightKind.Aura)),
            ("liquid", p => p.Family == "liquid", GpuWarmup.LiquidSites),
            ("strike legacy", p => p.Name.StartsWith("strike[legacy ", StringComparison.Ordinal), StrikeWarmup.LegacySites),
            // ED-NEW-warmupsplit-4: each brand's first fresh Mix frame, the one that links the brand's programs
            .. StrikeWarmup.MixBrands.Select(brand => ("strike " + brand + " first", (Func<GpuProgram, bool>)(p =>
                p.Name.StartsWith("strike[" + brand + " " + StrikeWarmup.MixStrikes[0] + " a", StringComparison.Ordinal)), StrikeWarmup.MixSites)),
        ];
        // the frames' GPU draws, counted on the CPU (the harness counted the same on ANGLE: 5, 3, 3, 8 and 8; a Mix frame 14, the
        // Mix raster's paint six of them, for every brand, strike and part)
        Assert.Equal([5, 3, 3, 8, 8, 14, 14, 14], families.Select(f => f.Sites));
        foreach (var (family, of, sites) in families)
        {
            var parts = GpuWarmup.Registered.Where(of).ToArray();
            Assert.True(parts.Length == sites, $"{family}: {parts.Length} parts for {sites} draws");
            foreach (var part in parts)
            {
                using (GpuWarmSites.Only(-1, andAfter: false)) { } // the last part closed: none yet
                part.Drawing(Cpu);
                Assert.True(GpuWarmSites.LastMet == sites, $"{part.Name}: its frame met {GpuWarmSites.LastMet} draws, {sites} pinned");
                Assert.True(GpuWarmSites.LastDrawn == 1, $"{part.Name}: drew {GpuWarmSites.LastDrawn} of its frame's draws in one step");
            }
        }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-warmupsplit-1")]
    public void A_part_draws_only_its_own_site_and_the_last_every_later_one_while_outside_a_part_every_site_draws()
    {
        Assert.True(GpuWarmSites.Draw());
        bool[] Sites(int only, bool andAfter, int count)
        {
            using var part = GpuWarmSites.Only(only, andAfter);
            return Enumerable.Range(0, count).Select(_ => GpuWarmSites.Draw()).ToArray();
        }
        Assert.Equal([false, true, false, false], Sites(1, andAfter: false, 4));
        Assert.Equal([false, false, true, true], Sites(2, andAfter: true, 4)); // a site added after the pinned count is still drawn
        Assert.Equal([false, false, false], Sites(-1, andAfter: false, 3));     // a count only
        Assert.Equal((3, 0), (GpuWarmSites.LastMet, GpuWarmSites.LastDrawn));
        using (GpuWarmSites.Only(0, andAfter: false))
        {
            using (GpuWarmSites.Only(-1, andAfter: false)) Assert.False(GpuWarmSites.Draw());
            Assert.True(GpuWarmSites.Draw()); // the enclosing part again, its site 0
        }
        Assert.True(GpuWarmSites.Draw()); // no part open: every site draws
    }

    [Fact]
    [Trait("Decision", "ED-NEW-warmupsplit-1")]
    public void The_strikes_mix_parts_draw_every_pass_the_whole_mix_draws_and_a_part_half_of_the_fresh_lightning_at_most()
    {
        var mix = GpuWarmup.Registered.Where(p => p.Family == "strike" && !p.Name.StartsWith("strike[legacy ", StringComparison.Ordinal)).ToArray();
        // three parts a strike, the first strike's first part in one part a GPU draw (ED-NEW-warmupsplit-4)
        Assert.Equal(StrikeWarmup.MixBrands.Count * (StrikeWarmup.MixStrikes.Count * 3 - 1 + StrikeWarmup.MixSites), mix.Length);
        foreach (var brand in StrikeWarmup.MixBrands)
        {
            var parts = 0;
            foreach (var strike in StrikeWarmup.MixStrikes)
                for (var part = 0; part < 3; part++) parts |= StrikeWarmup.DrawMixPart(Cpu, brand, strike, part);
            Assert.Equal(StrikeWarmup.DrawMix(Cpu, brand), parts); // the same ink, light and wash passes as the whole family drew
            var sites = 0;
            for (var site = 0; site < StrikeWarmup.MixSites; site++) sites |= StrikeWarmup.DrawMixPart(Cpu, brand, StrikeWarmup.MixStrikes[0], 0, site);
            Assert.Equal(StrikeWarmup.DrawMixPart(Cpu, brand, StrikeWarmup.MixStrikes[0], 0), sites); // its sites draw what the whole part drew
        }
        Assert.Equal(
            [.. StrikeWarmup.MixBrands.SelectMany(brand => StrikeWarmup.MixStrikes.SelectMany(strike => strike == StrikeWarmup.MixStrikes[0]
                ? [.. Enumerable.Range(0, StrikeWarmup.MixSites).Select(k => $"strike[{brand} {strike} a{k}]"), $"strike[{brand} {strike} b]", $"strike[{brand} {strike} c]"]
                : new[] { $"strike[{brand} {strike} a]", $"strike[{brand} {strike} b]", $"strike[{brand} {strike} c]" }))],
            mix.Select(p => p.Name));
        // a fresh frame's masks are split in two halves that stroke each mask once, in its order
        var storm = new StrikeStorm(1274);
        var geometry = new StrikeGeometry(298, 34, 272, 24);
        var ink = StrikeInk.Of(JewelLiquid.Of(EffortBrand.Claude));
        StrikeMask[] Fresh()
        {
            var frame = storm.Resolve(geometry, StrikeMix.Timing(StrikeWarmup.MixStrikes[3], 1274).At + 0.035, 1.3, 1, ink.Amount);
            return Enumerable.Range(0, frame.Masks.Count).Select(frame.Masks.At).ToArray();
        }
        StrikeMask[] Half(int half)
        {
            var frame = storm.Resolve(geometry, StrikeMix.Timing(StrikeWarmup.MixStrikes[3], 1274).At + 0.035, 1.3, 1, ink.Amount);
            StrikeWarmup.Halve(frame.Masks, half);
            return Enumerable.Range(0, frame.Masks.Count).Select(frame.Masks.At).ToArray();
        }
        var all = Fresh();
        Assert.True(all.Length > 100, $"precondition: the fresh lightning has {all.Length} masks");
        var (a, b) = (Half(0), Half(1));
        Assert.True(a.Length <= (all.Length + 1) / 2 && b.Length <= all.Length / 2, $"{a.Length} and {b.Length} of {all.Length} masks");
        Assert.Equal(all, Interleave(a, b));
    }

    private static StrikeMask[] Interleave(StrikeMask[] even, StrikeMask[] odd) =>
        Enumerable.Range(0, even.Length + odd.Length).Select(i => i % 2 == 0 ? even[i / 2] : odd[i / 2]).ToArray();

    [Fact]
    [Trait("Decision", "ED-NEW-warmupsplit-3")]
    public void A_step_the_cap_lets_through_once_the_animation_stopped_is_not_labelled_forced() => headless.OnUiThread(() =>
        Assert.False(StepAtTheCap(stillAnimating: false).Forced));

    [Fact]
    [Trait("Decision", "ED-NEW-warmupsplit-3")]
    public void A_step_the_cap_lets_through_while_something_animates_is_labelled_forced() => headless.OnUiThread(() =>
        Assert.True(StepAtTheCap(stillAnimating: true).Forced));

    // the idle fallback waits for calm frames while an animation runs; the cap lets the first step through, the animation
    // stopped just then (or still running)
    private static GpuWarmupStep StepAtTheCap(bool stillAnimating)
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var cap = GpuWarmup.IdleCap;
        GpuWarmup.IdleCap = TimeSpan.FromSeconds(1);
        GpuWarmup.PretendGpu = true;
        var host = new Border { Width = 40, Height = 20 };
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100, Content = host };
        window.Show();
        try
        {
            frames.RequestFrame(_ => { }); // animating
            var done = GpuWarmup.StepsDone;
            var asked = GpuWarmup.StepsAsked;
            GpuWarmup.WhenIdle(host);
            ClockedFrames(clock, TimeSpan.FromMilliseconds(480), () => false); // the pass begun, waiting for calm frames
            Assert.True(asked == GpuWarmup.StepsAsked, "precondition: a step asked while animating, before the cap");
            // the cap runs out, and (or not) the animation stops a moment after, before the fallback looks again: it looks at
            // the cap first (calm needs two frames), so the step goes through at the cap
            clock.Advance(GpuWarmup.IdleCap);
            if (!stillAnimating) frames.Advance(TimeSpan.FromMilliseconds(16));
            Assert.Equal(stillAnimating, GpuWarmup.Animating());
            ClockedFrames(clock, TimeSpan.FromSeconds(10), () => GpuWarmup.StepsDone > done);
            Assert.True(GpuWarmup.StepsDone > done, "no step drawn after the cap");
            var steps = GpuWarmup.Done.ToArray();
            return steps[^(GpuWarmup.StepsDone - done)]; // the pass's first step
        }
        finally { GpuWarmup.IdleCap = cap; GpuWarmup.PretendGpu = false; window.Close(); }
    }

    [Fact]
    [Trait("Decision", "ED-NEW-warmupsplit-1")]
    public void Once_quiet_the_idle_warm_up_steps_a_part_a_frame_and_waits_again_for_two_calm_frames_after_an_animation() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var cap = GpuWarmup.IdleCap;
        GpuWarmup.IdleCap = TimeSpan.FromSeconds(60); // never the cap here
        GpuWarmup.PretendGpu = true;
        var host = new Border { Width = 40, Height = 20 };
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100, Content = host };
        window.Show();
        try
        {
            var asked = GpuWarmup.StepsAsked;
            var at = new List<TimeSpan>();
            GpuWarmup.WhenIdle(host);
            ClockedFrames(clock, TimeSpan.FromSeconds(10), () =>
            {
                for (; asked < GpuWarmup.StepsAsked; asked++) at.Add(clock.Elapsed);
                return at.Count >= 12;
            });
            Assert.True(at.Count >= 12, $"precondition: {at.Count} steps asked in quiet frames");
            var gaps = at.Zip(at.Skip(1), (a, b) => (b - a).TotalMilliseconds).ToArray();
            Assert.True(gaps.Max() <= 32, $"a gap of {gaps.Max():F0} ms between steps in quiet frames: not a step a frame ({string.Join(", ", gaps)})");
            frames.RequestFrame(_ => { }); // an animation starts
            var before = GpuWarmup.StepsAsked;
            ClockedFrames(clock, TimeSpan.FromMilliseconds(16), () => false); // a step already asked may still be drawn
            var held = GpuWarmup.StepsAsked;
            Assert.True(held - before <= 1, $"{held - before} steps asked as the animation began");
            ClockedFrames(clock, TimeSpan.FromMilliseconds(320), () => false);
            Assert.True(held == GpuWarmup.StepsAsked, $"{GpuWarmup.StepsAsked - held} steps asked while animating");
            frames.Advance(TimeSpan.FromMilliseconds(16)); // it ends
            ClockedFrames(clock, TimeSpan.FromSeconds(2), () => GpuWarmup.StepsAsked > held);
            Assert.True(GpuWarmup.StepsAsked > held, "no step once quiet again");
        }
        finally { GpuWarmup.IdleCap = cap; GpuWarmup.PretendGpu = false; window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-warmupsplit-2")]
    public void The_idle_warm_up_asks_no_step_for_a_moment_after_input() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var cap = GpuWarmup.IdleCap;
        GpuWarmup.IdleCap = TimeSpan.Zero; // the cap long run out: a step a frame, whatever animates
        GpuWarmup.PretendGpu = true;
        var host = new Border { Width = 40, Height = 20 };
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100, Content = host };
        window.Show();
        try
        {
            frames.RequestFrame(_ => { }); // animating throughout
            var asked = GpuWarmup.StepsAsked;
            GpuWarmup.WhenIdle(host);
            ClockedFrames(clock, TimeSpan.FromSeconds(10), () => GpuWarmup.StepsAsked - asked >= 2);
            Assert.True(GpuWarmup.StepsAsked - asked >= 2, "precondition: the pass steps");
            GpuWarmup.NoteInput();
            var input = clock.Elapsed;
            var before = GpuWarmup.StepsAsked;
            ClockedFrames(clock, TimeSpan.FromMilliseconds(16), () => false); // a step already asked may still be drawn
            var held = GpuWarmup.StepsAsked;
            Assert.True(held - before <= 1, $"{held - before} steps asked in the frame after input");
            ClockedFrames(clock, GpuWarmup.InputQuiet - (clock.Elapsed - input) - TimeSpan.FromMilliseconds(16), () => false);
            Assert.True(held == GpuWarmup.StepsAsked, $"{GpuWarmup.StepsAsked - held} steps asked {(clock.Elapsed - input).TotalMilliseconds:F0} ms after input");
            ClockedFrames(clock, TimeSpan.FromMilliseconds(200), () => GpuWarmup.StepsAsked > held);
            Assert.True(GpuWarmup.StepsAsked > held, "the pass did not step again once the input was quiet");
            Assert.True(clock.Elapsed - input >= GpuWarmup.InputQuiet);
        }
        finally { GpuWarmup.IdleCap = cap; GpuWarmup.PretendGpu = false; window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-warmupsplit-2")]
    public void A_key_pressed_in_any_window_is_input_the_warm_up_waits_after() => headless.OnUiThread(() =>
    {
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        GpuWarmup.WatchInput();
        var field = new TextBox();
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100, Content = field };
        window.Show();
        try
        {
            Assert.True(field.Focus()); // the key goes to the focused element, in this window
            Assert.False(GpuWarmup.InputRecent(clock));
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.True(GpuWarmup.InputRecent(clock));
            clock.Advance(GpuWarmup.InputQuiet);
            Assert.False(GpuWarmup.InputRecent(clock));
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.True(GpuWarmup.InputRecent(clock));
        }
        finally { window.Close(); }
    });

    // frames 16 ms apart on the manual clock until done or the clock's span is up
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
}
