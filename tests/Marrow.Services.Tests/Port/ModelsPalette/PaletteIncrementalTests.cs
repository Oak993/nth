using Marrow.Core.Port.ModelsPalette;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using System.Text.Json;

namespace Marrow.Services.Tests.Port.ModelsPalette;

// T44: repeated checks must retain computed tables; changed inputs must still publish correct shades.
[Trait("Decision", "ED-617")]
public sealed class PaletteIncrementalTests
{
    [Fact]
    public async Task A_new_market_boundary_recomputes_changed_ranks_and_keeps_unaffected_tables()
    {
        using var roots = new ModelsPaletteRoots();
        using var fixture = new PalettePipelineFixture();
        var price = "0.000003";
        var original = fixture.Http.Answer ?? throw new InvalidOperationException("Fixture must provide catalogue replies.");
        fixture.Http.Answer = r => r.Url.AbsolutePath.EndsWith("/models", StringComparison.Ordinal)
            ? InProcessHttpHandler.Reply(200, "{\"data\":[{\"id\":\"openai/example-a\",\"created\":1790000000,\"pricing\":{\"completion\":\"" + price + "\"}},{\"id\":\"openai/example-b\",\"created\":1790000000,\"pricing\":{\"completion\":\"0.000006\"}}]}", "application/json", ("Date", fixture.Date))
            : original(r);
        var pack = fixture.Pack.WithBaseline([new("anthropic/claude-sonnet-4", "anthropic", 3, 1790000000, 200000, true)]);
        using var engine = new PaletteEngine(roots.New(), fixture.Http, fixture, fixture.Time, pack);
        await engine.CheckNow();
        var before = engine.Current;
        price = "0.000009";
        fixture.Date = "Wed, 07 Oct 2026 12:20:00 GMT";
        fixture.Time.Advance(TimeSpan.FromHours(6));
        await engine.CheckNow();
        Assert.NotEqual(before.Boundary, engine.Current.Boundary);
        Assert.NotEqual(before.Models["openai/example-a"].Position, engine.Current.Models["openai/example-a"].Position);
        Assert.NotEqual(before.Resolve("openai/example-a", "high", false), engine.Current.Resolve("openai/example-a", "high", false));
        Assert.Same(before.Resolve("anthropic/claude-sonnet-4", "high", false), engine.Current.Resolve("anthropic/claude-sonnet-4", "high", false));
    }

    [Fact]
    public async Task An_agreed_maker_seed_change_rebuilds_only_that_makers_shades()
    {
        using var roots = new ModelsPaletteRoots();
        using var fixture = new PalettePipelineFixture();
        using var engine = new PaletteEngine(roots.New(), fixture.Http, fixture, fixture.Time, fixture.Pack);
        await engine.CheckNow();
        var before = engine.Current;
        fixture.ImageVersion = 1;
        fixture.Date = "Thu, 08 Oct 2026 06:20:00 GMT";
        fixture.Time.Advance(TimeSpan.FromDays(1));
        await engine.CheckNow();
        fixture.Date = "Fri, 09 Oct 2026 06:20:00 GMT";
        fixture.Time.Advance(TimeSpan.FromDays(1));
        await engine.CheckNow();
        Assert.NotEqual(before.Resolve("acme/nova-1", "high", false), engine.Current.Resolve("acme/nova-1", "high", false));
        Assert.Same(before.Resolve("openai/gpt-6-astra", "high", false), engine.Current.Resolve("openai/gpt-6-astra", "high", false));
    }

    [Fact]
    public async Task An_unchanged_486_model_poll_allocates_less_than_eight_megabytes()
    {
        using var roots = new ModelsPaletteRoots();
        using var fixture = new PalettePipelineFixture();
        var body = JsonSerializer.Serialize(new { data = Enumerable.Range(0, 486).Select(i => new
        {
            id = "openai/model-" + i, name = "OpenAI: Model " + i, created = 1790000000,
            architecture = new { output_modalities = new[] { "text" } },
            pricing = new { completion = "0.000003" },
        }) });
        fixture.Http.Answer = r => InProcessHttpHandler.Reply(200,
            r.Url.AbsolutePath.EndsWith("/models", StringComparison.Ordinal) ? body : "{\"data\":[]}",
            "application/json", ("Date", fixture.Date));
        using var engine = new PaletteEngine(roots.New(), fixture.Http, fixture, fixture.Time, fixture.Pack);
        await engine.CheckNow();
        var before = await engine.PrepareRender([], [], false, (_, _) => GC.GetAllocatedBytesForCurrentThread());
        var snapshot = engine.Current;
        await engine.CheckNow();
        var bytes = await engine.PrepareRender([], [], false, (_, _) => GC.GetAllocatedBytesForCurrentThread()) - before;
        Assert.Same(snapshot, engine.Current);
        Assert.True(bytes < 8_000_000, $"unchanged 486-model poll allocated {bytes:N0} bytes");
    }

    [Fact]
    public async Task Adding_an_unrelated_model_retains_existing_shade_tables()
    {
        using var roots = new ModelsPaletteRoots();
        using var fixture = new PalettePipelineFixture();
        using var engine = new PaletteEngine(roots.New(), fixture.Http, fixture, fixture.Time, fixture.Pack);
        await engine.CheckNow();
        var before = engine.Current;
        await engine.Observe(["another/new-model"]);
        Assert.Contains("another/new-model", engine.Current.Models.Keys);
        Assert.NotEqual(before.Digest, engine.Current.Digest);
        foreach (var shade in before.Shades)
            Assert.Same(shade.Value, engine.Current.Shades[shade.Key]);
    }

    [Fact]
    public async Task An_unchanged_poll_allocates_less_than_one_megabyte_on_the_palette_actor()
    {
        using var roots = new ModelsPaletteRoots();
        using var fixture = new PalettePipelineFixture();
        using var engine = new PaletteEngine(roots.New(), fixture.Http, fixture, fixture.Time, fixture.Pack);
        await engine.CheckNow();
        var before = await engine.PrepareRender([], [], false, (_, _) => GC.GetAllocatedBytesForCurrentThread());
        await engine.CheckNow();
        var bytes = await engine.PrepareRender([], [], false, (_, _) => GC.GetAllocatedBytesForCurrentThread()) - before;
        Assert.True(bytes < 1_000_000, $"unchanged palette poll allocated {bytes:N0} bytes");
    }

    [Fact]
    public void Reloaded_equivalent_models_share_their_immutable_shades()
    {
        var palette = PaletteSourcePack.Default.AuthoritativeForMaker("openai");
        var models = new[]
        {
            new PaletteModel("openai/a", "openai", "A", PaletteState.Ready, palette, new(.5, 1, 1), false),
            new PaletteModel("openai/b", "openai", "B", PaletteState.Ready, palette, new(.5, 1, 1), false),
        };
        var before = PaletteSnapshot.Create(1, models);
        var reloaded = PaletteSnapshotCodec.Decode(PaletteSnapshotCodec.Encode(before));
        Assert.Equal(before.Digest, reloaded.Digest);
        Assert.Equal(before.Resolve("openai/a", "high", false), reloaded.Resolve("openai/a", "high", false));
        Assert.Same(reloaded.Resolve("openai/a", "high", false), reloaded.Resolve("openai/b", "high", false));
    }
}
