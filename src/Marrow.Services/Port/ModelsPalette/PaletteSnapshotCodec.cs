// PORT-ONLY: AC-3 snapshot 0 contains computed tables; loading performs no colour arithmetic.
using System.Collections.Frozen;
using System.Text.Json;
using Marrow.Core.Port;
using Marrow.Core.Port.ModelsPalette;
namespace Marrow.Services.Port.ModelsPalette;
internal static class PaletteSnapshotCodec
{
    internal sealed record Model(string Id,string Maker,string Name,PaletteState State,string Key,string[] Stops,string? Title,double Strength,int Level,int Levels,bool Crowded);
    internal sealed record Shade(string Id,string? Effort,bool Light,string[] Stops,string Title,string Glow,Dictionary<string,double> Motion,byte[] Row);
    internal sealed record Data(string Digest,long Boundary,Model[] Models,Shade[] Shades,PaletteMaker[] Makers);
    internal static Data Export(PaletteSnapshot snapshot)=>new(snapshot.Digest,snapshot.Boundary,
        snapshot.Models.Values.OrderBy(m=>m.Id,StringComparer.Ordinal).Select(m=>new Model(m.Id,m.Maker,m.Name,m.State,m.Palette.Key,m.Palette.Stops.ToArray(),m.Palette.Title,m.Position.Strength,m.Position.Level,m.Position.Levels,m.Crowded)).ToArray(),
        snapshot.Shades.OrderBy(p=>p.Key.ModelId,StringComparer.Ordinal).ThenBy(p=>p.Key.Effort,StringComparer.Ordinal).ThenBy(p=>p.Key.Light).Select(p=>new Shade(p.Key.ModelId,p.Key.Effort,p.Key.Light,p.Value.Stops.ToArray(),p.Value.Title,p.Value.Glow,p.Value.Motion.OrderBy(v=>v.Key,StringComparer.Ordinal).ToDictionary(),p.Value.GradientRow.ToArray())).ToArray(),
        snapshot.Makers.Values.OrderBy(m=>m.Id,StringComparer.Ordinal).ToArray());
    internal static PaletteSnapshot Import(Data data)
    {
        if(data.Digest is not {Length:64}||data.Models.Length>20000||data.Shades.Length!=data.Models.Length*18)throw new InvalidDataException("Invalid snapshot tables.");
        var models=data.Models.ToFrozenDictionary(m=>m.Id,m=>new PaletteModel(m.Id,m.Maker,m.Name,m.State is PaletteState.Ready?PaletteState.Known:m.State,new(m.Key,new(m.Stops),m.Title),new(m.Strength,m.Level,m.Levels),m.Crowded),StringComparer.Ordinal);
        var interned=new Dictionary<PaletteShade,PaletteShade>();
        var shades=data.Shades.ToFrozenDictionary(s=>new PaletteShadeKey(s.Id,s.Effort,s.Light),s=>
        {
            if(!models.ContainsKey(s.Id)||s.Row.Length!=1024||s.Stops.Length!=3||s.Motion.Values.Any(v=>!double.IsFinite(v)))throw new InvalidDataException("Invalid shade table.");
            var candidate=new PaletteShade(new(s.Stops),s.Title,s.Glow,s.Motion.ToFrozenDictionary(StringComparer.Ordinal),new(s.Row));
            if(interned.TryGetValue(candidate,out var shared))return shared;
            interned.Add(candidate,candidate);return candidate;
        });
        return new(data.Digest,data.Boundary,models,shades){Makers=data.Makers.ToFrozenDictionary(m=>m.Id,StringComparer.Ordinal)};
    }
    internal static byte[] Encode(PaletteSnapshot snapshot)=>JsonSerializer.SerializeToUtf8Bytes(Export(snapshot));
    internal static PaletteSnapshot Decode(byte[] bytes)=>Import(JsonSerializer.Deserialize<Data>(bytes)??throw new InvalidDataException("Missing snapshot."));
}
