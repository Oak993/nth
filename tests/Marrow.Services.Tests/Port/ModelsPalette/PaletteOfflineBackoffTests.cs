using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.Services.Tests.Port.ModelsPalette;
// PORT-ONLY: lean pass guard (ED-NEW-lean-23). The offline retry starts at the old 120 s, doubles to 30 min, and comes
// back to 120 s after a network-available event or a catalogue success. ManualTimeProvider only; time never moves here,
// so each retry deadline is exactly its delay.
[Trait("Decision","ED-NEW-lean-23")]
public sealed class PaletteOfflineBackoffTests
{
    private static TimeSpan S(int seconds)=>TimeSpan.FromSeconds(seconds);
    [Fact]
    public void The_offline_retry_starts_at_120_seconds_and_doubles_to_30_minutes()
    {
        Assert.Equal(new[]{120,240,480,960,1800,1800,1800},Enumerable.Range(1,7).Select(PaletteEngine.OfflineRetrySeconds));
        Assert.Equal(120,PaletteEngine.OfflineRetrySeconds(0));
    }
    [Fact]
    public async Task A_network_available_event_brings_the_retry_back_to_120_seconds()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture{FailModels=true};var network=new FakePaletteNetwork();
        using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack,network);
        engine.Start();
        await engine.CheckNow();Assert.Contains(S(120),fake.Time.TimerDeadlines);
        await engine.CheckNow();Assert.Contains(S(240),fake.Time.TimerDeadlines);
        await engine.CheckNow();Assert.Contains(S(480),fake.Time.TimerDeadlines);Assert.DoesNotContain(S(240),fake.Time.TimerDeadlines);
        network.Raise(true);
        await engine.CheckNow();Assert.Contains(S(120),fake.Time.TimerDeadlines);Assert.DoesNotContain(S(480),fake.Time.TimerDeadlines);Assert.DoesNotContain(S(960),fake.Time.TimerDeadlines);
    }
    [Fact]
    public async Task A_catalogue_success_brings_the_retry_back_to_120_seconds()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture{FailModels=true};
        using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);
        engine.Start();
        await engine.CheckNow();await engine.CheckNow();Assert.Contains(S(240),fake.Time.TimerDeadlines);
        fake.FailModels=false;Assert.True((await engine.CheckNow()).CatalogueSucceeded);
        fake.FailModels=true;await engine.CheckNow();
        Assert.Contains(S(120),fake.Time.TimerDeadlines);Assert.DoesNotContain(S(480),fake.Time.TimerDeadlines);
    }
    [Fact]
    public async Task A_manual_refresh_while_offline_does_not_lengthen_the_automatic_retry()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture{FailModels=true};
        using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);
        engine.Start();
        Assert.False((await engine.RefreshNow()).CatalogueSucceeded);Assert.Contains(S(120),fake.Time.TimerDeadlines);
        // The first automatic failure still waits the old 120 s: the manual refresh before it did not count.
        await engine.CheckNow();Assert.Contains(S(120),fake.Time.TimerDeadlines);Assert.DoesNotContain(S(240),fake.Time.TimerDeadlines);
        await engine.CheckNow();Assert.Contains(S(240),fake.Time.TimerDeadlines);
    }
}
