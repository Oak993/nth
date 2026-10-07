// PORT-ONLY: versioned seed evidence and full boundary snapshots; AC-3 PLAN 6.3; ED-NEW-models-palette-6.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Marrow.Core.Port.ModelsPalette;
namespace Marrow.Services.Port.ModelsPalette;
internal sealed record StoredSeed(string Key,ColourSeed Seed,SeedEvidence[] Evidence,long Day,string? CandidateHash=null,ColourSeed? Candidate=null,SeedEvidence[]? CandidateEvidence=null,long CandidateDay=-1);
internal sealed record StoredMarket(long Window,long Date,int Age,string Body);
internal sealed class PaletteStore
{
    public string SeedsPath { get; }
    public string MarketPath { get; }
    private readonly HashSet<string> readOnly=new(StringComparer.Ordinal);
    public PaletteStore(string root){SeedsPath=Path.Combine(root,"models-palette","seeds-v1.json");MarketPath=Path.Combine(root,"models-palette","market-v1.json");}
    public Dictionary<string,StoredSeed> LoadSeeds()
    {
        var result=new Dictionary<string,StoredSeed>(StringComparer.Ordinal);var body=Read(SeedsPath);if(body is null)return result;
        using(body)
        {
            if(!Schema(body.RootElement,SeedsPath))return result;
            if(!body.RootElement.TryGetProperty("data",out var rows)||rows.ValueKind!=JsonValueKind.Object){Quarantine(SeedsPath);return result;}
            foreach(var p in rows.EnumerateObject())
            {
                try
                {
                    var value=p.Value.Deserialize<StoredSeed>();
                    if(value is null||!Valid(value)) {Quarantine(SeedsPath);continue;}
                    result.Add(p.Name,value);
                }
                catch(JsonException){Quarantine(SeedsPath);}
            }
        }
        return result;
    }
    private static bool Valid(StoredSeed seed)=>seed.Key is {Length:64}&&seed.Day>=0&&seed.Seed is not null&&ValidSeed(seed.Seed)&&seed.Evidence is {Length:>0}&&seed.Evidence.All(e=>e is not null&&e.Sha256 is {Length:64}&&e.Rgba64 is {Length:0 or 16384})&&
        (seed.Candidate is null||ValidSeed(seed.Candidate)&&seed.CandidateEvidence is {Length:>0}&&seed.CandidateDay>=0);
    private static bool ValidSeed(ColourSeed s)=>s.Hue>=0&&s.Hue<360&&s.Lightness>=0&&s.Lightness<=200&&s.Chroma>=0&&s.Chroma<=90;
    public StoredMarket? LoadMarket()
    {
        foreach(var path in new[]{MarketPath,MarketPath+".prev"})
        {
            using var body=Read(path);if(body is null||!Schema(body.RootElement,path))continue;
            try
            {
                var m=body.RootElement.GetProperty("data").Deserialize<StoredMarket>();
                if(m is not null&&m.Window>=0&&m.Date>=m.Window*21600&&m.Age>=0&&PublicModelCatalogue.Parse(m.Body) is not null)return m;
            }
            catch(JsonException){}catch(KeyNotFoundException){}
            Quarantine(path);
        }
        return null;
    }
    private bool Schema(JsonElement root,string path)
    {
        if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("schema",out var s)||s.ValueKind!=JsonValueKind.Number||!s.TryGetInt32(out var schema)){Quarantine(path);return false;}
        if(schema>1){readOnly.Add(path);return false;}if(schema!=1){Quarantine(path);return false;}
        if(!root.TryGetProperty("hash",out var hash)||hash.ValueKind!=JsonValueKind.String||!root.TryGetProperty("data",out var data)||hash.GetString()!=Digest(data.GetRawText())){Quarantine(path);return false;}return true;
    }
    private static string Digest(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private JsonDocument? Read(string path)
    {
        try{return JsonDocument.Parse(File.ReadAllBytes(path));}
        catch(FileNotFoundException){return null;}catch(DirectoryNotFoundException){return null;}
        catch(JsonException){Quarantine(path);return null;}catch(IOException){readOnly.Add(path);return null;}catch(UnauthorizedAccessException){readOnly.Add(path);return null;}
    }
    private static void Quarantine(string path)
    {try{if(File.Exists(path))File.Copy(path,path+".corrupt-"+Guid.NewGuid().ToString("N"));}catch(IOException){}catch(UnauthorizedAccessException){}}
    public void SaveSeeds(Dictionary<string,StoredSeed> seeds)=>Write(SeedsPath,seeds,false);
    public void SaveMarket(StoredMarket market)=>Write(MarketPath,market,true);
    private void Write<T>(string path,T data,bool previous)
    {
        if(readOnly.Contains(path))return;
        var body=JsonSerializer.Serialize(data);var encoded=Encoding.UTF8.GetBytes("{\"schema\":1,\"hash\":\""+Digest(body)+"\",\"data\":"+body+"}");
        if(File.Exists(path)&&File.ReadAllBytes(path).AsSpan().SequenceEqual(encoded))return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllBytes(temporary,encoded);if(File.Exists(path))File.Replace(temporary,path,previous?path+".prev":null);else File.Move(temporary,path);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
