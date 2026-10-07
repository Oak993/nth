// PORT-ONLY: local-only C22 bake. Reads official artifacts already downloaded by the owner;
// never downloads, invents a seed, accepts a supplied hash, or publishes a pack. ED-NEW-models-palette-11.
using System.Security.Cryptography;
using System.Text.Json;
using Marrow.Core.Port.ModelsPalette;
namespace Marrow.Services.Port.ModelsPalette;
public static class PaletteBake
{
    public static PaletteReleasePack FromManifest(string manifest,ILogoDecoder decoder,PaletteSourcePack? source=null,PaletteReleasePack? previous=null)
    {
        using var json=JsonDocument.Parse(File.ReadAllBytes(manifest));var root=json.RootElement;var seeds=new List<PalettePackSeed>();var placeholderEvidence=new List<PalettePlaceholderEvidence>();var placeholders=new Dictionary<string,string[]>(StringComparer.Ordinal);
        if(previous is null&&root.TryGetProperty("previousPack",out var previousPath))
        {using var stream=File.OpenRead(Path.GetFullPath(previousPath.GetString()!,Path.GetDirectoryName(Path.GetFullPath(manifest))!));previous=PaletteReleasePack.ReadCompressed(stream);}
        foreach(var image in root.GetProperty("images").EnumerateArray())
        {
            var path=Path.GetFullPath(image.GetProperty("path").GetString()!,Path.GetDirectoryName(Path.GetFullPath(manifest))!);var bytes=File.ReadAllBytes(path);var type=image.GetProperty("type").GetString()!;
            if(bytes.Length>512*1024||!PaletteHttp.ImageMagic(bytes,type))throw new InvalidDataException("Invalid bake image.");
            var decoded=decoder.Decode(bytes,type)??throw new InvalidDataException("Bake decoder failed.");var dominant=DominantColour.Extract(decoded);
            if(!dominant.Decisive)throw new InvalidDataException("Bake evidence is not decisive.");
            var evidence=new PaletteSeedEvidence(Url(image),Convert.ToHexString(SHA256.HashData(bytes)),dominant.Rgba64.ToArray(),image.GetProperty("fetchedAt").GetDateTimeOffset(),bytes.LongLength);
            seeds.Add(new(image.GetProperty("maker").GetString()!,dominant.Seed!,[evidence]));
        }
        foreach(var group in root.GetProperty("placeholders").EnumerateArray().GroupBy(x=>x.GetProperty("kind").GetString()!,StringComparer.Ordinal))
            placeholders.Add(group.Key,group.Select(x=>{var url=Url(x);var when=x.GetProperty("fetchedAt").GetDateTimeOffset();var path=Path.GetFullPath(x.GetProperty("path").GetString()!,Path.GetDirectoryName(Path.GetFullPath(manifest))!);var bytes=File.ReadAllBytes(path);var hash=Convert.ToHexString(SHA256.HashData(bytes));placeholderEvidence.Add(new(group.Key,url,hash,when,bytes.LongLength));return hash;}).Order(StringComparer.Ordinal).ToArray());
        return PaletteReleasePack.Bake(source??PaletteSourcePack.Default,root.GetProperty("version").GetInt32(),seeds,previous,catalogueAge:root.TryGetProperty("catalogueAge",out var age)?age.GetInt32():0,placeholders:placeholders,placeholderEvidence:placeholderEvidence,catalogueDate:root.TryGetProperty("catalogueDate",out var date)?date.GetDateTimeOffset():null,aliases:root.TryGetProperty("aliases",out var a)?a.Deserialize<Dictionary<string,string>>():null);
    }
    private static string Url(JsonElement row)
    {var url=row.GetProperty("url").GetString()!;if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https")throw new InvalidDataException("Bake evidence needs its official HTTPS URL.");return url;}
    public static int Run(string[] args,ILogoDecoder decoder)
    {
        if(args.Length!=3){Console.Error.WriteLine("Usage: --colour-bake <manifest.json|--baseline> <pack.json.gz>");return 2;}
        try{var pack=args[1]=="--baseline"?PaletteReleasePack.Bake(PaletteSourcePack.Default,1,[]):FromManifest(args[1],decoder);File.WriteAllBytes(args[2],pack.Compress());Console.WriteLine("Wrote pack.v"+pack.Version+"; "+pack.Seeds.Count+" evidence-bearing seeds; digest "+pack.Snapshot0.Digest);return 0;}
        catch(Exception e) when(e is IOException or JsonException or InvalidDataException or ArgumentException){Console.Error.WriteLine(e.Message);return 1;}
    }
}
