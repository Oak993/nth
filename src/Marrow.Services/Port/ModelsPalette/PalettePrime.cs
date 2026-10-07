// PORT-ONLY: AC-3 Prime's pack-only tables precede ShowMain; disk and arithmetic stay on the actor.
namespace Marrow.Services.Port.ModelsPalette;
public sealed partial class PaletteEngine
{
    private string AliasMaker(string maker)=>releasePack?.Alias(maker)??Marrow.Core.Port.ModelsPalette.Makers.Alias(maker);
    private Marrow.Core.Port.ModelsPalette.MakerIdentity ResolveMaker(string provider,string id,IReadOnlyList<Marrow.Core.Port.ModelsPalette.ModelPaletteFact> facts,string? name=null,IReadOnlyDictionary<string,string>? names=null)
    {var identity=Marrow.Core.Port.ModelsPalette.Makers.Resolve(provider,id,facts,name,names);return identity with {Maker=AliasMaker(identity.Maker)};}
    public Task AdoptPack(byte[] bytes,Func<byte[],bool> verifySignature)=>Isolated(async()=>
    {
        if(running is {} check)await check;
        PrimeOnActor();
        var previous=releasePack??throw new InvalidDataException("Pack adoption requires a release-pack engine.");
        var next=PaletteReleasePack.Adopt(previous,bytes,verifySignature);
        var remapped=new Dictionary<string,StoredSeed>(StringComparer.Ordinal);
        foreach(var group in seeds.GroupBy(s=>next.Alias(s.Key),StringComparer.Ordinal))
            if(group.Count()==1)remapped[group.Key]=group.Single().Value;
        seeds=remapped;store.SaveSeeds(seeds);
        releasePack=next;pack=next.Source;frozenReserved=next.FrozenReserved.ToArray();chain.AdoptPack(next);derived.Clear();Publish();
    });
    private ITimer? storeRetryTimer;
    private int storeRetryFailures;
    public Task Prime()=>Isolated(PrimeOnActor);
    private void PrimeOnActor()
    {
        if(primed)
        {
            if(!store.HasPendingReads||storeRetryTimer is not null)return;
            store.RetryPendingWrites();
            foreach(var seed in store.LoadSeeds())seeds.TryAdd(seed.Key,seed.Value);
            if(store.LoadMarket() is {} restored&&(market is null||restored.Window>market.Window))market=restored;
            if(store.LoadSnapshot() is {} late&&ReferenceEquals(Current,releasePack?.Snapshot0)){Volatile.Write(ref current,late);Changed?.Invoke(late);}
            else Publish();
            ScheduleStoreRetry();return;
        }
        primed=true;seeds=store.LoadSeeds();market=store.LoadMarket();
        if(market is {} cached){serverSeconds=cached.Date;serverTick=time.GetTimestamp();}
        if(store.LoadSnapshot() is {} shown){Volatile.Write(ref current,shown);Changed?.Invoke(shown);}
        ScheduleStoreRetry();
    }
    private void ScheduleStoreRetry()
    {
        if(storeRetryTimer is not null)return;
        if(!store.HasPendingReads){storeRetryFailures=0;return;}
        var seconds=new[]{30,300,3600,21600,86400}[Math.Min(storeRetryFailures++,4)];
        storeRetryTimer=time.CreateTimer(_=>{if(!disposed)_ =Isolated(()=>{storeRetryTimer?.Dispose();storeRetryTimer=null;PrimeOnActor();});},null,TimeSpan.FromSeconds(seconds),Timeout.InfiniteTimeSpan);
    }
    private Marrow.Core.Port.ModelsPalette.PaletteAnchor Authority(string maker)
    {
        var locked=pack.AuthoritativeForMaker(maker);if(locked!=pack.Neutral)return locked;
        return releasePack?.BakedPalettes.GetValueOrDefault(maker,pack.Neutral)??pack.Neutral;
    }
    public Task ResetLocalColours()=>Isolated(()=>
    {
        PrimeOnActor();seeds.Clear();derived.Clear();backoff.Clear();store.SaveSeeds(seeds);Publish();QueueCheck();
    });
}
