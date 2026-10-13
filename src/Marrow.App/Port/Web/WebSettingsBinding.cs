using Marrow.App.Views.Browser;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Settings;
using Marrow.App.Port.Web.McpApps;
using Marrow.Core.NthUi;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.Services.MCP;
using Marrow.Services.Web;
using Marrow.UI.NthUi.Map;

// PORT-ONLY: web surfaces W10, the settings meet the units that read them (E:/Droppy/work/design/web-surfaces/DESIGN.md 9; ED-NEW-web-w10-3). No
// Swift counterpart. Every switch the Visus and Tabula pages own is read by the unit it belongs to through a seam that unit left (W8a to W26 each
// named "W10 binds ..."): this class binds them all, in one place, to the settings the app attaches (WebSettingsHost). A seam reads the attached
// settings on every call, so a change takes effect at once and no restart or rebind is needed; before the settings are attached (and in any
// host without them) each seam keeps the default it was made with. The result of Bind puts every seam back (tests).

namespace Marrow.App.Port.Web;

public static class WebSettingsBinding
{
    private static string? appliedTiles;

    /// <summary>The artifact runtime whose model policy follows "Artifacts may use your model"; the app's. Tests hand their own.</summary>
    public static Func<ArtifactRuntime?> Runtime { get; set; } = static () => ArtifactRuntime.Shared;

    /// <summary>
    /// Binds the seams. <paramref name="process"/> is the app (the ambient defaults are set for the process); false is a test (the ambient values are
    /// replaced for the calling flow until the result is disposed).
    /// </summary>
    public static IDisposable Bind(bool process = false)
    {
        var undo = new List<Action>();
        void Swap<T>(Func<T> get, Action<T> set, Func<T, T> make)
        {
            var old = get();
            set(make(old));
            undo.Add(() => set(old));
        }
        static WebSettings? Web() => WebSettingsHost.Active;

        Swap(() => VisusHost.SearchEngine, value => VisusHost.SearchEngine = value, old => () => Web() is { } web ? web.SearchEngine : old());
        Swap(() => VisusHost.SearchSuggestions, value => VisusHost.SearchSuggestions = value, old => () => Web() is { } web ? web.SearchSuggestions : old());
        Swap(() => VisusHost.PageWindowsInDefaultBrowser, value => VisusHost.PageWindowsInDefaultBrowser = value,
            old => () => Web() is { } web ? web.ChatLinks == ChatLinkTarget.DefaultBrowser : old());
        Swap(() => VisusHost.AskWhereToSave, value => VisusHost.AskWhereToSave = value, old => () => Web() is { } web ? web.AskWhereToSave : old());
        // A folder the reader chose is used only while it exists; if it is gone the download says so (no folder), and never falls back to another one.
        Swap(() => VisusHost.DownloadsFolder, value => VisusHost.DownloadsFolder = value,
            old => () => Web() is { DownloadsFolder.Length: > 0 } web ? (Directory.Exists(web.DownloadsFolder) ? web.DownloadsFolder : null) : old());
        Swap(() => ProjectPreviewHost.OpenAutomatically, value => ProjectPreviewHost.OpenAutomatically = value, old => () => Web() is { } web ? web.OpenPreviews : old());
        Swap(() => ProjectPreviewHost.ReloadOnSave, value => ProjectPreviewHost.ReloadOnSave = value, old => () => Web() is { } web ? web.ReloadOnSave : old());
        Swap(() => WebSurfaceOptions.DefaultMaxLiveViews, value => WebSurfaceOptions.DefaultMaxLiveViews = value, _ => () => Web()?.MaxLiveTabs ?? WebSettings.DefaultLiveTabs);
        Swap(() => McpAppCatalog.Enabled, value => McpAppCatalog.Enabled = value, old => () => Web() is { } web ? web.McpApps : old());
        Swap(() => MCPWire.AdvertiseApps, value => MCPWire.AdvertiseApps = value, old => () => Web() is { } web ? web.McpApps : old());

        // The teaching follows the reader's choices (what a session is told when it starts).
        Func<VisualAnswerLevel> level = () => Web()?.VisualAnswers ?? VisualAnswerLevel.Balanced;
        Func<bool> live = () => Web()?.LiveAnswers ?? true;
        SetAmbient(UiTeaching.Visual, level, process, undo);
        SetAmbient(UiTeaching.LiveAnswers, live, process, undo);

        // The live-answer host's two switches, and the model policy of an artifact's own call.
        var host = ArtifactHost.Shared.Current;
        host.Live.BindEnabled(live);
        host.Live.BindHtmlEnabled(() => Web()?.InlineHtmlWidgets ?? true);
        undo.Add(() =>
        {
            host.Live.BindEnabled(static () => true);
            host.Live.BindHtmlEnabled(static () => true);
        });
        if (Runtime() is { } runtime)
        {
            var oldPolicy = runtime.ModelPolicy;
            runtime.ModelPolicy = () => Web() is { } web ? (web.ArtifactModel == ArtifactModelChoice.Never ? ArtifactModelPolicy.Never : ArtifactModelPolicy.Ask) : oldPolicy();
            undo.Add(() => runtime.ModelPolicy = oldPolicy);
        }

        // An nth-html block (or a widget a restored chat holds) draws as the widget only while inline widgets are on; off, it is the card that opens it in Tabula.
        var oldHost = LiveAnswerHtml.Host.Current;
        SetAmbient(LiveAnswerHtml.Host, (view, runtimeOfChat) => Web() is { InlineHtmlWidgets: false } ? null : oldHost(view, runtimeOfChat), process, undo);

        VisusSettingsPage.Bound = () => WebSettingsPages.Visus();
        TabulaSettingsPage.Bound = () => WebSettingsPages.Tabula();
        undo.Add(() =>
        {
            VisusSettingsPage.Bound = static () => null;
            TabulaSettingsPage.Bound = static () => null;
        });
        return new Undo(undo);
    }

