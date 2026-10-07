using Marrow.App.Port.ModelsPalette;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.App.Tests.Port.ModelsPalette;
[Trait("Decision","ED-NEW-modelspalette-11")]
public sealed class PaletteColdLookupTests
{
    [Fact]
    public async Task Cold_lookup_posts_the_model_once_and_every_later_lookup_reads_the_frozen_pending_table()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);
        var posts=new Queue<Action>();using var facade=new ModelShades(engine,posts.Enqueue,fake.Time);
        Assert.Null(facade.Resolve("acme/nova-9","high",false));await engine.Observe([]);
        Assert.Contains("acme/nova-9",engine.Current.Models.Keys);Assert.Empty(fake.Http.Received);
        fake.Time.Advance(TimeSpan.FromMilliseconds(100));Assert.Single(posts);posts.Dequeue()();
        var shade=facade.Resolve("acme/nova-9","high",false);Assert.NotNull(shade);
        for(var i=0;i<10000;i++)Assert.Same(shade,facade.Resolve("acme/nova-9","high",false));
        Assert.Empty(posts);Assert.Empty(fake.Http.Received);
    }
}
