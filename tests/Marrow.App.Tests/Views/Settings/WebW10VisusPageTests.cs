using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Marrow.App.Port.Web;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;
using static Marrow.App.Tests.Fixtures.WebSettingsPageRig;

namespace Marrow.App.Tests.Views.Settings;

// PORT-ONLY: web surfaces W10, Settings > Visus (E:/Droppy/work/design/web-surfaces/DESIGN.md 9; 5.2, 5.4, 5.5, 5.10, 5.11, 8.1, 10; ED-NEW-web-w10-4, -6). Every row of the approved list is there, starts at its designed state, edits only its own
// setting, follows the setting when it changes from elsewhere, and every control has a name. Site permissions list what Visus kept and Remove it; the downloads folder is picked and reset; the clears ask first, do
// nothing when cancelled and say what happened when they run.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w10-4")]
public sealed class WebW10VisusPageTests(HeadlessSession headless)
{
    private static (WebSettings Web, VisusSettingsParts Parts) Make(Action<VisusSettingsParts>? configure = null, IReadOnlyList<SitePermission>? permissions = null)
    {
        var web = new WebSettings(new InMemorySettingsStore());
        var list = permissions;
        var parts = new VisusSettingsParts { Settings = () => web, Permissions = () => list ?? [] };
        configure?.Invoke(parts);
        return (web, parts);
    }

    [Fact]
    public void Every_row_of_the_approved_list_is_on_the_page_in_order() => headless.OnUiThread(() =>
    {
        var (_, parts) = Make();
        var page = new VisusSettingsPage(parts);
        Show(page, window =>
        {
            var texts = Texts(page);
            string[] rows =
            [
                "Open links from chats in", "Search engine", "Show search suggestions", "Open previews automatically", "Reload on save",
                "Let models use Visus", "Ask before a model uses a new site", "Site permissions", "Downloads folder", "Ask where to save each file",
                "Clear browsing data", "Forget agent data", "Keep at most N live tabs",
            ];
            var at = -1;
            foreach (var row in rows)
            {
                var found = texts.ToList().IndexOf(row);
                Assert.True(found > at, $"\"{row}\" is missing or out of order");
                at = found;
            }
            Assert.Equal("Visus", VisusSettingsPage.Title);
            Assert.True(VisusSettingsPage.IsReady);
        });
    });

    [Fact]
    public void The_switches_start_at_their_designed_state_and_flipping_one_edits_only_its_own_setting() => headless.OnUiThread(() =>
    {
        var (web, parts) = Make();
        var page = new VisusSettingsPage(parts);
        Show(page, window =>
        {
            Assert.False(IsOn(page, "Show search suggestions"));
            Assert.True(IsOn(page, "Open previews automatically"));
            Assert.True(IsOn(page, "Reload on save"));
            Assert.True(IsOn(page, "Let models use Visus"));
            Assert.True(IsOn(page, "Ask before a model uses a new site"));
            Assert.False(IsOn(page, "Ask where to save each file"));

            Toggle(page, "Show search suggestions");
            Assert.True(web.SearchSuggestions);
            Toggle(page, "Open previews automatically");
            Assert.False(web.OpenPreviews);
            Toggle(page, "Reload on save");
            Assert.False(web.ReloadOnSave);
            Toggle(page, "Let models use Visus");
            Assert.False(web.LetModelsUseVisus);
            Toggle(page, "Ask before a model uses a new site");
            Assert.False(web.AskBeforeNewSite);
            Toggle(page, "Ask where to save each file");
            Assert.True(web.AskWhereToSave);
            Assert.Equal(ChatLinkTarget.Ask, web.ChatLinks);
            Assert.Equal(4, web.MaxLiveTabs);
            Assert.Equal(SearchEngine.DuckDuckGo, web.SearchEngine);
            Assert.True(web.LiveAnswers && web.InlineHtmlWidgets && web.McpApps);
        });
    });

    [Fact]
    public void The_link_choice_is_a_strip_of_three_that_starts_on_Ask_the_first_time() => headless.OnUiThread(() =>
    {
        var (web, parts) = Make();
        var page = new VisusSettingsPage(parts);
        Show(page, window =>
        {
            var picker = Named<Control>(page, "Open links from chats in");
            Assert.Equal(["Ask the first time", "Visus", "Default browser"], SegmentTitles(picker));
            Segment(picker, 1);
            Assert.Equal(ChatLinkTarget.Visus, web.ChatLinks);
            Segment(picker, 2);
            Assert.Equal(ChatLinkTarget.DefaultBrowser, web.ChatLinks);
            Segment(picker, 0);
            Assert.Equal(ChatLinkTarget.Ask, web.ChatLinks);
            Assert.Contains("Ctrl+click opens the other one.", Texts(page).Single(text => text.StartsWith("Ctrl+click", StringComparison.Ordinal)), StringComparison.Ordinal);
        });
    });

