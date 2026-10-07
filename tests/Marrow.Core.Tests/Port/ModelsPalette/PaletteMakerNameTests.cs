using Marrow.Core.Port.ModelsPalette;
namespace Marrow.Core.Tests.Port.ModelsPalette;
[Trait("Decision","ED-NEW-modelspalette-11")]
public sealed class PaletteMakerNameTests
{
    [Fact]
    public void Boundary_names_and_known_name_prefixes_follow_family_rules_and_unknown_names_stay_provider_owned()
    {
        ModelPaletteFact[] facts=[new("a/model","a",0,0,0,true),new("b/model","b",0,0,0,true)];
        var names=new Dictionary<string,string>{{"a/model","Identical public name"},{"b/model","Identical public name"}};
        Assert.Equal("a",Makers.Resolve("cursor","unlisted",facts,"Identical public name",names).Maker);
        Assert.Equal("nvidia",Makers.Resolve("cursor","unlisted",[],"NVIDIA: Nemotron").Maker);
        Assert.Equal("anthropic",Makers.Resolve("cursor","claude-opus-5.5",[],"NVIDIA: Nemotron").Maker);
        Assert.True(Makers.Resolve("cursor","unlisted",[],"Invented Company: Nova").ProviderOwn);
        Assert.Equal("meta",Makers.Resolve("pi","openrouter/meta-llama/llama-4",[]).Maker);
    }
}
