using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.App.Port.FirstRun;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.FirstRun;

// GR-1 adversarial review, round 3. OD-0096: "on the first run after install (and again only when the prepared state is invalid:
// a new GPU or driver, or a new app build) ... Later launches skip it"; W1's subtitle (gr_lib.py:28): "This happens once."
// GpuProgramCache is off where the renderer is not ANGLE (the software fallback Avalonia's Win32 platform uses when ANGLE/D3D11
// cannot be made), and GpuWarmup's pass returns on the CPU renderer before it can write the marker, so the prepared state of such
// a machine is never valid: every launch would run the first run again (and, once VT-15 registers it, the voice hardware check).
[Collection("Headless")]
public sealed class FirstRunPreparationReview3Tests(HeadlessSession headless)
{
    [Fact] // GR-1.skip
    [Trait("Decision", "ED-252")]
    public void REVIEW3_a_completed_first_run_on_the_CPU_renderer_leaves_the_machine_prepared_so_the_next_launch_skips_it() => headless.OnUiThread(() =>
    {
        using var cache = new GpuProgramCacheFolder();
        var source = GpuProgramCache.MachineSource;
        GpuProgramCache.MachineSource = () => "10de:2704:00000000:a1 Test GPU 1.2.3.4";
        GpuWarmup.PretendGpu = false; // the CPU renderer, as the app draws when ANGLE cannot be made
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100 };
        window.Show();
        try
        {
            var first = new FirstRunPreparation().RunAsync(window, new FirstRunQuietScreen());
            Pump(TimeSpan.FromSeconds(60), () => first.IsCompleted);
            Assert.True(first.IsCompletedSuccessfully, first.Exception?.InnerException?.ToString());
            Assert.Equal(FirstRunOutcomeKind.Prepared, first.Result.Kind);
            Pump(TimeSpan.FromSeconds(5), () => new FirstRunPreparation().IsPrepared);
            Assert.True(new FirstRunPreparation().IsPrepared,
                "the first run completed (Prepared) on this machine, build, adapter and driver, yet the prepared state is still invalid: the next launch would show Getting ready again");
            var next = new FirstRunPreparation().RunAsync(window, new FirstRunQuietScreen());
            Pump(TimeSpan.FromSeconds(60), () => next.IsCompleted);
            Assert.Equal(FirstRunOutcomeKind.Skipped, next.Result.Kind);
        }
        finally { GpuProgramCache.MachineSource = source; window.Close(); }
    });

    private static void Pump(TimeSpan span, Func<bool> done)
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
