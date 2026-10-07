using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.ModelsPalette;

[Trait("Decision", "ED-NEW-modelspalette-2")]
public sealed class PublicModelCatalogueStoreCacheValidationTests
{
    [Theory]
    [InlineData("{\"version\":\"1\",\"fingerprint\":\"wrong\",\"data\":[{\"id\":\"maker/model\"}]}")]
    [InlineData("{\"version\":null,\"fingerprint\":\"wrong\",\"data\":[{\"id\":\"maker/model\"}]}")]
    [InlineData("{\"version\":1,\"fingerprint\":42,\"data\":[{\"id\":\"maker/model\"}]}")]
    [InlineData("{\"version\":1,\"fingerprint\":\"wrong\",\"data\":[{\"id\":\"maker/model\"}]}")]
    public void Wrong_typed_versions_and_fingerprints_are_rejected_without_mutating_cache_bytes(string body)
    {
        using var roots = new ModelsPaletteRoots();
        var root = roots.New();
        string path;
        using (var fresh = new PublicModelCatalogueStore(root)) path = fresh.CachePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, body);
        using var reloaded = new PublicModelCatalogueStore(root);
        Assert.Null(reloaded.Current);
        Assert.Equal(body, File.ReadAllText(path));
    }
}
