using System.Security.Cryptography;
using System.Text;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.Services.Tests.Port.ModelsPalette;
// PORT-ONLY: seed caches are untrusted, malformed entries are quarantined individually; ED-NEW-models-palette-6.
[Trait("Decision","ED-NEW-modelspalette-6")]
public sealed class PaletteSeedCacheTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Key\":null,\"Seed\":null,\"Evidence\":null,\"Day\":0}")]
    [InlineData("{\"Key\":\"bad\",\"Seed\":{},\"Evidence\":[],\"Day\":0}")]
    public void Null_or_incomplete_entries_cannot_crash_launch_or_rewrite_the_original(string entry)
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();var root=roots.New();
        var path=Path.Combine(root,"models-palette","seeds-v1.json");Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var data="{\"acme\":"+entry+"}";var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data)));
        var body=Encoding.UTF8.GetBytes("{\"schema\":1,\"hash\":\""+hash+"\",\"data\":"+data+"}");File.WriteAllBytes(path,body);
        using var engine=new PaletteEngine(root,fake.Http,fake,fake.Time,fake.Pack);
        Assert.Equal(body,File.ReadAllBytes(path));Assert.NotEmpty(Directory.GetFiles(Path.GetDirectoryName(path)!,"*.corrupt-*"));
        Assert.NotEmpty(engine.Current.Models);
    }
    [Fact]
    public async Task A_future_seed_schema_is_never_overwritten_by_this_engine()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();var root=roots.New();
        var path=Path.Combine(root,"models-palette","seeds-v1.json");Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var body=Encoding.UTF8.GetBytes("{\"schema\":2,\"data\":{\"unrecognised\":true}}");File.WriteAllBytes(path,body);
        using var engine=new PaletteEngine(root,fake.Http,fake,fake.Time,fake.Pack);await engine.RefreshNow();
        Assert.Equal(body,File.ReadAllBytes(path));Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!,"*.corrupt-*"));
    }
}
