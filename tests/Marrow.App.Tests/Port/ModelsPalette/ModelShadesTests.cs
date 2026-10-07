using Marrow.App.Port.ModelsPalette;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
namespace Marrow.App.Tests.Port.ModelsPalette;
// PORT-ONLY: C18 frozen reads and bounded posts, UI gate closed; ED-NEW-models-palette-6.
[Trait("Decision", "ED-NEW-modelspalette-6")]
public sealed class ModelShadesTests
{
    [Fact]
    public async Task Resolve_reads_frozen_tables_and_progress_and_changes_share_a_single_post()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();
        using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);
        var posts=new Queue<Action>();using var facade=new ModelShades(engine,posts.Enqueue,fake.Time);
        var changed=0;facade.Changed+=()=>changed++;
        await engine.CheckNow();Assert.Empty(posts);
        fake.Time.Advance(TimeSpan.FromMilliseconds(100));Assert.Single(posts);
        posts.Dequeue()();Assert.Equal(1,changed);
        var calls=fake.Http.Received.Count;var a=facade.Resolve("acme/nova-1","high",false);
        Assert.Same(engine.Current.Resolve("acme/nova-1","high",false),a);
        for(var i=0;i<10000;i++)Assert.Same(a,facade.Resolve("acme/nova-1","high",false));
        Assert.Equal(calls,fake.Http.Received.Count);Assert.False(ModelShades.ApplyToUi);
        Assert.Equal(PaletteRefreshPhase.Done,facade.Progress?.Phase);
    }
}
