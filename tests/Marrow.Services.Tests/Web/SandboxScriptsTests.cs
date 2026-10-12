using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces W13 (DESIGN.md 6.5). nth's own sandbox scripts ship inside the assembly: the hardening and
// bridge scripts every sandboxed document runs first (hardening before the bridge), and the runtime files the library
// host serves, with the loader's table and the missing-file marker filled in.
[Trait("Decision", "ED-NEW-web-w13-3")]
public sealed class SandboxScriptsTests
{
    [Fact]
    public void Every_sandbox_script_is_embedded()
    {
        foreach (var name in SandboxScripts.RuntimeFiles.Concat(["hardening.js", "bridge.js", "loader.js", "missing.js"]))
            Assert.False(string.IsNullOrWhiteSpace(SandboxScripts.Read(name)), name);
        Assert.Equal([SandboxScripts.Hardening, SandboxScripts.Bridge], SandboxScripts.DocumentScripts);
        Assert.Throws<InvalidOperationException>(() => SandboxScripts.Read("nothing.js"));
    }

    [Fact]
    public void The_document_scripts_act_only_on_nths_sandboxed_origins()
    {
        const string guard = "/^https:\\/\\/[amw]-[a-z0-9-]+\\.invalid$/.test(location.origin)";
        Assert.Contains(guard, SandboxScripts.Hardening, StringComparison.Ordinal);
        Assert.Contains(guard, SandboxScripts.Bridge, StringComparison.Ordinal);
        foreach (var name in new[] { "RTCPeerConnection", "webkitRTCPeerConnection", "RTCDataChannel", "dns-prefetch|preconnect|prefetch|prerender", "speculationrules", "MutationObserver" })
            Assert.Contains(name, SandboxScripts.Hardening, StringComparison.Ordinal);
        Assert.DoesNotContain("new Function", string.Concat(SandboxScripts.RuntimeFiles.Select(SandboxScripts.Read)), StringComparison.Ordinal);
        Assert.DoesNotContain("eval(", SandboxScripts.Bridge + SandboxScripts.Hardening + SandboxScripts.Read("loader.js"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_loader_gets_the_library_table_and_the_marker_names_the_file()
    {
        var loader = SandboxScripts.Loader(ArtifactRuntimeRig.Libraries.Catalog);
        Assert.DoesNotContain("/*NTH_TABLE*/", loader, StringComparison.Ordinal);
        Assert.DoesNotContain("/*NTH_PRELUDES*/", loader, StringComparison.Ordinal);
        Assert.Contains("\"lucide-react\": () => { globalThis.react = globalThis.React; }", loader, StringComparison.Ordinal);
        Assert.Contains("\"specifiers\":{", loader, StringComparison.Ordinal);

        var marker = SandboxScripts.Missing("https://cdn.example/x.js\";alert(1);//");
        Assert.Contains("\"https://cdn.example/x.js\\u0022;alert(1);//\"", marker, StringComparison.Ordinal);
    }
}
