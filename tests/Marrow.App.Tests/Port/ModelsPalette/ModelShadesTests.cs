using Marrow.App.Port.ModelsPalette;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
namespace Marrow.App.Tests.Port.ModelsPalette;
// PORT-ONLY: C18 frozen reads and bounded posts, UI gate closed; ED-617.
[Trait("Decision", "ED-NEW-modelspalette-6")]
public sealed class ModelShadesTests
{
    [Fact]
    [Trait("Decision", "ED-NEW-T45")]
    public async Task A_timer_callback_queued_before_disposal_never_posts_to_the_retired_actor()
    {
        using var roots = new ModelsPaletteRoots(); using var fake = new PalettePipelineFixture();
        using var engine = new PaletteEngine(roots.New(), fake.Http, fake, fake.Time, fake.Pack);
        var timer = new Marrow.App.Tests.Fixtures.RetiredPaletteTimer();
        var posts = new Queue<Action>();
        using var facade = new ModelShades(engine, posts.Enqueue, timer);
        await engine.CheckNow();
        facade.Dispose();
        Assert.True(timer.IsDisposed);
        timer.DeliverQueuedCallback();
        Assert.Empty(posts);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-T45")]
    public async Task A_drain_already_enqueued_before_disposal_does_not_publish_a_change()
    {
        using var roots = new ModelsPaletteRoots(); using var fake = new PalettePipelineFixture();
        using var engine = new PaletteEngine(roots.New(), fake.Http, fake, fake.Time, fake.Pack);
        var posts = new Queue<Action>(); using var facade = new ModelShades(engine, posts.Enqueue, fake.Time);
        var changes = 0; facade.Changed += () => changes++;
        await engine.CheckNow(); fake.Time.Advance(TimeSpan.FromMilliseconds(100));
        var drain = Assert.Single(posts);
        facade.Dispose(); drain();
        Assert.Equal(0, changes);
    }

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
        Assert.Equal(calls,fake.Http.Received.Count);Assert.True(ModelShades.ApplyToUi);
        Assert.Equal(PaletteRefreshPhase.Done,facade.Progress?.Phase);
    }
}