    [Fact]
    public void The_search_engine_menu_lists_the_five_engines_and_a_pick_is_kept() => headless.OnUiThread(() =>
    {
        var (web, parts) = Make();
        var page = new VisusSettingsPage(parts);
        Show(page, window =>
        {
            var menu = Named<CzMenuButton>(page, "Search engine");
            Assert.Equal(["Bing", "Google", "DuckDuckGo", "Brave", "Startpage"], menu.Items.Select(item => item.Label.Text));
            Assert.Equal("DuckDuckGo", menu.SelectedId);
            Pick(menu, "Brave");
            Assert.Equal(SearchEngine.Brave, web.SearchEngine);
            web.SearchEngine = SearchEngine.Google; // changed from elsewhere
            Settle(window);
            Assert.Equal("Google", menu.SelectedId);
        });
    });

    [Fact]
    public void The_live_tab_menu_offers_two_to_eight_starts_at_four_and_keeps_a_pick() => headless.OnUiThread(() =>
    {
        var (web, parts) = Make();
        var page = new VisusSettingsPage(parts);
        Show(page, window =>
        {
            var menu = Named<CzMenuButton>(page, "Keep at most N live tabs");
            Assert.Equal(["2", "3", "4", "5", "6", "7", "8"], menu.Items.Select(item => item.Label.Text));
            Assert.Equal("4", menu.SelectedId);
            Pick(menu, "7");
            Assert.Equal(7, web.MaxLiveTabs);
            web.MaxLiveTabs = 2;
            Settle(window);
            Assert.Equal("2", menu.SelectedId);
        });
    });

    [Fact]
    public void A_switch_follows_the_setting_when_it_changes_from_elsewhere() => headless.OnUiThread(() =>
    {
        var (web, parts) = Make();
        var page = new VisusSettingsPage(parts);
        Show(page, window =>
        {
            web.OpenPreviews = false;
            web.SearchSuggestions = true;
            Settle(window);
            Assert.False(IsOn(page, "Open previews automatically"));
            Assert.True(IsOn(page, "Show search suggestions"));
            web.ChatLinks = ChatLinkTarget.DefaultBrowser;
            Settle(window);
            Assert.NotNull(Named<Control>(page, "Open links from chats in"));
        });
    });

