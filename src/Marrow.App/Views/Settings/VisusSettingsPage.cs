using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Port.Web;
using Marrow.Core.Web;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

// PORT-ONLY: web surfaces W10, Settings > Visus (E:/Droppy/work/design/web-surfaces/DESIGN.md 9 "Visus (browser)": Open links from chats in;
// Search engine; Show search suggestions; Open previews automatically; Reload on save; Let models use Visus; Ask before a model uses a new
// site; Site permissions; Downloads folder; Ask where to save each file; Clear browsing data (per profile); Forget agent data; Keep at most N
// live tabs; 5.11, 5.2, 5.4, 5.5, 5.10, 8.1; ED-NEW-web-w10-4). No Swift counterpart. A self-contained section: one Decorator that takes the
// settings and the few things only the app can do (the permission list, the folder picker, the clears) as parts, so the Settings window,
// the in-window Settings and a gallery all build it the same way and it needs nothing of the window's own code.

namespace Marrow.App.Views.Settings;

/// <summary>What Settings › Visus does beyond switching settings; the app binds them (<see cref="VisusSettingsPage.Bound"/>).</summary>
public sealed class VisusSettingsParts
{
    /// <summary>The settings the page edits.</summary>
    public required Func<WebSettings> Settings { get; init; }

    /// <summary>The site answers Visus keeps, per profile (browse and preview).</summary>
    public Func<IReadOnlyList<SitePermission>> Permissions { get; init; } = () => [];

    /// <summary>Removes one kept answer.</summary>
    public Action<SitePermission>? RemovePermission { get; init; }

    /// <summary>Tells the page when the kept answers changed (a prompt's "Always", a Reset on the site note); the result stops it.</summary>
    public Func<Action, IDisposable>? ObservePermissions { get; init; }

    /// <summary>The PC's Downloads folder, shown while no folder is chosen; null when unknown.</summary>
    public Func<string?> DefaultDownloadsFolder { get; init; } = () => null;

    /// <summary>The system folder picker; null when there is none (a gallery).</summary>
    public Func<Task<string?>>? PickFolder { get; init; }

    /// <summary>Clears one profile's cookies, cache, site storage, history, kept answers and zoom; null: not offered here.</summary>
    public Func<WebProfileKind, Task<string>>? ClearBrowsingData { get; init; }

    /// <summary>Forgets what the model's own tabs left, everywhere; null: not offered here.</summary>
    public Func<Task<string>>? ForgetAgentData { get; init; }

    /// <summary>Asks before something that cannot be undone: the question and the verb; true to go on. Tests answer it.</summary>
    public Func<string, string, string, Task<bool>> Confirm { get; init; } = static (_, _, _) => Task.FromResult(true);
}

public sealed class VisusSettingsPage : Decorator
{
    public const bool IsReady = true;

    public const string Title = "Visus";
    /// <summary>The words Settings search finds the page by: its title and what a reader would type for its rows.</summary>
    public static IReadOnlyList<string> Keywords { get; } =
    [
        "visus", "browser", "web", "links", "chat links", "default browser", "search engine", "suggestions", "previews", "reload on save",
        "permissions", "site permissions", "downloads", "downloads folder", "cookies", "browsing data", "privacy", "forget", "live tabs", "tabs",
    ];

    public const string LinksTitle = "Links";
    public const string ChatLinksRow = "Open links from chats in";
    public const string ChatLinksDetail = "Ctrl+click opens the other one. Sign-in pages always open in your default browser.";
    public const string AskFirstTime = "Ask the first time";
    public const string SearchTitle = "Search";
    public const string SearchEngineRow = "Search engine";
    public const string SuggestionsRow = "Show search suggestions";
    public const string SuggestionsDetail = "Off: suggestions come from your history and open tabs only. On: what you type is also sent to your search engine.";
    public const string PreviewsTitle = "Previews";
    public const string OpenPreviewsRow = "Open previews automatically";
    public const string ReloadRow = "Reload on save";
    public const string ModelsTitle = "Models";
    public const string LetModelsRow = "Let models use Visus";
    public const string AskNewSiteRow = "Ask before a model uses a new site";
    public const string AskNewSiteDetail = "A tab you share with a model always asks.";
    public const string PermissionsTitle = "Site permissions";
    public const string PermissionsEmpty = "No site has a saved answer. Choose \"Always on this site\" or \"Block\" in a prompt and it appears here.";
    public const string RemoveLabel = "Remove";
    public const string DownloadsTitle = "Downloads";
    public const string DownloadsFolderRow = "Downloads folder";
    public const string ChangeLabel = "Change";
    public const string ResetLabel = "Reset";
    public const string AskWhereRow = "Ask where to save each file";
    public const string PrivacyTitle = "Privacy";
    public const string ClearRow = "Clear browsing data";
    public const string ClearDetail = "Cookies, cache, site storage, history and saved answers of one profile. Choose which.";
    public const string ForgetRow = "Forget agent data";
    public const string ForgetDetail = "What the pages a model opened left behind, and the sites you always allowed it.";
    public const string ClearLabel = "Clear";
    public const string ForgetLabel = "Forget";
    public const string TabsTitle = "Tabs";
    public const string LiveTabsRow = "Keep at most N live tabs";
    public const string LiveTabsDetail = "Other tabs show a snapshot until you open them.";

