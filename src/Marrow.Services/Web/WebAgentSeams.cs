// PORT-ONLY: web surfaces W11, what the web tools drive and whom they ask (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.10
// "Model access (in the same safety model as Tactus)"; ED-NEW-web-w11-1). No Swift counterpart. The engine (WebAgentEngine)
// reaches Visus only through these seams: the tabs a chat's model may use (its own agent tabs and the tabs the user shared;
// never a private tab), the user's answers (the per-site card, the cross-origin card, the guarded-action card and the
// per-project script card, each drawn by the app under the tab's trust strip), the standing grants, and the settings. The app
// binds Visus; tests bind fakes over FakeWebEngine.
using System.Text.Json;
using Marrow.Core.Web;

namespace Marrow.Services.Web;

/// <summary>One Visus tab the model may use. Every member runs on the UI thread.</summary>
public interface IWebAgentTab
{
    string Id { get; }

    /// <summary>Agent (the model opened it), or Browse and Preview once the user shared it. Never Private.</summary>
    VisusTabKind Kind { get; }

    /// <summary>The user shared this tab with the model ("Let the model use this tab", DESIGN 2.3).</summary>
    bool Shared { get; }

    string Url { get; }

    string Title { get; }

    /// <summary>The tab's live web view, or null while it has none (a new tab, a discarded one).</summary>
    IWebEngineView? View { get; }

    /// <summary>The ordered CDP channel of <see cref="View"/>.</summary>
    CdpBroker? Cdp { get; }

    bool IsClosed { get; }

    /// <summary>The page is loading (between a navigation's start and its end).</summary>
    bool Loading { get; }

    /// <summary>Makes the tab's view live (restores a discarded one) and waits until its platform view attached.</summary>
    Task<bool> MakeLiveAsync(CancellationToken cancellationToken);

    void Navigate(string url);

    void GoBack();

    void GoForward();

    void Reload();

    /// <summary>A model step runs on the tab: it is never suspended or discarded meanwhile (DESIGN 2.3).</summary>
    IDisposable BeginModelStep();

    /// <summary>The user clicked or typed inside the tab's page (DESIGN 5.10 "User takes over").</summary>
    event Action? UserInput;
}

/// <summary>Visus for the web tools: the tabs of a chat, opening agent tabs, and the UI thread.</summary>
public interface IWebAgentHost
{
    /// <summary>The tabs the chat's model may use now: its agent tabs and the tabs the user shared, never a private one.</summary>
    IReadOnlyList<IWebAgentTab> Tabs(string thread);

    /// <summary>Opens <paramref name="url"/> in a new agent tab of the chat (the Visus pane opens if it is closed).</summary>
    Task<IWebAgentTab?> OpenAgentTab(string thread, string url, CancellationToken cancellationToken);

    /// <summary>Runs <paramref name="work"/> on the UI thread (CoreWebView2 lives there) and returns its result.</summary>
    Task<T> OnUi<T>(Func<Task<T>> work);

    /// <summary>The user's search engine's results page for a query (Settings › Visus › Search engine).</summary>
    string SearchUrl(string query) => SearchEngines.QueryUrl(SearchEngines.Default, query);

    /// <summary>Whether an address appears in the chat's own conversation (the user's messages and the model's answers).</summary>
    bool InConversation(string thread, string url) => false;

    /// <summary>The model's name as the cards show it ("Claude Sonnet"), from the session's model id.</summary>
    string ModelName(string? model) => string.IsNullOrWhiteSpace(model) ? "The model" : model;

    /// <summary>The chat's title for the guarded-action card's "who" row; null when it has none.</summary>
    string? ChatTitle(string thread) => null;
}

/// <summary>The per-site card's answers (DESIGN 5.10 "Allow once / Always allow / Don't allow").</summary>
public enum WebSiteAnswer
{
    Deny,
    AllowOnce,
    AlwaysAllow,
}

/// <summary>The cross-origin card's answers ("Allow once / Always for this site pair / Don't allow").</summary>
public enum WebNavigateAnswer
{
    Deny,
    AllowOnce,
    AlwaysForPair,
}

/// <summary>"Let Claude Sonnet use example.com in Visus?" for one tab. A shared tab is never offered "Always allow".</summary>
public sealed record WebSiteAsk(string Thread, string Tab, string Origin, string Model, bool Shared)
{
    public bool AllowAlways => !Shared;

    /// <summary>The card's title.</summary>
    public string Title => $"Let {Model} use {WebPolicy.SourceName(Origin)} in Visus?";
}

/// <summary>
/// A navigation to <paramref name="Url"/> after the model read other sites (<paramref name="From"/>): the card shows the full
/// address with its query spelled out and highlighted, and says when it carries text from a page the model read.
/// </summary>
public sealed record WebNavigateAsk(string Thread, string? Tab, IReadOnlyList<string> From, string Url, IReadOnlyList<WebQueryField> Query, string Model)
{
    public bool CarriesPageText => Query.Any(q => q.FromPage);

    /// <summary>"This address carries text from the page you were on" (DESIGN 5.10), when it does.</summary>
    public const string CarriesNote = "This address carries text from the page you were on";

    public string Title => $"Let {Model} open {WebPolicy.SourceName(Url)}?";
}

/// <summary>
/// The guarded-action card ("Send this invite?" with the element, the content rows, the chat and the model): Confirm runs the one
/// step, Cancel refuses it. There is no "always".
/// </summary>
public sealed record WebGuardAsk(string Thread, string Tab, string Origin, WebGuardCategory Category, string Word, string Element, string Model)
{
    /// <summary>The content rows: the form's fields as the page shows them (never a password), and the site.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Rows { get; init; } = [];

