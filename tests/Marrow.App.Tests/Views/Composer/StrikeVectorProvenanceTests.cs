using System.Security.Cryptography;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Views.Composer;

public sealed class StrikeVectorProvenanceTests
{
    [Fact]
    [Trait("Decision", "ED-248")]
    public void Every_locked_vector_and_its_generator_keep_the_original_bytes()
    {
        static string Hash(string name) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(StrikeVectors.Folder, name)))).ToLowerInvariant();
        Assert.Equal("7af9484be718d690ab74b1758d5f1dcb70e73b2c65f6e0c5effa78db6237aa5b", Hash("manifest.json"));
        Assert.Equal("47867eb37b6ab60332808513ac7021547efce9b1d86b4ab8905d988d26c11f5a", Hash("gen-vectors.mjs"));
        using var manifest = StrikeVectors.Read("manifest.json");
        Assert.Equal(13, manifest.RootElement.GetProperty("files").EnumerateObject().Count());
        foreach (var file in manifest.RootElement.GetProperty("files").EnumerateObject())
        {
            Assert.Equal(file.Value.GetProperty("sha256").GetString(), Hash(file.Name));
            Assert.Equal(file.Value.GetProperty("bytesOnDisk").GetInt64(), new FileInfo(Path.Combine(StrikeVectors.Folder, file.Name)).Length);
        }
        Assert.Equal("ce2da0566ae9f611141ed9c3b744c702061283007d25b87ccc6eb25b9328a518", manifest.RootElement.GetProperty("sources").GetProperty("strike-engine.js").GetString());
    }
}