    /// <summary>
    /// The app model exists: attaches its settings (every seam reads them from here on) and applies the street tile source the reader added, and
    /// follows its changes. Returns what puts the settings back (tests).
    /// </summary>
    public static IDisposable Attach(WebSettings settings)
    {
        var attachment = WebSettingsHost.Attach(settings);
        appliedTiles = null;
        ApplyTiles(settings);
        void Changed() => ApplyTiles(settings);
        settings.Changed += Changed;
        return new Undo([() => settings.Changed -= Changed, () => attachment.Dispose(), () => { NthUiMapTiles.Use(null); appliedTiles = null; }]);
    }

    /// <summary>Where the street tiles are kept: inside the web data folder, so the uninstall removes them with the rest.</summary>
    public static Func<string?> TileCacheFolder { get; set; } = static () => Path.Combine(Marrow.Services.Web.WebProfiles.DefaultUserDataFolder, "nth", "map-tiles");

    /// <summary>Makes a tile source for an address; tests replace it with a fake transport.</summary>
    public static Func<MapTileTemplate, INthUiMapTiles> MakeTiles { get; set; } = static template => new HttpMapTiles(template, Marrow.Core.Port.MainActor.Post, cacheFolder: TileCacheFolder());

    // Binds the source for the address in the settings, or none; a change of the address replaces the source (the old one stops and keeps nothing).
    private static void ApplyTiles(WebSettings settings)
    {
        var address = settings.MapTileSource;
        if (string.Equals(address, appliedTiles, StringComparison.Ordinal)) return;
        appliedTiles = address;
        var previous = NthUiMapTiles.Source.Current as HttpMapTiles;
        if (address.Length == 0 || !MapTileTemplate.TryParse(address, out var template)) NthUiMapTiles.Use(null);
        else NthUiMapTiles.Use(MakeTiles(template));
        previous?.DeleteCache(); // the source the reader took away keeps nothing on this PC
    }

    private static void SetAmbient<T>(Ambient<T> ambient, T value, bool process, List<Action> undo) where T : class
    {
        if (process)
        {
            ambient.SetProcessDefault(value);
            return;
        }
        var scope = ambient.Override(value);
        undo.Add(scope.Dispose);
    }

    private sealed class Undo(IReadOnlyList<Action> actions) : IDisposable
    {
        private bool done;

        public void Dispose()
        {
            if (done) return;
            done = true;
            for (var i = actions.Count - 1; i >= 0; i--) actions[i]();
        }
    }
}
