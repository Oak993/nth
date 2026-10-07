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
    private readonly PaletteSourcePack pack;
    private readonly TimeProvider time;
    private readonly IPaletteNetworkStatus network;
    private readonly bool ownsNetwork;
    private readonly CancellationTokenSource lifetime=new();
    private readonly Dictionary<string,StoredSeed> seeds;
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
    public PaletteEngine(string dataRoot,HttpMessageHandler transport,ILogoDecoder decoder,TimeProvider? time=null,PaletteSourcePack? pack=null,IPaletteNetworkStatus? network=null)
        :this(dataRoot,transport,decoder,time??TimeProvider.System,pack??PaletteSourcePack.Default,new PaletteExecutor(),network){}
    private PaletteEngine(string root,HttpMessageHandler transport,ILogoDecoder decoder,TimeProvider time,PaletteSourcePack pack,PaletteExecutor executor,IPaletteNetworkStatus? network):base(executor)
    {
        this.executor=executor;this.time=time;this.pack=pack;store=new(root);http=new(transport,time);chain=new(http,decoder,time);
        this.network=network??new PaletteNetworkStatus();ownsNetwork=network is null;
        seeds=store.LoadSeeds();market=store.LoadMarket();
        if(market is {} cached){serverSeconds=cached.Date;serverTick=time.GetTimestamp();}
        current=Snapshot(known:true);
    }
    private long ServerNow=>serverSeconds+(long)((time.GetTimestamp()-serverTick)/(double)time.TimestampFrequency);
    private static string Hash(string x)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(x)));
    /// <summary>Automatic check; callers join the single in-flight pipeline. Does not reload account-specific provider lists.</summary>
    public Task<PaletteCheckResult> CheckNow()=>Isolated(async()=>
    {
        if(running is {} joined)return await joined;
        running=Run(false);try{return await running;}finally{running=null;}
    });
    public Task Observe(IReadOnlyList<string> modelIds)=>Isolated(()=>
    {
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
        var makers=facts.Where(f=>f.Eligible).Select(f=>f.Maker).Concat(prefetch?.Models.Select(m=>Makers.Alias(m.Maker))??[])
            .Concat(observed.Select(id=>Makers.Resolve("openrouter",id,facts).Maker)).Where(m=>m!="openrouter").Distinct(StringComparer.Ordinal)
            .OrderByDescending(m=>observed.Any(id=>Makers.Resolve("openrouter",id,facts).Maker==m)).ThenBy(m=>m,StringComparer.Ordinal).ToArray();
        var added=new List<string>();var seedChanged=false;var completed=0;
        var modelIds=facts.Where(f=>f.Eligible).Select(f=>f.Id).Concat(prefetch?.Models.Select(m=>m.Id)??[]).Concat(observed).Distinct(StringComparer.Ordinal).ToArray();
        var makerCounts=modelIds.GroupBy(id=>Makers.Resolve("openrouter",id,facts).Maker,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.Count(),StringComparer.Ordinal);
        var checkedModels=0;
        var stageOnePublished=false;var batchAt=time.GetTimestamp();var batchCount=0;
        var visibleMakers=observed.Select(id=>Makers.Resolve("openrouter",id,facts).Maker).ToHashSet(StringComparer.Ordinal);
        var pendingReasons=new Dictionary<string,string>(StringComparer.Ordinal);
        Progress?.Invoke(new(PaletteRefreshPhase.Makers,0,makers.Length));
        foreach(var maker in makers)
        {
            if(lifetime.IsCancellationRequested)break;
            Progress?.Invoke(new(PaletteRefreshPhase.Makers,completed,makers.Length){ModelsCompleted=checkedModels,ModelsTotal=modelIds.Length});
            checkedModels+=makerCounts.GetValueOrDefault(maker);
            if(manual&&!stageOnePublished&&(!visibleMakers.Contains(maker)||time.GetElapsedTime(startedAt).TotalSeconds>=8))
            {Progress?.Invoke(new(PaletteRefreshPhase.Applying,completed,makers.Length));Publish();stageOnePublished=true;batchAt=time.GetTimestamp();}
            if(manual&&stageOnePublished&&time.GetElapsedTime(startedAt).TotalSeconds>=60)break;
            if(pack.AuthoritativeForMaker(maker)!=pack.Neutral){completed++;continue;}
            var (key,org,name)=SeedInputs(maker,facts,publicRows);
            var old=seeds.GetValueOrDefault(maker);var valid=old?.Key==key;
            if(valid&&old!.Day==day){completed++;continue;}
            var retrySeconds=backoff.TryGetValue(maker,out var wait)?new[]{30,300,3600,21600,86400}[Math.Clamp(wait.Failures-1,0,4)]:0;
            if(!manual&&!valid&&retrySeconds>0&&time.GetElapsedTime(wait.Tick).TotalSeconds<retrySeconds){completed++;continue;}
            var domain=VotedDomain(maker);
            SeedAnswer answer;
            try{answer=await chain.Find(maker,name,org,domain,manual&&!stageOnePublished?stage.Token:lifetime.Token);}
            catch(Exception e) when(e is not OutOfMemoryException){answer=new(null,[],"Worker unavailable: "+e.GetType().Name);}
            if(answer.Decisive)
            {
                var next=new StoredSeed(key,answer.Seed!,answer.Evidence,day);
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
            var seconds=!catalogueOk?120:Math.Max(1,nextBoundary+60-ServerNow);
            pollTimer=time.CreateTimer(_=>QueueCheck(),null,TimeSpan.FromSeconds(seconds),Timeout.InfiniteTimeSpan);
        }
        return new(catalogueOk,Current.Models.Count,new(added.Where(m=>!before.Models.Values.Any(x=>x.Maker==m&&x.State is PaletteState.Ready or PaletteState.Known)).Order(StringComparer.Ordinal)),new(Current.Models.Values.Where(m=>m.State==PaletteState.Pending).Select(m=>m.Maker).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)),new(Current.Models.Values.Where(m=>m.Crowded).Select(m=>m.Maker).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)),Current.Boundary,Current.Digest)
        {
            NewModels=new(Current.Models.Keys.Where(id=>!before.Models.ContainsKey(id)).Order(StringComparer.Ordinal)),
            ReSpacedModels=new(Current.Models.Values.Where(m=>before.Models.TryGetValue(m.Id,out var old)&&(old.Position!=m.Position||!old.Palette.Stops.Equals(m.Palette.Stops))).Select(m=>m.Id).Order(StringComparer.Ordinal)),
            PendingReasons=pendingReasons.ToFrozenDictionary(StringComparer.Ordinal),
            UsedBoundarySnapshot=market?.Window!=before.Boundary,CheckedAtServerSeconds=ServerNow,
            NextPublicationServerSeconds=(ServerNow/21600+1)*21600+900,Partial=completed<makers.Length
        };
    }
    private void AcceptMarket(StoredMarket next)
    {try{store.SaveMarket(next);market=next;}catch(IOException){}catch(UnauthorizedAccessException){}}
    private static string EvidenceHash(SeedEvidence[] e)=>Hash(string.Join('\n',e.Select(x=>x.Source+"|"+x.Sha256)));
    private PublicModelCatalogueEntry[] PublicRows()=>market is {} m?PublicModelCatalogue.Parse(m.Body)?.Models.ToArray()??[]:[];
    private List<ModelPaletteFact> Facts()
    {
        var result=pack.Baseline.GroupBy(f=>f.Id,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.Ordinal);
        if(market is not {} m)return result.Values.ToList();
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
            result[row.Id]=new(row.Id,Makers.Alias(row.Maker),price,created,context,eligible);
        }
        return result.Values.ToList();
    }
    private (string Key,string Org,string? Name) SeedInputs(string maker,IReadOnlyList<ModelPaletteFact> facts,IReadOnlyList<PublicModelCatalogueEntry> rows)
    {
        var eligible=facts.Where(f=>f.Eligible&&f.Maker==maker).Select(f=>f.Id).ToHashSet(StringComparer.Ordinal);
        var related=rows.Where(r=>Makers.Alias(r.Maker)==maker&&eligible.Contains(r.Id)).ToArray();
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
        var key=Hash("S2\n"+(pinned is null?"none":JsonSerializer.Serialize(pinned))+"\n"+(ids.Length==0?"slug-only":string.Join('\n',ids))+"\n"+org+"\n"+string.Join('\n',names.Order(StringComparer.Ordinal)));
        return (key,org,name);
    }
    private string? VotedDomain(string maker)
    {
        if(providers is null)return null;
        var votes=new List<MakerDomainVote>();
        foreach(var row in providers.RootElement.GetProperty("data").EnumerateArray())
        {
            if(row.ValueKind!=JsonValueKind.Object||!row.TryGetProperty("slug",out var slug)||slug.ValueKind!=JsonValueKind.String||Makers.Alias(slug.GetString()!)!=maker)continue;
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
            var anchor=pack.AuthoritativeForMaker(maker);
            if(anchor!=pack.Neutral)return palettes[maker]=(anchor,known?PaletteState.Known:PaletteState.Ready,false);
            var (key,_,_)=SeedInputs(maker,facts,rows);var seed=seeds.GetValueOrDefault(maker);
            if(seed?.Key==key)
            {
                var cacheKey=JsonSerializer.Serialize(seed.Seed);if(!derived.TryGetValue(cacheKey,out var d))derived[cacheKey]=d=PaletteDeriver.Derive(seed.Seed,pack.Palettes.Values.Concat(pack.Reserved).ToArray());
                return palettes[maker]=(d.Palette,known?PaletteState.Known:PaletteState.Ready,d.Crowded);
            }
            return palettes[maker]=(pack.Neutral,PaletteState.Pending,false);
        }
        var lineups=LineupRanker.Build(facts,m=>For(m).Palette);var models=new List<PaletteModel>();
        var ids=facts.Where(f=>f.Eligible).Select(f=>f.Id).Concat(prefetch?.Models.Select(m=>m.Id)??[]).Concat(observed).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        foreach(var id in ids)
        {
            var maker=facts.FirstOrDefault(f=>f.Id==id)?.Maker??Makers.Resolve("openrouter",id,facts).Maker;
            if(maker=="openrouter"){models.Add(new(id,maker,id,PaletteState.Router,pack.Neutral,new(.5,1,1),false));continue;}
            var p=For(maker);var position=lineups.TryGetValue(maker,out var lineup)?lineup.Lookup(id):new ModelPalettePosition(.5,1,1);
            models.Add(new(id,maker,names.GetValueOrDefault(id,observedNames.GetValueOrDefault(id,id)),p.State,p.Palette,position,p.Crowded));
        }
        return PaletteSnapshot.Create(market?.Window??-1,models,pack.Version);
    }
    private void Publish()
    {
        var next=Snapshot();if(Current.Digest==next.Digest)return;Volatile.Write(ref current,next);Changed?.Invoke(next);
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;lifetime.Cancel();if(started)network.AvailabilityChanged-=NetworkAvailable;
        if(ownsNetwork&&network is IDisposable disposableNetwork)disposableNetwork.Dispose();
        triggerTimer?.Dispose();pollTimer?.Dispose();publicationTimer?.Dispose();providers?.Dispose();http.Dispose();executor.Dispose();lifetime.Dispose();
    }
}
