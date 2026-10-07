using System.Security.Cryptography;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
namespace Marrow.Services.Tests.Port.ModelsPalette;
[Trait("Decision","ED-NEW-modelspalette-11")]
public sealed class PaletteLowerSourceTests
{
    [Fact]
    public async Task A_validated_lower_domain_runs_speculatively_but_cannot_win_over_unavailable_HF()
    {
        using var fake=new PalettePipelineFixture();var original=fake.Http.Answer!;
        fake.Http.Answer=r=>r.Url.Host switch
        {
            "huggingface.co"=>InProcessHttpHandler.Reply(503,"offline","application/json"),
            "t0.gstatic.com"=>Image(200,[137,80,78,71,13,10,26,10,1]),
            _=>original(r)
        };
        using var http=new PaletteHttp(fake.Http,fake.Time);var chain=new PaletteSeedChain(http,fake,fake.Time);
        var answer=await chain.Find("acme",null,"acme","acme.example",CancellationToken.None,validatedDomain:true);
        Assert.False(answer.Decisive);Assert.Contains(fake.Http.Received,r=>r.Url.Host=="t0.gstatic.com");
        Assert.True(fake.Http.Received.ToList().FindIndex(r=>r.Url.Host=="t0.gstatic.com")<fake.Http.Received.ToList().FindIndex(r=>r.Url.Host=="huggingface.co"));
    }
    [Fact]
    public async Task Favicon_origins_exact_verified_404_placeholder_is_a_miss_without_decoding()
    {
        using var fake=new PalettePipelineFixture();byte[] bytes=[137,80,78,71,13,10,26,10,2];
        fake.Http.Answer=r=>r.Url.Host=="huggingface.co"?InProcessHttpHandler.Reply(404,"{\"error\":\"not found\"}","application/json"):Image(404,bytes);
        var pack=PaletteReleasePack.Bake(fake.Pack,1,[],placeholders:new(){["favicon"]=[Convert.ToHexString(SHA256.HashData(bytes))]});
        using var http=new PaletteHttp(fake.Http,fake.Time);var decoder=new MonoPaletteDecoder(fake);var chain=new PaletteSeedChain(http,decoder,fake.Time,pack);
        var answer=await chain.Find("acme",null,"acme","acme.example",CancellationToken.None);
        Assert.True(answer.Decisive);Assert.True(answer.Seed!.Mono);Assert.Equal(0,decoder.Calls);
        Assert.Contains(answer.Evidence,e=>e.Source.StartsWith("https://t0.gstatic.com/",StringComparison.Ordinal)&&e.Sha256==Convert.ToHexString(SHA256.HashData(bytes)));
    }
    private static System.Net.Http.HttpResponseMessage Image(int status,byte[] bytes)
    {var reply=InProcessHttpHandler.Reply(status,"","image/png");reply.Content=new System.Net.Http.ByteArrayContent(bytes);reply.Content.Headers.ContentType=new("image/png");return reply;}
}
