using System.Globalization;
using Marrow.Core.Port.ModelsPalette;
namespace Marrow.Core.Tests.Port.ModelsPalette;
[Trait("Decision", "ED-NEW-modelspalette-6")]
public sealed class PaletteIdentityTests
{
    [Fact]
    public void Routing_aliases_and_anchored_words_are_culture_independent()
    {
        var before=CultureInfo.CurrentCulture;
        try
        {
            foreach(var culture in new[]{"en-US","tr-TR","de-DE","ar-SA"})
            {
                CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo(culture);
                Assert.Equal("moonshotai",Makers.Resolve("openrouter","KIMI-K2-Instruct",[]).Maker);
                Assert.Equal("meta",Makers.Resolve("pi","openrouter/meta-llama/llama-4",[]).Maker);
                Assert.True(Makers.Resolve("openrouter","openrouter/auto",[]).Router);
                foreach(var unknown in new[]{"hermes-3-llama-3.1-70b","dolphin-mistral-24b","aion-rp-llama-3.1-8b","solar-pro4"})
                    Assert.True(Makers.Resolve("openrouter",unknown,[]).ProviderOwn);
            }
        }
        finally{CultureInfo.CurrentCulture=before;}
    }
    [Fact]
    public void Domain_votes_use_the_icann_suffix_and_reject_aggregators_and_guessed_domains()
    {
        Assert.Equal("acme.edu.cn",DomainVote.Registrable("https://docs.acme.edu.cn/legal"));
        Assert.Equal("acme.com",DomainVote.Choose([new("https://docs.acme.com",false),new("https://status.acme.com",false)]));
        Assert.Null(DomainVote.Choose([new("https://acme.com",false)]));
        Assert.Equal("acme.com",DomainVote.Choose([new("https://acme.com",true)]));
        Assert.Null(DomainVote.Choose([new("https://github.com/acme",true),new("https://huggingface.co/acme",true)]));
        Assert.Null(DomainVote.Choose([]));
    }
    [Fact]
    public void Draft_swatches_cannot_be_mistaken_for_baked_seed_authority()
    {
        var pack=PaletteSourcePack.Default;
        Assert.Equal(pack.Neutral,pack.AuthoritativeForMaker("nvidia"));
        Assert.Equal(pack.Neutral,pack.AuthoritativeForMaker("qwen"));
        Assert.Equal(pack.ForMaker("anthropic"),pack.AuthoritativeForMaker("anthropic"));
        Assert.Contains(pack.Reserved,p=>p.Key=="Purple");Assert.Contains(pack.Reserved,p=>p.Key=="Fast");Assert.Contains(pack.Reserved,p=>p.Key=="FastFill");
    }
}
