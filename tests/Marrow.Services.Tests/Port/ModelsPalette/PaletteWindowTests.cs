using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
namespace Marrow.Services.Tests.Port.ModelsPalette;
[Trait("Decision", "ED-NEW-modelspalette-6")]
public sealed class PaletteWindowTests
{
    [Fact]
    public async Task Snapshot_is_published_at_server_boundary_plus_fifteen_minutes()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture {Date="Wed, 07 Oct 2026 06:01:00 GMT"};
        using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);
        await engine.CheckNow();Assert.Equal(-1,engine.Current.Boundary);
        fake.Time.Advance(TimeSpan.FromMinutes(13));await engine.Observe([]);Assert.Equal(-1,engine.Current.Boundary);
        fake.Time.Advance(TimeSpan.FromMinutes(1));await engine.Observe([]);Assert.True(engine.Current.Boundary>=0);
    }
    [Fact]
    public async Task A_stale_cdn_response_cannot_supply_the_boundary_snapshot()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();var original=fake.Http.Answer!;
        fake.Http.Answer=r=>{var result=original(r);if(r.Url.AbsolutePath.EndsWith("/models",StringComparison.Ordinal))result.Headers.TryAddWithoutValidation("Age","1800");return result;};
        using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);
        await engine.CheckNow();Assert.Equal(-1,engine.Current.Boundary);
        Assert.Contains("acme/nova-1",engine.Current.Models.Keys);
        fake.Http.Answer=original;await engine.CheckNow();Assert.True(engine.Current.Boundary>=0);
    }
    [Fact]
    public async Task Os_network_event_uses_captured_fake_transport_and_debounces_on_the_monotonic_clock()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();var network=new FakePaletteNetwork();
        using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack,network);
        var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.Progress+=p=>{if(p.Phase==PaletteRefreshPhase.Done)done.TrySetResult();};
        engine.Start();await Task.Run(()=>{for(var i=0;i<40;i++)network.Raise(true);});
        fake.Time.Advance(TimeSpan.FromMilliseconds(249));Assert.Empty(fake.Http.Received);
        fake.Time.Advance(TimeSpan.FromMilliseconds(1));await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1,fake.Http.Received.Count(r=>r.Url.AbsolutePath.EndsWith("/models",StringComparison.Ordinal)));
        Assert.Equal(PaletteState.Ready,engine.Current.Models["acme/nova-1"].State);
    }
}
