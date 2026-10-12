using System.Text.Json;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Browser;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.Services.Store;
using Marrow.Services.Web;

namespace Marrow.App.Tests.Views.Browser;

// PORT-ONLY: web surfaces W8a (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.1 tabs, 5.2 address and search, 5.3 navigation,
// 2.3 "Users who never open Visus never pay for a browser process" and the profiles, 3.7 the browser keys, 3.11 persistence,
// 8.1 "pop-ups and new windows open as Visus tabs, never as top-level windows", 8.3 external protocols; ED-NEW-web-w8a-2).
// A thread's tabs over the fake engine: what each command, address and page event does.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w8a-2")]
public sealed class VisusSessionTests(HeadlessSession headless)
{
    [Fact]
    public void A_new_tab_is_blank_has_no_browser_yet_and_asks_for_the_address() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        var asked = 0;
        rig.Session.FocusAddressRequested += () => asked++;

        var tab = rig.Session.NewTab();

        Assert.Same(tab, Assert.Single(rig.Session.Tabs));
        Assert.Same(tab, rig.Session.Selected);
        Assert.True(tab.IsBlank);
        Assert.Equal("New tab", tab.Title);
        Assert.Null(tab.View);
        Assert.False(VisusHost.IsStarted); // no page, no browser process (2.3)
        Assert.Equal(1, asked);
        Assert.Equal(SiteChipKind.None, tab.Chip.Kind);
    });

    [Fact]
    public void An_address_opens_in_the_tab_and_text_searches_with_the_engine() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        var tab = rig.Session.NewTab();

        Assert.Equal(AddressAction.Navigate, rig.Session.Go("example.com")!.Action);
        Assert.Same(tab, rig.Session.Selected);
        Assert.Equal("https://example.com/", tab.Url);
        Assert.NotNull(tab.View);
        Assert.True(VisusHost.IsStarted);
        Assert.True(tab.IsLoading);

        rig.Session.Go("how to cook rice");
        Assert.Equal("https://duckduckgo.com/?q=how%20to%20cook%20rice", tab.Url);
        VisusHost.SearchEngine = () => SearchEngine.Bing;
        rig.Session.Go("rice");
        Assert.Equal("https://www.bing.com/search?q=rice", tab.Url);
        Assert.Single(rig.Session.Tabs);
    });

    [Fact]
    public void A_dev_host_typed_into_a_blank_tab_turns_it_into_a_preview_tab_in_place() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        var first = rig.Page("https://example.com/");
        rig.Session.NewTab();

        rig.Session.Go("localhost:5173");

        Assert.Equal(2, rig.Session.Tabs.Count);
        var preview = rig.Session.Tabs[1];
        Assert.Same(preview, rig.Session.Selected);
        Assert.Equal(VisusTabKind.Preview, preview.Kind);
        Assert.Equal("http://localhost:5173/", preview.Url);
        Assert.Equal(SiteChipKind.Preview, preview.Chip.Kind);
        Assert.Equal(WebSurfaceKind.PreviewTab, preview.View!.Kind);
        Assert.Same(first, rig.Session.Tabs[0]);
    });

    [Fact]
    public void An_address_of_the_other_kind_opens_beside_a_page_and_leaves_it() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        var browse = rig.Page("https://example.com/");

        rig.Session.Go("http://192.168.1.20:8080");
        var preview = rig.Session.Selected!;
        Assert.Equal(VisusTabKind.Preview, preview.Kind);
        Assert.Equal(new[] { browse, preview }, rig.Session.Tabs);
        Assert.Equal("https://example.com/", browse.Url);

        rig.Session.Go("example.org");
        var back = rig.Session.Selected!;
        Assert.Equal(VisusTabKind.Browse, back.Kind);
        Assert.Equal(3, rig.Session.Tabs.Count);
        Assert.Equal(2, rig.Session.Tabs.ToList().IndexOf(back));
    });

    [Fact]
    public void A_private_tab_stays_private_whatever_is_typed() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        var tab = rig.Session.NewTab(VisusTabKind.Private);

        rig.Session.Go("localhost:3000");
        Assert.Same(tab, Assert.Single(rig.Session.Tabs));
        Assert.Equal(VisusTabKind.Private, tab.Kind);
        Assert.Equal(WebSurfaceKind.VisusPrivateTab, tab.View!.Kind);
        Assert.Equal(SiteChipKind.Private, tab.Chip.Kind);
        Assert.Equal("Private tab", rig.Session.NewTab(VisusTabKind.Private).Title);
    });

    [Theory]
    [InlineData("file:///C:/Users/dana/notes.html", VisusAddress.LocalFileRefusal)]
    [InlineData(@"C:\Users\dana\notes.html", VisusAddress.LocalFileRefusal)]
    [InlineData("javascript:alert(1)", VisusAddress.ScriptRefusal)]
    [InlineData("data:text/html,x", VisusAddress.DataRefusal)]
    [InlineData("edge://settings", VisusAddress.InternalRefusal)]
    [InlineData("ms-msdt:/id", "Visus does not open ms-msdt: links.")]
    public void A_refused_address_leaves_the_tab_and_says_why(string typed, string why) => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        var tab = rig.Session.NewTab();
        var notices = new List<(VisusTab? Tab, string Text)>();
        rig.Session.Notice += (t, text) => notices.Add((t, text));

        Assert.Equal(AddressAction.Refused, rig.Session.Go(typed)!.Action);

        Assert.True(tab.IsBlank);
        Assert.Null(tab.View);
        var (who, text) = Assert.Single(notices);
        Assert.Same(tab, who);
        Assert.Equal(why, text);
    });

    [Fact]
    public void Closing_chooses_the_right_neighbour_and_a_closed_tab_comes_back_where_it_was() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        var a = rig.Page("https://a.example/", title: "A");
        var b = rig.Page("https://b.example/", title: "B");
        var c = rig.Page("https://c.example/", title: "C");
        rig.Session.Select(b);

        rig.Session.Close(b);
        Assert.Equal(new[] { a, c }, rig.Session.Tabs);
        Assert.Same(c, rig.Session.Selected);
        Assert.Null(b.View); // its page is closed with it

        rig.Session.Close(c);
        Assert.Same(a, rig.Session.Selected); // no right neighbour: the left one
        Assert.True(rig.Session.CanReopenClosed);

        Assert.True(rig.Session.Perform(VisusCommand.ReopenClosedTab));
        var again = rig.Session.Selected!;
        Assert.Equal("https://c.example/", again.Url);
        Assert.Equal("C", again.Title);
        Assert.Equal(1, rig.Session.Tabs.ToList().IndexOf(again));
        Assert.True(rig.Session.ReopenClosed());
        Assert.Equal(new[] { "https://a.example/", "https://b.example/", "https://c.example/" }, rig.Session.Tabs.Select(t => t.Url));
        Assert.False(rig.Session.ReopenClosed());
    });

    [Fact]
    public void A_closed_private_tab_and_a_blank_tab_are_gone_for_good() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        rig.Page("https://a.example/");
        var secret = rig.Page("https://secret.example/", VisusTabKind.Private);
        var blank = rig.Session.NewTab();

        rig.Session.Close(secret);
        rig.Session.Close(blank);

        Assert.False(rig.Session.CanReopenClosed);
    });

    [Fact]
    public void Closing_the_last_tab_empties_the_pane() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        var emptied = 0;
        rig.Session.Emptied += () => emptied++;
        var only = rig.Session.NewTab();

        rig.Session.Perform(VisusCommand.CloseTab);

        Assert.Empty(rig.Session.Tabs);
        Assert.Null(rig.Session.Selected);
        Assert.Equal(1, emptied);
        Assert.Null(only.View);
    });

    [Fact]
    public void Close_others_duplicate_and_move_keep_the_order_the_user_sees() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        var a = rig.Page("https://a.example/");
        var b = rig.Page("https://b.example/");
        var copy = rig.Session.Duplicate(a)!;
        Assert.Equal(new[] { a, copy, b }, rig.Session.Tabs);
        Assert.Equal(a.Url, copy.Url);
        Assert.Null(rig.Session.Duplicate(rig.Session.NewTab())); // a blank tab has nothing to copy

        rig.Session.Move(b, 0);
        Assert.Same(b, rig.Session.Tabs[0]);
        rig.Session.CloseOthers(copy);
        Assert.Same(copy, Assert.Single(rig.Session.Tabs));
        Assert.Same(copy, rig.Session.Selected);
    });

    [Fact]
    public void The_browser_commands_choose_tabs_and_wrap() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        var tabs = Enumerable.Range(0, 4).Select(i => rig.Page($"https://t{i}.example/")).ToList();
        rig.Session.Select(tabs[3]);

        rig.Session.Perform(VisusCommand.NextTab);
        Assert.Same(tabs[0], rig.Session.Selected);
        rig.Session.Perform(VisusCommand.PreviousTab);
        Assert.Same(tabs[3], rig.Session.Selected);
        rig.Session.Perform(VisusCommand.SelectTab2);
        Assert.Same(tabs[1], rig.Session.Selected);
        rig.Session.Perform(VisusCommand.SelectTab8); // no eighth tab: nothing changes
        Assert.Same(tabs[1], rig.Session.Selected);
        rig.Session.Perform(VisusCommand.SelectLastTab);
        Assert.Same(tabs[3], rig.Session.Selected);
        rig.Session.Perform(VisusCommand.NewPrivateTab);
        Assert.Equal(VisusTabKind.Private, rig.Session.Selected!.Kind);
        Assert.Equal(4, rig.Session.Tabs.ToList().IndexOf(rig.Session.Selected));
    });

    [Fact]
    public void Browser_keys_pressed_in_a_page_run_for_that_page() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        var a = rig.Page("https://a.example/");
        var b = rig.Page("https://b.example/");
        var page = VisusRig.Live(a);

        Assert.True(page.RaiseKey(new KeyChord(0x14, ChordModifiers.Control)).Handled); // Ctrl+T
        Assert.Equal(3, rig.Session.Tabs.Count);
        Assert.True(rig.Session.Selected!.IsBlank);

        Assert.True(page.RaiseKey(new KeyChord(0x11, ChordModifiers.Control)).Handled); // Ctrl+W closes the page's own tab
        Assert.DoesNotContain(a, rig.Session.Tabs);
        Assert.Contains(b, rig.Session.Tabs);

        Assert.False(VisusRig.Live(b).RaiseKey(new KeyChord(0x2E, ChordModifiers.Control)).Handled); // Ctrl+C is the page's
    });

    [Fact]
    public void Reload_back_and_forward_reach_the_engine() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        var tab = rig.Page("https://a.example/");
        var page = VisusRig.Live(tab);
        page.CanGoBack = true;

        tab.Reload();
        tab.Reload(hard: true);
        tab.GoBack();
        Assert.True(tab.IsLoading);
        tab.Stop();
        Assert.False(tab.IsLoading);

        Assert.Contains(page.Log, line => line.Contains("Reload", StringComparison.Ordinal));
        Assert.Contains(page.Log, line => line.Contains("GoBack", StringComparison.Ordinal));
    });

    // ED-NEW-web-nocap-8: a tab frozen while on screen (more tabs on screen than the live budget) comes back when the user
    // uses the toolbar's back, forward or reload, not only when they click into the page.
    [Fact]
    public void Back_on_a_tab_frozen_while_on_screen_restores_it_first() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        var first = rig.Page("https://a.example/");
        var page = VisusRig.Live(first);
        page.CanGoBack = true;
        VisusHost.Surface.Options.MaxLiveViews = 2;
        first.View!.SetShown(true);
        for (var i = 0; i < 2; i++) rig.Page("https://b" + i + ".example/").View!.SetShown(true);
        first.View.SetLiveVisible(false);
        Assert.Equal(WebLiveness.Suspended, first.View.Liveness);
        page.Log.Clear();

        first.GoBack();

        Assert.Equal(WebLiveness.Live, first.View.Liveness);
        Assert.Equal("Resume", page.Log[0]);
        Assert.Contains(page.Log, line => line.Contains("GoBack", StringComparison.Ordinal));
    });

    [Fact]
    public void A_link_to_a_new_window_opens_a_tab_beside_its_page_in_the_kind_the_rules_give() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        var browse = rig.Page("https://a.example/");
        var other = rig.Page("https://z.example/");
        rig.Session.Select(browse);

        var request = VisusRig.Live(browse).RaiseNewWindow("https://b.example/", userInitiated: true);
        Assert.Equal(WebNewWindowDecision.HandledByNth, request.Decision.Result);
        var opened = rig.Session.Selected!;
        Assert.Equal(new[] { browse, opened, other }, rig.Session.Tabs);
        Assert.Equal(VisusTabKind.Browse, opened.Kind);
        Assert.Equal("https://b.example/", opened.Url);

        VisusRig.Live(opened).RaiseNewWindow("http://localhost:5173/", userInitiated: true);
        Assert.Equal(VisusTabKind.Preview, rig.Session.Selected!.Kind);

        var secret = rig.Page("https://secret.example/", VisusTabKind.Private);
        VisusRig.Live(secret).RaiseNewWindow("https://b.example/", userInitiated: true);
        Assert.Equal(VisusTabKind.Private, rig.Session.Selected!.Kind); // a private tab's links stay private
    });

    [Fact]
    public void A_pop_up_without_a_click_is_blocked_and_offered_by_the_chip() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        var tab = rig.Page("https://a.example/");

        var request = VisusRig.Live(tab).RaiseNewWindow("https://ads.example/", userInitiated: false);

        Assert.Equal(WebNewWindowDecision.Block, request.Decision.Result);
        Assert.Single(rig.Session.Tabs);
        Assert.Equal("https://ads.example/", tab.BlockedPopup);
        tab.OpenBlockedPopup();
        Assert.Null(tab.BlockedPopup);
        Assert.Equal("https://ads.example/", rig.Session.Selected!.Url);

        VisusRig.Live(tab).RaiseNewWindow("https://ads.example/2", userInitiated: false);
        tab.DismissBlockedPopup();
        Assert.Null(tab.BlockedPopup);
        Assert.Equal(WebNewWindowDecision.Block, VisusRig.Live(tab).RaiseNewWindow("file:///C:/x", userInitiated: true).Decision.Result);
    });

    [Fact]
    public void A_page_may_only_load_web_pages_and_frames_their_own_kinds() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        var tab = rig.Page("https://a.example/");
        var page = VisusRig.Live(tab);
        var notices = new List<string>();
        rig.Session.Notice += (_, text) => notices.Add(text);

        Assert.True(page.RaiseNavigationStarting("file:///C:/Users/dana/secret.txt").Cancel);
        Assert.True(page.RaiseNavigationStarting("javascript:alert(1)").Cancel);
        Assert.True(page.RaiseNavigationStarting("edge://settings", userInitiated: false).Cancel);
        Assert.False(page.RaiseNavigationStarting("https://b.example/").Cancel);
        Assert.False(page.RaiseNavigationStarting("mailto:dana@example.com").Cancel); // the app prompt decides (8.3)
        Assert.Equal(new[] { VisusAddress.LocalFileRefusal, VisusAddress.ScriptRefusal }, notices); // the redirect said nothing

        Assert.True(page.RaiseNavigationStarting("file:///C:/x", mainFrame: false).Cancel);
        Assert.False(page.RaiseNavigationStarting("data:image/png;base64,AAAA", mainFrame: false).Cancel);
        Assert.False(page.RaiseNavigationStarting("about:srcdoc", mainFrame: false).Cancel);
    });

    [Fact]
    public void Visits_of_browse_and_preview_tabs_are_kept_and_private_ones_never() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        rig.Page("https://a.example/", title: "A page");
        rig.Page("http://localhost:5173/", VisusTabKind.Preview, title: "Orbit");
        rig.Page("https://secret.example/", VisusTabKind.Private, title: "Secret");

        var urls = VisusHost.History.Entries.Select(entry => entry.Url).ToList();
        Assert.Equal(new[] { "http://localhost:5173/", "https://a.example/" }, urls);
        Assert.Equal("A page", VisusHost.History.Entries[1].Title);
    });

    [Fact]
    public void Titles_and_favicons_come_from_the_page() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        var tab = rig.Page("https://a.example/");
        var page = VisusRig.Live(tab);
        Assert.Equal("a.example", tab.Title); // no title yet: the address

        page.RaiseTitle("Example Domain");
        page.FaviconBytes = [1, 2, 3];
        page.RaiseFavicon("https://a.example/favicon.ico");

        Assert.Equal("Example Domain", tab.Title);
        Assert.Equal(new byte[] { 1, 2, 3 }, tab.Favicon);
        Assert.Equal("Example Domain", VisusHost.History.Entries[0].Title);
    });

    [Fact]
    public void The_history_menus_list_the_entries_nearest_first()
    {
        const string json = """
            {"currentIndex":2,"entries":[
              {"id":1,"url":"https://a.example/","title":"A"},
              {"id":2,"url":"file:///C:/x","title":"File"},
              {"id":3,"url":"https://c.example/","title":"C"},
              {"id":4,"url":"https://d.example/","title":"D"},
              {"id":5,"url":"https://e.example/","title":"E"}]}
            """;
        Assert.Equal(new[] { 1 }, VisusTab.ParseHistory(json, back: true).Select(item => item.EntryId)); // file: is never offered
        Assert.Equal(new[] { 4, 5 }, VisusTab.ParseHistory(json, back: false).Select(item => item.EntryId));
        Assert.Empty(VisusTab.ParseHistory("{oops", back: true));
        Assert.Empty(VisusTab.ParseHistory("{}", back: false));
    }

    [Fact]
    public void The_history_menu_reads_and_moves_through_the_devtools_protocol() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        var tab = rig.Page("https://c.example/");
        var page = VisusRig.Live(tab);
        page.CdpHandler = (method, _, _) => Task.FromResult(method == "Page.getNavigationHistory"
            ? """{"currentIndex":1,"entries":[{"id":7,"url":"https://a.example/","title":"A"},{"id":8,"url":"https://c.example/","title":"C"}]}"""
            : "{}");

        var back = tab.HistoryAsync(back: true).GetAwaiter().GetResult();
        tab.GoToHistoryAsync(Assert.Single(back)).GetAwaiter().GetResult();

        Assert.Equal("A", back[0].Title);
        Assert.Contains(page.CdpCalls, call => call.Method == "Page.navigateToHistoryEntry" && call.Params.Contains("\"entryId\":7", StringComparison.Ordinal));
    });

    [Fact]
    public void A_launch_without_a_click_or_to_a_blocked_scheme_is_denied_without_asking() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        var tab = rig.Page("https://a.test/");
        var asked = 0;
        rig.Session.LaunchRequested += _ => { asked++; return true; };
        var page = VisusRig.Live(tab);

        Assert.False(Decided(page.RaiseExternalUri("mailto:dana@example.com", "https://a.test", userInitiated: false)));
        Assert.False(Decided(page.RaiseExternalUri("ms-msdt:/id PCWDiagnostic", "https://a.test", userInitiated: true)));
        Assert.False(Decided(page.RaiseExternalUri("search-ms:query=calc", "https://a.test", userInitiated: true)));
        Assert.Equal(0, asked);
    });

    [Fact]
    public void A_clicked_mailto_link_asks_and_the_answer_can_be_remembered_for_the_site() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        var tab = rig.Page("https://a.test/");
        var launches = new List<VisusExternalLaunch>();
        rig.Session.LaunchRequested += launch => { launches.Add(launch); return true; };
        var page = VisusRig.Live(tab);

        var first = page.RaiseExternalUri("mailto:dana@example.com", "https://a.test", userInitiated: true);
        Assert.False(first.Decision.IsCompleted); // waiting for the prompt
        var launch = Assert.Single(launches);
        Assert.Equal("a.test", launch.Source);
        Assert.Equal("mailto", launch.Scheme);
        Assert.True(launch.CanRemember);
        launch.Answer(open: true, remember: true);
        Assert.True(first.Decision.Result);

        var second = page.RaiseExternalUri("mailto:sam@example.com", "https://a.test", userInitiated: true);
        Assert.True(second.Decision.Result); // remembered: no prompt
        Assert.Single(launches);
        var elsewhere = page.RaiseExternalUri("mailto:sam@example.com", "https://b.test", userInitiated: true);
        Assert.False(elsewhere.Decision.IsCompleted); // another site asks again
        Assert.Equal(2, launches.Count);
    });

    [Fact]
    public void Other_apps_ask_every_time_and_a_cancel_denies() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        var tab = rig.Page("https://a.test/");
        var launches = new List<VisusExternalLaunch>();
        rig.Session.LaunchRequested += launch => { launches.Add(launch); return true; };
        var page = VisusRig.Live(tab);

        var request = page.RaiseExternalUri("zoommtg://zoom.us/join?confno=1", "https://a.test", userInitiated: true);
        var launch = Assert.Single(launches);
        Assert.False(launch.CanRemember);
        launch.Answer(open: true, remember: true);
        Assert.True(request.Decision.Result);
        Assert.Empty(VisusHost.SchemeChoices.All);

        var again = page.RaiseExternalUri("zoommtg://zoom.us/join?confno=2", "https://a.test", userInitiated: true);
        launches[1].Answer(open: false);
        Assert.False(again.Decision.Result);
        launches[1].Answer(open: true); // only the first answer counts
        Assert.False(again.Decision.Result);
    });

    [Fact]
    public void A_private_tab_asks_but_never_remembers_and_an_agent_tab_never_asks() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        var launches = new List<VisusExternalLaunch>();
        rig.Session.LaunchRequested += launch => { launches.Add(launch); return true; };
        var secret = rig.Page("https://a.test/", VisusTabKind.Private);

        var request = VisusRig.Live(secret).RaiseExternalUri("mailto:dana@example.com", "https://a.test", userInitiated: true);
        var launch = Assert.Single(launches);
        Assert.False(launch.CanRemember);
        launch.Answer(open: true, remember: true);
        Assert.True(request.Decision.Result);
        Assert.Empty(VisusHost.SchemeChoices.All);

        var agent = rig.Page("https://a.test/", VisusTabKind.Agent);
        Assert.False(Decided(VisusRig.Live(agent).RaiseExternalUri("mailto:dana@example.com", "https://a.test", userInitiated: true)));
        Assert.Single(launches);
    });

    [Fact]
    public void With_no_pane_to_ask_in_a_launch_is_denied() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        var tab = rig.Page("https://a.test/");
        Assert.False(Decided(VisusRig.Live(tab).RaiseExternalUri("mailto:dana@example.com", "https://a.test", userInitiated: true)));
    });

    [Fact]
    public void A_typed_app_link_asks_as_Visus_and_opens_only_on_yes() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        rig.Session.NewTab();
        var launches = new List<VisusExternalLaunch>();
        rig.Session.LaunchRequested += launch => { launches.Add(launch); return true; };

        Assert.Equal(AddressAction.External, rig.Session.Go("mailto:dana@example.com")!.Action);
        var launch = Assert.Single(launches);
        Assert.Equal("Visus", launch.Source);
        Assert.False(launch.CanRemember);
        Assert.Empty(rig.Opened);
        launch.Answer(open: true);
        Assert.Equal(new[] { "mailto:dana@example.com" }, rig.Opened);

        rig.Session.Go("tel:+15550100");
        launches[1].Answer(open: false);
        Assert.Single(rig.Opened);
    });

    [Fact]
    public void Private_tabs_are_never_in_the_saved_form() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        var a = rig.Page("https://a.example/", title: "A");
        rig.Page("https://secret.example/", VisusTabKind.Private);
        var saved = rig.Session.Snapshot();
        Assert.Equal(new[] { a.Id }, saved.Tabs.Select(tab => tab.Id));
        Assert.Equal(a.Id, saved.SelectedId); // the private tab was chosen; the saved choice falls back
        Assert.Equal("A", saved.Tabs[0].Title);
    });

    [Fact]
    public void Saved_tabs_come_back_as_addresses_and_go_live_on_first_sight() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        using var store = new AppModelHarness();
        var project = AppModelSamples.Project("Visus tabs", store.NewFolder("visus-tabs"));
        var thread = AppModelSamples.Thread(project);
        store.WriteLibrary([project], [thread]);
        AppModelHarness.WriteDocument(thread.Id);
        var app = store.MakeModel();
        store.Main.OnMain(() =>
        {
            var runtime = new ThreadRuntime(thread.Id, app);
            runtime.VisusTabs = new VisusTabsState
            {
                Tabs = new ValueList<VisusTabState>(
                [
                    new("a", VisusTabKind.Browse, "https://a.example/", "A page"),
                    new("b", VisusTabKind.Preview, "http://localhost:5173/", "Orbit", 1.25),
                    new("p", VisusTabKind.Private, "https://secret.example/", "Secret"),
                ]),
                SelectedId = "b",
            };

            var session = new VisusSession(runtime);
            Assert.Equal(new[] { "a", "b" }, session.Tabs.Select(tab => tab.Id));
            Assert.Equal("b", session.Selected!.Id);
            Assert.All(session.Tabs, tab => Assert.Null(tab.View)); // addresses only until they are seen
            Assert.Equal("A page", session.Tabs[0].Title);
            Assert.Equal(1.25, session.Tabs[1].Zoom);

            session.IsVisible = true;
            Assert.NotNull(session.Tabs[1].View); // the chosen tab goes live
            Assert.Null(session.Tabs[0].View);
            Assert.Equal("Orbit", session.Tabs[1].Title); // its saved title stays until the page names itself

            session.NewTab(VisusTabKind.Private, "https://secret.example/");
            session.Select(session.Tabs[0]);
            Assert.NotNull(session.Tabs[0].View);
            Assert.Equal(new[] { "a", "b" }, runtime.VisusTabs.Tabs.Select(tab => tab.Id)); // saved as it goes, never the private tab
            Assert.Equal("a", runtime.VisusTabs.SelectedId);
            session.Dispose();
        });
    });

    [Fact]
    public void The_tabs_are_kept_with_the_thread_and_come_back_after_a_restart() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        using var store = new AppModelHarness();
        var project = AppModelSamples.Project("Visus kept", store.NewFolder("visus-kept"));
        var thread = AppModelSamples.Thread(project);
        store.WriteLibrary([project], [thread]);
        var app = store.MakeModel();
        store.Main.OnMain(() =>
        {
            var runtime = app.Runtime(thread.Id);
            var session = new VisusSession(runtime);
            session.NewTab(VisusTabKind.Browse, "https://a.example/");
            session.NewTab(VisusTabKind.Private, "https://secret.example/");
            session.NewTab(VisusTabKind.Preview, "http://localhost:5173/");
            session.Dispose();
            // The Browser pane's data in the thread's workspace (web W3c), never the private tab.
            var kept = runtime.GetPaneState(PaneKind.Browser);
            Assert.NotNull(kept);
            using (var document = JsonDocument.Parse(kept)) // the library's form escapes slashes, so it is read as JSON
            {
                var urls = document.RootElement.GetProperty("tabs").EnumerateArray().Select(tab => tab.GetProperty("url").GetString()).ToArray();
                Assert.Equal(new[] { "https://a.example/", "http://localhost:5173/" }, urls);
            }
            Assert.DoesNotContain("secret", kept, StringComparison.Ordinal);
        });
        store.Main.Advance(TimeSpan.FromSeconds(10));
        DiskWriter.Shared.WaitForQueuedWrites();

        var restarted = store.MakeModel();
        var back = store.Main.OnMain(() => restarted.Runtime(thread.Id).VisusTabs);
        Assert.Equal(new[] { "https://a.example/", "http://localhost:5173/" }, back.Tabs.Select(tab => tab.Url));
        Assert.Equal(VisusTabKind.Preview, back.Tabs[1].Kind);
        Assert.Equal(back.Tabs[1].Id, back.SelectedId);
    });

    private static bool Decided(WebExternalUriRequest request)
    {
        Assert.True(request.Decision.IsCompleted);
        return request.Decision.Result;
    }
}
