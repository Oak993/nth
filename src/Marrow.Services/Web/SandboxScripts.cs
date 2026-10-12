// PORT-ONLY: web surfaces W13 (DESIGN.md 6.5; ED-NEW-web-w13-1, -3, -4). nth's own sandbox scripts, embedded in this
// assembly so they ship with the code that relies on them: the hardening and bridge scripts every sandboxed document
// runs first, and the runtime files the library host serves under /nth/ (the loader with the library table, the React,
// project and Mermaid runners, the code view, the JSX runtime, the Babel shim, the missing-file marker and the offline
// shim of exported pages). No Swift counterpart.
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Marrow.Core.Web;

namespace Marrow.Services.Web;

public static class SandboxScripts
{
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    /// <summary>The runtime files the library host serves at /nth/&lt;name&gt; (besides the generated ones).</summary>
    public static IReadOnlyList<string> RuntimeFiles { get; } =
        ["react-runner.js", "project-runner.js", "mermaid-runner.js", "code-view.js", "jsx-runtime.js", "babel-shim.js", "export-shim.js"];

    /// <summary>DESIGN 6.5 and 8.1: closes the channels CSP does not (WebRTC, prefetch hints, clipboard writes, fullscreen ...).</summary>
    public static string Hardening => Read("hardening.js");

    /// <summary>DESIGN 6.5: window.nth and the compatibility shims.</summary>
    public static string Bridge => Read("bridge.js");

    /// <summary>The document scripts of every sandboxed view, in order: hardening first, then the bridge.</summary>
    public static IReadOnlyList<string> DocumentScripts => [Hardening, Bridge];

    /// <summary>One embedded script by file name.</summary>
    public static string Read(string name) => Cache.GetOrAdd(name, static key =>
    {
        using var stream = typeof(SandboxScripts).Assembly.GetManifestResourceStream("Marrow.Services.Web.Sandbox." + key)
            ?? throw new InvalidOperationException("The sandbox script " + key + " is missing from the build.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    });

    /// <summary>The loader with the library table and the preludes filled in.</summary>
    public static string Loader(ArtifactLibraryCatalog catalog)
    {
        var preludes = new StringBuilder("{");
        foreach (var library in catalog.Libraries.Where(l => l.Prelude is { Length: > 0 }))
            preludes.Append(JsonSerializer.Serialize(library.Id)).Append(": () => { ").Append(library.Prelude).Append(" }, ");
        preludes.Append('}');
        return Read("loader.js")
            .Replace("/*NTH_TABLE*/{ \"specifiers\": {}, \"libraries\": {} }", catalog.RunnerTableJson(), StringComparison.Ordinal)
            .Replace("/*NTH_PRELUDES*/{}", preludes.ToString(), StringComparison.Ordinal);
    }

    /// <summary>The marker for a CDN file nth has no copy of: it names the file in the page's console.</summary>
    public static string Missing(string url) =>
        Read("missing.js").Replace("/*NTH_URL*/\"\"", JsonSerializer.Serialize(url), StringComparison.Ordinal);
}
