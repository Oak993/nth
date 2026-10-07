using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.Services.Tests.Port.ModelsPalette;
[Trait("Decision","ED-NEW-modelspalette-11")]
public sealed class PalettePackAliasSnapshotTests
{
    [Fact]
    public void Pack_zero_and_global_lineups_apply_the_pack_alias_policy_before_first_draw()
    {
        using var fake=new PalettePipelineFixture();var source=fake.Pack.WithBaseline(fake.Pack.Baseline.Select(f=>f with {Maker="openai-labs"}).ToArray());
        var pack=PaletteReleasePack.Bake(source,1,[],aliases:new(){["openai-labs"]="openai"});var read=PaletteReleasePack.Read(pack.Encode());
        var model=Assert.Single(read.Snapshot0.Models.Values);Assert.Equal("openai",model.Maker);Assert.Equal(source.AuthoritativeForMaker("openai"),model.Palette);
        Assert.Equal(PaletteState.Known,model.State);Assert.Equal("openai",Assert.Single(read.Source.Baseline).Maker);
    }
}
