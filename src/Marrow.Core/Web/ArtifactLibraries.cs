// PORT-ONLY: web surfaces W13 (DESIGN.md 6.5 "Libraries"; ED-NEW-web-w13-3). The vendored offline libraries as nth
// reads them from Assets/lib/manifest.json (built by tools/artifact-libs/build.mjs): each library's version, licence,
// global, files and the names it answers. From it come the import map every sandboxed page gets, the table the React
// runner loads libraries by, and the rewrite of CDN addresses (cdnjs, unpkg, jsDelivr, esm.sh, Skypack, JSPM, the Tailwind
// play CDN) in artifacts made for other apps: to the local copy when one exists, else to a marker that says clearly which
// file is not available offline. No Swift counterpart.
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Marrow.Core.Web;

/// <summary>One file of a library, with its size and SHA-256 as the manifest records them.</summary>
public sealed record ArtifactLibraryFile(string Path, long Bytes, string Sha256);

/// <summary>One vendored library (a manifest entry).</summary>
public sealed record ArtifactLibrary
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Package { get; init; }
    public required string Version { get; init; }
    public required string License { get; init; }
    public string? LicenseFile { get; init; }

    /// <summary>The global the classic script sets (dotted for a member, as THREE.OrbitControls), or null (Tailwind).</summary>
    public string? Global { get; init; }

    /// <summary>The classic script, relative to the library host.</summary>
    public required string Script { get; init; }

    public IReadOnlyList<string> Css { get; init; } = [];
    public IReadOnlyList<string> Deps { get; init; } = [];
    public IReadOnlyList<string> Specifiers { get; init; } = [];
    public IReadOnlyList<string> Cdn { get; init; } = [];

    /// <summary>The ES module wrapper the import map points at, or null.</summary>
    public string? Esm { get; init; }

    /// <summary>Script that runs before the classic file (Lucide React reads React as the global "react").</summary>
    public string? Prelude { get; init; }

    public string? ExportAs { get; init; }
    public IReadOnlyList<string> Exports { get; init; } = [];
    public IReadOnlyList<ArtifactLibraryFile> Files { get; init; } = [];
}

/// <summary>How a CDN address was answered.</summary>
public sealed record ArtifactCdnRewrite(string Original, string? Local)
{
    public bool IsMissing => Local is null;
}

public sealed partial class ArtifactLibraryCatalog
{
    /// <summary>nth's own runtime files on the library host (served from the Services assembly).</summary>
    public const string RuntimePrefix = "nth/";

    /// <summary>The JSX runtime module (React's automatic runtime over the UMD global).</summary>
    public const string JsxRuntime = SandboxSites.LibraryOrigin + "/nth/jsx-runtime.js";

    /// <summary>The marker a CDN address with no local copy becomes; the original address is its query.</summary>
    public const string MissingScript = SandboxSites.LibraryOrigin + "/nth/missing.js";

    public const string MissingStyle = SandboxSites.LibraryOrigin + "/nth/missing.css";

    /// <summary>nth's stand-in for in-browser Babel: text/babel scripts compiled with the vendored Sucrase.</summary>
    public const string BabelShim = SandboxSites.LibraryOrigin + "/nth/babel-shim.js";

