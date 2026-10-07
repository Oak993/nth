using System.Security.Cryptography;
using Marrow.Core.Port.ModelsPalette;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
namespace Marrow.Services.Tests.Port.ModelsPalette;
[Trait("Decision","ED-NEW-modelspalette-11")]
public sealed class PaletteSourceChainCompletionTests
{
    [Fact]
    public async Task Mono_avatar_uses_only_the_validated_HF_website_vote_and_favicon()
    {
        using var fake=new PalettePipelineFixture();var original=fake.Http.Answer!;
        fake.Http.Answer=r=>r.Url.Host switch
        {
            "cdn-avatars.huggingface.co"=>Image([137,80,78,71,13,10,26,10,2]),
            "t0.gstatic.com"=>Image([137,80,78,71,13,10,26,10,1]),
            "huggingface.co" when !r.Url.AbsolutePath.StartsWith("/api/",StringComparison.Ordinal)=>InProcessHttpHandler.Reply(200,"<html><body><a href=\"https://acme.example/about\" rel=\"nofollow\">Website</a></body></html>","text/html"),
            _=>original(r)
        };
        using var http=new PaletteHttp(fake.Http,fake.Time);var decoder=new MonoPaletteDecoder(fake);
        var chain=new PaletteSeedChain(http,decoder,fake.Time);
        var answer=await chain.Find("acme",null,"acme","",CancellationToken.None);
        Assert.True(answer.Decisive);Assert.False(answer.Seed!.Mono);
        Assert.Contains(fake.Http.Received,r=>r.Url.Host=="t0.gstatic.com"&&r.Url.Query.Contains("acme.example",StringComparison.Ordinal));
        Assert.All(fake.Http.Received,r=>Assert.Contains(r.Url.Host,new[]{"huggingface.co","cdn-avatars.huggingface.co","t0.gstatic.com"}));
    }
    [Fact]
    public async Task Verified_placeholder_is_rejected_before_decode_and_an_unavailable_page_never_falls_through()
    {
        using var fake=new PalettePipelineFixture();var original=fake.Http.Answer!;byte[] placeholder=[137,80,78,71,13,10,26,10,2];
        fake.Http.Answer=r=>r.Url.Host switch
        {
            "cdn-avatars.huggingface.co"=>Image(placeholder),
            "huggingface.co" when !r.Url.AbsolutePath.StartsWith("/api/",StringComparison.Ordinal)=>InProcessHttpHandler.Reply(403,"blocked","text/html"),
            _=>original(r)
        };
        var pack=PaletteReleasePack.Bake(fake.Pack,1,[],placeholders:new(){["hf"]=[Convert.ToHexString(SHA256.HashData(placeholder))]});
        using var http=new PaletteHttp(fake.Http,fake.Time);var decoder=new MonoPaletteDecoder(fake);
        var chain=new PaletteSeedChain(http,decoder,fake.Time,pack);
        var answer=await chain.Find("acme",null,"acme","acme.example",CancellationToken.None);
        Assert.False(answer.Decisive);Assert.Equal(0,decoder.Calls);Assert.DoesNotContain(fake.Http.Received,r=>r.Url.Host=="t0.gstatic.com");
    }
    private static System.Net.Http.HttpResponseMessage Image(byte[] bytes)
    {var reply=InProcessHttpHandler.Reply(200,"","image/png");reply.Content=new System.Net.Http.ByteArrayContent(bytes);reply.Content.Headers.ContentType=new("image/png");return reply;}
}
