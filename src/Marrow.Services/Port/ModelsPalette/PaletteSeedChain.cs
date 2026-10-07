// PORT-ONLY: AC-3 S2 priority chain. Unavailable NEVER becomes Miss, regardless of attempts;
// ED-NEW-models-palette-6. No race may select a lower source while a higher source is unavailable.
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using Marrow.Core.Port.ModelsPalette;
namespace Marrow.Services.Port.ModelsPalette;
internal sealed record SeedEvidence(string Source,string Sha256,byte[] Rgba64);
internal sealed record SeedAnswer(ColourSeed? Seed,SeedEvidence[] Evidence,string Reason)
{public bool Decisive=>Seed is not null;}
internal sealed class PaletteSeedChain(PaletteHttp http,ILogoDecoder decoder,TimeProvider time,PaletteReleasePack? pack=null)
{
    private readonly Queue<long> hfCalls=[];
    private long retryUntil;
    public void AdoptPack(PaletteReleasePack next)=>pack=next;
    public async Task<SeedAnswer> Find(string maker,string? name,string org,string? domain,CancellationToken ct,bool validatedDomain=false)
    {
        if(pack?.Seeds.TryGetValue(maker,out var authoritative)==true)return new(authoritative.Seed,authoritative.Evidence.Select(e=>new SeedEvidence(e.Source,e.Sha256,e.Rgba64)).ToArray(),"");
        var pinned=PinnedSeedColours.Find(maker,name,org);
        if(pinned is not null)return new(pinned,[new("pinned-toc",PinnedSeedColours.Digest,[])],"");
        using var budget=new CancellationTokenSource(TimeSpan.FromSeconds(8),time);
        using var cancelled=CancellationTokenSource.CreateLinkedTokenSource(ct,budget.Token);
        var evidence=new List<SeedEvidence>();
        using var speculative=CancellationTokenSource.CreateLinkedTokenSource(cancelled.Token);
        var lowerDomain=pack?.MakerDomains.GetValueOrDefault(maker)??(validatedDomain?domain:null);
        Task<SeedAnswer>? lower=string.IsNullOrEmpty(lowerDomain)?null:Image(Favicon(lowerDomain),speculative.Token);
        try
        {
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
            var reset=ResetSeconds(reply);
            if(reset>0)retryUntil=Math.Max(retryUntil,now+(long)(reset*frequency));
            if(reply.Status==429)
            {
                var seconds=RetrySeconds(reply);
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
                var page=await http.Get(new Uri("https://huggingface.co/"+Uri.EscapeDataString(parts[1])),1024*1024,4,false,cancelled.Token);
                if(page is not {Status:200,Type:"text/html"}||!Encoding.UTF8.GetString(page.Body).Contains("<html",StringComparison.OrdinalIgnoreCase))return new(null,evidence.ToArray(),"HF website unavailable");
                evidence.Add(new(page.Url.AbsoluteUri,Convert.ToHexString(SHA256.HashData(page.Body)),[]));
                if(domain is null)return new(null,evidence.ToArray(),"Domain inputs unavailable");
                var votes=new List<MakerDomainVote>();if(domain.Length>0)votes.Add(new("https://"+domain,true));
                foreach(Match link in Regex.Matches(Encoding.UTF8.GetString(page.Body),"<a\\b(?<attrs>[^>]*?href=[\"'](?<url>[^\"']+)[\"'][^>]*)>(?<text>.*?)</a>",RegexOptions.CultureInvariant|RegexOptions.IgnoreCase|RegexOptions.Singleline))
                    if(Regex.Replace(link.Groups["text"].Value,"<[^>]+>","",RegexOptions.CultureInvariant).Trim().Equals("Website",StringComparison.OrdinalIgnoreCase))votes.Add(new(WebUtility.HtmlDecode(link.Groups["url"].Value),true));
                domain=DomainVote.Choose(votes)??"";miss=true;break;
            }
            catch(JsonException){return new(null,[],"HF malformed");}
        }
        if(!miss)return new(null,[],"HF unavailable");
        domain=pack?.MakerDomains.GetValueOrDefault(maker)??domain;
        if(domain is null)return new(null,evidence.ToArray(),"Domain inputs unavailable");
        if(domain.Length==0)return new(new(true,0,0,0),evidence.ToArray(),"");
        var answer=await (lower is not null&&lowerDomain==domain?lower:Image(Favicon(domain),cancelled.Token));if(!answer.Decisive)return answer;
        return answer with {Evidence=[..evidence,..answer.Evidence]};
        }finally{speculative.Cancel();}
    }
    private static Uri Favicon(string domain)=>new("https://t0.gstatic.com/faviconV2?client=SOCIAL&type=FAVICON&fallback_opts=TYPE,SIZE,URL&url="+Uri.EscapeDataString("https://"+domain)+"&size=256");
    private async Task<SeedAnswer> Image(Uri url,CancellationToken ct)
    {
        var reply=await http.Get(url,512*1024,4,false,ct);
        if(reply is null||!PaletteHttp.ImageMagic(reply.Body,reply.Type))return new(null,[],"Image unavailable");
        var hash=Convert.ToHexString(SHA256.HashData(reply.Body));var kind=url.Host=="t0.gstatic.com"?"favicon":"hf";
        if((reply.Status==200||kind=="favicon"&&reply.Status==404)&&pack?.PlaceholderHashes.TryGetValue(kind,out var hashes)==true&&hashes.Contains(hash,StringComparer.OrdinalIgnoreCase))return new(new(true,0,0,0),[new(url.AbsoluteUri,hash,[])],"");
        if(reply.Status!=200)return new(null,[],"Image unavailable");
        LogoPixels? decoded;
        try{decoded=decoder.Decode(reply.Body,reply.Type);}catch(Exception){return new(null,[],"Image decoder unavailable");}
        if(decoded is null)return new(null,[],"Image decoder unavailable");
        var result=DominantColour.Extract(decoded);
        var evidence=new SeedEvidence(url.AbsoluteUri,hash,result.Rgba64.ToArray());
        // A decoded tiny/transparent negative is decisive and may proceed to the next source.
        return new(result.Decisive?result.Seed:new(true,0,0,0),[evidence],"");
    }
    private static double RetrySeconds(PaletteHttpReply reply)
    {
        if(double.TryParse(reply.RetryAfter,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)&&double.IsFinite(n))return Math.Max(30,n);
        if(reply.Date is {} date&&DateTimeOffset.TryParse(reply.RetryAfter,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var until))return Math.Max(30,(until-date).TotalSeconds);
        return 300;
    }
    private static double ResetSeconds(PaletteHttpReply reply)
    {
        if(reply.Remaining=="0"&&double.TryParse(reply.Reset,NumberStyles.Float,CultureInfo.InvariantCulture,out var reset)&&double.IsFinite(reset))return Math.Max(0,reset);
        if(reply.RateLimit is {} limit&&Regex.IsMatch(limit,@"\br=0\b",RegexOptions.CultureInvariant))
        {var m=Regex.Match(limit,@"\bt=(\d+)\b",RegexOptions.CultureInvariant);if(m.Success&&double.TryParse(m.Groups[1].Value,NumberStyles.None,CultureInfo.InvariantCulture,out var seconds))return seconds;}
        return 0;
    }
}
