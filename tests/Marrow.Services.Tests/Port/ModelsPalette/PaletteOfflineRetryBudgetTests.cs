using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.Services.Tests.Port.ModelsPalette;
// PORT-ONLY: lean pass budget (ED-NEW-lean-23). Headless and deterministic: the ManualTimeProvider is the only clock,
// every catalogue reply is an in-process 503, so the engine stays offline for the whole simulated hour. Debug or Release
// make no difference to a request count.
[Trait("Decision","ED-NEW-lean-23")]
public sealed class PaletteOfflineRetryBudgetTests
{
    private static int Catalogue(PalettePipelineFixture fake)=>fake.Http.Received.Count(r=>r.Url.AbsolutePath.EndsWith("/models",StringComparison.Ordinal));
    [Fact]
    public async Task Catalogue_retries_in_one_simulated_offline_hour_stay_within_budget()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture{FailModels=true};
        using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);
        using var done=new SemaphoreSlim(0);engine.Progress+=p=>{if(p.Phase==PaletteRefreshPhase.Done)done.Release();};
        engine.Start();fake.Time.Advance(TimeSpan.FromMilliseconds(250));
        while(true)
        {
            Assert.True(await done.WaitAsync(TimeSpan.FromSeconds(10)));
            var poll=fake.Time.TimerDeadlines.Max();
            if(poll+TimeSpan.FromMilliseconds(250)>TimeSpan.FromHours(1))break;
            fake.Time.Advance(poll-fake.Time.Elapsed);fake.Time.Advance(TimeSpan.FromMilliseconds(250));
        }
        // Measured on the unchanged base: 30 (a flat 120 s retry; limit was 45). After the backoff (120, 240, 480, 960, then
        // 1800 s): 5. Limit about 1.3x.
        Assert.InRange(Catalogue(fake),1,6);
    }
}
