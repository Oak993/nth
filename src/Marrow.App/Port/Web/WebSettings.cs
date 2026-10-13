using Marrow.Core.NthUi;
using Marrow.Core.Port;
using Marrow.Core.Web;

// PORT-ONLY: web surfaces W10 (E:/Droppy/work/design/web-surfaces/DESIGN.md 9 "Settings": Visus and Tabula; 5.11, 5.2, 5.4, 5.10, 7.7;
// ED-NEW-web-w10-2). No Swift counterpart. The reader's choices for Visus, Tabula and the live answers, each under a key of its own (all
// "web..."), read once when the settings are made and written only when the reader changes one: loading an absent, old or damaged
// value writes nothing and falls back to the designed default. Every other unit reads its switch through one of the seams the
// WebSettingsBinding binds; nothing here reaches a browser, a file or the network.

namespace Marrow.App.Port.Web;

public sealed class WebSettings : MainActorModel
{
    /// <summary>The fewest live tabs the reader may keep (DESIGN 9: 2 to 8; the default is 4).</summary>
    public const int MinLiveTabs = 2;

    public const int MaxLiveTabsLimit = 8;

    public const int DefaultLiveTabs = 4;

    private readonly ISettingsStore store;
    private ChatLinkTarget chatLinks;
    private SearchEngine searchEngine;
    private VisualAnswerLevel visualAnswers;
    private ArtifactModelChoice artifactModel;
    private bool searchSuggestions;
    private bool openPreviews;
    private bool reloadOnSave;
    private bool letModelsUseVisus;
    private bool askBeforeNewSite;
    private bool askWhereToSave;
    private bool liveAnswers;
    private bool inlineHtmlWidgets;
    private bool mcpApps;
    private int maxLiveTabs;
    private string downloadsFolder;
    private string mapTileSource;

    public WebSettings(ISettingsStore store)
    {
        this.store = store;
        chatLinks = ChatLinkRules.ParseSetting(store.String(Keys.ChatLinks));
        searchEngine = Enum.TryParse<SearchEngine>(store.String(Keys.SearchEngine), out var engine) && Enum.IsDefined(engine) ? engine : SearchEngines.Default;
        visualAnswers = Enum.TryParse<VisualAnswerLevel>(store.String(Keys.VisualAnswers), out var level) && Enum.IsDefined(level) ? level : VisualAnswerLevel.Balanced;
        artifactModel = Enum.TryParse<ArtifactModelChoice>(store.String(Keys.ArtifactModel), out var choice) && Enum.IsDefined(choice) ? choice : ArtifactModelChoice.AskEachArtifact;
        searchSuggestions = Plist.AsBool(store.Object(Keys.SearchSuggestions)) ?? false;
        openPreviews = Plist.AsBool(store.Object(Keys.OpenPreviews)) ?? true;
        reloadOnSave = Plist.AsBool(store.Object(Keys.ReloadOnSave)) ?? true;
        letModelsUseVisus = Plist.AsBool(store.Object(Keys.LetModelsUseVisus)) ?? true;
        askBeforeNewSite = Plist.AsBool(store.Object(Keys.AskBeforeNewSite)) ?? true;
        askWhereToSave = Plist.AsBool(store.Object(Keys.AskWhereToSave)) ?? false;
        liveAnswers = Plist.AsBool(store.Object(Keys.LiveAnswers)) ?? true;
        inlineHtmlWidgets = Plist.AsBool(store.Object(Keys.InlineHtmlWidgets)) ?? true;
        mcpApps = Plist.AsBool(store.Object(Keys.McpApps)) ?? true;
        maxLiveTabs = ClampTabs(Plist.AsInt(store.Object(Keys.MaxLiveTabs)) ?? DefaultLiveTabs);
        downloadsFolder = CleanFolder(store.String(Keys.DownloadsFolder));
        mapTileSource = MapTileTemplate.TryParse(store.String(Keys.MapTileSource), out var template) ? template.Text : "";
    }

