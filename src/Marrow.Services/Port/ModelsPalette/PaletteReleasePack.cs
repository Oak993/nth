// PORT-ONLY: AC-3 C9/C22; evidence-bearing, forward-only release packs; ED-622.
using System.Collections.Frozen;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Marrow.Core.Port;
using Marrow.Core.Port.ModelsPalette;
namespace Marrow.Services.Port.ModelsPalette;
public sealed record PaletteSeedEvidence(string Source,string Sha256,byte[] Rgba64,DateTimeOffset? FetchedAt=null,long? ByteLength=null,int? Status=null,string? Type=null);
public sealed record PalettePackSeed(string Maker,ColourSeed Seed,PaletteSeedEvidence[] Evidence,bool Owner=false,int Epoch=0,string? PinnedName=null,string? PinnedOrg=null);
public sealed record PalettePlaceholderEvidence(string Kind,string Source,string Sha256,DateTimeOffset FetchedAt,long ByteLength);
public sealed record PalettePackMarket(long Boundary,long Date,int Age);
public sealed record PaletteTombstone(ModelPaletteFact Fact,DateTimeOffset WithdrawnAt);
public sealed class PaletteReleasePack
{
    public int Version {get;}
    public PalettePackMarket? Market {get;}
    public string MinimumEngine=>"AC-3";
    public string SeedVersion=>"S2";
    public string DeriveVersion=>"D3";
    public FrozenDictionary<string,PalettePackSeed> Seeds {get;}
    public FrozenDictionary<string,PaletteAnchor> BakedPalettes {get;}
    public ValueList<PaletteAnchor> FrozenReserved {get;}
    public FrozenDictionary<string,string[]> PlaceholderHashes {get;}
    public ValueList<PalettePlaceholderEvidence> PlaceholderEvidence {get;}
    public FrozenDictionary<string,string> MakerDomains {get;}
    public PaletteSnapshot Snapshot0 {get;}
    public PaletteSourcePack Source {get;}
    public ValueList<PaletteTombstone> Tombstones {get;}
    public FrozenDictionary<string,string> MakerAliases {get;}
    private sealed record Anchor(string Key,string[] Stops,string? Title);
    private sealed record Document(int version,string minimumEngine,string seedVersion,string deriveVersion,JsonElement source,PalettePackSeed[] seeds,Anchor[] baked,Anchor[] frozen,Dictionary<string,string[]> placeholders,Dictionary<string,string> domains,PaletteSnapshotCodec.Data snapshot,string toc,string psl,PaletteTombstone[] tombstones,Dictionary<string,string> aliases,PalettePlaceholderEvidence[] placeholderEvidence,PalettePackMarket? market);
    private PaletteReleasePack(PaletteSourcePack source,int version,IEnumerable<PalettePackSeed> seeds,IEnumerable<PaletteAnchor> baked,IEnumerable<PaletteAnchor> frozen,Dictionary<string,string[]> placeholders,Dictionary<string,string> domains,PaletteSnapshot snapshot,IEnumerable<PaletteTombstone> tombstones,Dictionary<string,string> aliases,IEnumerable<PalettePlaceholderEvidence> placeholderEvidence,PalettePackMarket? market)
    {Market=market;PlaceholderEvidence=new(placeholderEvidence);Source=source;Version=version;Seeds=seeds.ToFrozenDictionary(s=>s.Maker,StringComparer.Ordinal);BakedPalettes=baked.ToFrozenDictionary(p=>p.Key,StringComparer.Ordinal);FrozenReserved=new(frozen);PlaceholderHashes=placeholders.ToFrozenDictionary(StringComparer.Ordinal);MakerDomains=domains.ToFrozenDictionary(StringComparer.Ordinal);Snapshot0=snapshot;Tombstones=new(tombstones);MakerAliases=aliases.ToFrozenDictionary(StringComparer.Ordinal);foreach(var key in aliases.Keys)_ =Alias(key);}
    public string Alias(string maker)
    {
        var key=Makers.Alias(maker);for(var n=0;n<=MakerAliases.Count;n++){if(!MakerAliases.TryGetValue(key,out var next))return key;key=Makers.Alias(next);}throw new InvalidDataException("Cyclic maker aliases.");
    }
    public static PaletteReleasePack Bake(PaletteSourcePack source,int version,IReadOnlyList<PalettePackSeed> additions,PaletteReleasePack? previous=null,string minimumEngine="AC-3",Dictionary<string,string[]>? placeholders=null,Dictionary<string,string>? domains=null,DateTimeOffset? catalogueDate=null,Dictionary<string,string>? aliases=null,IReadOnlyList<PalettePlaceholderEvidence>? placeholderEvidence=null,int catalogueAge=0)
    {
        if(minimumEngine!="AC-3"||version<=0||previous is not null&&version<=previous.Version)throw new InvalidDataException("Unsupported engine or non-forward pack.");
        aliases??=previous?.MakerAliases.ToDictionary()??[];
        string Canonical(string maker)
        {var key=Makers.Alias(maker);for(var n=0;n<=aliases.Count;n++){if(!aliases.TryGetValue(key,out var next))return key;key=Makers.Alias(next);}throw new InvalidDataException("Cyclic maker aliases.");}
        source=source.WithBaseline(source.Baseline.Select(f=>f with {Maker=Canonical(f.Maker)}).ToArray());
        var market=catalogueDate is {} date?new PalettePackMarket(date.ToUnixTimeSeconds()/21600,date.ToUnixTimeSeconds(),catalogueAge):previous?.Market;
        ValidateMarket(market);
        var active=source.Baseline.Select(f=>f.Id).ToHashSet(StringComparer.Ordinal);
        var tombstones=previous?.Tombstones.Where(t=>!active.Contains(t.Fact.Id)&&(catalogueDate is null||catalogueDate<t.WithdrawnAt.AddMonths(24))).ToList()??[];
        if(previous is not null)
            foreach(var fact in previous.Source.Baseline.Where(f=>!active.Contains(f.Id)&&!previous.Tombstones.Any(t=>t.Fact.Id==f.Id)))
            {if(catalogueDate is null)throw new InvalidDataException("Withdrawal needs the public catalogue's server date.");tombstones.Add(new(fact,catalogueDate.Value));}
        source=source.WithBaseline(source.Baseline.Concat(tombstones.Select(t=>t.Fact)).ToArray());
        var seeds=previous?.Seeds.ToDictionary()??new Dictionary<string,PalettePackSeed>(StringComparer.Ordinal);
        var frozen=previous?.FrozenReserved??new ValueList<PaletteAnchor>(source.MakerPalette.Keys.Select(source.AuthoritativeForMaker).Where(p=>p!=source.Neutral).Distinct().OrderBy(p=>p.Key,StringComparer.Ordinal).Concat(source.Reserved));
        var baked=previous?.BakedPalettes.ToDictionary()??new Dictionary<string,PaletteAnchor>(StringComparer.Ordinal);
        var epoch=seeds.Values.Select(s=>s.Epoch).DefaultIfEmpty(0).Max();
        foreach(var seed in additions.OrderBy(s=>s.Maker,StringComparer.Ordinal))
        {
            Validate(seed,placeholders??previous?.PlaceholderHashes.ToDictionary()??[]);
            if(source.AuthoritativeForMaker(seed.Maker)!=source.Neutral)throw new InvalidDataException("Locked brands cannot be re-derived.");
            if(seeds.ContainsKey(seed.Maker))throw new InvalidDataException("Bake epochs and seeds are append-only.");
            var derived=PaletteDeriver.Derive(seed.Seed,frozen);
            var palette=derived.Palette with {Key=seed.Maker};
            // Later epochs alone can move on a collision; predecessors are immutable.
            if(baked.Values.Any(p=>PaletteMath.Distance(PaletteMath.Lab(p.Stops[1]),PaletteMath.Lab(palette.Stops[1]))<.060))
                palette=PaletteDeriver.Derive(seed.Seed,new ValueList<PaletteAnchor>(frozen.Concat(baked.Values))).Palette with {Key=seed.Maker};
            seeds.Add(seed.Maker,seed with {Epoch=++epoch});baked.Add(seed.Maker,palette);
        }
        if(previous is null)frozen=new ValueList<PaletteAnchor>(frozen.Concat(baked.Values.OrderBy(p=>seeds[p.Key].Epoch)));
        placeholders??=previous?.PlaceholderHashes.ToDictionary()??[];domains??=previous?.MakerDomains.ToDictionary()??[];
        placeholderEvidence??=previous?.PlaceholderEvidence.ToArray()??[];
        foreach(var e in placeholderEvidence)
            if(!HashValid(e.Sha256)||!Uri.TryCreate(e.Source,UriKind.Absolute,out var url)||url.Scheme!="https"||e.ByteLength<0||!placeholders.TryGetValue(e.Kind,out var hashes)||!hashes.Contains(e.Sha256,StringComparer.Ordinal))throw new InvalidDataException("Invalid placeholder source evidence.");
        foreach(var hashes in placeholders.Values)if(hashes.Any(h=>!HashValid(h)))throw new InvalidDataException("Placeholder hashes must be complete SHA-256 evidence.");
        foreach(var domain in domains.Values)if(DomainVote.Registrable("https://"+domain)!=domain)throw new InvalidDataException("Invalid makers-table domain.");
        PaletteAnchor For(string maker)=>source.AuthoritativeForMaker(maker)!=source.Neutral?source.AuthoritativeForMaker(maker):baked.GetValueOrDefault(maker,source.Neutral);
        var lineups=LineupRanker.Build(source.Baseline,For);
        var models=source.Baseline.Where(f=>f.Eligible).GroupBy(f=>f.Id,StringComparer.Ordinal).Select(g=>g.First()).Select(f=>new PaletteModel(f.Id,f.Maker,f.Id,For(f.Maker)==source.Neutral?PaletteState.Pending:PaletteState.Known,For(f.Maker),lineups[f.Maker].Lookup(f.Id),false));
        return new(source,version,seeds.Values,baked.Values,frozen,placeholders,domains,PaletteSnapshot.Create(market?.Boundary??-1,models,"pack.v"+version),tombstones,aliases,placeholderEvidence,market);
    }
    private static void ValidateMarket(PalettePackMarket? market)
    {if(market is {} m&&(m.Boundary<0||m.Age<0||m.Date-m.Age<m.Boundary*21600||m.Date/21600!=m.Boundary))throw new InvalidDataException("Invalid pack catalogue boundary.");}
    private static bool HashValid(string? hash)=>hash is {Length:64}&&hash.All(char.IsAsciiHexDigit);
    private static void Validate(PalettePackSeed seed,IReadOnlyDictionary<string,string[]> placeholders)
    {
        if(seed.Maker.Length==0||seed.Evidence.Length==0||seed.Seed.Hue is <0 or >=360||seed.Seed.Lightness is <0 or >200||seed.Seed.Chroma is <0 or >90)throw new InvalidDataException("Invalid seed.");
        foreach(var e in seed.Evidence)
            if(!HashValid(e.Sha256)||!Uri.TryCreate(e.Source,UriKind.Absolute,out var url)||url.Scheme!="https"||e.Rgba64.Length is not(0 or 16384))throw new InvalidDataException("Invalid source evidence.");
        var pinned=seed.Evidence.Any(e=>e.Source==PinnedSeedColours.SourceUrl&&e.Sha256==PinnedSeedColours.Digest)&&PinnedSeedColours.Find(seed.Maker,seed.PinnedName,seed.PinnedOrg)==seed.Seed;
        bool Placeholder(PaletteSeedEvidence e)=>placeholders.Values.Any(h=>h.Contains(e.Sha256,StringComparer.Ordinal));
        var negative=seed.Seed.Mono&&seed.Evidence.All(e=>e.Rgba64.Length==16384||e.Status is 404 or 410||e.Type=="text/html"||Placeholder(e))&&seed.Evidence.Any(e=>Placeholder(e)||e.Status is 404 or 410||e.Type=="text/html"||e.Rgba64.Length==16384&&!DominantColour.Extract(new(64,64,e.Rgba64)).Decisive);
        if(!seed.Owner&&!pinned&&!negative&&!seed.Evidence.Any(e=>e.Rgba64.Length==16384&&DominantColour.Extract(new(64,64,e.Rgba64)).Seed==seed.Seed))throw new InvalidDataException("Baked seed must equal its pinned input or captured evidence.");
    }
    public byte[] Encode()
    {
        var document=new Document(Version,MinimumEngine,SeedVersion,DeriveVersion,Source.Export(),Seeds.Values.OrderBy(s=>s.Epoch).ToArray(),BakedPalettes.Values.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(ToAnchor).ToArray(),FrozenReserved.Select(ToAnchor).ToArray(),PlaceholderHashes.ToDictionary(),MakerDomains.ToDictionary(),PaletteSnapshotCodec.Export(Snapshot0),Resource("Marrow.Core.ModelsPalette.Toc.json"),Resource("Marrow.Core.ModelsPalette.PslIcann.txt"),Tombstones.ToArray(),MakerAliases.OrderBy(p=>p.Key,StringComparer.Ordinal).ToDictionary(),PlaceholderEvidence.OrderBy(e=>e.Kind,StringComparer.Ordinal).ThenBy(e=>e.Sha256,StringComparer.Ordinal).ToArray(),Market);
        return PaletteJsonEnvelope.Encode(document);
    }
    private static Anchor ToAnchor(PaletteAnchor p)=>new(p.Key,p.Stops.ToArray(),p.Title);
    private static PaletteAnchor FromAnchor(Anchor p)=>p.Stops.Length==3?new(p.Key,new(p.Stops),p.Title):throw new InvalidDataException("Invalid palette.");
    private static string Resource(string name){using var stream=typeof(PaletteSourcePack).Assembly.GetManifestResourceStream(name)??throw new InvalidDataException("Missing frozen source.");using var reader=new StreamReader(stream);return reader.ReadToEnd();}
    public static PaletteReleasePack Read(byte[] bytes)
    {
        try
        {
            var envelope=PaletteJsonEnvelope.Read(bytes);
            if(envelope.Schema!=1||envelope.Data is not {} data||envelope.Hash!=PaletteJsonEnvelope.Digest(data.Span))throw new InvalidDataException("Invalid pack checksum.");
            var d=JsonSerializer.Deserialize<Document>(data.Span)??throw new InvalidDataException("Missing pack.");
            if(d.version<=0||d.minimumEngine!="AC-3"||d.seedVersion!="S2"||d.deriveVersion!="D3"||d.toc!=Resource("Marrow.Core.ModelsPalette.Toc.json")||d.psl!=Resource("Marrow.Core.ModelsPalette.PslIcann.txt"))throw new InvalidDataException("Incompatible pack rules.");
            ValidateMarket(d.market);
            foreach(var seed in d.seeds)Validate(seed,d.placeholders);
            if(d.seeds.Select(s=>s.Epoch).Distinct().Count()!=d.seeds.Length||d.seeds.Any(s=>s.Epoch<=0)||d.placeholders.Values.Any(h=>h.Any(v=>!HashValid(v))))throw new InvalidDataException("Invalid pack authority.");
            return new(PaletteSourcePack.Import(d.source),d.version,d.seeds,d.baked.Select(FromAnchor),d.frozen.Select(FromAnchor),d.placeholders,d.domains,PaletteSnapshotCodec.Import(d.snapshot),d.tombstones??[],d.aliases??[],d.placeholderEvidence??[],d.market);
        }
        catch(Exception e) when(e is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException){throw new InvalidDataException("Malformed colour pack.",e);}
    }
    public static PaletteReleasePack Adopt(PaletteReleasePack current,byte[] bytes,Func<byte[],bool> verifySignature)
    {if(!verifySignature(bytes))throw new InvalidDataException("Untrusted colour pack.");var next=Read(bytes);if(next.Version<=current.Version)throw new InvalidDataException("Pack rollback rejected.");if(!current.FrozenReserved.Equals(next.FrozenReserved)||current.Seeds.Any(p=>!next.Seeds.TryGetValue(p.Key,out var s)||s.Epoch!=p.Value.Epoch||s.Seed!=p.Value.Seed))throw new InvalidDataException("Pack rewrote frozen authority.");return next;}
    public byte[] Compress()
    {using var output=new MemoryStream();using(var zip=new GZipStream(output,CompressionLevel.SmallestSize,true))zip.Write(Encode());return output.ToArray();}
    public static PaletteReleasePack ReadCompressed(Stream stream)
    {
        using var zip=new GZipStream(stream,CompressionMode.Decompress);using var output=new MemoryStream();var buffer=new byte[8192];int n;
        while((n=zip.Read(buffer))!=0){if(output.Length+n>32*1024*1024)throw new InvalidDataException("Colour pack exceeds its decoded cap.");output.Write(buffer,0,n);}return Read(output.ToArray());
    }
    public static PaletteReleasePack ReadEmbedded()
    {using var stream=typeof(PaletteReleasePack).Assembly.GetManifestResourceStream("Marrow.Services.ModelsPalette.Pack1.gz")??throw new InvalidDataException("Missing embedded release pack.");return ReadCompressed(stream);}
}
