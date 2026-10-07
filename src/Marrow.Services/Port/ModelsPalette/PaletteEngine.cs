// PORT-ONLY: automatic AC-3 palette pipeline, global lineup windows, S2 seeds and D3 palettes;
// ED-NEW-models-palette-6. No UI control. Strike consumes immutable snapshots through the separate App seam.
using System.Collections.Frozen;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Marrow.Core.Port;
using Marrow.Core.Port.ModelsPalette;
namespace Marrow.Services.Port.ModelsPalette;
public sealed partial class PaletteEngine : Actor,IDisposable
{
    private readonly PaletteExecutor executor;
    private readonly PaletteHttp http;
    private readonly PaletteSeedChain chain;
    private readonly PaletteStore store;
    private PaletteSourcePack pack;
    private PaletteAnchor[] frozenReserved;
    private PaletteReleasePack? releasePack;
    private bool primed;
    private readonly TimeProvider time;
    private readonly IPaletteNetworkStatus network;
    private readonly bool ownsNetwork;
    private readonly CancellationTokenSource lifetime=new();
    private Dictionary<string,StoredSeed> seeds;
    private readonly Dictionary<string,DerivedPalette> derived=new(StringComparer.Ordinal);
    private readonly HashSet<string> observed=new(StringComparer.Ordinal);
    private readonly Dictionary<string,(long Tick,int Failures)> backoff=new(StringComparer.Ordinal);
    private StoredMarket? market,unpublished;
    private PublicModelCatalogueSnapshot? prefetch;
    private JsonDocument? providers;
    private long providersDay=-1,serverSeconds,serverTick;
    private PaletteSnapshot current;
    private Task<PaletteCheckResult>? running;
    private ITimer? triggerTimer,pollTimer,publicationTimer;
    private bool started,disposed;
    public PaletteSnapshot Current=>Volatile.Read(ref current);
    public string SeedsPath=>store.SeedsPath;
    public long LastCheckedAtServerSeconds=>serverSeconds;
    public event Action<PaletteSnapshot>? Changed;
    public event Action<PaletteRefreshProgress>? Progress;
    public PaletteEngine(string dataRoot,HttpMessageHandler transport,ILogoDecoder decoder,TimeProvider? time=null,PaletteSourcePack? pack=null,IPaletteNetworkStatus? network=null,PaletteReleasePack? releasePack=null)
        :this(dataRoot,transport,decoder,time??TimeProvider.System,pack??releasePack?.Source??PaletteSourcePack.Default,new PaletteExecutor(),network,releasePack){}
    private PaletteEngine(string root,HttpMessageHandler transport,ILogoDecoder decoder,TimeProvider time,PaletteSourcePack pack,PaletteExecutor executor,IPaletteNetworkStatus? network,PaletteReleasePack? releasePack):base(executor)
    {
        this.executor=executor;this.time=time;this.pack=pack;
        this.releasePack=releasePack;
        frozenReserved=releasePack?.FrozenReserved.ToArray()??pack.MakerPalette.Keys.Select(pack.AuthoritativeForMaker).Where(p=>p!=pack.Neutral).Distinct().Concat(pack.Reserved).ToArray();
        store=new(root);http=new(transport,time);chain=new(http,decoder,time,releasePack);
        this.network=network??new PaletteNetworkStatus();ownsNetwork=network is null;
        if(releasePack is not null){seeds=[];current=releasePack.Snapshot0;return;}
        seeds=store.LoadSeeds();market=store.LoadMarket();primed=true;
        if(market is {} cached){serverSeconds=cached.Date;serverTick=time.GetTimestamp();}
        current=Snapshot(known:true);
    }
    private long ServerNow=>serverSeconds+(long)((time.GetTimestamp()-serverTick)/(double)time.TimestampFrequency);
    private static string Hash(string x)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(x)));
    /// <summary>Automatic check; callers join the single in-flight pipeline. Does not reload account-specific provider lists.</summary>
    public Task<PaletteCheckResult> CheckNow()=>Isolated(async()=>
    {
        PrimeOnActor();
        if(running is {} joined)return await joined;
        running=Run(false);try{return await running;}finally{running=null;}
    });
    public Task Observe(IReadOnlyList<string> modelIds)=>Isolated(()=>
    {
        PrimeOnActor();
        var changed=false;foreach(var id in modelIds)changed|=observed.Add(id);
        if(changed){Publish();QueueCheck();}
    });
    public void NotifyResume()=>QueueCheck();
    public void NotifyCatalogueChanged(IReadOnlyList<string> ids)=>_ =Observe(ids);
    public void Start()
    {
        if(started||disposed)return;started=true;
        network.AvailabilityChanged+=NetworkAvailable;
        QueueCheck();
    }
    private void NetworkAvailable(bool available){if(available)QueueCheck();}
    private void QueueCheck()
    {
        if(disposed)return;
        // Every source (launch, resume, network, provider diff) enters this same 250ms debounce.
        lock(lifetime)
        {
            triggerTimer?.Dispose();triggerTimer=time.CreateTimer(_=>{if(!disposed)_=CheckNow();},null,TimeSpan.FromMilliseconds(250),Timeout.InfiniteTimeSpan);
        }
    }
    private async Task<PaletteCheckResult> Run(bool manual)
    {
        PrimeOnActor();
        var before=Current;var startedAt=time.GetTimestamp();
        using var stageBudget=new CancellationTokenSource(TimeSpan.FromSeconds(8),time);
        using var stage=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token,stageBudget.Token);
        Progress?.Invoke(new(PaletteRefreshPhase.Catalogue,0,1));
        var reply=await http.Get(new Uri("https://openrouter.ai/api/v1/models"),4*1024*1024,10,manual,manual?stage.Token:lifetime.Token);
        var catalogueOk=false;
        if(reply is {Status:200,Type:"application/json",Date:not null})
        {
            var parsed=PublicModelCatalogue.Parse(Encoding.UTF8.GetString(reply.Body));
            if(parsed is not null)
            {
                catalogueOk=true;serverSeconds=reply.Date.Value.ToUnixTimeSeconds();serverTick=time.GetTimestamp();prefetch=parsed;
                var generation=serverSeconds-reply.Age;var window=serverSeconds/21600;var boundary=window*21600;
                if((market is null||market.Window<window)&&(unpublished is null||unpublished.Window<window)&&generation>=boundary)
                {
                    var next=new StoredMarket(window,serverSeconds,reply.Age,Encoding.UTF8.GetString(reply.Body));
                    if(serverSeconds>=boundary+900)AcceptMarket(next);
                    else
                    {
                        unpublished=next;publicationTimer?.Dispose();
                        publicationTimer=time.CreateTimer(_=>_ =Isolated(()=>{if(unpublished is {} ready){AcceptMarket(ready);unpublished=null;Publish();}}),null,TimeSpan.FromSeconds(boundary+900-serverSeconds),Timeout.InfiniteTimeSpan);
                    }
                }
            }
        }
        if(!catalogueOk)
        {
            if(started){pollTimer?.Dispose();pollTimer=time.CreateTimer(_=>QueueCheck(),null,TimeSpan.FromSeconds(120),Timeout.InfiniteTimeSpan);}
            Progress?.Invoke(new(PaletteRefreshPhase.Done,1,1));
            return new(false,Current.Models.Count,ValueList<string>.Empty,ValueList<string>.Empty,ValueList<string>.Empty,Current.Boundary,Current.Digest);
        }
        if(!manual)Publish();
        Progress?.Invoke(new(PaletteRefreshPhase.Providers,1,1));
        var day=ServerNow/86400;
        if(providers is null||providersDay!=day)
        {
            var response=await http.Get(new Uri("https://openrouter.ai/api/v1/providers"),1024*1024,4,false,lifetime.Token);
            if(response is {Status:200,Type:"application/json"})
            {
                try
                {
                    var document=JsonDocument.Parse(response.Body);
                    if(document.RootElement.ValueKind==JsonValueKind.Object&&document.RootElement.TryGetProperty("data",out var data)&&data.ValueKind==JsonValueKind.Array)
                    {providers?.Dispose();providers=document;providersDay=day;}else document.Dispose();
                }
                catch(JsonException){}
            }
        }
        var facts=Facts();var publicRows=PublicRows();
        var makers=facts.Where(f=>f.Eligible).Select(f=>f.Maker).Concat(prefetch?.Models.Select(m=>AliasMaker(m.Maker))??[])
            .Concat(observed.Select(id=>ResolveMaker("openrouter",id,facts).Maker)).Where(m=>m!="openrouter").Distinct(StringComparer.Ordinal)
            .OrderByDescending(m=>observed.Any(id=>ResolveMaker("openrouter",id,facts).Maker==m)).ThenBy(m=>m,StringComparer.Ordinal).ToArray();
        var added=new List<string>();var seedChanged=false;var completed=0;
        var modelIds=facts.Where(f=>f.Eligible).Select(f=>f.Id).Concat(prefetch?.Models.Select(m=>m.Id)??[]).Concat(observed).Distinct(StringComparer.Ordinal).ToArray();
        var makerCounts=modelIds.GroupBy(id=>ResolveMaker("openrouter",id,facts).Maker,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.Count(),StringComparer.Ordinal);
        var checkedModels=0;
        var stageOnePublished=false;var batchAt=time.GetTimestamp();var batchCount=0;
        var visibleMakers=observed.Select(id=>ResolveMaker("openrouter",id,facts).Maker).ToHashSet(StringComparer.Ordinal);
        var pendingReasons=new Dictionary<string,string>(StringComparer.Ordinal);
        var makerJobs=new Dictionary<string,Task<SeedAnswer>>(StringComparer.Ordinal);
        Task<SeedAnswer> BeginMaker(string maker,CancellationToken ct)
        {
            if(makerJobs.TryGetValue(maker,out var joined))return joined;
            var (_,org,name,_)=SeedInputs(maker,facts,publicRows);
            return makerJobs[maker]=FindSafely(maker,name,org,VotedDomain(maker),ct);
        }
        async Task<SeedAnswer> FindSafely(string maker,string? name,string org,string? domain,CancellationToken ct)
        {try{return await chain.Find(maker,name,org,domain,ct,validatedDomain:domain is {Length:>0});}catch(Exception e) when(e is not OutOfMemoryException){return new(null,[],"Worker unavailable: "+e.GetType().Name);}}
        bool NeedsLookup(string maker)
        {
            if(Authority(maker)!=pack.Neutral)return false;
            var (key,_,_,input)=SeedInputs(maker,facts,publicRows);var old=seeds.GetValueOrDefault(maker);
            if(old is not null&&old.SeedVersion!=DominantColour.Version&&old.InputDigest==input)return false;
            if(old?.Key==key&&old.SeedVersion==DominantColour.Version&&old.Day==day)return false;
            if(!manual&&old?.Key!=key&&backoff.TryGetValue(maker,out var retry)&&time.GetElapsedTime(retry.Tick).TotalSeconds<new[]{30,300,3600,21600,86400}[Math.Clamp(retry.Failures-1,0,4)])return false;
            return true;
        }
        Progress?.Invoke(new(PaletteRefreshPhase.Makers,0,makers.Length));
        for(var makerIndex=0;makerIndex<makers.Length;makerIndex++)
        {
            var maker=makers[makerIndex];
            if(lifetime.IsCancellationRequested)break;
            Progress?.Invoke(new(PaletteRefreshPhase.Makers,completed,makers.Length){ModelsCompleted=checkedModels,ModelsTotal=modelIds.Length});
            checkedModels+=makerCounts.GetValueOrDefault(maker);
            if(manual&&!stageOnePublished&&(!visibleMakers.Contains(maker)||time.GetElapsedTime(startedAt).TotalSeconds>=8))
            {Progress?.Invoke(new(PaletteRefreshPhase.Applying,completed,makers.Length));Publish();stageOnePublished=true;batchAt=time.GetTimestamp();}
            if(manual&&stageOnePublished&&time.GetElapsedTime(startedAt).TotalSeconds>=60)break;
            if(Authority(maker)!=pack.Neutral){completed++;continue;}
            var (key,org,name,inputDigest)=SeedInputs(maker,facts,publicRows);
            var old=seeds.GetValueOrDefault(maker);
            // Extractor-only migrations may re-extract verified evidence, never retain old seed values.
            if(old is not null&&old.SeedVersion!=DominantColour.Version&&old.InputDigest==inputDigest)
            {
                var resampled=old.Evidence.Where(e=>e.Rgba64.Length==16384).Select(e=>DominantColour.Extract(new(64,64,e.Rgba64))).FirstOrDefault(r=>r.Decisive&&r.Seed?.Mono==false);
                if(resampled?.Seed is {} extracted){old=old with {Key=key,Seed=extracted,SeedVersion=DominantColour.Version,Candidate=null,CandidateHash=null,CandidateEvidence=null,CandidateDay=-1};seeds[maker]=old;seedChanged=true;}
            }
            var valid=old?.Key==key&&old.SeedVersion==DominantColour.Version;
            if(valid&&old!.Day==day){completed++;continue;}
            var retrySeconds=backoff.TryGetValue(maker,out var wait)?new[]{30,300,3600,21600,86400}[Math.Clamp(wait.Failures-1,0,4)]:0;
            if(!manual&&!valid&&retrySeconds>0&&time.GetElapsedTime(wait.Tick).TotalSeconds<retrySeconds){completed++;continue;}
            var token=manual&&!stageOnePublished?stage.Token:lifetime.Token;
            var ownJob=BeginMaker(maker,token);
            // At most one look-ahead maker: two independent chains in flight, four HTTP requests globally.
            // Publication still consumes their results in the deterministic visible-first/ordinal order.
            var ahead=makers.Skip(makerIndex+1).FirstOrDefault(m=>NeedsLookup(m)&&(!manual||stageOnePublished||visibleMakers.Contains(m)));
            if(ahead is not null)_ =BeginMaker(ahead,token);
            var answer=await ownJob;
            if(answer.Decisive)
            {
                var next=new StoredSeed(key,answer.Seed!,answer.Evidence,day,SeedVersion:DominantColour.Version,InputDigest:inputDigest);
                if(!valid){seeds[maker]=next;added.Add(maker);seedChanged=true;}
                else
                {
                    var evidenceHash=EvidenceHash(answer.Evidence);var oldHash=EvidenceHash(old!.Evidence);
                    if(evidenceHash==oldHash){seeds[maker]=old with {Day=day,CandidateHash=null,Candidate=null,CandidateEvidence=null,CandidateDay=-1};seedChanged=true;}
                    else if(old.CandidateHash==evidenceHash&&old.CandidateDay!=day)
                    {seeds[maker]=next;seedChanged=true;}
                    else
                    {seeds[maker]=old with {Day=day,CandidateHash=evidenceHash,Candidate=answer.Seed,CandidateEvidence=answer.Evidence,CandidateDay=day};seedChanged=true;}
                }
                backoff.Remove(maker);
            }
            else
            {
                pendingReasons[maker]=answer.Reason;
                if(!manual){var failures=backoff.GetValueOrDefault(maker).Failures+1;backoff[maker]=(time.GetTimestamp(),failures);}
                // Scheduling affects only retries. Source failures never alter authority or choose a lower source.
            }
            completed++;Progress?.Invoke(new(PaletteRefreshPhase.Makers,completed,makers.Length){ModelsCompleted=checkedModels,ModelsTotal=modelIds.Length});
            if(stageOnePublished&&(++batchCount>=20||time.GetElapsedTime(batchAt).TotalSeconds>=2))
            {Publish();batchCount=0;batchAt=time.GetTimestamp();}
        }
        if(seedChanged)try{store.SaveSeeds(seeds);}catch(IOException){}catch(UnauthorizedAccessException){}
        Progress?.Invoke(new(PaletteRefreshPhase.Applying,0,1));Publish();
        Progress?.Invoke(new(PaletteRefreshPhase.Done,1,1));
        if(started)
        {
            pollTimer?.Dispose();var nextBoundary=(ServerNow/21600+1)*21600;
            var seconds=!catalogueOk?120:Math.Max(1,nextBoundary+Random.Shared.Next(60,601)-ServerNow);
            pollTimer=time.CreateTimer(_=>QueueCheck(),null,TimeSpan.FromSeconds(seconds),Timeout.InfiniteTimeSpan);
        }
        return new(catalogueOk,Current.Models.Count,new(added.Where(m=>!before.Models.Values.Any(x=>x.Maker==m&&x.State is PaletteState.Ready or PaletteState.Known)).Order(StringComparer.Ordinal)),new(Current.Models.Values.Where(m=>m.State==PaletteState.Pending).Select(m=>m.Maker).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)),new(Current.Models.Values.Where(m=>m.Crowded).Select(m=>m.Maker).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)),Current.Boundary,Current.Digest)
        {
            NewModels=new(Current.Models.Keys.Where(id=>!before.Models.ContainsKey(id)).Order(StringComparer.Ordinal)),
            ReSpacedModels=new(Current.Models.Values.Where(m=>before.Models.TryGetValue(m.Id,out var old)&&(old.Position!=m.Position||!old.Palette.Stops.Equals(m.Palette.Stops))).Select(m=>m.Id).Order(StringComparer.Ordinal)),
            PendingReasons=pendingReasons.ToFrozenDictionary(StringComparer.Ordinal),
            UsedBoundarySnapshot=EffectiveBoundary!=before.Boundary,CheckedAtServerSeconds=ServerNow,
            NextPublicationServerSeconds=(ServerNow/21600+1)*21600+900,Partial=completed<makers.Length
        };
    }
    private void AcceptMarket(StoredMarket next)
    {try{store.SaveMarket(next);market=next;}catch(IOException){}catch(UnauthorizedAccessException){}}
    private static string EvidenceHash(SeedEvidence[] e)=>Hash(string.Join('\n',e.Select(x=>x.Source+"|"+x.Sha256)));
    private StoredMarket? EffectiveMarket=>market is {} m&&m.Window>(releasePack?.Market?.Boundary??-1)?m:null;
    private long EffectiveBoundary=>EffectiveMarket?.Window??releasePack?.Market?.Boundary??-1;
    private PublicModelCatalogueEntry[] PublicRows()=>EffectiveMarket is {} m?PublicModelCatalogue.Parse(m.Body)?.Models.ToArray()??[]:[];
    private List<ModelPaletteFact> Facts()
    {
        var result=pack.Baseline.Select(f=>f with {Maker=AliasMaker(f.Maker)}).GroupBy(f=>f.Id,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.Ordinal);
        if(EffectiveMarket is not {} m)return result.Values.ToList();
        var generated=m.Date-m.Age;var cutoff=m.Window*21600-1800;
        foreach(var row in PublicRows())
        {
            using var document=JsonDocument.Parse(row.PublicFacts);var x=document.RootElement;
            var created=x.TryGetProperty("created",out var t)&&t.ValueKind==JsonValueKind.Number&&t.TryGetInt64(out var c)?c:0;
            var eligible=!row.Id.StartsWith('~')&&row.Maker!="openrouter"&&!x.TryGetProperty("alias_target",out var alias);
            if(x.TryGetProperty("architecture",out var arch)&&arch.ValueKind==JsonValueKind.Object&&arch.TryGetProperty("output_modalities",out var output)&&output.ValueKind==JsonValueKind.Array)eligible&=output.EnumerateArray().Any(p=>p.ValueKind==JsonValueKind.String&&p.GetString()=="text");
            if(x.TryGetProperty("expiration_date",out var expiry)&&expiry.ValueKind==JsonValueKind.String&&DateTimeOffset.TryParse(expiry.GetString(),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var expired))eligible&=expired.ToUnixTimeSeconds()>cutoff;
            if(created>generated+86400||created>=cutoff)continue;
            double price=0;long context=0;
            if(x.TryGetProperty("pricing",out var pricing)&&pricing.ValueKind==JsonValueKind.Object&&pricing.TryGetProperty("completion",out var completion)&&completion.ValueKind==JsonValueKind.String&&double.TryParse(completion.GetString(),NumberStyles.Float,CultureInfo.InvariantCulture,out var parsed)&&double.IsFinite(parsed))price=Math.Max(0,parsed*1000000);
            if(x.TryGetProperty("context_length",out var ctx)&&ctx.ValueKind==JsonValueKind.Number)ctx.TryGetInt64(out context);
            result[row.Id]=new(row.Id,AliasMaker(row.Maker),price,created,context,eligible);
        }
        return result.Values.ToList();
    }
    private (string Key,string Org,string? Name,string InputDigest) SeedInputs(string maker,IReadOnlyList<ModelPaletteFact> facts,IReadOnlyList<PublicModelCatalogueEntry> rows)
    {
        var eligible=facts.Where(f=>f.Eligible&&f.Maker==maker).Select(f=>f.Id).ToHashSet(StringComparer.Ordinal);
        var related=rows.Where(r=>AliasMaker(r.Maker)==maker&&eligible.Contains(r.Id)).ToArray();
        var orgs=new List<string>();var names=new List<string>();
        foreach(var row in related)
        {
            using var doc=JsonDocument.Parse(row.PublicFacts);var x=doc.RootElement;
            if(x.TryGetProperty("hugging_face_id",out var hf)&&hf.ValueKind==JsonValueKind.String&&!string.IsNullOrEmpty(hf.GetString()))orgs.Add(hf.GetString()!.Split('/')[0]);
            if(x.TryGetProperty("name",out var n)&&n.ValueKind==JsonValueKind.String)names.Add(n.GetString()!);
        }
        var org=orgs.GroupBy(x=>x,StringComparer.Ordinal).OrderByDescending(g=>g.Count()).ThenBy(g=>g.Key,StringComparer.Ordinal).Select(g=>g.Key).FirstOrDefault()??maker;
        var name=names.Order(StringComparer.Ordinal).FirstOrDefault()?.Split(':')[0];
        var ids=facts.Where(f=>f.Maker==maker).Select(f=>f.Id).Order(StringComparer.Ordinal).ToArray();
        var pinned=PinnedSeedColours.Find(maker,name,org);
        var input=(pinned is null?"none":JsonSerializer.Serialize(pinned))+"\n"+(ids.Length==0?"slug-only":string.Join('\n',ids))+"\n"+org+"\n"+string.Join('\n',names.Order(StringComparer.Ordinal));
        var key=Hash("S2\n"+input);
        return (key,org,name,Hash(input));
    }
    private string? VotedDomain(string maker)
    {
        if(providers is null)return null;
        var votes=new List<MakerDomainVote>();
        foreach(var row in providers.RootElement.GetProperty("data").EnumerateArray())
        {
            if(row.ValueKind!=JsonValueKind.Object||!row.TryGetProperty("slug",out var slug)||slug.ValueKind!=JsonValueKind.String||AliasMaker(slug.GetString()!)!=maker)continue;
            foreach(var key in new[]{"privacy_policy_url","terms_of_service_url","status_page_url"})if(row.TryGetProperty(key,out var url)&&url.ValueKind==JsonValueKind.String)votes.Add(new(url.GetString()!,true));
        }
        return DomainVote.Choose(votes)??"";
    }
    private PaletteSnapshot Snapshot(bool known=false)
    {
        var facts=Facts();var rows=PublicRows();
        var names=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var row in rows.Concat(prefetch?.Models??ValueList<PublicModelCatalogueEntry>.Empty))
        {using var doc=JsonDocument.Parse(row.PublicFacts);if(doc.RootElement.TryGetProperty("name",out var n)&&n.ValueKind==JsonValueKind.String)names[row.Id]=n.GetString()!;}
        var palettes=new Dictionary<string,(PaletteAnchor Palette,PaletteState State,bool Crowded)>(StringComparer.Ordinal);
        (PaletteAnchor Palette,PaletteState State,bool Crowded) For(string maker)
        {
            if(palettes.TryGetValue(maker,out var got))return got;
            var anchor=Authority(maker);
            if(anchor!=pack.Neutral)return palettes[maker]=(anchor,known?PaletteState.Known:PaletteState.Ready,false);
            var (key,_,_,_)=SeedInputs(maker,facts,rows);var seed=seeds.GetValueOrDefault(maker);
            if(seed?.Key==key&&seed.SeedVersion==DominantColour.Version)
            {
                var cacheKey=JsonSerializer.Serialize(seed.Seed);if(!derived.TryGetValue(cacheKey,out var d))derived[cacheKey]=d=PaletteDeriver.Derive(seed.Seed,frozenReserved);
                return palettes[maker]=(d.Palette,known?PaletteState.Known:PaletteState.Ready,d.Crowded);
            }
            return palettes[maker]=(pack.Neutral,PaletteState.Pending,false);
        }
        var lineups=LineupRanker.Build(facts,m=>For(m).Palette);var models=new List<PaletteModel>();
        var ids=facts.Where(f=>f.Eligible).Select(f=>f.Id).Concat(prefetch?.Models.Select(m=>m.Id)??[]).Concat(observed).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        foreach(var id in ids)
        {
            var maker=facts.FirstOrDefault(f=>f.Id==id)?.Maker??ResolveMaker("openrouter",id,facts).Maker;
            if(maker=="openrouter"){models.Add(new(id,maker,id,PaletteState.Router,pack.Neutral,new(.5,1,1),false));continue;}
            var p=For(maker);var position=lineups.TryGetValue(maker,out var lineup)?lineup.Lookup(id):new ModelPalettePosition(.5,1,1);
            models.Add(new(id,maker,names.GetValueOrDefault(id,observedNames.GetValueOrDefault(id,id)),p.State,p.Palette,position,p.Crowded));
        }
        return PaletteSnapshot.Create(EffectiveBoundary,models,releasePack is {} release?"pack.v"+release.Version:pack.Version);
    }
    private void Publish()
    {
        var next=Snapshot();if(Current.Digest==next.Digest){ScheduleStoreRetry();return;}Volatile.Write(ref current,next);
        try{store.SaveSnapshot(next);}catch(IOException){}catch(UnauthorizedAccessException){}
        ScheduleStoreRetry();Changed?.Invoke(next);
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;lifetime.Cancel();if(started)network.AvailabilityChanged-=NetworkAvailable;
        if(ownsNetwork&&network is IDisposable disposableNetwork)disposableNetwork.Dispose();
        triggerTimer?.Dispose();pollTimer?.Dispose();publicationTimer?.Dispose();storeRetryTimer?.Dispose();providers?.Dispose();http.Dispose();executor.Dispose();lifetime.Dispose();
    }
}