    /// <summary>The keys, all new and all prefixed "web".</summary>
    public static class Keys
    {
        public const string ChatLinks = "webChatLinks";
        public const string SearchEngine = "webSearchEngine";
        public const string VisualAnswers = "webVisualAnswers";
        public const string ArtifactModel = "webArtifactModel";
        public const string SearchSuggestions = "webSearchSuggestions";
        public const string OpenPreviews = "webOpenPreviews";
        public const string ReloadOnSave = "webReloadOnSave";
        public const string LetModelsUseVisus = "webLetModelsUseVisus";
        public const string AskBeforeNewSite = "webAskBeforeNewSite";
        public const string AskWhereToSave = "webAskWhereToSave";
        public const string LiveAnswers = "webLiveAnswers";
        public const string InlineHtmlWidgets = "webInlineHtmlWidgets";
        public const string McpApps = "webMcpApps";
        public const string MaxLiveTabs = "webMaxLiveTabs";
        public const string DownloadsFolder = "webDownloadsFolder";
        public const string MapTileSource = "webMapTileSource";
    }

    /// <summary>Raised after any choice changed and was written (the bindings re-apply what they own).</summary>
    public event Action? Changed;

    /// <summary>Settings › Visus › "Open links from chats in": Ask (the default), Visus or Default browser. Local is not a choice.</summary>
    public ChatLinkTarget ChatLinks
    {
        get => Read(chatLinks);
        set
        {
            if (value is not (ChatLinkTarget.Ask or ChatLinkTarget.Visus or ChatLinkTarget.DefaultBrowser)) return;
            Set(ref chatLinks, value, _ => Write(Keys.ChatLinks, chatLinks.ToString()));
        }
    }

    /// <summary>Settings › Visus › "Search engine" (DuckDuckGo by default).</summary>
    public SearchEngine SearchEngine
    {
        get => Read(searchEngine);
        set
        {
            if (!Enum.IsDefined(value)) return;
            Set(ref searchEngine, value, _ => Write(Keys.SearchEngine, searchEngine.ToString()));
        }
    }

    /// <summary>Settings › Tabula › "Visual answers": Fewer, Balanced (the default) or More.</summary>
    public VisualAnswerLevel VisualAnswers
    {
        get => Read(visualAnswers);
        set
        {
            if (!Enum.IsDefined(value)) return;
            Set(ref visualAnswers, value, _ => Write(Keys.VisualAnswers, visualAnswers.ToString()));
        }
    }

    /// <summary>Settings › Tabula › "Artifacts may use your model": Ask each artifact (the default) or Never.</summary>
    public ArtifactModelChoice ArtifactModel
    {
        get => Read(artifactModel);
        set
        {
            if (!Enum.IsDefined(value)) return;
            Set(ref artifactModel, value, _ => Write(Keys.ArtifactModel, artifactModel.ToString()));
        }
    }

    /// <summary>Settings › Visus › "Show search suggestions" (off): the engine's own suggestions beside the local ones.</summary>
    public bool SearchSuggestions
    {
        get => Read(searchSuggestions);
        set => Set(ref searchSuggestions, value, _ => Write(Keys.SearchSuggestions, searchSuggestions));
    }

    /// <summary>Settings › Visus › "Open previews automatically" (on).</summary>
    public bool OpenPreviews
    {
        get => Read(openPreviews);
        set => Set(ref openPreviews, value, _ => Write(Keys.OpenPreviews, openPreviews));
    }

    /// <summary>Settings › Visus › "Reload on save" (on): the default for a new preview.</summary>
    public bool ReloadOnSave
    {
        get => Read(reloadOnSave);
        set => Set(ref reloadOnSave, value, _ => Write(Keys.ReloadOnSave, reloadOnSave));
    }

    /// <summary>Settings › Visus › "Let models use Visus" (on).</summary>
    public bool LetModelsUseVisus
    {
        get => Read(letModelsUseVisus);
        set => Set(ref letModelsUseVisus, value, _ => Write(Keys.LetModelsUseVisus, letModelsUseVisus));
    }