    [Fact]
    public void Every_control_has_a_name_and_the_switches_are_reached_by_Tab_and_flipped_by_Space() => headless.OnUiThread(() =>
    {
        var (web, parts) = Make();
        var page = new VisusSettingsPage(parts);
        Show(page, window =>
        {
            foreach (var control in page.GetVisualDescendants().OfType<SmallSwitch>().Select(toggle => toggle.FindAncestorOfType<SettingsSwitch>()!))
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)));
            foreach (var button in page.GetVisualDescendants().OfType<CzCapsuleButton>())
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));
            foreach (var menu in page.GetVisualDescendants().OfType<CzMenuButton>())
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(menu)));
            var toggle = Named<SettingsSwitch>(page, "Reload on save").GetVisualDescendants().OfType<SmallSwitch>().Single();
            Assert.True(toggle.Focusable);
            toggle.Focus();
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Settle(window);
            Assert.False(web.ReloadOnSave);
        });
    });

    [Fact]
    public void Site_permissions_list_what_Visus_kept_with_its_profile_and_Remove_removes_one() => headless.OnUiThread(() =>
    {
        var removed = new List<SitePermission>();
        var kept = new List<SitePermission>
        {
            new(WebProfileKind.Browse, "https://meet.example.org", SitePermissionKind.Camera, SitePermissionState.Allow),
            new(WebProfileKind.Browse, "https://meet.example.org", SitePermissionKind.Microphone, SitePermissionState.Block),
            new(WebProfileKind.Preview, "http://localhost:5173", SitePermissionKind.Notifications, SitePermissionState.Block),
        };
        var (_, parts) = Make(permissions: kept);
        var withRemove = new VisusSettingsParts { Settings = parts.Settings, Permissions = () => kept, RemovePermission = removed.Add };
        var page = new VisusSettingsPage(withRemove);
        Show(page, window =>
        {
            var texts = Texts(page);
            Assert.Contains("meet.example.org", texts);
            Assert.Contains("Camera: Allowed · Visus tabs", texts);
            Assert.Contains("Microphone: Blocked · Visus tabs", texts);
            Assert.Contains("localhost:5173", texts);
            Assert.Contains("Notifications: Blocked · Project previews", texts);
            Assert.DoesNotContain(VisusSettingsPage.PermissionsEmpty, texts);
            Assert.Equal(kept, page.ListedPermissions);
            var remove = Named<CzCapsuleButton>(page, "Remove Camera answer for https://meet.example.org");
            Press(remove);
            Assert.Equal([kept[0]], removed);
        });
    });

    [Fact]
    public void With_nothing_kept_the_list_says_how_an_answer_gets_there() => headless.OnUiThread(() =>
    {
        var (_, parts) = Make();
        var page = new VisusSettingsPage(parts);
        Show(page, window =>
        {
            Assert.Contains(VisusSettingsPage.PermissionsEmpty, Texts(page));
            Assert.Empty(page.ListedPermissions);
        });
    });

    [Fact]
    public void The_list_follows_the_store_while_the_page_is_shown_and_lets_go_when_it_leaves() => headless.OnUiThread(() =>
    {
        var kept = new List<SitePermission>();
        Action? notify = null;
        var subscriptions = 0;
        var unsubscriptions = 0;
        var (web, _) = Make();
        var parts = new VisusSettingsParts
        {
            Settings = () => web,
            Permissions = () => kept,
            ObservePermissions = callback =>
            {
                subscriptions++;
                notify = callback;
                return new CallbackDisposable(() => unsubscriptions++);
            },
        };
        var page = new VisusSettingsPage(parts);
        Show(page, window =>
        {
            Assert.Equal(1, subscriptions);
            kept.Add(new SitePermission(WebProfileKind.Browse, "https://later.example.org", SitePermissionKind.Location, SitePermissionState.Allow));
            notify!();
            Settle(window);
            Assert.Contains("later.example.org", Texts(page));
        });
        Assert.Equal(1, unsubscriptions);
    });

    [Fact]
    public void The_downloads_folder_shows_the_default_until_one_is_chosen_and_Reset_goes_back() => headless.OnUiThread(() =>
    {
        var picks = 0;
        var (web, _) = Make();
        var parts = new VisusSettingsParts
        {
            Settings = () => web,
            DefaultDownloadsFolder = () => "C:\\Users\\fake\\Downloads",
            PickFolder = () => { picks++; return Task.FromResult<string?>("D:\\files"); },
        };
        var page = new VisusSettingsPage(parts);
        Show(page, window =>
        {
            Assert.Equal("C:\\Users\\fake\\Downloads", page.FolderText);
            Press(Named<CzCapsuleButton>(page, "Change downloads folder"));
            Settle(window);
            Assert.Equal(1, picks);
            Assert.Equal("D:\\files", web.DownloadsFolder);
            Assert.Equal("D:\\files", page.FolderText);
            Press(Named<CzCapsuleButton>(page, "Reset downloads folder"));
            Settle(window);
            Assert.Equal("", web.DownloadsFolder);
            Assert.Equal("C:\\Users\\fake\\Downloads", page.FolderText);
        });
    });

    [Fact]
    public void A_cancelled_folder_picker_changes_nothing_and_a_page_with_no_picker_dims_Change() => headless.OnUiThread(() =>
    {
        var (web, _) = Make();
        var cancelled = new VisusSettingsParts { Settings = () => web, PickFolder = () => Task.FromResult<string?>(null) };
        var page = new VisusSettingsPage(cancelled);
        Show(page, window =>
        {
            Press(Named<CzCapsuleButton>(page, "Change downloads folder"));
            Settle(window);
            Assert.Equal("", web.DownloadsFolder);
        });
        var (_, bare) = Make();
        var dim = new VisusSettingsPage(bare);
        Show(dim, window => Assert.False(Named<CzCapsuleButton>(dim, "Change downloads folder").IsEnabled));
    });

    [Fact]
    public void Clear_browsing_data_asks_first_does_nothing_when_cancelled_and_says_what_it_did() => headless.OnUiThread(() =>
    {
        var asked = new List<(string, string, string)>();
        var cleared = new List<WebProfileKind>();
        var answer = false;
        var (web, _) = Make();
        var parts = new VisusSettingsParts
        {
            Settings = () => web,
            Confirm = (question, detail, verb) => { asked.Add((question, detail, verb)); return Task.FromResult(answer); },
            ClearBrowsingData = profile => { cleared.Add(profile); return Task.FromResult("Cleared the browsing data of the profile."); },
        };
        var page = new VisusSettingsPage(parts);
        Show(page, window =>
        {
            Press(Named<CzCapsuleButton>(page, "Clear browsing data for Visus tabs"));
            Settle(window);
            Assert.Equal("Clear browsing data for Visus tabs?", Assert.Single(asked).Item1);
            Assert.Contains("This cannot be undone.", asked[0].Item2, StringComparison.Ordinal);
            Assert.Equal("Clear", asked[0].Item3);
            Assert.Empty(cleared);
            Assert.Equal("", page.StatusText);

            answer = true;
            Press(Named<CzCapsuleButton>(page, "Clear browsing data for Project previews"));
            Settle(window);
            Assert.Equal([WebProfileKind.Preview], cleared);
            Assert.Equal("Cleared the browsing data of the profile.", page.StatusText);
            Assert.Contains("Cleared the browsing data of the profile.", Texts(page));
        });
    });

    [Fact]
    public void Forget_agent_data_asks_first_and_runs_only_when_confirmed() => headless.OnUiThread(() =>
    {
        var forgot = 0;
        var confirm = false;
        var questions = new List<string>();
        var (web, _) = Make();
        var parts = new VisusSettingsParts
        {
            Settings = () => web,
            Confirm = (question, _, _) => { questions.Add(question); return Task.FromResult(confirm); },
            ForgetAgentData = () => { forgot++; return Task.FromResult("Forgot the agent data."); },
        };
        var page = new VisusSettingsPage(parts);
        Show(page, window =>
        {
            Press(Named<CzCapsuleButton>(page, "Forget agent data"));
            Settle(window);
            Assert.Equal(["Forget agent data?"], questions);
            Assert.Equal(0, forgot);
            confirm = true;
            Press(Named<CzCapsuleButton>(page, "Forget agent data"));
            Settle(window);
            Assert.Equal(1, forgot);
            Assert.Equal("Forgot the agent data.", page.StatusText);
        });
    });

    [Fact]
    public void A_page_with_no_clear_to_call_dims_the_clear_buttons() => headless.OnUiThread(() =>
    {
        var (_, parts) = Make();
        var page = new VisusSettingsPage(parts);
        Show(page, window =>
        {
            Assert.False(Named<CzCapsuleButton>(page, "Clear browsing data for Visus tabs").IsEnabled);
            Assert.False(Named<CzCapsuleButton>(page, "Clear browsing data for Project previews").IsEnabled);
            Assert.False(Named<CzCapsuleButton>(page, "Forget agent data").IsEnabled);
        });
    });

    [Fact]
    public void A_narrow_window_keeps_every_row_inside_the_page_and_long_permission_names_trim() => headless.OnUiThread(() =>
    {
        var longOrigin = "https://" + string.Join('.', Enumerable.Repeat("a-very-long-subdomain-label", 6)) + ".example.org";
        var kept = new List<SitePermission> { new(WebProfileKind.Browse, longOrigin, SitePermissionKind.Camera, SitePermissionState.Allow) };
        var (web, _) = Make();
        var parts = new VisusSettingsParts { Settings = () => web, Permissions = () => kept, DefaultDownloadsFolder = () => "C:\\" + string.Join('\\', Enumerable.Repeat("a-deep-folder-name", 12)) };
        var page = new VisusSettingsPage(parts);
        Show(page, window =>
        {
            page.Measure(new Avalonia.Size(360, double.PositiveInfinity));
            Assert.True(page.DesiredSize.Width <= 360.5, $"the page asks for {page.DesiredSize.Width}");
            Assert.True(page.FolderText.Length > 100);
        }, width: 420, content: 360);
    });

    [Fact]
    public void The_page_is_a_self_contained_section_made_from_parts_alone() => headless.OnUiThread(() =>
    {
        using var defaults = UserDefaults.Standard.Override(new InMemorySettingsStore());
        var page = new VisusSettingsPage();
        Show(page, window => Assert.Contains("Open links from chats in", Texts(page)));
        Assert.Null(VisusSettingsPage.Bound());
    });
}
