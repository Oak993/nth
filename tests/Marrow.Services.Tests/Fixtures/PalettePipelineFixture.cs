using Marrow.Core.Port;
using Marrow.Core.Port.ModelsPalette;
using Marrow.Testing;
namespace Marrow.Services.Tests.Fixtures;
// PORT-ONLY: keyless public hosts answered in-process. All image samples are synthetic.
public sealed class PalettePipelineFixture : IDisposable,ILogoDecoder
{
    public InProcessHttpHandler Http { get; }=new();
    public ManualTimeProvider Time { get; }=new();
    public string Date="Wed, 07 Oct 2026 06:20:00 GMT";
    public bool FailModels,FailHf;
    public string Org="acme";
    public int ImageVersion;
    public PaletteSourcePack Pack=>PaletteSourcePack.Default.WithBaseline([new("openai/gpt-6-astra","openai",10,1790000000,1000000,true)]);
    public PalettePipelineFixture()
    {
        Http.Answer=r=>
        {
            if(r.Url.Host=="openrouter.ai"&&r.Url.AbsolutePath.EndsWith("/models",StringComparison.Ordinal))
                return InProcessHttpHandler.Reply(FailModels?503:200,"{\"data\":[{\"id\":\"acme/nova-1\",\"name\":\"Acme: Nova 1\",\"created\":1791300000,\"hugging_face_id\":\""+Org+"/nova-1\",\"architecture\":{\"output_modalities\":[\"text\"]},\"pricing\":{\"completion\":\"0.000003\"}}]}","application/json",("Date",Date));
            if(r.Url.Host=="openrouter.ai"&&r.Url.AbsolutePath.EndsWith("/providers",StringComparison.Ordinal))
                return InProcessHttpHandler.Reply(200,"{\"data\":[]}","application/json",("Date",Date));
            if(r.Url.Host=="huggingface.co"&&r.Url.AbsolutePath.EndsWith("/overview",StringComparison.Ordinal))
                return InProcessHttpHandler.Reply(FailHf?503:200,"{\"avatarUrl\":\"https://cdn-avatars.huggingface.co/v1/acme.png\",\"fullname\":\"Acme\"}","application/json",("Date",Date));
            if(r.Url.Host=="cdn-avatars.huggingface.co")
            {
                var reply=InProcessHttpHandler.Reply(200,"","image/png",("Date",Date));
                reply.Content=new System.Net.Http.ByteArrayContent([137,80,78,71,13,10,26,10,(byte)ImageVersion]);
                reply.Content.Headers.ContentType=new("image/png");return reply;
            }
            throw new InvalidOperationException("Unexpected fake-only request: "+r.Url);
        };
    }
    public LogoPixels? Decode(byte[] bytes,string type)=>new(64,64,Enumerable.Range(0,4096).SelectMany(_=>bytes[^1]==0?new byte[]{10,170,110,255}:new byte[]{90,100,210,255}).ToArray());
    public void Dispose()=>Http.Dispose();
}
