// PORT-ONLY: immutable precomputed shade tables; AC-3 PLAN C16, C18; ED-617.
using System.Collections.Frozen;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Marrow.Core.Port;
using Marrow.Core.Port.ModelsPalette;
namespace Marrow.Services.Port.ModelsPalette;
public enum PaletteState {Ready,Known,Pending,Router}
public readonly record struct PaletteShadeKey(string ModelId,string? Effort,bool Light);
public sealed record PaletteModel(string Id,string Maker,string Name,PaletteState State,PaletteAnchor Palette,ModelPalettePosition Position,bool Crowded);
public sealed record PaletteMaker(string Id,string Name,string Heart,int Models);
public sealed record PaletteShade(ValueList<string> Stops,string Title,string Glow,FrozenDictionary<string,double> Motion,ValueList<byte> GradientRow)
{
    public bool Equals(PaletteShade? other)=>other is not null&&Stops.Equals(other.Stops)&&Title==other.Title&&Glow==other.Glow&&GradientRow.Equals(other.GradientRow)&&Motion.Count==other.Motion.Count&&Motion.All(p=>other.Motion.TryGetValue(p.Key,out var v)&&p.Value.Equals(v));
    public override int GetHashCode()=>HashCode.Combine(Stops,Title,Glow,GradientRow);
}
public sealed record PaletteSnapshot(string Digest,long Boundary,FrozenDictionary<string,PaletteModel> Models,FrozenDictionary<PaletteShadeKey,PaletteShade> Shades)
{
    public FrozenDictionary<string,PaletteMaker> Makers {get;init;}=FrozenDictionary<string,PaletteMaker>.Empty;
    public PaletteShade? Resolve(string modelId,string? effort,bool light)
    {
        var word=PaletteIntensity.Word(effort);word=word switch {null or "off" or "none" or "minimal" or "low" or "medium" or "high" or "xhigh" or "max"=>word,_=>"medium"};
        return Shades.GetValueOrDefault(new(modelId,word,light));
    }
    internal static PaletteSnapshot Create(long boundary,IEnumerable<PaletteModel> models,string packVersion="source1",PaletteSnapshot? previous=null)
    {
        var map=models.OrderBy(m=>m.Id,StringComparer.Ordinal).ToFrozenDictionary(m=>m.Id,StringComparer.Ordinal);
        var canonical="AC3-S2-D3-IR3\n"+packVersion+"\n"+boundary.ToString(CultureInfo.InvariantCulture)+"\n"+string.Join('\n',map.Values.OrderBy(m=>m.Id,StringComparer.Ordinal).Select(m=>string.Join('|',m.Id,m.Maker,m.State is PaletteState.Pending or PaletteState.Router?m.State.ToString():"decided",m.Palette.Key,string.Join(',',m.Palette.Stops),m.Position.Strength.ToString("R",CultureInfo.InvariantCulture),m.Position.Level.ToString(CultureInfo.InvariantCulture))));
        var digest=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        // T44: no gradient arithmetic or shade tables for an unchanged publication.
        if(previous?.Digest==digest)return previous;
        var tables=new Dictionary<PaletteShadeKey,PaletteShade>();
        var shared=new Dictionary<(PaletteAnchor,double,string?,bool),PaletteShade>();
        if(previous is not null)
            foreach(var p in previous.Shades)
            {
                var model=previous.Models[p.Key.ModelId];
                shared.TryAdd((model.Palette,model.Position.Strength,p.Key.Effort,p.Key.Light),p.Value);
            }
        string?[] words=[null,"off","none","minimal","low","medium","high","xhigh","max"];
        bool[] themes=[false,true];
        foreach(var m in map.Values)foreach(var word in words)foreach(var light in themes)
        {
            var key=(m.Palette,m.Position.Strength,word,light);
            if(!shared.TryGetValue(key,out var shade))
            {
                var stops=PaletteIntensity.Stops(m.Palette,m.Position.Strength,PaletteIntensity.Effort(word));
                var title=PaletteIntensity.Title(m.Palette,stops)??(light?"#14161C":"#F4F6FB");
                var gradient=new byte[256*4];
                for(var x=0;x<256;x++)
                {
                    var at=(x+.5)/256;var from=at<=.55?0:1;var t=from==0?at/.55:(at-.55)/.45;
                    var colour=PaletteMath.Mix(stops[from],stops[from+1],t);
                    Convert.FromHexString(colour[1..]).CopyTo(gradient,x*4);gradient[x*4+3]=255;
                }
                shade=new(stops,title,stops[1],PaletteIntensity.Motion(m.Palette.Key,m.Position.Strength,PaletteIntensity.Effort(word)),new(gradient));shared[key]=shade;
            }
            tables.Add(new(m.Id,word,light),shade);
        }
        return new(digest,boundary,map,tables.ToFrozenDictionary())
        {Makers=map.Values.GroupBy(m=>m.Maker,StringComparer.Ordinal).ToFrozenDictionary(g=>g.Key,g=>new PaletteMaker(g.Key,MakerName(g.Key,g.Select(m=>m.Name)),g.OrderByDescending(m=>m.Position.Strength).ThenBy(m=>m.Id,StringComparer.Ordinal).First().Palette.Stops[1],g.Count()),StringComparer.Ordinal)};
    }
    private static string MakerName(string id,IEnumerable<string> names)=>id switch
    {"openai"=>"OpenAI","anthropic"=>"Anthropic","google"=>"Google","mistralai"=>"Mistral","x-ai"=>"xAI","meta"=>"Meta","deepseek"=>"DeepSeek","moonshotai"=>"Moonshot AI","qwen"=>"Qwen","nvidia"=>"NVIDIA",_=>names.Where(n=>n.Contains(": ",StringComparison.Ordinal)).Order(StringComparer.Ordinal).Select(n=>n.Split(": ",2,StringSplitOptions.None)[0]).FirstOrDefault()??(id.Length==0?id:char.ToUpperInvariant(id[0])+id[1..])};
}
public enum PaletteRefreshPhase {Catalogue,Providers,Makers,Applying,Done}
public sealed record PaletteRefreshProgress(PaletteRefreshPhase Phase,int Completed,int Total)
{public int ModelsCompleted {get;init;} public int ModelsTotal {get;init;}}
public sealed record PaletteCheckResult(bool CatalogueSucceeded,int ModelsChecked,ValueList<string> NewMakers,ValueList<string> PendingMakers,ValueList<string> CrowdedMakers,long Boundary,string Digest)
{
    public ValueList<string> NewModels {get;init;}=ValueList<string>.Empty;
    public ValueList<string> ReSpacedModels {get;init;}=ValueList<string>.Empty;
    public FrozenDictionary<string,string> PendingReasons {get;init;}=FrozenDictionary<string,string>.Empty;
    public bool UsedBoundarySnapshot {get;init;}
    public long CheckedAtServerSeconds {get;init;}
    public long NextPublicationServerSeconds {get;init;}
    public bool Partial {get;init;}
}