    /// <summary>The chat's title for the card's "who" row.</summary>
    public string? Chat { get; init; }

    /// <summary>The card's first line: the element's own words with a question mark ("Send invite?").</summary>
    public string Title => Category switch
    {
        WebGuardCategory.Upload => "Choose a file to upload?",
        WebGuardCategory.Download => "Save this file?",
        WebGuardCategory.NewSite when Element.Length == 0 => $"Open {WebPolicy.SourceName(Origin)}?",
        _ => (Element.Length > 0 ? Element : Capitalized(Word)) + "?",
    };

    /// <summary>"The model will press Send invite in Visus for you."</summary>
    public string Detail => Category switch
    {
        WebGuardCategory.Upload => "The model wants to upload a file in Visus. You choose the file; the model never sees where it is.",
        WebGuardCategory.Download => $"The model started a download from {WebPolicy.SourceName(Origin)} in Visus.",
        WebGuardCategory.NewSite when Element.Length == 0 => "The model wants to move your tab to another site.",
        _ => $"The model will press {(Element.Length > 0 ? Element : Word)} in Visus for you.",
    };

    public string Foot => WebAgentPolicy.AskedBecause(Category);

    /// <summary>The confirm button's words: the element's own label ("Send invite"), else Confirm.</summary>
    public string ConfirmLabel => Category switch
    {
        WebGuardCategory.Upload => "Choose a file",
        WebGuardCategory.Download => "Save",
        _ when Element.Length is > 0 and <= 28 => Element,
        _ => "Confirm",
    };

    private static string Capitalized(string word) => word.Length == 0 ? "Confirm" : char.ToUpperInvariant(word[0]) + word[1..];
}

/// <summary>"Let Claude Sonnet run scripts in localhost:5173?" (DESIGN 5.10 web_eval: "after the user allows it per project").</summary>
public sealed record WebEvalAsk(string Thread, string Tab, string Origin, string Model)
{
    public string Title => $"Let {Model} run scripts in {WebPolicy.SourceName(Origin)}?";
}

/// <summary>Asks the user. Every ask is withdrawn when its token is cancelled (a stop, the turn's end, the ask's own lifetime).</summary>
public interface IWebAgentAsker
{
    Task<WebSiteAnswer> AskSite(WebSiteAsk ask, CancellationToken cancellationToken);

    Task<WebNavigateAnswer> AskNavigate(WebNavigateAsk ask, CancellationToken cancellationToken);

    Task<bool> Confirm(WebGuardAsk ask, CancellationToken cancellationToken);

    Task<bool> AskEval(WebEvalAsk ask, CancellationToken cancellationToken);
}

/// <summary>Settings › Visus for the web tools (DESIGN 9).</summary>
public sealed record WebAgentSettings
{
    public static WebAgentSettings Default { get; } = new();

    /// <summary>"Let models use Visus" (on), independent of Tactus's own switch.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>"Ask before a model uses a new site" (on); it cannot be turned off for shared tabs.</summary>
    public bool AskNewSites { get; init; } = true;

    /// <summary>Reduce Motion: the agent pointer jumps instead of gliding, no ring, no rim breathing (DESIGN 10).</summary>
    public bool ReduceMotion { get; init; }
}

/// <summary>
/// The standing grants: "Always allow" sites (agent tabs only), "Always for this site pair" pairs and the projects whose previews
/// may run the model's scripts. Kept in a small JSON file in the web data folder (so "Forget agent data" and the uninstall reach
/// it); in memory when no file is given.
/// </summary>
public sealed class WebAgentGrants
{
    private readonly string? file;
    private readonly Lock gate = new();
    private Saved saved = new();

    public WebAgentGrants(string? file = null)
    {
        this.file = file;
        if (file is not null && File.Exists(file))
        {
            try
            {
                saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(file)) ?? new Saved();
            }
            catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
            {
                saved = new Saved();
            }
        }
    }

    public bool Site(string origin) => Has(saved.Sites, origin);

    public bool Pair(string from, string to) => Has(saved.Pairs, WebAgentUrls.PairKey(from, to));

    public bool Eval(string origin) => Has(saved.Scripts, origin);

    public void GrantSite(string origin) => Add(s => s.Sites, origin);

    public void GrantPair(string from, string to) => Add(s => s.Pairs, WebAgentUrls.PairKey(from, to));

    public void GrantEval(string origin) => Add(s => s.Scripts, origin);

    /// <summary>The standing site grants (Settings lists them with Remove).</summary>
    public IReadOnlyList<string> Sites
    {
        get
        {
            lock (gate) return [.. saved.Sites];
        }
    }

    public void RemoveSite(string origin)
    {
        lock (gate) saved.Sites.RemoveAll(s => string.Equals(s, origin, StringComparison.OrdinalIgnoreCase));
        Write();
    }

    /// <summary>"Forget agent data": every standing grant goes.</summary>
    public void Clear()
    {
        lock (gate) saved = new Saved();
        Write();
    }

    private bool Has(List<string> list, string key)
    {
        lock (gate) return list.Contains(key, StringComparer.OrdinalIgnoreCase);
    }

    private void Add(Func<Saved, List<string>> list, string key)
    {
        lock (gate)
        {
            var items = list(saved);
            if (items.Contains(key, StringComparer.OrdinalIgnoreCase)) return;
            items.Add(key);
        }
        Write();
    }

    private void Write()
    {
        if (file is null) return;
        try
        {
            string json;
            lock (gate) json = JsonSerializer.Serialize(saved);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var temp = file + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, file, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class Saved
    {
        public List<string> Sites { get; set; } = [];
        public List<string> Pairs { get; set; } = [];
        public List<string> Scripts { get; set; } = [];
    }
}
