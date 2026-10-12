using Marrow.Core.Models;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces W13 (DESIGN.md 6.5, 13.1 "Artifact runtime: synthetic origin answering, CSP and isolation
// headers, library rewrite ... multi-file projects serve relative paths"). Each artifact is answered by nth on its own
// site with the design's CSP and isolation headers; the library host serves only the vendored files; every other
// address is refused, and the view never leaves its site.
[Trait("Decision", "ED-NEW-web-w13-1")]
public sealed class ArtifactRuntimeTests
{
    private const string Weather = "<!doctype html><html><head><title>Weather</title></head><body><h1>Weather</h1></body></html>";


    [Fact]
    public void Resuming_an_artifact_that_missed_a_version_reloads_the_current_store_version()
    {
        using var rig = new ArtifactRuntimeRig();
        var (mount, engine) = rig.Mount(rig.Create(ArtifactKind.Html, Weather));
        mount.View.SetShown(true);
        for (var i = 0; i < 4; i++) rig.Web.OpenLive(shown: true);
        mount.View.SetLiveVisible(false);
        Assert.Equal(WebLiveness.Suspended, mount.View.Liveness);
        rig.Store.Update(ArtifactRuntimeRig.Chat, "weather", new ArtifactRevision { Content = "<p>new version</p>" }, ArtifactRuntimeRig.Model, null);
        engine.Log.Clear();
        mount.View.Activate();
        Assert.Equal(WebLiveness.Live, mount.View.Liveness);
        Assert.Contains("Navigate " + mount.Address, engine.Log);
        Assert.Contains("new version", ArtifactRuntimeRig.Body(ArtifactRuntimeRig.Get(engine, mount.Address)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Incoming_navigation_or_theme_activates_a_shown_suspended_artifact(bool theme)
    {
        using var rig = new ArtifactRuntimeRig();
        var (mount, engine) = rig.Mount(rig.Create(ArtifactKind.Html, Weather));
        mount.View.SetShown(true);
        for (var i = 0; i < 4; i++) rig.Web.OpenLive(shown: true);
        mount.View.SetLiveVisible(false);
        Assert.Equal(WebLiveness.Suspended, mount.View.Liveness);
        engine.Log.Clear();
        if (theme) rig.Host.RaiseThemeChanged();
        else mount.Show(null);
        Assert.Equal(WebLiveness.Live, mount.View.Liveness);
        Assert.Equal("Resume", engine.Log[0]);
        if (theme) engine.RaiseNavigationCompleted(mount.Address);
        Assert.Contains(engine.Log, l => theme ? l.StartsWith("Post ", StringComparison.Ordinal) : l == "Navigate " + mount.Address);
    }

    [Fact]
    public void A_streaming_morph_activates_the_shown_artifact_before_posting()
    {
        using var rig = new ArtifactRuntimeRig();
        var (mount, engine) = rig.Mount(rig.Create(ArtifactKind.Html, Weather));
        mount.View.SetShown(true);
        for (var i = 0; i < 4; i++) rig.Web.OpenLive(shown: true);
        mount.View.SetLiveVisible(false);
        engine.Log.Clear();
        const string morph = "{\"nth\":1,\"event\":\"morph\",\"html\":\"<p>new</p>\"}";
        mount.View.PostMessage(morph);
        Assert.Equal(WebLiveness.Live, mount.View.Liveness);
        Assert.Equal("Resume", engine.Log[0]);
        Assert.DoesNotContain("Post " + morph, engine.Log);
        engine.RaiseNavigationCompleted(mount.Address);
        Assert.Equal("Post " + morph, engine.Log[^1]);
    }

    [Fact]
    public void A_queued_latest_morph_cannot_overwrite_an_explicit_historical_version()
    {
        using var rig = new ArtifactRuntimeRig();
        var (mount, engine) = rig.Mount(rig.Create(ArtifactKind.Html, "<p>one</p>"));
        rig.Store.Update(ArtifactRuntimeRig.Chat, "weather", new ArtifactRevision { Content = "<p>two</p>" }, ArtifactRuntimeRig.Model, null);
        mount.View.Navigate(mount.Address);
        const string morph = "{\"nth\":1,\"event\":\"morph\",\"html\":\"<p>two</p>\"}";
        mount.View.PostMessage(morph);
        mount.Show(1);
        engine.RaiseNavigationCompleted(mount.Address);
        Assert.DoesNotContain("Post " + morph, engine.Log);
        Assert.Contains("<p>one</p>", ArtifactRuntimeRig.Body(ArtifactRuntimeRig.Get(engine, mount.Address)), StringComparison.Ordinal);
    }

    [Fact]
    public void A_mount_binds_the_view_to_the_artifacts_own_site_with_the_sandbox_scripts_first()
    {
        using var rig = new ArtifactRuntimeRig();
        var (mount, view) = rig.Mount(rig.Create(ArtifactKind.Html, Weather));

        Assert.Equal(SandboxSites.Origin(SandboxSites.ArtifactHost(ArtifactRuntimeRig.Chat, "weather")), mount.Origin);
        Assert.Equal(mount.Origin, mount.View.SandboxOrigin);
        Assert.Equal(mount.Address, view.Url);
        Assert.Same(mount, view.ResourceHandler);
        Assert.Equal([SandboxScripts.Hardening, SandboxScripts.Bridge], view.DocumentScripts);
        Assert.True(view.Log.IndexOf("AddDocumentScript") < view.Log.IndexOf("Navigate " + mount.Address));
    }

    [Fact]
    public void The_page_carries_the_csp_the_isolation_headers_and_the_import_map_first()
    {
        using var rig = new ArtifactRuntimeRig();
        var (mount, view) = rig.Mount(rig.Create(ArtifactKind.Html, Weather));

        var response = ArtifactRuntimeRig.Get(view, mount.Address);
        Assert.Equal(200, response.StatusCode);
        Assert.Equal("text/html; charset=utf-8", response.Header("Content-Type"));
        Assert.Equal(CspBuilder.ArtifactDefault, response.Header("Content-Security-Policy"));
        Assert.Equal("?1", response.Header("Origin-Agent-Cluster"));
        Assert.Equal("same-origin", response.Header("Cross-Origin-Opener-Policy"));
        Assert.Equal("same-origin", response.Header("Cross-Origin-Resource-Policy"));
        Assert.Equal("nosniff", response.Header("X-Content-Type-Options"));
        Assert.Equal("no-referrer", response.Header("Referrer-Policy"));
        Assert.Equal(SandboxHeaders.PermissionsPolicy, response.Header("Permissions-Policy"));
        var body = ArtifactRuntimeRig.Body(response);
        Assert.StartsWith("<!doctype html><html><head><script type=\"importmap\">", body, StringComparison.Ordinal);
        Assert.Contains("\"react\":\"https://lib-nth.invalid/esm/react.js\"", body, StringComparison.Ordinal);
        Assert.Contains("<h1>Weather</h1>", body, StringComparison.Ordinal);
        Assert.Empty(ArtifactRuntimeRig.Get(view, mount.Address, "HEAD").Body);
        Assert.Equal(405, ArtifactRuntimeRig.Get(view, mount.Address, "POST").StatusCode);
    }

    [Fact]
    public void The_library_host_serves_only_the_vendored_files_and_nths_runtime_files()
    {
        using var rig = new ArtifactRuntimeRig();
        var (_, view) = rig.Mount(rig.Create(ArtifactKind.Html, Weather));

        var react = ArtifactRuntimeRig.Get(view, "https://lib-nth.invalid/react/react.production.min.js");
        Assert.Equal(200, react.StatusCode);
        Assert.Equal("cross-origin", react.Header("Cross-Origin-Resource-Policy"));
        Assert.Equal("*", react.Header("Access-Control-Allow-Origin"));
        Assert.Equal("text/javascript; charset=utf-8", react.Header("Content-Type"));
        Assert.Contains("React", ArtifactRuntimeRig.Body(react), StringComparison.Ordinal);

        var loader = ArtifactRuntimeRig.Body(ArtifactRuntimeRig.Get(view, "https://lib-nth.invalid/nth/loader.js"));
        Assert.DoesNotContain("/*NTH_TABLE*/", loader, StringComparison.Ordinal);
        Assert.Contains("\"recharts\":", loader, StringComparison.Ordinal);
        Assert.Equal(200, ArtifactRuntimeRig.Get(view, "https://lib-nth.invalid/nth/react-runner.js").StatusCode);
        Assert.Equal(200, ArtifactRuntimeRig.Get(view, "https://lib-nth.invalid/esm/react.js").StatusCode);

        foreach (var outside in new[] { "manifest.json", "react/../manifest.json", "nth/hardening.js", "nth/bridge.js", "../Marrow.Services.dll", "react/" })
            Assert.Equal(404, ArtifactRuntimeRig.Get(view, "https://lib-nth.invalid/" + outside).StatusCode);
        Assert.Equal(405, ArtifactRuntimeRig.Get(view, "https://lib-nth.invalid/react/react.production.min.js", "POST").StatusCode);
    }

    [Fact]
    public void Every_other_address_is_refused_including_another_artifacts_site()
    {
        using var rig = new ArtifactRuntimeRig();
        var (_, view) = rig.Mount(rig.Create(ArtifactKind.Html, Weather));
        var other = rig.Create(ArtifactKind.Html, "<p>secret</p>", "Notes");
        var sameIdOtherChat = rig.Create(ArtifactKind.Html, "<p>other chat</p>", "Weather", chat: ArtifactRuntimeRig.OtherChat);

        foreach (var url in new[]
        {
            ArtifactRuntime.Address(other.Artifact.Thread, other.Artifact.Id),
            ArtifactRuntime.Address(sameIdOtherChat.Artifact.Thread, sameIdOtherChat.Artifact.Id),
            "https://" + SandboxSites.WidgetHost("chart") + "/",
            "https://" + SandboxSites.AppHost("shipyard", "ui://a") + "/",
            "https://api.open-meteo.com/v1/forecast?lat=1",
            "http://localhost:3000/",
            "wss://socket.example/",
            "https://lib-nth.invalid:444/react/react.production.min.js",
        })
        {
            var response = ArtifactRuntimeRig.Get(view, url);
            Assert.Equal(403, response.StatusCode);
            Assert.Null(response.Header("Access-Control-Allow-Origin"));
        }

        Assert.Null(view.Request("data:image/png;base64,AAAA"));
    }

    [Fact]
    public void A_network_grant_opens_exactly_the_declared_sites_of_that_version()
    {
        using var rig = new ArtifactRuntimeRig();
        var page = "<html><head><meta name=\"nth-connect\" content=\"api.open-meteo.com\"></head><body>w</body></html>";
        var snapshot = rig.Create(ArtifactKind.Html, page);
        var (mount, view) = rig.Mount(snapshot);
        Assert.Equal(["api.open-meteo.com"], rig.Runtime.NetworkRequest(snapshot).HostNames);
        Assert.Equal(403, ArtifactRuntimeRig.Get(view, "https://api.open-meteo.com/v1/forecast").StatusCode);

        rig.Runtime.Data.AllowNetwork(mount.Artifact, ["api.open-meteo.com"]);
        Assert.Null(view.Request("https://api.open-meteo.com/v1/forecast"));
        Assert.Equal(403, ArtifactRuntimeRig.Get(view, "https://tracker.example/x").StatusCode);
        var csp = CspBuilder.Parse(ArtifactRuntimeRig.Get(view, mount.Address).Header("Content-Security-Policy")!);
        Assert.Equal(["https://api.open-meteo.com"], csp["connect-src"]);
        Assert.Contains("Navigate " + mount.Address, view.Log.Skip(view.Log.IndexOf("Navigate " + mount.Address) + 1));

        // A version that changes the list asks again: the old grant no longer applies.
        rig.Store.Update(ArtifactRuntimeRig.Chat, "weather", new ArtifactRevision { Content = page.Replace("api.open-meteo.com", "api.open-meteo.com evil.example", StringComparison.Ordinal) }, ArtifactRuntimeRig.Model, null);
        Assert.Equal(403, ArtifactRuntimeRig.Get(view, "https://api.open-meteo.com/v1/forecast").StatusCode);
        Assert.Equal(CspBuilder.ArtifactDefault, ArtifactRuntimeRig.Get(view, mount.Address).Header("Content-Security-Policy"));
    }

    [Fact]
    public void A_project_serves_its_files_at_their_paths_and_runs_tsx_through_the_runner()
    {
        using var rig = new ArtifactRuntimeRig();
        var snapshot = rig.CreateProject("index.html",
            ("index.html", "<html><head><link rel=\"stylesheet\" href=\"style.css\"></head><body><div id=\"root\"></div><script type=\"module\" src=\"./src/main.tsx\"></script><script src=\"plain.js\"></script></body></html>"),
            ("src/main.tsx", "import { App } from './App';\nimport { createRoot } from 'react-dom/client';\ncreateRoot(document.getElementById('root')!).render(<App />);"),
            ("src/App.tsx", "export function App() { return <h1>Orbit</h1>; }"),
            ("style.css", "h1 { color: teal }"),
            ("plain.js", "console.log('plain');"));
        var (mount, view) = rig.Mount(snapshot);

        var page = ArtifactRuntimeRig.Body(ArtifactRuntimeRig.Get(view, mount.Address));
        Assert.DoesNotContain("src=\"./src/main.tsx\"", page, StringComparison.Ordinal);
        Assert.Contains("<script src=\"plain.js\"></script>", page, StringComparison.Ordinal);
        Assert.Contains("id=\"nth-files\"", page, StringComparison.Ordinal);
        Assert.Contains("\"entries\":[\"src/main.tsx\"]", page, StringComparison.Ordinal);
        Assert.Contains("https://lib-nth.invalid/nth/project-runner.js", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<h1>Orbit</h1>", page, StringComparison.Ordinal); // JSON in the page escapes every "<"

        var css = ArtifactRuntimeRig.Get(view, mount.Origin + "/style.css");
        Assert.Equal("text/css; charset=utf-8", css.Header("Content-Type"));
        Assert.Equal("h1 { color: teal }", ArtifactRuntimeRig.Body(css));
        Assert.Equal("console.log('plain');", ArtifactRuntimeRig.Body(ArtifactRuntimeRig.Get(view, mount.Origin + "/plain.js")));
        Assert.Equal(CspBuilder.ArtifactDefault, css.Header("Content-Security-Policy"));
        Assert.Equal(404, ArtifactRuntimeRig.Get(view, mount.Origin + "/src/missing.tsx").StatusCode);
    }

    [Fact]
    public void Cdn_addresses_become_the_local_copies_and_missing_files_are_reported_once()
    {
        using var rig = new ArtifactRuntimeRig();
        var page = "<html><head><script src=\"https://unpkg.com/react@18/umd/react.production.min.js\"></script>"
            + "<script src=\"https://cdnjs.cloudflare.com/ajax/libs/nothing-here/1.0/nothing.min.js\"></script>"
            + "<script src=\"https://cdnjs.cloudflare.com/ajax/libs/nothing-here/1.0/nothing.min.js\"></script>"
            + "<link rel=\"stylesheet\" href=\"https://cdn.jsdelivr.net/npm/tailwindcss@2/dist/tailwind.min.css\"></head><body></body></html>";
        var (mount, view) = rig.Mount(rig.Create(ArtifactKind.Html, page));
        var missing = new List<string>();
        mount.LibraryMissing += missing.Add;

        var body = ArtifactRuntimeRig.Body(ArtifactRuntimeRig.Get(view, mount.Address));
        Assert.Contains("src=\"https://lib-nth.invalid/react/react.production.min.js\"", body, StringComparison.Ordinal);
        Assert.Contains("<script src=\"https://lib-nth.invalid/tailwindcss/tailwind.browser.js\"></script>", body, StringComparison.Ordinal);
        Assert.Contains("https://lib-nth.invalid/nth/missing.js?u=", body, StringComparison.Ordinal);
        Assert.DoesNotContain("unpkg.com", body, StringComparison.Ordinal);
        Assert.Equal(["https://cdnjs.cloudflare.com/ajax/libs/nothing-here/1.0/nothing.min.js"], missing);

        var marker = ArtifactRuntimeRig.Body(ArtifactRuntimeRig.Get(view, "https://lib-nth.invalid/nth/missing.js?u=" + Uri.EscapeDataString("https://cdnjs.cloudflare.com/ajax/libs/nothing-here/1.0/nothing.min.js")));
        Assert.Contains("\"https://cdnjs.cloudflare.com/ajax/libs/nothing-here/1.0/nothing.min.js\"", marker, StringComparison.Ordinal);
        Assert.Single(missing);
    }

    [Fact]
    public void Each_kind_gets_its_own_page()
    {
        using var rig = new ArtifactRuntimeRig();
        string Page(ArtifactSnapshot snapshot)
        {
            var (mount, view) = rig.Mount(snapshot);
            return ArtifactRuntimeRig.Body(ArtifactRuntimeRig.Get(view, mount.Address));
        }

        var react = Page(rig.Create(ArtifactKind.React, "export default function App() { return <p>Hi</p>; }", "Hello", language: "tsx"));
        Assert.Contains("https://lib-nth.invalid/nth/react-runner.js", react, StringComparison.Ordinal);
        Assert.Contains("\"file\":\"artifact.tsx\"", react, StringComparison.Ordinal);
        Assert.Contains("https://lib-nth.invalid/sucrase/sucrase.min.js", react, StringComparison.Ordinal);

        Assert.Contains("https://lib-nth.invalid/nth/mermaid-runner.js", Page(rig.Create(ArtifactKind.Mermaid, "graph TD; A-->B", "Flow")), StringComparison.Ordinal);
        Assert.Contains("<h1>Notes</h1>", Page(rig.Create(ArtifactKind.Markdown, "# Notes\n\nText.", "Doc")), StringComparison.Ordinal);
        Assert.Contains("class=\"language-python\"", Page(rig.Create(ArtifactKind.Code, "print('<hi>')", "Script", language: "python")), StringComparison.Ordinal);

        var svg = rig.Create(ArtifactKind.Svg, "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>", "Logo");
        var (svgMount, svgView) = rig.Mount(svg);
        Assert.Contains("<img src=\"artifact.svg\"", ArtifactRuntimeRig.Body(ArtifactRuntimeRig.Get(svgView, svgMount.Address)), StringComparison.Ordinal);
        var image = ArtifactRuntimeRig.Get(svgView, svgMount.Origin + "/artifact.svg");
        Assert.Equal("image/svg+xml", image.Header("Content-Type"));
        Assert.Equal(CspBuilder.ArtifactDefault, image.Header("Content-Security-Policy"));
    }

    [Fact]
    public void An_older_version_can_be_shown()
    {
        using var rig = new ArtifactRuntimeRig();
        var (mount, view) = rig.Mount(rig.Create(ArtifactKind.Html, "<p>one</p>"));
        rig.Store.Update(ArtifactRuntimeRig.Chat, "weather", new ArtifactRevision { Content = "<p>two</p>" }, ArtifactRuntimeRig.Model, null);
        Assert.Contains("<p>two</p>", ArtifactRuntimeRig.Body(ArtifactRuntimeRig.Get(view, mount.Address)), StringComparison.Ordinal);

        mount.Show(1);
        Assert.Contains("<p>one</p>", ArtifactRuntimeRig.Body(ArtifactRuntimeRig.Get(view, mount.Address)), StringComparison.Ordinal);
        rig.Store.Remove(ArtifactRuntimeRig.Chat, "weather");
        Assert.Equal(404, ArtifactRuntimeRig.Get(view, mount.Address).StatusCode);
    }
}
