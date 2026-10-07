// PORT-ONLY: versioned seed evidence and full boundary snapshots; AC-3 PLAN 6.3; ED-NEW-models-palette-6.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Marrow.Core.Port.ModelsPalette;
namespace Marrow.Services.Port.ModelsPalette;
internal sealed record StoredSeed(string Key,ColourSeed Seed,SeedEvidence[] Evidence,long Day,string? CandidateHash=null,ColourSeed? Candidate=null,SeedEvidence[]? CandidateEvidence=null,long CandidateDay=-1,string SeedVersion="S2",string? InputDigest=null);
internal sealed record StoredMarket(long Window,long Date,int Age,string Body);
internal sealed class PaletteStore
{
    public string SeedsPath { get; }
    public string MarketPath { get; }
    private string SnapshotPath=>Path.Combine(Path.GetDirectoryName(SeedsPath)!,"snapshot-v1.json");
    private readonly HashSet<string> pendingReads=new(StringComparer.Ordinal);
    private readonly Dictionary<string,(byte[] Bytes,bool Previous)> pendingWrites=new(StringComparer.Ordinal);
    public bool HasPendingReads=>pendingReads.Count!=0||pendingWrites.Count!=0;
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
        try{var body=JsonDocument.Parse(File.ReadAllBytes(path));pendingReads.Remove(path);return body;}
        catch(FileNotFoundException){pendingReads.Remove(path);return null;}catch(DirectoryNotFoundException){pendingReads.Remove(path);return null;}
        catch(JsonException){pendingReads.Remove(path);Quarantine(path);return null;}catch(IOException){pendingReads.Add(path);return null;}catch(UnauthorizedAccessException){pendingReads.Add(path);return null;}
    }
    private static void Quarantine(string path)
    {try{if(File.Exists(path))File.Copy(path,path+".corrupt-"+Guid.NewGuid().ToString("N"));}catch(IOException){}catch(UnauthorizedAccessException){}}
    public void SaveSeeds(Dictionary<string,StoredSeed> seeds)=>Write(SeedsPath,seeds,false);
    public void SaveMarket(StoredMarket market)=>Write(MarketPath,market,true);
    public void SaveSnapshot(PaletteSnapshot snapshot)=>Write(SnapshotPath,PaletteSnapshotCodec.Export(snapshot),false);
    public PaletteSnapshot? LoadSnapshot()
    {
        using var body=Read(SnapshotPath);if(body is null||!Schema(body.RootElement,SnapshotPath))return null;
        try{return PaletteSnapshotCodec.Import(body.RootElement.GetProperty("data").Deserialize<PaletteSnapshotCodec.Data>()!);}
        catch(Exception e) when(e is JsonException or InvalidDataException or ArgumentException or NullReferenceException){Quarantine(SnapshotPath);return null;}
    }
    private void Write<T>(string path,T data,bool previous)
    {
        if(readOnly.Contains(path))return;
        var body=JsonSerializer.Serialize(data);var encoded=Encoding.UTF8.GetBytes("{\"schema\":1,\"hash\":\""+Digest(body)+"\",\"data\":"+body+"}");
        WriteEncoded(path,encoded,previous);
    }
    public void RetryPendingWrites()
    {
        foreach(var p in pendingWrites.ToArray())
            try{WriteEncoded(p.Key,p.Value.Bytes,p.Value.Previous);}catch(IOException){}catch(UnauthorizedAccessException){}
    }
    private void WriteEncoded(string path,byte[] encoded,bool previous)
    {
        if(readOnly.Contains(path)){pendingWrites.Remove(path);return;}
        try
        {
        if(File.Exists(path)&&File.ReadAllBytes(path).AsSpan().SequenceEqual(encoded)){pendingWrites.Remove(path);return;}
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllBytes(temporary,encoded);if(File.Exists(path))File.Replace(temporary,path,previous?path+".prev":null);else File.Move(temporary,path);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
        pendingWrites.Remove(path);pendingReads.Remove(path);
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){pendingWrites[path]=(encoded,previous);throw;}
    }
}
