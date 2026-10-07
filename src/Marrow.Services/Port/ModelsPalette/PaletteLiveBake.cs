// PORT-ONLY: owner-local C22 capture + same-engine bake; OD-0058/0065, ED-NEW-models-palette-19.
// All HTTP is captured behind the production transport seam; no supplied seed or hash is trusted.
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Marrow.Core.Port.ModelsPalette;
namespace Marrow.Services.Port.ModelsPalette;
public sealed record PaletteSourceCapture(string Url,int Status,string Type,DateTimeOffset FetchedAt,DateTimeOffset? Date,int Age,string Sha256,long Bytes,string Path);
public static class PaletteLiveBake
{
    private static string Favicon(string domain)=>"https://t0.gstatic.com/faviconV2?client=SOCIAL&type=FAVICON&fallback_opts=TYPE,SIZE,URL&url="+Uri.EscapeDataString("https://"+domain)+"&size=256";
    public static async Task<PaletteReleasePack> Capture(string output,HttpMessageHandler transport,ILogoDecoder decoder,TimeProvider? time=null,PaletteSourcePack? source=null,PaletteReleasePack? previous=null)
    {
        time??=TimeProvider.System;source??=PaletteSourcePack.Default;output=Path.GetFullPath(output);
        Directory.CreateDirectory(output);using var captured=new CaptureHandler(transport,output,time);using var http=new PaletteHttp(captured,time);
        var hf=await http.Get(new(Favicon("huggingface.co")),512*1024,8,true,CancellationToken.None);
        var missing=await http.Get(new(Favicon("marrow-colour-placeholder.invalid")),512*1024,8,true,CancellationToken.None);
        if(hf is not {Status:200}||missing is not {Status:404}||!PaletteHttp.ImageMagic(hf.Body,hf.Type)||!PaletteHttp.ImageMagic(missing.Body,missing.Type))throw new InvalidDataException("The official placeholder probes were unavailable. No pack was written.");
        string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
        var placeholders=new Dictionary<string,string[]>(StringComparer.Ordinal){["hf"]=[Hash(hf.Body)],["favicon"]=[Hash(hf.Body),Hash(missing.Body)]};
        var evidence=new List<PalettePlaceholderEvidence>();
        foreach(var (kind,reply) in new[]{("hf",hf),("favicon",hf),("favicon",missing)})
        {var capture=captured.Get(reply.Url.AbsoluteUri,Hash(reply.Body));evidence.Add(new(kind,capture.Url,capture.Sha256,capture.FetchedAt,capture.Bytes));}
        // A temporary seed-free pack supplies genuine placeholder identities to exactly the app's source chain.
        var probe=PaletteReleasePack.Bake(source,previous?.Version+1??1,[],placeholders:placeholders,placeholderEvidence:evidence);
        using var engine=new PaletteEngine(Path.Combine(output,"engine-data"),captured,decoder,time,source,releasePack:probe);
        var result=await engine.CheckNow();
        if(!result.CatalogueSucceeded)throw new InvalidDataException("The official public catalogue was unavailable. No pack was written.");
        var pack=await engine.BakeCaptured(captured,previous,placeholders,evidence);
        var path=Path.Combine(output,"pack.v"+pack.Version+".json.gz");File.WriteAllBytes(path,pack.Compress());
        File.WriteAllText(Path.Combine(output,"capture.json"),JsonSerializer.Serialize(new {sources=captured.Rows.OrderBy(x=>x.Url,StringComparer.Ordinal).ThenBy(x=>x.Sha256,StringComparer.Ordinal),snapshot=pack.Snapshot0.Digest,pending=pack.Snapshot0.Models.Values.Where(m=>m.State==PaletteState.Pending).Select(m=>m.Id).Order(StringComparer.Ordinal)},new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(Path.Combine(output,"SHA256SUMS"),string.Join('\n',Directory.EnumerateFiles(output,"*",SearchOption.AllDirectories).Where(p=>Path.GetFileName(p)!="SHA256SUMS").Order(StringComparer.Ordinal).Select(p=>Hash(File.ReadAllBytes(p))+"  "+Path.GetRelativePath(output,p)))+"\n");
        return pack;
    }
    public static int Run(string[] args,ILogoDecoder decoder)
    {
        if(args.Length is <2 or >3){Console.Error.WriteLine("Usage: --colour-bake-live <output-directory> [previous-pack.json.gz]");return 2;}
        try
        {
            PaletteReleasePack? previous=null;if(args.Length==3){using var input=File.OpenRead(args[2]);previous=PaletteReleasePack.ReadCompressed(input);}
            using var transport=new HttpClientHandler{AllowAutoRedirect=false};var pack=Capture(args[1],transport,decoder,previous:previous).GetAwaiter().GetResult();
            Console.WriteLine("Wrote authentic pack.v"+pack.Version+"; "+pack.Seeds.Count+" captured seeds; "+pack.Snapshot0.Models.Values.Count(x=>x.State==PaletteState.Pending)+" pending models; digest "+pack.Snapshot0.Digest);return 0;
        }
        catch(Exception e) when(e is IOException or HttpRequestException or InvalidDataException or JsonException or ArgumentException){Console.Error.WriteLine(e.Message);return 1;}
    }
    internal sealed class CaptureHandler:DelegatingHandler
    {
        private readonly string output;private readonly TimeProvider time;
        private readonly ConcurrentDictionary<(string,string),PaletteSourceCapture> rows=new();
        public IEnumerable<PaletteSourceCapture> Rows=>rows.Values;
        public CaptureHandler(HttpMessageHandler transport,string output,TimeProvider time):base(transport){this.output=output;this.time=time;Directory.CreateDirectory(Path.Combine(output,"responses"));}
        public PaletteSourceCapture Get(string url,string hash)=>rows.TryGetValue((url,hash),out var row)?row:throw new InvalidDataException("Missing raw source capture for "+url);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            var response=await base.SendAsync(request,cancellationToken).ConfigureAwait(false);
            var cap=request.RequestUri!.Host=="openrouter.ai"?4*1024*1024:1024*1024;
            if(response.Content.Headers.ContentLength>cap){response.Dispose();throw new HttpRequestException("Capture response exceeds its cap.");}
            using var input=await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);using var data=new MemoryStream();var buffer=new byte[8192];int read;
            while((read=await input.ReadAsync(buffer,cancellationToken).ConfigureAwait(false))>0){if(data.Length+read>cap){response.Dispose();throw new HttpRequestException("Capture response exceeds its cap.");}data.Write(buffer,0,read);}
            var bytes=data.ToArray();var hash=Convert.ToHexString(SHA256.HashData(bytes));var path=Path.Combine("responses",hash+".bin");
            // Same digest may occur in simultaneous requests. Atomic create-or-verify keeps content-addressed bytes.
            try{using var file=new FileStream(Path.Combine(output,path),FileMode.CreateNew,FileAccess.Write,FileShare.Read);file.Write(bytes);}catch(IOException) when(File.Exists(Path.Combine(output,path))){}
            var row=new PaletteSourceCapture(request.RequestUri.AbsoluteUri,(int)response.StatusCode,response.Content.Headers.ContentType?.MediaType??"",time.GetUtcNow(),response.Headers.Date,(int)(response.Headers.Age?.TotalSeconds??0),hash,bytes.Length,path);rows[(row.Url,hash)]=row;
            var headers=response.Content.Headers.Select(x=>(x.Key,Values:x.Value.ToArray())).ToArray();response.Content.Dispose();response.Content=new ByteArrayContent(bytes);foreach(var h in headers)response.Content.Headers.TryAddWithoutValidation(h.Key,h.Values);
            File.WriteAllText(Path.Combine(output,"responses",hash+".json"),JsonSerializer.Serialize(row));return response;
        }
    }
}
public sealed partial class PaletteEngine
{
    internal Task<PaletteReleasePack> BakeCaptured(PaletteLiveBake.CaptureHandler captures,PaletteReleasePack? previous,Dictionary<string,string[]> placeholders,IReadOnlyList<PalettePlaceholderEvidence> placeholderEvidence)=>Isolated(()=>
    {
        if(prefetch is null||serverSeconds<=0)throw new InvalidDataException("Bake requires captured public facts.");
        var facts=Facts();var rows=PublicRows();var additions=new List<PalettePackSeed>();
        foreach(var (maker,stored) in seeds.OrderBy(x=>x.Key,StringComparer.Ordinal))
        {
            if(previous?.Seeds.ContainsKey(maker)==true)continue;
            var seedEvidence=new List<PaletteSeedEvidence>();
            foreach(var e in stored.Evidence)
            {
                if(e.Source=="pinned-toc")
                {seedEvidence.Add(new(PinnedSeedColours.SourceUrl,e.Sha256,[],null,null));continue;}
                var capture=captures.Get(e.Source,e.Sha256);seedEvidence.Add(new(capture.Url,capture.Sha256,e.Rgba64,capture.FetchedAt,capture.Bytes,capture.Status,capture.Type));
            }
            var (_,org,name,_)=SeedInputs(maker,facts,rows);
            additions.Add(new(maker,stored.Seed,seedEvidence.ToArray(),PinnedName:name,PinnedOrg:org));
        }
        var catalogue=captures.Rows.Where(x=>x.Url=="https://openrouter.ai/api/v1/models"&&x.Status==200).OrderByDescending(x=>x.FetchedAt).First();
        var date=catalogue.Date??throw new InvalidDataException("Catalogue Date is absent.");
        if(date.ToUnixTimeSeconds()-catalogue.Age<date.ToUnixTimeSeconds()/21600*21600)throw new InvalidDataException("Catalogue is stale across the public boundary. Try after its cache refresh.");
        return PaletteReleasePack.Bake(pack.WithBaseline(facts),previous?.Version+1??1,additions,previous,placeholders:placeholders,placeholderEvidence:placeholderEvidence,catalogueDate:date,catalogueAge:catalogue.Age);
    });
}