    private static readonly HashSet<string> CdnHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "cdnjs.cloudflare.com", "unpkg.com", "cdn.jsdelivr.net", "esm.sh", "cdn.skypack.dev", "ga.jspm.io", "cdn.tailwindcss.com",
    };

    private readonly Dictionary<string, ArtifactLibrary> byId;
    private readonly Dictionary<string, ArtifactLibrary> bySpecifier;
    private readonly Dictionary<string, List<ArtifactLibrary>> byCdnName;

    private ArtifactLibraryCatalog(IReadOnlyList<ArtifactLibrary> libraries)
    {
        Libraries = libraries;
        byId = libraries.ToDictionary(l => l.Id, StringComparer.Ordinal);
        bySpecifier = new Dictionary<string, ArtifactLibrary>(StringComparer.Ordinal);
        byCdnName = new Dictionary<string, List<ArtifactLibrary>>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in libraries)
        {
            foreach (var specifier in library.Specifiers) bySpecifier[specifier] = library;
            foreach (var name in library.Cdn.Append(library.Package).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!byCdnName.TryGetValue(name, out var list)) byCdnName[name] = list = [];
                list.Add(library);
            }
        }
    }

    public static ArtifactLibraryCatalog Empty { get; } = new([]);

    public IReadOnlyList<ArtifactLibrary> Libraries { get; }

    /// <summary>Reads manifest.json. Throws <see cref="JsonException"/> or <see cref="InvalidDataException"/> on a broken manifest.</summary>
    public static ArtifactLibraryCatalog Parse(string manifestJson)
    {
        var root = JsonNode.Parse(manifestJson) as JsonObject ?? throw new InvalidDataException("The library manifest is not an object.");
        if (root["format"]?.GetValue<int>() != 1) throw new InvalidDataException("Unknown library manifest format.");
        var libraries = new List<ArtifactLibrary>();
        foreach (var node in root["libraries"]?.AsArray() ?? [])
        {
            if (node is not JsonObject entry) continue;
            libraries.Add(new ArtifactLibrary
            {
                Id = Text(entry, "id")!,
                Name = Text(entry, "name")!,
                Package = Text(entry, "package")!,
                Version = Text(entry, "version")!,
                License = Text(entry, "license")!,
                LicenseFile = Text(entry, "licenseFile"),
                Global = Text(entry, "global"),
                Script = Text(entry, "script")!,
                Css = List(entry, "css"),
                Deps = List(entry, "deps"),
                Specifiers = List(entry, "specifiers"),
                Cdn = List(entry, "cdn"),
                Esm = Text(entry, "esm"),
                Prelude = Text(entry, "prelude"),
                ExportAs = Text(entry, "exportAs"),
                Exports = List(entry, "exports"),
                Files = (entry["files"]?.AsArray() ?? []).OfType<JsonObject>()
                    .Select(f => new ArtifactLibraryFile(Text(f, "path")!, f["bytes"]!.GetValue<long>(), Text(f, "sha256")!)).ToList(),
            });
        }

        if (libraries.Any(l => l.Id is null || l.Script is null || l.Version is null)) throw new InvalidDataException("A library entry is incomplete.");
        return new ArtifactLibraryCatalog(libraries);
    }

    public ArtifactLibrary? ById(string id) => byId.GetValueOrDefault(id);

    public ArtifactLibrary? BySpecifier(string specifier) => bySpecifier.GetValueOrDefault(specifier);

    /// <summary>The address of a library file on the library host.</summary>
    public static string Url(string path) => SandboxSites.LibraryOrigin + "/" + path;

    /// <summary>
    /// The import map every sandboxed page gets before its own scripts: each specifier to its ES module wrapper, and
    /// React's JSX runtimes to nth's module.
    /// </summary>
    public string ImportMapJson()
    {
        var imports = new JsonObject();
        foreach (var library in Libraries.Where(l => l.Esm is not null))
            foreach (var specifier in library.Specifiers.Order(StringComparer.Ordinal))
                imports[specifier] = Url(library.Esm!);
        if (byId.ContainsKey("react"))
        {
            imports["react/jsx-runtime"] = JsxRuntime;
            imports["react/jsx-dev-runtime"] = JsxRuntime;
        }

        return new JsonObject { ["imports"] = imports }.ToJsonString();
    }

    /// <summary>
    /// The table nth's runners load libraries by: every specifier to its library, and each library's script, global,
    /// prelude and dependencies.
    /// </summary>
    public string RunnerTableJson()
    {
        var specifiers = new JsonObject();
        foreach (var library in Libraries)
            foreach (var specifier in library.Specifiers.Order(StringComparer.Ordinal))
                specifiers[specifier] = library.Id;
        var libraries = new JsonObject();
        foreach (var library in Libraries)
        {
            libraries[library.Id] = new JsonObject
            {
                ["script"] = Url(library.Script),
                ["global"] = library.Global,
                ["prelude"] = library.Prelude,
                ["exportAs"] = library.ExportAs,
                ["deps"] = new JsonArray(library.Deps.Select(d => (JsonNode)d).ToArray()),
                ["css"] = new JsonArray(library.Css.Select(c => (JsonNode)Url(c)).ToArray()),
            };
        }

        return new JsonObject { ["specifiers"] = specifiers, ["libraries"] = libraries }.ToJsonString();
    }

    /// <summary>Libraries with every dependency before its dependents, each once.</summary>
    public IReadOnlyList<ArtifactLibrary> LoadOrder(IEnumerable<string> ids)
    {
        var order = new List<ArtifactLibrary>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string id)
        {
            if (!seen.Add(id) || !byId.TryGetValue(id, out var library)) return;
            foreach (var dep in library.Deps) Visit(dep);
            order.Add(library);
        }

        foreach (var id in ids) Visit(id);
        return order;
    }

    [GeneratedRegex(@"(?<![\w.@-])(?:https?:)?//(cdnjs\.cloudflare\.com|unpkg\.com|cdn\.jsdelivr\.net|esm\.sh|cdn\.skypack\.dev|ga\.jspm\.io|cdn\.tailwindcss\.com)(/[^\s""'`<>()\\]*)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CdnUrl();

    [GeneratedRegex(@"<link\b[^>]*\bhref\s*=\s*[""']([^""']*tailwind[^""']*\.css)[^""']*[""'][^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TailwindStylesheet();

    /// <summary>
    /// Rewrites every CDN address in a page, a script or a style sheet: to the local copy, or to the missing marker. A
    /// Tailwind style sheet link becomes the Tailwind browser script (there is no static Tailwind sheet offline). Each
    /// rewrite is reported in <paramref name="rewrites"/>.
    /// </summary>
    public string RewriteCdn(string text, List<ArtifactCdnRewrite>? rewrites = null)
    {
        if (text.Length == 0 || !text.Contains("//", StringComparison.Ordinal)) return text;
        if (byId.TryGetValue("tailwindcss", out var tailwind))
        {
            text = TailwindStylesheet().Replace(text, match =>
            {
                if (!IsCdnAddress(match.Groups[1].Value)) return match.Value;
                rewrites?.Add(new ArtifactCdnRewrite(match.Groups[1].Value, Url(tailwind.Script)));
                return "<script src=\"" + Url(tailwind.Script) + "\"></script>";
            });
        }

        return CdnUrl().Replace(text, match =>
        {
            var original = match.Value;
            var local = Resolve(original);
            rewrites?.Add(new ArtifactCdnRewrite(original, local));
            return local ?? Missing(original);
        });
    }

    /// <summary>The local address for a CDN address, or null when nth has no copy of that file.</summary>
    public string? Resolve(string address)
    {
        var url = address.StartsWith("//", StringComparison.Ordinal) ? "https:" + address : address;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !CdnHosts.Contains(uri.Host)) return null;
        var host = uri.Host.ToLowerInvariant();
        var path = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        string? package;
        string file;
        switch (host)
        {
            case "cdn.tailwindcss.com":
                return byId.TryGetValue("tailwindcss", out var play) ? Url(play.Script) : null;
            case "cdnjs.cloudflare.com":
            {
                // ajax/libs/<name>/<version>/<file>
                var parts = path.Split('/', 5);
                if (parts.Length < 4 || parts[0] != "ajax" || parts[1] != "libs") return null;
                package = parts[2];
                file = parts.Length == 5 ? parts[4] : parts[3];
                break;
            }

            case "ga.jspm.io":
            {
                if (!path.StartsWith("npm:", StringComparison.Ordinal)) return null;
                (package, file) = SplitPackage(path[4..]);
                break;
            }

            case "cdn.jsdelivr.net":
            {
                if (!path.StartsWith("npm/", StringComparison.Ordinal)) return null;
                (package, file) = SplitPackage(path[4..]);
                break;
            }

            case "esm.sh":
            {
                var rest = Regex.Replace(path, @"^(?:v\d+|stable)/", "", RegexOptions.CultureInvariant);
                (package, file) = SplitPackage(rest);
                break;
            }

            default:
                (package, file) = SplitPackage(path);
                break;
        }

        if (package is "@babel/standalone" or "babel-standalone" && byId.ContainsKey("sucrase")) return BabelShim;
        if (package is null || !byCdnName.TryGetValue(package, out var candidates)) return null;
        var esm = host is "esm.sh" or "cdn.skypack.dev" or "ga.jspm.io" || file.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase)
            || file.Contains("/esm/", StringComparison.OrdinalIgnoreCase) || file.StartsWith("esm/", StringComparison.OrdinalIgnoreCase)
            || file.Contains("+esm", StringComparison.OrdinalIgnoreCase) || file.Contains(".module.", StringComparison.OrdinalIgnoreCase)
            || file.StartsWith("es/", StringComparison.OrdinalIgnoreCase) || file.Contains("/jsm/", StringComparison.OrdinalIgnoreCase)
            || file.StartsWith("addons/", StringComparison.OrdinalIgnoreCase);
        return Pick(candidates, file, esm);
    }

    private string? Pick(List<ArtifactLibrary> candidates, string file, bool esm)
    {
        var lower = file.ToLowerInvariant();
        ArtifactLibrary Lib(string id) => byId[id];
        var library = candidates[0];

        // Members that live in their own entry: OrbitControls, KaTeX's auto-render, React's JSX runtime.
        if (lower.Contains("orbitcontrols", StringComparison.Ordinal))
            return byId.ContainsKey("three-orbit-controls") ? Choose(Lib("three-orbit-controls"), esm) : null;
        if (lower.Contains("auto-render", StringComparison.Ordinal))
            return byId.ContainsKey("katex-auto-render") ? Choose(Lib("katex-auto-render"), esm) : null;
        if (library.Id == "react" && lower.Contains("jsx-", StringComparison.Ordinal)) return JsxRuntime;
        if (library.Id == "react-dom" && lower.Contains("server", StringComparison.Ordinal)) return null;
        if (lower.Contains("/examples/", StringComparison.Ordinal) || lower.StartsWith("examples/", StringComparison.Ordinal)) return null;

        if (lower.EndsWith(".css", StringComparison.Ordinal))
        {
            if (library.Css.Count == 0) return null;
            var dark = library.Css.FirstOrDefault(c => c.Contains("dark", StringComparison.Ordinal));
            var light = library.Css.FirstOrDefault(c => !c.Contains("dark", StringComparison.Ordinal)) ?? library.Css[0];
            return Url(lower.Contains("dark", StringComparison.Ordinal) && dark is not null ? dark : light);
        }

        if (lower.Contains("fonts/", StringComparison.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var font = library.Files.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f.Path).Equals(name, StringComparison.OrdinalIgnoreCase) && f.Path.EndsWith(".woff2", StringComparison.Ordinal));
            return font is null ? null : Url(font.Path);
        }

        if (lower.Contains("languages/", StringComparison.Ordinal) || lower.Contains("locale", StringComparison.Ordinal)) return null;
        if (lower.Length > 0 && !lower.EndsWith(".js", StringComparison.Ordinal) && !lower.EndsWith(".mjs", StringComparison.Ordinal)
            && !lower.EndsWith('/') && Path.HasExtension(lower)) return null;
        return Choose(library, esm);
    }

    private static string Choose(ArtifactLibrary library, bool esm) => Url(esm && library.Esm is not null ? library.Esm : library.Script);

    /// <summary>"name@version/path", "@scope/name@version/path" or "name/path" as the package name and the path.</summary>
    private static (string? Package, string File) SplitPackage(string path)
    {
        if (path.Length == 0) return (null, "");
        var parts = path.Split('/').ToList();
        var count = parts[0].StartsWith('@') ? 2 : 1;
        if (parts.Count < count) return (null, "");
        var name = string.Join('/', parts.Take(count));
        var at = name.IndexOf('@', 1);
        if (at > 0) name = name[..at];
        return (name, string.Join('/', parts.Skip(count)));
    }

    private static bool IsCdnAddress(string address)
    {
        var url = address.StartsWith("//", StringComparison.Ordinal) ? "https:" + address : address;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && CdnHosts.Contains(uri.Host);
    }

    private static string Missing(string original)
    {
        var path = original.Split('?', '#')[0];
        var marker = path.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ? MissingStyle : MissingScript;
        return marker + "?u=" + Uri.EscapeDataString(original);
    }

    private static string? Text(JsonObject entry, string name) => entry[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static IReadOnlyList<string> List(JsonObject entry, string name) =>
        (entry[name] as JsonArray)?.Select(n => n?.GetValue<string>()).Where(s => s is not null).Select(s => s!).ToList() ?? [];

    /// <summary>A short description of the libraries for the notices and the export header.</summary>
    public string Summary()
    {
        var builder = new StringBuilder();
        foreach (var library in Libraries) builder.Append(library.Name).Append(' ').Append(library.Version).Append(" (").Append(library.License).Append(")\n");
        return builder.ToString();
    }
}
