using Marrow.App.Port.ModelsPalette;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.App.Tests.Port.ModelsPalette;
[Trait("Decision","ED-NEW-modelspalette-15")]
public sealed class PaletteSharedRefreshTests
{
    [Fact]
    public async Task Leaving_the_Colours_page_releases_its_listener_but_keeps_the_process_facade_alive()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);
        var posts=new Queue<Action>();using var facade=new ModelShades(engine,posts.Enqueue,fake.Time);var frames=0;facade.Changed+=()=>frames++;
        using(var controller=new PaletteColoursController(engine,()=>0,fake.Time,sharedFacade:facade)){controller.Attach();controller.Detach();}
        await engine.CheckNow();fake.Time.Advance(TimeSpan.FromMilliseconds(100));Assert.Single(posts);posts.Dequeue()();Assert.Equal(1,frames);
        Assert.Same(engine.Current,facade.Current);Assert.NotNull(facade.Resolve("acme/nova-1","high",false));
    }
}
