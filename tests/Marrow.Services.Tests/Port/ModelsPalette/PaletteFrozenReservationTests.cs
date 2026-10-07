using Marrow.Core.Port.ModelsPalette;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.Services.Tests.Port.ModelsPalette;
// PORT-ONLY: PLAN 5.3 fixed/actually-baked reservations; draft swatches are not baked seeds. ED-NEW-models-palette-6.
[Trait("Decision","ED-NEW-modelspalette-6")]
public sealed class PaletteFrozenReservationTests
{
    [Fact]
    public async Task Draft_example_swatches_cannot_change_a_new_makers_derived_palette()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();
        var locked=fake.Pack.MakerPalette.Keys.Select(fake.Pack.AuthoritativeForMaker).Where(p=>p!=fake.Pack.Neutral).Distinct().Concat(fake.Pack.Reserved).ToArray();
        var seed=DominantColour.Extract(fake.Decode([0],"image/png")!).Seed!;
        var expected=PaletteDeriver.Derive(seed,locked);
        using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);await engine.CheckNow();
        Assert.Equal(expected.Palette.Stops,engine.Current.Models["acme/nova-1"].Palette.Stops);
    }
}
