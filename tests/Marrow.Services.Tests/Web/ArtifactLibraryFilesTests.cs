using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces W13 (DESIGN.md 6.5 "Libraries are vendored and served offline ... Each licence is checked and
// bundled"). The library folder copied beside the app holds every library the design names, each with its licence file,
// every file matching the hash its manifest records; CDN addresses from other apps' artifacts map to the local copies.
[Trait("Decision", "ED-NEW-web-w13-3")]
public sealed class ArtifactLibraryFilesTests
{
    private static readonly ArtifactLibraryCatalog Catalog = ArtifactRuntimeRig.Libraries.Catalog;

    [Fact]
    public void Every_library_the_design_names_is_vendored_with_its_licence()
    {
        foreach (var id in new[] { "react", "react-dom", "sucrase", "tailwindcss", "recharts", "lucide", "lucide-react", "d3", "chart.js", "mermaid", "three", "katex", "highlight.js", "papaparse", "lodash", "mathjs" })
        {
            var library = Catalog.ById(id);
            Assert.True(library is not null, id + " is not vendored");
            Assert.False(string.IsNullOrEmpty(library!.License), id + " has no licence id");
            Assert.NotNull(library.LicenseFile);
            Assert.Contains(library.Files, f => f.Path == library.LicenseFile);
            Assert.Contains(library.Files, f => f.Path == library.Script);
        }

        Assert.Equal("18.3.1", Catalog.ById("react")!.Version);
    }

    [Fact]
    public void Every_listed_file_is_served_and_matches_its_hash()
    {
        var files = Catalog.Libraries.SelectMany(l => l.Files).ToList();
        Assert.True(files.Count > 40);
        foreach (var file in files)
        {
            var bytes = ArtifactRuntimeRig.Libraries.Read(file.Path);
            Assert.True(bytes is not null, file.Path + " is missing or does not match its hash");
            Assert.Equal(file.Bytes, bytes!.LongLength);
        }

        foreach (var library in Catalog.Libraries.Where(l => l.Esm is not null))
            Assert.NotNull(ArtifactRuntimeRig.Libraries.Read(library.Esm!));
        Assert.Null(ArtifactRuntimeRig.Libraries.Read("manifest.json"));
        Assert.Null(ArtifactRuntimeRig.Libraries.Read("../Marrow.Services.dll"));
    }

    [Fact]
    public void A_damaged_file_is_never_served()
    {
        var folder = Path.Combine(Marrow.Testing.TestDataDirectory.Read()!, "ArtifactLibraries", Guid.NewGuid().ToString("N"));
        foreach (var path in Directory.GetFiles(ArtifactLibraryFiles.DefaultFolder, "*", SearchOption.AllDirectories).Where(p => p.Contains("papaparse", StringComparison.Ordinal) || p.EndsWith("manifest.json", StringComparison.Ordinal)))
        {
            var target = Path.Combine(folder, Path.GetRelativePath(ArtifactLibraryFiles.DefaultFolder, path));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(path, target);
        }

        var script = Catalog.ById("papaparse")!.Script;
        File.AppendAllText(Path.Combine(folder, script.Replace('/', Path.DirectorySeparatorChar)), "\nfetch('https://evil.example');");
        Assert.Null(new ArtifactLibraryFiles(folder).Read(script));
        Assert.Same(ArtifactLibraryCatalog.Empty, new ArtifactLibraryFiles(Path.Combine(folder, "nowhere")).Catalog);
    }

    [Fact]
    public void Cdn_addresses_map_to_the_local_copies()
    {
        Assert.Equal("https://lib-nth.invalid/react/react.production.min.js", Catalog.Resolve("https://unpkg.com/react@18/umd/react.production.min.js"));
        Assert.Equal("https://lib-nth.invalid/esm/react.js", Catalog.Resolve("https://esm.sh/react@18.3.1"));
        Assert.Equal(ArtifactLibraryCatalog.Url(Catalog.ById("d3")!.Script), Catalog.Resolve("https://cdn.jsdelivr.net/npm/d3@7"));
        Assert.Equal(ArtifactLibraryCatalog.Url(Catalog.ById("chart.js")!.Script), Catalog.Resolve("https://cdnjs.cloudflare.com/ajax/libs/Chart.js/4.4.1/chart.umd.min.js"));
        Assert.Equal(ArtifactLibraryCatalog.Url(Catalog.ById("tailwindcss")!.Script), Catalog.Resolve("https://cdn.tailwindcss.com/3.4.1"));
        Assert.Equal(ArtifactLibraryCatalog.BabelShim, Catalog.Resolve("https://unpkg.com/@babel/standalone/babel.min.js"));
        Assert.Equal(ArtifactLibraryCatalog.JsxRuntime, Catalog.Resolve("https://esm.sh/react@18/jsx-runtime"));
        Assert.StartsWith("https://lib-nth.invalid/katex/", Catalog.Resolve("https://cdn.jsdelivr.net/npm/katex@0.16.9/dist/katex.min.css"), StringComparison.Ordinal);
        Assert.Null(Catalog.Resolve("https://unpkg.com/left-pad@1.3.0/index.js"));
        Assert.Null(Catalog.Resolve("https://cdn.jsdelivr.net/npm/react-dom@18/server.browser.js"));

        var rewrites = new List<ArtifactCdnRewrite>();
        var text = Catalog.RewriteCdn("<link href=\"https://unpkg.com/nothing@1/x.css\" rel=stylesheet><script src='//unpkg.com/left-pad@1.3.0/index.js'></script>", rewrites);
        Assert.Contains(ArtifactLibraryCatalog.MissingStyle + "?u=", text, StringComparison.Ordinal);
        Assert.Contains(ArtifactLibraryCatalog.MissingScript + "?u=", text, StringComparison.Ordinal);
        Assert.All(rewrites, r => Assert.True(r.IsMissing));
    }

    [Fact]
    public void The_import_map_and_the_runner_table_cover_every_library()
    {
        var map = System.Text.Json.Nodes.JsonNode.Parse(Catalog.ImportMapJson())!["imports"]!.AsObject();
        Assert.Equal("https://lib-nth.invalid/esm/recharts.js", map["recharts"]!.GetValue<string>());
        Assert.Equal(ArtifactLibraryCatalog.JsxRuntime, map["react/jsx-runtime"]!.GetValue<string>());
        Assert.Equal("https://lib-nth.invalid/esm/three-orbit-controls.js", map["three/examples/jsm/controls/OrbitControls.js"]?.GetValue<string>() ?? map.First(p => p.Key.Contains("OrbitControls", StringComparison.Ordinal)).Value!.GetValue<string>());
        var table = System.Text.Json.Nodes.JsonNode.Parse(Catalog.RunnerTableJson())!;
        Assert.Equal("recharts", table["specifiers"]!["recharts"]!.GetValue<string>());
        Assert.Equal(["react", "react-dom", "prop-types"], table["libraries"]!["recharts"]!["deps"]!.AsArray().Select(d => d!.GetValue<string>()));
        Assert.Equal(["react", "react-dom", "prop-types", "recharts"], Catalog.LoadOrder(["recharts"]).Select(l => l.Id));
    }
}