    /// <summary>The profiles Clear browsing data lists, with the words the page uses for them.</summary>
    public static IReadOnlyList<(WebProfileKind Profile, string Name)> ClearableProfiles { get; } =
        [(WebProfileKind.Browse, "Visus tabs"), (WebProfileKind.Preview, "Project previews")];

    /// <summary>The app binds the parts (the permission store, the folder picker, the clears); null: the page edits the settings alone.</summary>
    public static Func<VisusSettingsParts?> Bound { get; set; } = static () => null;

    private readonly VisusSettingsParts parts;
    private readonly WebSettings settings;
    private readonly StackPanel permissionRows = new();
    private readonly TextBlock folderText = new() { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 260 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 6, 16, 10), IsVisible = false };
    private IDisposable? observing;

    public VisusSettingsPage()
        : this(Bound() ?? new VisusSettingsParts { Settings = () => WebSettingsHost.Current })
    {
    }

    public VisusSettingsPage(VisusSettingsParts parts)
    {
        this.parts = parts;
        settings = parts.Settings();
        SwiftFont.Footnote.Apply(status);
        status.Foreground = ChromeTokens.SecondaryText.Brush(status);
        AutomationProperties.SetLiveSetting(status, Avalonia.Automation.AutomationLiveSetting.Polite);

        var links = WebSettingsRows.Card(
            WebSettingsRows.Segments(ChatLinksRow,
                [(ChatLinkTarget.Ask, AskFirstTime), (ChatLinkTarget.Visus, "Visus"), (ChatLinkTarget.DefaultBrowser, "Default browser")],
                () => settings.ChatLinks, value => settings.ChatLinks = value, ChatLinksDetail));

        var search = WebSettingsRows.Card(
            WebSettingsRows.Menu(SearchEngineRow, [.. SearchEngines.All.Select(engine => (engine.ToString(), SearchEngines.Name(engine)))],
                () => settings.SearchEngine.ToString(), id => { if (Enum.TryParse<SearchEngine>(id, out var engine)) settings.SearchEngine = engine; }),
            WebSettingsRows.Toggle(SuggestionsRow, () => settings.SearchSuggestions, on => settings.SearchSuggestions = on, SuggestionsDetail));

        var previews = WebSettingsRows.Card(
            WebSettingsRows.Toggle(OpenPreviewsRow, () => settings.OpenPreviews, on => settings.OpenPreviews = on),
            WebSettingsRows.Toggle(ReloadRow, () => settings.ReloadOnSave, on => settings.ReloadOnSave = on));

        var models = WebSettingsRows.Card(
            WebSettingsRows.Toggle(LetModelsRow, () => settings.LetModelsUseVisus, on => settings.LetModelsUseVisus = on),
            WebSettingsRows.Toggle(AskNewSiteRow, () => settings.AskBeforeNewSite, on => settings.AskBeforeNewSite = on, AskNewSiteDetail));

        var permissions = new ChromeCard(() => permissionRows);

        var change = WebSettingsRows.Capsule(ChangeLabel, parts.PickFolder is null ? null : () => _ = ChooseFolder(), ChangeLabel + " downloads folder");
        var reset = WebSettingsRows.Capsule(ResetLabel, () => settings.DownloadsFolder = "", ResetLabel + " downloads folder");
        folderText.Foreground = ChromeTokens.SecondaryText.Brush(folderText);
        SwiftFont.Subheadline.Apply(folderText);
        AutomationProperties.SetName(folderText, DownloadsFolderRow);
        folderText.Track(() => settings.DownloadsFolder, _ => ShowFolder());
        var downloads = WebSettingsRows.Card(
            new ChromeRow(DownloadsFolderRow, () => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { folderText, change, reset } }),
            WebSettingsRows.Toggle(AskWhereRow, () => settings.AskWhereToSave, on => settings.AskWhereToSave = on));

        var clearButtons = new List<CzCapsuleButton>();
        foreach (var (profile, name) in ClearableProfiles)
            clearButtons.Add(WebSettingsRows.Capsule(name, parts.ClearBrowsingData is null ? null : () => _ = Clear(profile, name), ClearRow + " for " + name));
        var forget = WebSettingsRows.Capsule(ForgetLabel, parts.ForgetAgentData is null ? null : () => _ = Forget(), ForgetRow);
        var privacy = WebSettingsRows.Card(
            WebSettingsRows.Buttons(ClearRow, ClearDetail, [.. clearButtons]),
            new ChromeRow(ForgetRow, () => forget, ForgetDetail));

        var tabs = WebSettingsRows.Card(
            WebSettingsRows.Menu(LiveTabsRow, [.. Enumerable.Range(WebSettings.MinLiveTabs, WebSettings.MaxLiveTabsLimit - WebSettings.MinLiveTabs + 1).Select(n => (n.ToString(CultureInfo.InvariantCulture), n.ToString(CultureInfo.InvariantCulture)))],
                () => settings.MaxLiveTabs.ToString(CultureInfo.InvariantCulture), id => { if (int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) settings.MaxLiveTabs = n; }, LiveTabsDetail));

        Child = new StackPanel
        {
            Spacing = ChromeTokens.SectionSpacing,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children =
            {
                WebSettingsRows.Section(LinksTitle, links),
                WebSettingsRows.Section(SearchTitle, search),
                WebSettingsRows.Section(PreviewsTitle, previews),
                WebSettingsRows.Section(ModelsTitle, models),
                WebSettingsRows.Section(PermissionsTitle, permissions),
                WebSettingsRows.Section(DownloadsTitle, downloads),
                WebSettingsRows.Section(PrivacyTitle, new StackPanel { Children = { privacy, status } }),
                WebSettingsRows.Section(TabsTitle, tabs),
            },
        };
        ShowFolder();
        RebuildPermissions();
        AttachedToVisualTree += (_, _) =>
        {
            observing ??= parts.ObservePermissions?.Invoke(() => Avalonia.Threading.Dispatcher.UIThread.Post(RebuildPermissions));
            RebuildPermissions();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            observing?.Dispose();
            observing = null;
        };
    }

    /// <summary>The words under Clear and Forget after one ran ("" before).</summary>
    public string StatusText => status.IsVisible ? status.Text ?? "" : "";

    /// <summary>The permission rows as listed: one per kept answer.</summary>
    public IReadOnlyList<SitePermission> ListedPermissions { get; private set; } = [];

    /// <summary>The folder text as shown.</summary>
    public string FolderText => folderText.Text ?? "";

    private void ShowFolder()
    {
        var chosen = settings.DownloadsFolder;
        folderText.Text = chosen.Length > 0 ? chosen : parts.DefaultDownloadsFolder() ?? "Downloads";
    }

    private async Task ChooseFolder()
    {
        if (parts.PickFolder is not { } pick || await pick() is not { Length: > 0 } folder) return;
        settings.DownloadsFolder = folder;
    }

    private void RebuildPermissions()
    {
        permissionRows.Children.Clear();
        var all = parts.Permissions();
        ListedPermissions = all;
        if (all.Count == 0)
        {
            permissionRows.Children.Add(WebSettingsRows.Note(PermissionsEmpty));
            return;
        }
        var first = true;
        foreach (var group in ClearableProfiles)
        {
            var mine = all.Where(entry => entry.Profile == group.Profile).ToList();
            if (mine.Count == 0) continue;
            if (!first) permissionRows.Children.Add(new ChromeRowDivider());
            first = false;
            foreach (var entry in mine)
            {
                var kept = entry;
                var remove = WebSettingsRows.Capsule(RemoveLabel, parts.RemovePermission is null ? null : () => parts.RemovePermission(kept),
                    RemoveLabel + " " + SitePermissions.Noun(kept.Kind) + " answer for " + kept.Origin);
                var row = new ChromeRow(WebPolicy.SourceName(kept.Origin), () => remove, SitePermissions.Noun(kept.Kind) + ": " + SitePermissions.StateWord(kept.State) + " · " + group.Name);
                permissionRows.Children.Add(row);
                if (!ReferenceEquals(entry, mine[^1])) permissionRows.Children.Add(new ChromeRowDivider());
            }
        }
    }

    private async Task Clear(WebProfileKind profile, string name)
    {
        if (parts.ClearBrowsingData is not { } clear) return;
        if (!await parts.Confirm("Clear browsing data for " + name + "?", ClearDetail + " This cannot be undone.", ClearLabel)) return;
        Say(await clear(profile));
    }

    private async Task Forget()
    {
        if (parts.ForgetAgentData is not { } forget) return;
        if (!await parts.Confirm("Forget agent data?", ForgetDetail + " This cannot be undone.", ForgetLabel)) return;
        Say(await forget());
    }

    private void Say(string text)
    {
        status.Text = text;
        status.IsVisible = text.Length > 0;
    }
}
