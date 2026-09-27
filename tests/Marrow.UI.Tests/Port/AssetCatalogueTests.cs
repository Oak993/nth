using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Platform;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

[Collection("Headless")]
public sealed class AssetCatalogueTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-215")]
    public void All_77_non_tour_images_resolve_with_their_declared_intent_and_original_bytes()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Marrow.sln"))) dir = dir.Parent;
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "tests", "Marrow.UI.Tests", "Port", "AssetCatalogueManifest.json")));
            Assert.Equal(77, manifest.RootElement.GetArrayLength());
            foreach (var entry in manifest.RootElement.EnumerateArray())
            {
                var name = entry.GetProperty("name").GetString()!;
                Assert.False(name.StartsWith("tour-", StringComparison.Ordinal));
                var image = SwiftImage.Named(name);
                Assert.False(image.IsEmpty, name);
                Assert.Equal(entry.GetProperty("intent").GetString() == "template" ? ImageRenderingMode.Template : ImageRenderingMode.Original, image.EffectiveRenderingMode);
                foreach (var file in entry.GetProperty("files").EnumerateArray())
                {
                    var filename = file.GetProperty("filename").GetString()!;
                    var asset = name is "droppy-logo" or "hydra-mark" or "badge-edit" || name.StartsWith("hydra-head-", StringComparison.Ordinal)
                        ? filename : name + ".imageset/" + filename;
                    using var stream = AssetLoader.Open(new Uri("avares://Marrow.UI/Assets/Images/" + asset));
                    Assert.Equal(file.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(stream)));
                    if (filename.EndsWith(".svg", StringComparison.Ordinal)) Assert.NotEmpty(image.Icon!.Shapes);
                    else if (file.GetProperty("scale").GetString() == "2x") Assert.NotNull(image.Bitmap2x);
                    else Assert.NotNull(image.Bitmap);
                }
            }
        });
    }
}
