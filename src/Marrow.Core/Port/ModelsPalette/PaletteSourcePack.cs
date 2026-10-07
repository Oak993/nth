// PORT-ONLY: immutable source-derived inputs, not a fabricated baked pack; ED-NEW-models-palette-6.
using System.Collections.Frozen;
using System.Text.Json;
namespace Marrow.Core.Port.ModelsPalette;

public sealed record PaletteAnchor(string Key,ValueList<string> Stops,string? Title);
public sealed record ModelPaletteFact(string Id,string Maker,double Price,long Created,long Context,bool Eligible,bool Builtin=false);
public sealed class PaletteSourcePack
{
    public static PaletteSourcePack Default { get; }=Load();
    public string Version { get; }
    public FrozenDictionary<string,PaletteAnchor> Palettes { get; }
    public FrozenDictionary<string,string> MakerPalette { get; }
    public ValueList<ModelPaletteFact> Baseline { get; }
    public PaletteAnchor Neutral { get; }=new("Neutral",new ValueList<string>(new[]{-.07,0,.10}.Select((dl,i)=>PaletteMath.Fit(.56+dl,.045*new[]{1.02,1,.88}[i],250).Hex)),null);
    private PaletteSourcePack(JsonElement root)
    {
        Version=root.GetProperty("version").GetString()!;
        var titles=root.GetProperty("titles");
        Palettes=root.GetProperty("palettes").EnumerateObject().ToFrozenDictionary(p=>p.Name,p=>new PaletteAnchor(p.Name,
            new ValueList<string>(p.Value.EnumerateArray().Select(x=>x.GetString()!)),titles.TryGetProperty(p.Name,out var t)?t.GetString():null),StringComparer.Ordinal);
        MakerPalette=root.GetProperty("makerPalette").EnumerateObject().ToFrozenDictionary(p=>p.Name,p=>p.Value.GetString()!,StringComparer.Ordinal);
        Baseline=new ValueList<ModelPaletteFact>(root.GetProperty("baseline").EnumerateArray().Select(x=>new ModelPaletteFact(x.GetProperty("id").GetString()!,x.GetProperty("maker").GetString()!,x.GetProperty("price").GetDouble(),x.GetProperty("created").GetInt64(),x.GetProperty("context").GetInt64(),x.GetProperty("eligible").GetBoolean(),x.TryGetProperty("builtin",out var b)&&b.GetBoolean())));
    }
    private PaletteSourcePack(PaletteSourcePack source,ValueList<ModelPaletteFact> baseline)
    { Version=source.Version;Palettes=source.Palettes;MakerPalette=source.MakerPalette;Baseline=baseline; }
    /// <summary>An explicit immutable data pack, used by isolated tests and pack-upgrade verification.</summary>
    public PaletteSourcePack WithBaseline(IReadOnlyList<ModelPaletteFact> baseline)=>new(this,new(baseline));
    public PaletteAnchor ForMaker(string maker)=>MakerPalette.TryGetValue(maker,out var key)?Palettes[key]:Neutral;
    /// <summary>IR-3's DERIVED swatches are reference examples, not evidence-bearing baked seeds.</summary>
    public PaletteAnchor AuthoritativeForMaker(string maker)
    {
        var palette=ForMaker(maker);
        return palette.Key is "Silver" or "Claude" or "Deepseek" or "Meta" or "Antigravity" or "Cursor" or "Grok" or "Opencode" or "OpencodeGo" or "Zai" or "Devin" or "Copilot" or "Commandcode" or "Pi"?palette:Neutral;
    }
    public ValueList<PaletteAnchor> Reserved {get;}=new([
        // Locked null-provider Purple and the two fast-mode hearts are explicit D3 reservations (PLAN 5.3).
        new PaletteAnchor("Purple",new(["#7A4BE0","#8C57F7","#B48CFF"]),null),
        new PaletteAnchor("Fast",new(["#FFD65C","#FFD65C","#FFD65C"]),null),
        new PaletteAnchor("FastFill",new(["#EDA821","#EDA821","#EDA821"]),null)]);
    private static PaletteSourcePack Load()
    {
        using var stream=typeof(PaletteSourcePack).Assembly.GetManifestResourceStream("Marrow.Core.ModelsPalette.Source1.json")!;
        using var json=JsonDocument.Parse(stream);return new(json.RootElement);
    }
}
