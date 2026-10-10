using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.ModelsPalette;

// ED-NEW-stutterfix-6: a list of cold models observed one by one (ModelShades.Resolve for each row of a long picker) queued one actor job
// each, and each published a whole snapshot and saved it: 487 snapshots, about 1 GB/s of allocation while the OpenRouter picker opened.
// Observations queued together now publish once, after the last of them, with the same snapshot and the same saved bytes as one
// observation of them all; an observation on its own still publishes when it is done.
[Trait("Decision", "ED-NEW-stutterfix-6")]
public sealed class PaletteObserveBatchTests
{
    private static readonly string[] Ids = Enumerable.Range(0, 200).Select(i => "acme/nova-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();

    private static byte[] Saved(PaletteEngine engine) => File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(engine.SeedsPath)!, "snapshot-v1.json"));

    [Fact]
    public async Task Observations_queued_together_publish_one_snapshot_equal_to_observing_them_all_at_once()
    {
        using var roots = new ModelsPaletteRoots();
        using var fixture = new PalettePipelineFixture();
        using var one = new PaletteEngine(roots.New(), fixture.Http, fixture, fixture.Time, fixture.Pack);
        using var many = new PaletteEngine(roots.New(), fixture.Http, fixture, fixture.Time, fixture.Pack);
        await one.Observe(["acme/first"]);
        await one.Observe(Ids);

        using var gate = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        var published = 0;
        many.Changed += _ => { if (Interlocked.Increment(ref published) == 1) { entered.Set(); gate.Wait(TimeSpan.FromSeconds(10)); } };
        var first = many.Observe(["acme/first"]); // publishes, and its Changed holds the actor while the rest queue up
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        var queued = Ids.Select(id => many.Observe([id])).ToArray();
        gate.Set();
        await first;
        await Task.WhenAll(queued);

        Assert.Equal(2, published);
        Assert.Equal(one.Current.Digest, many.Current.Digest);
        Assert.Equal(Saved(one), Saved(many));
    }

    [Fact]
    public async Task An_observation_on_its_own_publishes_when_it_is_done()
    {
        using var roots = new ModelsPaletteRoots();
        using var fixture = new PalettePipelineFixture();
        using var engine = new PaletteEngine(roots.New(), fixture.Http, fixture, fixture.Time, fixture.Pack);
        foreach (var id in Ids.Take(3))
        {
            await engine.Observe([id]);
            Assert.Contains(id, engine.Current.Models.Keys);
        }
        await engine.Observe([Ids[0]]); // nothing new: nothing published
        Assert.Contains(Ids[2], engine.Current.Models.Keys);
    }
}
