using Marrow.Core.Support;
using Marrow.Setup.Tests.Fixtures;

namespace Marrow.Setup.Tests.Support;

public sealed class C7U9SweepBoundsTests : IDisposable
{
    private readonly SweepArea area = new();
    public void Dispose() => area.Dispose();

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_sweep_reports_kept_work_once_its_two_thousand_entry_budget_is_exhausted()
    {
        for (var i = 0; i < 2001; i++) area.PutInSetup("1.0.0", "logs", i.ToString(System.Globalization.CultureInfo.InvariantCulture), "setup.log");
        var result = SetupSweeper.Sweep(area.LocalAppData, area.Temp);
        Assert.False(result.Clean, "a sweep must stop and report unvisited work beyond its entry budget");
        Assert.Contains(result.Kept, kept => kept.Reason.ToString() == "LimitReached");
        Assert.True(Directory.Exists(area.SetupRoot));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_sweep_reports_kept_work_beyond_its_sixty_four_level_depth_budget()
    {
        area.PutInSetup("1.0.0", "setup.log");
        var at = Path.Combine(area.SetupRoot, "1.0.0", "logs");
        for (var depth = 0; depth < 66; depth++) at = Path.Combine(at, "2026");
        area.Put(Path.Combine(at, "setup.log"));
        var result = SetupSweeper.Sweep(area.LocalAppData, area.Temp);
        Assert.False(result.Clean, "an excessively deep unit must be left for the next bounded cleanup");
        Assert.Contains(result.Kept, kept => kept.Reason.ToString() == "LimitReached");
        Assert.True(File.Exists(Path.Combine(at, "setup.log")));
    }
}
