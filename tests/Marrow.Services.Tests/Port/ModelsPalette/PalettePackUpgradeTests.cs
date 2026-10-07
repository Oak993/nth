using Marrow.Core.Port.ModelsPalette;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.Services.Tests.Port.ModelsPalette;
[Trait("Decision","ED-NEW-modelspalette-11")]
public sealed class PalettePackUpgradeTests
{
    [Fact]
    public void Withdrawn_pack_rows_keep_their_last_facts_for_twenty_four_calendar_months()
    {
        using var fake=new PalettePipelineFixture();var original=fake.Pack;var first=PaletteReleasePack.Bake(original,1,[]);
        var when=DateTimeOffset.Parse("2026-10-07T06:20:00Z",System.Globalization.CultureInfo.InvariantCulture);
        var next=PaletteReleasePack.Bake(original.WithBaseline([]),2,[],first,catalogueDate:when);
        var tombstone=Assert.Single(next.Tombstones);Assert.Equal(original.Baseline[0],tombstone.Fact);Assert.Equal(when,tombstone.WithdrawnAt);
        Assert.Equal(first.Snapshot0.Models[original.Baseline[0].Id].Position,next.Snapshot0.Models[original.Baseline[0].Id].Position);
        var late=PaletteReleasePack.Bake(original.WithBaseline([]),3,[],next,catalogueDate:when.AddMonths(24));Assert.Empty(late.Tombstones);Assert.Empty(late.Source.Baseline);
        Assert.Throws<InvalidDataException>(()=>PaletteReleasePack.Adopt(first,next.Encode(),_=>false));
        Assert.Throws<InvalidDataException>(()=>PaletteReleasePack.Adopt(next,first.Encode(),_=>true));
    }
}
