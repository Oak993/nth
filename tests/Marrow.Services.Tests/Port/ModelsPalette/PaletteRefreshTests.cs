using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.ModelsPalette;

// PORT-ONLY: RR-21 same pipeline and OD-0127 Settings projection; ED-NEW-models-palette-7.
[Trait("Decision", "ED-NEW-modelspalette-7")]
public sealed class PaletteRefreshTests
{
    [Fact]
    public async Task Refresh_and_automatic_checks_use_identical_facts_seeds_and_tables()
    {
        using var roots=new ModelsPaletteRoots();
        using var auto=new PalettePipelineFixture();using var refresh=new PalettePipelineFixture();
        using var a=new PaletteEngine(roots.New(),auto.Http,auto,auto.Time,auto.Pack);
        using var b=new PaletteEngine(roots.New(),refresh.Http,refresh,refresh.Time,refresh.Pack);
        var phases=new List<PaletteRefreshPhase>();b.Progress+=p=>phases.Add(p.Phase);
        var x=await a.CheckNow();var y=await b.RefreshNow();
        Assert.Equal(x.Digest,y.Digest);Assert.Equal(x.NewMakers,y.NewMakers);
        Assert.Equal(new[]{"acme"},y.NewMakers);Assert.Empty(y.PendingMakers);
        Assert.Equal(a.Current.Resolve("acme/nova-1","high",false),b.Current.Resolve("acme/nova-1","high",false));
        Assert.Contains(PaletteRefreshPhase.Catalogue,phases);Assert.Contains(PaletteRefreshPhase.Makers,phases);
        Assert.Equal(PaletteRefreshPhase.Done,phases[^1]);
        Assert.Contains(refresh.Http.Received,r=>r.Header("Cache-Control")?.Contains("no-cache",StringComparison.Ordinal)==true);
        Assert.All(refresh.Http.Received,r=>Assert.Null(r.Header("Authorization")));
    }
    [Fact]
    public async Task Same_day_refresh_cannot_double_observe_or_force_more_than_once_in_120_seconds()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();
        using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);
        await engine.RefreshNow();var count=fake.Http.Received.Count;
        await engine.RefreshNow();Assert.Equal(count,fake.Http.Received.Count);
        fake.Time.Advance(TimeSpan.FromSeconds(121));await engine.RefreshNow();
        Assert.Equal(count+1,fake.Http.Received.Count);
        Assert.Equal(1,fake.Http.Received.Count(r=>r.Url.Host=="cdn-avatars.huggingface.co"));
    }
    [Fact]
    public async Task Agreed_seed_changes_report_the_existing_makers_whose_shades_are_re_spaced()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();
        using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);
        await engine.RefreshNow();fake.ImageVersion=1;fake.Date="Thu, 08 Oct 2026 06:20:00 GMT";fake.Time.Advance(TimeSpan.FromDays(1));
        var first=await engine.RefreshNow();Assert.Empty(first.ReSpacedModels);
        fake.Date="Fri, 09 Oct 2026 06:20:00 GMT";fake.Time.Advance(TimeSpan.FromDays(1));
        var second=await engine.RefreshNow();Assert.Equal(new[]{"acme/nova-1"},second.ReSpacedModels);Assert.Empty(second.NewMakers);
    }

}
