using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Port.Web;
using Marrow.Core.Localisation;
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
    public static IReadOnlyList<string> Keywords =>
    [
        UiText.Get("settings.visus.keyword_visus"), UiText.Get("settings.visus.keyword_browser"), UiText.Get("settings.visus.keyword_web"), UiText.Get("settings.visus.keyword_links"),
        UiText.Get("settings.visus.keyword_chat_links"), UiText.Get("settings.visus.keyword_default_browser"), UiText.Get("settings.visus.keyword_search_engine"), UiText.Get("settings.visus.keyword_suggestions"),
        UiText.Get("settings.visus.keyword_previews"), UiText.Get("settings.visus.keyword_reload_on_save"), UiText.Get("settings.visus.keyword_permissions"), UiText.Get("settings.visus.keyword_site_permissions"),
        UiText.Get("settings.visus.keyword_downloads"), UiText.Get("settings.visus.keyword_downloads_folder"), UiText.Get("settings.visus.keyword_cookies"), UiText.Get("settings.visus.keyword_browsing_data"),
        UiText.Get("settings.visus.keyword_privacy"), UiText.Get("settings.visus.keyword_forget"), UiText.Get("settings.visus.keyword_live_tabs"), UiText.Get("settings.visus.keyword_tabs"),
    ];

    public static string LinksTitle => UiText.Get("settings.visus.links_title");
    public static string ChatLinksRow => UiText.Get("settings.visus.chat_links_row");
    public static string ChatLinksDetail => UiText.Get("settings.visus.chat_links_detail");
    public static string AskFirstTime => UiText.Get("settings.visus.ask_first_time");
    public static string SearchTitle => UiText.Get("settings.visus.search_title");
    public static string SearchEngineRow => UiText.Get("settings.visus.search_engine_row");
    public static string SuggestionsRow => UiText.Get("settings.visus.suggestions_row");
    public static string SuggestionsDetail => UiText.Get("settings.visus.suggestions_detail");
    public static string PreviewsTitle => UiText.Get("settings.visus.previews_title");
    public static string OpenPreviewsRow => UiText.Get("settings.visus.open_previews_row");
    public static string ReloadRow => UiText.Get("settings.visus.reload_row");
    public static string ModelsTitle => UiText.Get("settings.visus.models_title");
    public static string LetModelsRow => UiText.Get("settings.visus.let_models_row");
    public static string AskNewSiteRow => UiText.Get("settings.visus.ask_new_site_row");
    public static string AskNewSiteDetail => UiText.Get("settings.visus.ask_new_site_detail");
    public static string PermissionsTitle => UiText.Get("settings.visus.permissions_title");
    public static string PermissionsEmpty => UiText.Get("settings.visus.permissions_empty");
    public static string RemoveLabel => UiText.Get("settings.visus.remove_label");
    public static string DownloadsTitle => UiText.Get("settings.visus.downloads_title");
    public static string DownloadsFolderRow => UiText.Get("settings.visus.downloads_folder_row");
    public static string ChangeLabel => UiText.Get("settings.visus.change_label");
    public static string ResetLabel => UiText.Get("settings.visus.reset_label");
    public static string AskWhereRow => UiText.Get("settings.visus.ask_where_row");
    public static string PrivacyTitle => UiText.Get("settings.visus.privacy_title");
    public static string ClearRow => UiText.Get("settings.visus.clear_row");
    public static string ClearDetail => UiText.Get("settings.visus.clear_detail");
    public static string ForgetRow => UiText.Get("settings.visus.forget_row");
    public static string ForgetDetail => UiText.Get("settings.visus.forget_detail");
    public static string ClearLabel => UiText.Get("settings.visus.clear_label");
    public static string ForgetLabel => UiText.Get("settings.visus.forget_label");
    public static string TabsTitle => UiText.Get("settings.visus.tabs_title");
    public static string LiveTabsRow => UiText.Get("settings.visus.live_tabs_row");
    public static string LiveTabsDetail => UiText.Get("settings.visus.live_tabs_detail");

    /// <summary>The profiles Clear browsing data lists, with the words the page uses for them.</summary>
    public static IReadOnlyList<(WebProfileKind Profile, string Name)> ClearableProfiles =>
        [(WebProfileKind.Browse, UiText.Get("settings.visus.profile_browse")), (WebProfileKind.Preview, UiText.Get("settings.visus.profile_preview"))];

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
                [(ChatLinkTarget.Ask, AskFirstTime), (ChatLinkTarget.Visus, "Visus"), (ChatLinkTarget.DefaultBrowser, UiText.Get("settings.visus.default_browser"))],
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

        var change = WebSettingsRows.Capsule(ChangeLabel, parts.PickFolder is null ? null : () => _ = ChooseFolder(), UiText.Get("settings.visus.change_folder_name"));
        var reset = WebSettingsRows.Capsule(ResetLabel, () => settings.DownloadsFolder = "", UiText.Get("settings.visus.reset_folder_name"));
        folderText.Foreground = ChromeTokens.SecondaryText.Brush(folderText);
        SwiftFont.Subheadline.Apply(folderText);
        AutomationProperties.SetName(folderText, DownloadsFolderRow);
        folderText.Track(() => settings.DownloadsFolder, _ => ShowFolder());
        var downloads = WebSettingsRows.Card(
            new ChromeRow(DownloadsFolderRow, () => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { folderText, change, reset } }),
            WebSettingsRows.Toggle(AskWhereRow, () => settings.AskWhereToSave, on => settings.AskWhereToSave = on));

        var clearButtons = new List<CzCapsuleButton>();
        foreach (var (profile, name) in ClearableProfiles)
            clearButtons.Add(WebSettingsRows.Capsule(name, parts.ClearBrowsingData is null ? null : () => _ = Clear(profile, name), UiText.Format("settings.visus.clear_for_name", name)));
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
        folderText.Text = chosen.Length > 0 ? chosen : parts.DefaultDownloadsFolder() ?? UiText.Get("settings.visus.downloads_folder_default");
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
                    UiText.Format("settings.visus.permission_remove_name", SitePermissions.Noun(kept.Kind), kept.Origin));
                var row = new ChromeRow(WebPolicy.SourceName(kept.Origin), () => remove, UiText.Format("settings.visus.permission_detail", SitePermissions.Noun(kept.Kind), SitePermissions.StateWord(kept.State), group.Name));
                permissionRows.Children.Add(row);
                if (!ReferenceEquals(entry, mine[^1])) permissionRows.Children.Add(new ChromeRowDivider());
            }
        }
    }

    private async Task Clear(WebProfileKind profile, string name)
    {
        if (parts.ClearBrowsingData is not { } clear) return;
        if (!await parts.Confirm(UiText.Format("settings.visus.clear_confirm_title", name), UiText.Get("settings.visus.clear_confirm_body"), ClearLabel)) return;
        Say(await clear(profile));
    }

    private async Task Forget()
    {
        if (parts.ForgetAgentData is not { } forget) return;
        if (!await parts.Confirm(UiText.Get("settings.visus.forget_confirm_title"), UiText.Get("settings.visus.forget_confirm_body"), ForgetLabel)) return;
        Say(await forget());
    }

    private void Say(string text)
    {
        status.Text = text;
        status.IsVisible = text.Length > 0;
    }
}
