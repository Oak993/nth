using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces security review residual R1 (ED-NEW-web-sec-res-1; DESIGN.md 6.5 "WebRTC: blocked for the
// artifacts profile", 2.3, which this departs from by the lead's decision: one folder and one set of options for all). The
// hardening script cannot catch every way a page reaches a new frame's WebRTC before its scrub (the indexed setters
// select[i] = option and select.options[i] = option, and a script inside the very insertion that adds a frame with a src), so
// the sandboxed views (artifacts, inline widgets, MCP Apps) run in a WebView2 environment of their own: their own user data
// folder and browser arguments that block WebRTC at the engine (no UDP outside a proxy, in both spellings WebView2 runtimes
// read). Visus, previews and agent tabs keep the one shared environment, unchanged. The sandbox environment starts only when the
// first sandboxed view asks for it and is let go with the last one. The engine runs here with fake environments; the real
// runtime is WebView2EngineNativeTests and SandboxWebRtcEngineNativeTests.
[Trait("Decision", "ED-NEW-web-sec-res-1")]
public sealed class WebSandboxEnvironmentTests
{
    private const string SandboxFolder = WebRig.UserData + @"\Sandbox";
    private const string VisusArguments = "--site-per-process --force-webrtc-ip-handling-policy=disable_non_proxied_udp";

    private static readonly WebProfiles Profiles = new(WebRig.UserData, "en-US");

    private static readonly WebSurfaceKind[] Sandboxed = [WebSurfaceKind.Artifact, WebSurfaceKind.InlineWidget, WebSurfaceKind.McpApp];

    private static readonly WebSurfaceKind[] Visus = [WebSurfaceKind.VisusTab, WebSurfaceKind.VisusPrivateTab, WebSurfaceKind.PreviewTab, WebSurfaceKind.AgentTab];

    // The engine with fake environments: what it made (one pointer each, 16 apart) and the pointers it let go.
    private static (WebView2Engine Engine, List<WebCreationOptions> Created, List<nint> Released) Rig()
    {
        var created = new List<WebCreationOptions>();
        var released = new List<nint>();
        var engine = new WebView2Engine(
            creation =>
            {
                created.Add(creation);
                return Task.FromResult<(object, nint)>((new object(), created.Count * 16));
            },
            (_, pointer) => released.Add(pointer));
        return (engine, created, released);
    }

    [Fact]
    public void Sandboxed_views_have_their_own_folder_and_arguments_that_block_webrtc_and_Visus_keeps_its_own()
    {
        foreach (var kind in Sandboxed)
        {
            var creation = Profiles.For(kind);
            Assert.Equal(SandboxFolder, creation.UserDataFolder);
            Assert.Equal("en-US", creation.Language);
            var arguments = (creation.AdditionalBrowserArguments ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // Exactly site isolation and the UDP block in both spellings: --webrtc-ip-handling-policy is the one runtime 154 reads
            // (a command-line preference), --force-webrtc-ip-handling-policy the one older runtimes read.
            Assert.Equal(
                ["--site-per-process", "--force-webrtc-ip-handling-policy=disable_non_proxied_udp", "--webrtc-ip-handling-policy=disable_non_proxied_udp"],
                arguments);
        }

        foreach (var kind in Visus)
        {
            var creation = Profiles.For(kind, @"C:\threads\one");
            Assert.Equal(WebRig.UserData, creation.UserDataFolder);
            Assert.Equal(VisusArguments, creation.AdditionalBrowserArguments);
        }
    }

    [Fact]
    public async Task Visus_shares_one_environment_and_the_sandboxed_views_share_another()
    {
        var made = Rig();
        using var engine = made.Engine;

        var visus = new List<nint>();
        foreach (var kind in Visus) visus.Add(await engine.AcquireEnvironmentAsync(Profiles.For(kind, @"C:\threads\one"), CancellationToken.None));
        var sandboxed = new List<nint>();
        foreach (var kind in Sandboxed) sandboxed.Add(await engine.AcquireEnvironmentAsync(Profiles.For(kind), CancellationToken.None));

        Assert.Single(visus.Distinct());
        Assert.Single(sandboxed.Distinct());
        Assert.NotEqual(0, visus[0]);
        Assert.NotEqual(0, sandboxed[0]);
        Assert.NotEqual(visus[0], sandboxed[0]);
        Assert.Equal(2, made.Created.Count);
        Assert.Equal(WebRig.UserData, made.Created[0].UserDataFolder);
        Assert.Equal(SandboxFolder, made.Created[1].UserDataFolder);
        Assert.Equal(Profiles.For(WebSurfaceKind.Artifact).EnvironmentKey, made.Created[1].EnvironmentKey);
        Assert.Empty(made.Released);
    }

    [Fact]
    public async Task The_sandbox_environment_starts_with_the_first_sandboxed_view_and_goes_with_the_last()
    {
        var made = Rig();
        using var engine = made.Engine;

        // The pre-warm starts Visus's environment only.
        await engine.PrewarmAsync(Profiles.For(WebSurfaceKind.PreviewTab), CancellationToken.None);
        var visus = engine.CreateView(Profiles.For(WebSurfaceKind.VisusTab));
        var main = await engine.AcquireEnvironmentAsync(visus.Creation, CancellationToken.None);
        Assert.Single(made.Created);

        // The first sandboxed view starts the sandbox environment; the second shares it.
        var widget = engine.CreateView(Profiles.For(WebSurfaceKind.InlineWidget));
        var first = await engine.AcquireEnvironmentAsync(widget.Creation, CancellationToken.None);
        var app = engine.CreateView(Profiles.For(WebSurfaceKind.McpApp));
        Assert.Equal(first, await engine.AcquireEnvironmentAsync(app.Creation, CancellationToken.None));
        Assert.Equal(2, made.Created.Count);
        Assert.NotEqual(main, first);

        // One sandboxed view closed: the environment stays for the other. The last one closed: it goes, Visus's stays.
        widget.Dispose();
        Assert.Empty(made.Released);
        app.Dispose();
        Assert.Equal([first], made.Released);

        // A new sandboxed view starts a fresh one.
        var artifact = engine.CreateView(Profiles.For(WebSurfaceKind.Artifact));
        var again = await engine.AcquireEnvironmentAsync(artifact.Creation, CancellationToken.None);
        Assert.Equal(3, made.Created.Count);
        Assert.NotEqual(first, again);
        Assert.Equal(main, await engine.AcquireEnvironmentAsync(Profiles.For(WebSurfaceKind.VisusTab), CancellationToken.None));

        // Visus's last view closing keeps its environment (as before); "Restart Visus" lets both go.
        visus.Dispose();
        Assert.Equal([first], made.Released);
        engine.ResetEnvironment();
        Assert.Equal(new[] { first, again, main }.Order(), made.Released.Order());
        artifact.Dispose();
        Assert.Equal(3, made.Released.Count);
    }

    [Fact]
    public async Task A_sandbox_environment_that_arrives_after_its_last_view_closed_is_let_go_at_once()
    {
        var gate = new TaskCompletionSource<(object, nint)>();
        var released = new List<nint>();
        var calls = 0;
        using var engine = new WebView2Engine(
            _ =>
            {
                calls++;
                return calls == 1 ? gate.Task : Task.FromResult<(object, nint)>((new object(), 64));
            },
            (_, pointer) => released.Add(pointer));

        var widget = engine.CreateView(Profiles.For(WebSurfaceKind.InlineWidget));
        var waiting = engine.AcquireEnvironmentAsync(widget.Creation, CancellationToken.None);
        widget.Dispose();
        gate.SetResult((new object(), 32));

        Assert.Equal(0, await waiting); // no view is left to use it: the platform view would make its own
        Assert.Equal([(nint)32], released);
        var next = engine.CreateView(Profiles.For(WebSurfaceKind.Artifact));
        Assert.Equal(64, await engine.AcquireEnvironmentAsync(next.Creation, CancellationToken.None));
        next.Dispose();
    }
}
