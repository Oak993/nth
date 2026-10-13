using Marrow.Core.Support;
using Marrow.Setup.Flow;
using Marrow.Setup.Tests.Fixtures;

namespace Marrow.Setup.Tests.Support;

public sealed class C7U9SweepBudgetEntryPointsTests : IDisposable
{
    private readonly SweepArea area = new();
    public void Dispose() => area.Dispose();

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Depth_cutoff_is_observed_even_when_time_and_count_have_room()
    {
        area.PutInSetup("1.0.0", "setup.log");
        var at = Path.Combine(area.SetupRoot, "1.0.0", "logs");
        for (var level = 0; level < 66; level++) at = Path.Combine(at, "2026");
        var payload = area.Put(Path.Combine(at, "setup.log"));
        var result = SetupSweeper.Sweep(area.LocalAppData, area.Temp, new(TimeSpan.FromSeconds(30), 1000000, 64), new C7SteppingClock());
        Assert.Contains(result.Kept, kept => kept.Reason == SweepKeptReason.LimitReached);
        Assert.True(File.Exists(payload));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Monotonic_deadline_stops_a_sweep_without_sleep_or_massive_tree()
    {
        var payload = area.PutInSetup("1.0.0", "nth Setup Engine.exe");
        var clock = new C7SteppingClock { Step = TimeSpan.FromMilliseconds(100) };
        var result = SetupSweeper.Sweep(area.LocalAppData, area.Temp, new(TimeSpan.FromSeconds(1), 1000000, 64), clock);
        Assert.Contains(result.Kept, kept => kept.Reason == SweepKeptReason.LimitReached);
        Assert.True(File.Exists(payload));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void RemoveFolder_applies_one_count_budget_to_unit_admission_and_parent_cleanup()
    {
        var payload = area.PutInSetup("runtime", "4f9c2a", "libSkiaSharp.dll");
        var result = SetupSweeper.RemoveFolder(area.LocalAppData, Path.GetDirectoryName(payload)!, new(TimeSpan.FromSeconds(30), 1, 64), new C7SteppingClock());
        Assert.False(result.Clean);
        Assert.Contains(result.Kept, kept => kept.Reason == SweepKeptReason.LimitReached);
        Assert.True(File.Exists(payload));
        Assert.True(Directory.Exists(Path.Combine(area.SetupRoot, "runtime")));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Release_records_its_count_cutoff_for_current_process_diagnostics()
    {
        using var scope = StagingScope.Create(area.LocalAppData, area.Temp, "1.0.0", lastChance: false, cleanupLimits: new(TimeSpan.FromSeconds(1), 1, 64), clock: new C7SteppingClock());
        var folder = scope.Ensure();
        var payload = area.Put(Path.Combine(folder, SetupFolders.EngineFileName));
        var result = scope.Release();
        Assert.Contains(result.Kept, kept => kept.Reason == SweepKeptReason.LimitReached);
        Assert.Same(result, scope.LastCleanup);
        Assert.True(File.Exists(payload));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Last_chance_uses_its_short_deadline_and_records_what_it_kept()
    {
        var clock = new C7SteppingClock();
        using var scope = StagingScope.Create(area.LocalAppData, area.Temp, "1.0.0", lastChance: false, clock: clock);
        var folder = scope.Ensure();
        var payload = area.Put(Path.Combine(folder, SetupFolders.EngineFileName));
        clock.Step = TimeSpan.FromMilliseconds(200);
        try
        {
            scope.OnProcessExit(null, EventArgs.Empty);
            Assert.Contains(scope.LastCleanup.Kept, kept => kept.Reason == SweepKeptReason.LimitReached);
            Assert.True(File.Exists(payload));
        }
        finally { clock.Step = TimeSpan.Zero; }
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_zero_entry_claim_budget_creates_no_unit_marker_or_claim()
    {
        var folder = Path.Combine(area.SetupRoot, "1.0.0");
        Assert.ThrowsAny<IOException>(() => { using var claim = FolderClaim.Take(folder, new(TimeSpan.FromSeconds(1), 0, 64)); });
        Assert.False(Directory.Exists(folder));
        Assert.False(File.Exists(Path.Combine(folder, ".nth-setup-owner")));
        Assert.False(File.Exists(Path.Combine(folder, SetupFolders.ClaimFileName)));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_zero_entry_fast_sweep_budget_does_not_consult_the_setup_probe()
    {
        area.PutInSetup("1.0.0", SetupFolders.EngineFileName);
        var probe = new C7CountingSetupProbe();
        var result = SetupSweeper.SweepUnlessSetupRuns(area.LocalAppData, area.Temp, probe, new(TimeSpan.FromSeconds(1), 0, 64));
        Assert.Equal(0, probe.Asked);
        Assert.False(result.Clean);
        Assert.Contains(result.Kept, kept => kept.Reason == SweepKeptReason.LimitReached);
    }

}