    /// <summary>Settings › Visus › "Ask before a model uses a new site" (on; a shared tab always asks, whatever this says).</summary>
    public bool AskBeforeNewSite
    {
        get => Read(askBeforeNewSite);
        set => Set(ref askBeforeNewSite, value, _ => Write(Keys.AskBeforeNewSite, askBeforeNewSite));
    }

    /// <summary>Settings › Visus › "Ask where to save each file" (off).</summary>
    public bool AskWhereToSave
    {
        get => Read(askWhereToSave);
        set => Set(ref askWhereToSave, value, _ => Write(Keys.AskWhereToSave, askWhereToSave));
    }

    /// <summary>Settings › Tabula › "Live answers in chat" (on).</summary>
    public bool LiveAnswers
    {
        get => Read(liveAnswers);
        set => Set(ref liveAnswers, value, _ => Write(Keys.LiveAnswers, liveAnswers));
    }

    /// <summary>Settings › Tabula › "Inline HTML widgets" (on).</summary>
    public bool InlineHtmlWidgets
    {
        get => Read(inlineHtmlWidgets);
        set => Set(ref inlineHtmlWidgets, value, _ => Write(Keys.InlineHtmlWidgets, inlineHtmlWidgets));
    }

    /// <summary>Settings › Tabula › "MCP Apps" (on).</summary>
    public bool McpApps
    {
        get => Read(mcpApps);
        set => Set(ref mcpApps, value, _ => Write(Keys.McpApps, mcpApps));
    }

    /// <summary>Settings › Visus › "Keep at most N live tabs": 2 to 8, four by default. A value outside the range is brought into it.</summary>
    public int MaxLiveTabs
    {
        get => Read(maxLiveTabs);
        set => Set(ref maxLiveTabs, ClampTabs(value), _ => Write(Keys.MaxLiveTabs, (long)maxLiveTabs));
    }

    /// <summary>Settings › Visus › "Downloads folder": a full path, or empty for the PC's Downloads folder.</summary>
    public string DownloadsFolder
    {
        get => Read(downloadsFolder);
        set => Set(ref downloadsFolder, CleanFolder(value), _ => Write(Keys.DownloadsFolder, downloadsFolder));
    }

    /// <summary>Settings › Tabula › "Map street tiles source": an https address with {z}, {x} and {y}, or empty for none.</summary>
    public string MapTileSource
    {
        get => Read(mapTileSource);
        set
        {
            var next = MapTileTemplate.TryParse(value, out var template) ? template.Text : "";
            if (value is { Length: > 0 } && next.Length == 0) return;
            Set(ref mapTileSource, next, _ => Write(Keys.MapTileSource, mapTileSource));
        }
    }

    /// <summary>Where a link from a chat goes under the current choice (see <see cref="ChatLinkRules.Destination"/>).</summary>
    public ChatLinkTarget Destination(string link, bool control, bool forceExternal) =>
        ChatLinkRules.Destination(ChatLinks, link, control, forceExternal);

    /// <summary>
    /// The reader answered the first-time question: Visus or the default browser. "Remember" stores the answer as the setting; Ctrl held
    /// for this click sends this one link to the other browser. Returns where this link goes.
    /// </summary>
    public ChatLinkTarget CompleteChoice(bool visus, bool remember, bool control)
    {
        var selected = visus ? ChatLinkTarget.Visus : ChatLinkTarget.DefaultBrowser;
        if (remember) ChatLinks = selected;
        return control ? ChatLinkRules.Other(selected) : selected;
    }

    private static int ClampTabs(long value) => (int)Math.Clamp(value, MinLiveTabs, MaxLiveTabsLimit);

    // A folder is stored only as a full path; anything else (a relative path, a stored number, a blank) reads as the default.
    private static string CleanFolder(string? value) =>
        value is { Length: > 0 and <= 1024 } text && Path.IsPathFullyQualified(text) && !text.Contains('\0', StringComparison.Ordinal) ? text : "";

    private void Write(string key, object value)
    {
        store.Set(key, value);
        Changed?.Invoke();
    }
}
