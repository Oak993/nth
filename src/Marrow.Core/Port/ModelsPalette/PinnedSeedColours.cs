// PORT-ONLY: pinned lobehub toc from the licensed design mirror (MIT), never fetched at runtime; ED-NEW-models-palette-6.
using System.Security.Cryptography;
using System.Text.Json;
namespace Marrow.Core.Port.ModelsPalette;
public static class PinnedSeedColours
{
    private static readonly (string[] Keys,ColourSeed Seed)[] entries;
    public static string Digest { get; }
    static PinnedSeedColours()
    {
        using var stream=typeof(PinnedSeedColours).Assembly.GetManifestResourceStream("Marrow.Core.ModelsPalette.Toc.json")!;
        using var bytes=new MemoryStream();stream.CopyTo(bytes);var data=bytes.ToArray();Digest=Convert.ToHexString(SHA256.HashData(data));
        using var root=JsonDocument.Parse(data);var list=new List<(string[],ColourSeed)>();
        foreach(var entry in root.RootElement.EnumerateArray())
        {
            if(!entry.TryGetProperty("color",out var colour)||colour.ValueKind!=JsonValueKind.String)continue;
            var hex=colour.GetString()!;if(hex.Length==4)hex="#"+string.Concat(hex.Skip(1).Select(c=>new string(c,2)));
            if(hex.Length!=7)continue;var lab=PaletteMath.Lab(hex);if(lab.C<.06||lab.L<=.25||lab.L>=.95)continue;
            var keys=new[]{"id","title","fullTitle","docsUrl"}.Select(k=>entry.TryGetProperty(k,out var p)&&p.ValueKind==JsonValueKind.String?Normalize(p.GetString()!):"").Where(k=>k.Length>0).ToArray();
            list.Add((keys,new(false,(int)Math.Round(lab.L/.005),(int)Math.Round(lab.C/.005),(int)Math.Round(lab.Hue)%360)));
        }
        entries=list.ToArray();
    }
    private static string Normalize(string x)=>new(x.ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());
    public static ColourSeed? Find(string maker,string? display,string? hfOrg)
    {
        var candidates=new[]{maker,maker.EndsWith("-ai",StringComparison.Ordinal)?maker[..^3]:maker,display??"",hfOrg??""}.Select(Normalize).Where(k=>k.Length>0).ToHashSet(StringComparer.Ordinal);
        return entries.Where(e=>e.Keys.Any(candidates.Contains)).OrderBy(e=>string.Join('|',e.Keys),StringComparer.Ordinal).Select(e=>e.Seed).FirstOrDefault();
    }
}
