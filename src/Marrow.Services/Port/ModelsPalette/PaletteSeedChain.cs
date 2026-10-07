// PORT-ONLY: AC-3 S2 priority chain. Unavailable NEVER becomes Miss, regardless of attempts;
// ED-NEW-models-palette-6. No race may select a lower source while a higher source is unavailable.
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Marrow.Core.Port.ModelsPalette;
namespace Marrow.Services.Port.ModelsPalette;
internal sealed record SeedEvidence(string Source,string Sha256,byte[] Rgba64);
internal sealed record SeedAnswer(ColourSeed? Seed,SeedEvidence[] Evidence,string Reason)
{public bool Decisive=>Seed is not null;}
internal sealed class PaletteSeedChain(PaletteHttp http,ILogoDecoder decoder,TimeProvider time)
{
    private readonly Queue<long> hfCalls=[];
    private long retryUntil;
    public async Task<SeedAnswer> Find(string maker,string? name,string org,string? domain,CancellationToken ct)
    {
        var pinned=PinnedSeedColours.Find(maker,name,org);
        if(pinned is not null)return new(pinned,[new("pinned-toc",PinnedSeedColours.Digest,[])],"");
        using var budget=new CancellationTokenSource(TimeSpan.FromSeconds(8),time);
        using var cancelled=CancellationTokenSource.CreateLinkedTokenSource(ct,budget.Token);
        var evidence=new List<SeedEvidence>();
        // Exact identity probes, never fuzzy search. A validated org miss can be a user,
        // followed by the maker slug when the public HF org and maker differ; at most three calls.
        var probes=new[]{"organizations/"+org,"users/"+org,"organizations/"+maker}.Distinct(StringComparer.Ordinal).ToArray();
        var miss=false;
        foreach(var probe in probes)
        {
            var now=time.GetTimestamp();var frequency=time.TimestampFrequency;
            while(hfCalls.TryPeek(out var first)&&(now-first)/(double)frequency>=300)hfCalls.Dequeue();
            if(now<retryUntil||hfCalls.Count>=60)return new(null,[],"HF budget unavailable");
            hfCalls.Enqueue(now);
            var parts=probe.Split('/');var reply=await http.Get(new Uri("https://huggingface.co/api/"+parts[0]+"/"+Uri.EscapeDataString(parts[1])+"/overview"),1024*1024,4,false,cancelled.Token);
            if(reply is null)return new(null,[],"HF unavailable");
            if(reply.Status==429)
            {
                var seconds=double.TryParse(reply.RetryAfter,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)?Math.Max(30,n):300;
                retryUntil=now+(long)(seconds*frequency);return new(null,[],"HF rate limited");
            }
            if(reply.Type!="application/json")return new(null,[],"HF JSON unavailable");
            try
            {
                using var doc=JsonDocument.Parse(reply.Body);var root=doc.RootElement;
                if(reply.Status is 404 or 410&&root.ValueKind==JsonValueKind.Object&&root.TryGetProperty("error",out var error)&&error.ValueKind==JsonValueKind.String)
                {
                    evidence.Add(new(reply.Url.AbsoluteUri,Convert.ToHexString(SHA256.HashData(reply.Body)),[]));miss=true;continue;
                }
                if(reply.Status!=200||root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("avatarUrl",out var av)||av.ValueKind!=JsonValueKind.String)return new(null,[],"HF unavailable");
                if(!Uri.TryCreate(av.GetString(),UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host is not("huggingface.co" or "cdn-avatars.huggingface.co"))return new(null,[],"HF avatar URL unavailable");
                var image=await Image(uri,cancelled.Token);if(!image.Decisive)return image;
                evidence.AddRange(image.Evidence);if(!image.Seed!.Mono)return image with {Evidence=evidence.ToArray()};
                // HTML domain evidence and the placeholder pack are not yet available. A mono
                // avatar cannot turn missing higher-priority domain inputs into a decisive miss.
                return new(null,evidence.ToArray(),"HF website/placeholder evidence unavailable");
            }
            catch(JsonException){return new(null,[],"HF malformed");}
        }
        if(!miss)return new(null,[],"HF unavailable");
        if(domain is null)return new(null,evidence.ToArray(),"Domain inputs unavailable");
        if(domain.Length==0)return new(new(true,0,0,0),evidence.ToArray(),"");
        var favicon=new Uri("https://t0.gstatic.com/faviconV2?client=SOCIAL&type=FAVICON&fallback_opts=TYPE,SIZE,URL&url="+Uri.EscapeDataString("https://"+domain)+"&size=256");
        var answer=await Image(favicon,cancelled.Token);if(!answer.Decisive)return answer;
        return answer with {Evidence=[..evidence,..answer.Evidence]};
    }
    private async Task<SeedAnswer> Image(Uri url,CancellationToken ct)
    {
        var reply=await http.Get(url,512*1024,4,false,ct);
        if(reply is null||reply.Status!=200||!PaletteHttp.ImageMagic(reply.Body,reply.Type))return new(null,[],"Image unavailable");
        LogoPixels? decoded;
        try{decoded=decoder.Decode(reply.Body,reply.Type);}catch(Exception){return new(null,[],"Image decoder unavailable");}
        if(decoded is null)return new(null,[],"Image decoder unavailable");
        var result=DominantColour.Extract(decoded);
        var evidence=new SeedEvidence(url.AbsoluteUri,Convert.ToHexString(SHA256.HashData(reply.Body)),result.Rgba64.ToArray());
        // A decoded tiny/transparent negative is decisive and may proceed to the next source.
        return new(result.Decisive?result.Seed:new(true,0,0,0),[evidence],"");
    }
}
