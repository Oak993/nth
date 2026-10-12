using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Browser;
using Marrow.App.Views.Panes;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.Services.Web;
using Marrow.UI.Chrome;

namespace Marrow.App.Tests.Views.Browser;

// PORT-ONLY: web surfaces W8b (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.4 downloads, 5.5 permissions, 8.4 every row for
// browse, preview and agent tabs, 10.1 the keyboard path for every prompt; the canvas's #perm2 and #dl2 and their toasts;
// ED-NEW-web-w8b-1, -2, -3, -4). The Visus pane in the workspace over the fake engine: each kind of question a tab waits on, as
// a card hanging from the trust strip, answered by pointer and by keyboard.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w8b-4")]
[Trait("Feature", "W-8b")]
public sealed class VisusAskTests(HeadlessSession headless)
{
    private static readonly KeyChord Escape = new(0x01, ChordModifiers.None);

    private static Func<PaneKind, PaneContent?> Contents(VisusRig visus) => kind => kind == PaneKind.Browser
        ? VisusPane.Create(visus.Session, null)
        : new PaneContent(new Border { Child = new TextBox { Text = PaneKinds.Title(kind) } }, PaneKinds.Title(kind));

    private static (PaneWorkspaceRig Rig, VisusPaneBody Body) Open(VisusRig visus)
    {
        var rig = new PaneWorkspaceRig(width: 1400, contents: Contents(visus), popupLayers: true);
        rig.Workspace.Open(PaneKind.Browser);
        rig.Settle();
        return (rig, Assert.IsType<VisusPaneBody>(rig.Card(PaneKind.Browser).Content.Body));
    }

    private static VisusTab Go(VisusRig visus, PaneWorkspaceRig rig, string address, string? title = null)
    {
        visus.Session.Go(address);
        var tab = visus.Session.Selected!;
        var page = VisusRig.Live(tab);
        if (title is not null) page.RaiseTitle(title);
        page.RaiseNavigationCompleted(tab.Url);
        rig.Layout();
        return tab;
    }

    private static void Press(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Pump(PaneWorkspaceRig rig)
    {
        for (var i = 0; i < 4; i++) Dispatcher.UIThread.RunJobs();
        rig.Layout();
    }

    private static T CardOf<T>(VisusPaneBody body) where T : VisusPromptCard => Assert.IsType<T>(body.Card);

    // ---- permissions (5.5) ----

    [Fact]
    public void A_permission_asks_in_the_canvas_words_and_Allow_this_time_keeps_nothing() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var tab = Go(visus, rig, "http://localhost:5173/", "Scan a receipt");
        var page = VisusRig.Live(tab);

        var request = page.RaisePermission("http://localhost:5173", WebPermissionKind.Camera);
        rig.Layout();
        var card = CardOf<VisusPermissionPrompt>(body);
        Assert.Equal("localhost:5173 wants to use your camera", card.Title);
        Assert.Equal("For \"Scan a receipt\" on this page.", card.Detail);
        Assert.Equal(["Allow this time", "Always on this site", "Block"], card.Buttons.Select(Label));
        Assert.Equal("Allow localhost:5173 to use your camera this time", AutomationProperties.GetName(card.AllowOnceButton));
        Assert.EndsWith("Press F6 to answer.", card.Announcement, StringComparison.Ordinal);
        Assert.Equal(card.Announcement, AutomationProperties.GetName(card));
        Assert.False(request.Decision.IsCompleted);

        Press(card.AllowOnceButton); // within the 400 ms shield: nothing
        Assert.False(request.Decision.IsCompleted);
        visus.Time.Advance(VisusPromptCard.Shield);
        Press(card.AllowOnceButton);
        Assert.Equal(WebPermissionDecision.AllowOnce, request.Decision.Result);
        Assert.Null(body.Card);
        Assert.Equal("Camera allowed this time", body.NoticeText);
        Assert.Empty(VisusHost.Permissions.All);

        page.RaisePermission("http://localhost:5173", WebPermissionKind.Camera);
        rig.Layout();
        Assert.IsType<VisusPermissionPrompt>(body.Card); // this time only: it asks again
    });

    [Fact]
    public void Always_is_kept_answered_at_once_next_time_and_shown_in_the_site_note_with_Reset() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var tab = Go(visus, rig, "http://localhost:5173/");
        Assert.Equal(VisusTabKind.Preview, tab.Kind); // a dev server: the preview profile keeps its own answers (5.5)
        var page = VisusRig.Live(tab);

        var first = page.RaisePermission("http://localhost:5173", WebPermissionKind.Camera);
        rig.Layout();
        visus.Time.Advance(VisusPromptCard.Shield);
        Press(CardOf<VisusPermissionPrompt>(body).AlwaysButton!);
        Assert.Equal(WebPermissionDecision.AllowAlways, first.Decision.Result);
        Assert.Equal("Camera always allowed on localhost:5173", body.NoticeText);
        Assert.Equal(new SitePermission(WebProfileKind.Preview, "http://localhost:5173", SitePermissionKind.Camera, SitePermissionState.Allow), Assert.Single(VisusHost.Permissions.All));
        Assert.Null(VisusHost.Permissions.StateOf(WebProfileKind.Browse, "http://localhost:5173", SitePermissionKind.Camera)); // per profile

        var again = page.RaisePermission("http://localhost:5173", WebPermissionKind.Camera);
        Assert.Equal(WebPermissionDecision.AllowOnce, again.Decision.Result); // nth's store answers; the engine keeps nothing
        Assert.Null(body.Card);

        var trust = body.Toolbar.Trust;
        var kept = Assert.Single(trust.KeptPermissions());
        Assert.Equal("Camera: allowed", VisusTrustStrip.PermissionsLine([kept]));
        trust.ResetPermissions();
        Assert.Empty(VisusHost.Permissions.All);
        page.RaisePermission("http://localhost:5173", WebPermissionKind.Camera);
        rig.Layout();
        Assert.IsType<VisusPermissionPrompt>(body.Card);
    });

    [Fact]
    public void Esc_blocks_and_the_block_is_kept_for_the_site() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var tab = Go(visus, rig, "https://example.com/");
        Assert.Equal(VisusTabKind.Browse, tab.Kind);
        var page = VisusRig.Live(tab);

        var request = page.RaisePermission("https://example.com", WebPermissionKind.Location);
        rig.Layout();
        var card = CardOf<VisusPermissionPrompt>(body);
        var esc = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, PhysicalKey = PhysicalKey.Escape };
        card.BlockButton.RaiseEvent(esc); // Esc gives the safe answer even within the shield
        Assert.True(esc.Handled);
        Assert.Equal(WebPermissionDecision.Deny, request.Decision.Result);
        Assert.Equal("Location blocked on this site", body.NoticeText);
        Assert.Equal(SitePermissionState.Block, VisusHost.Permissions.StateOf(WebProfileKind.Browse, "https://example.com", SitePermissionKind.Location));
        Assert.Equal(WebPermissionDecision.Deny, page.RaisePermission("https://example.com", WebPermissionKind.Location).Decision.Result);
        Assert.Null(body.Card);

        // The page's own Esc while a card is open is the card's too (8.4, 10.1).
        var mic = page.RaisePermission("https://example.com", WebPermissionKind.Microphone);
        rig.Layout();
        Assert.True(page.RaiseKey(Escape).Handled);
        Assert.Equal(WebPermissionDecision.Deny, mic.Decision.Result);
    });

    [Fact]
    public void A_private_tab_offers_no_Always_and_its_Block_holds_for_the_tab_only() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        visus.Session.NewTab(VisusTabKind.Private);
        var tab = Go(visus, rig, "https://a.test/");
        Assert.Equal(VisusTabKind.Private, tab.Kind);
        var page = VisusRig.Live(tab);

        var request = page.RaisePermission("https://a.test", WebPermissionKind.Camera);
        rig.Layout();
        var card = CardOf<VisusPermissionPrompt>(body);
        Assert.Null(card.AlwaysButton);
        Assert.Equal(["Allow this time", "Block"], card.Buttons.Select(Label));
        visus.Time.Advance(VisusPromptCard.Shield);
        Press(card.BlockButton);
        Assert.Equal(WebPermissionDecision.Deny, request.Decision.Result);
        Assert.Equal("Camera blocked in this tab", body.NoticeText);
        Assert.Empty(VisusHost.Permissions.All); // private tabs leave nothing
        Assert.Equal(WebPermissionDecision.Deny, page.RaisePermission("https://a.test", WebPermissionKind.Camera).Decision.Result);

        // Notifications never ask in a private tab.
        Assert.Equal(WebPermissionDecision.Deny, page.RaisePermission("https://a.test", WebPermissionKind.Notifications).Decision.Result);
        Assert.Null(body.Card);

        visus.Session.NewTab(VisusTabKind.Private);
        var fresh = Go(visus, rig, "https://a.test/");
        VisusRig.Live(fresh).RaisePermission("https://a.test", WebPermissionKind.Camera);
        rig.Layout();
        Assert.IsType<VisusPermissionPrompt>(body.Card); // another private tab asks again
    });

    [Fact]
    public void A_closed_tab_or_a_page_that_went_away_answers_safely_and_keeps_nothing() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var tab = Go(visus, rig, "https://a.test/");
        var page = VisusRig.Live(tab);

        var request = page.RaisePermission("https://a.test", WebPermissionKind.Camera);
        rig.Layout();
        Assert.NotNull(body.Card);
        page.RaiseNavigationStarting("https://b.test/");
        Assert.Equal(WebPermissionDecision.Deny, request.Decision.Result);
        Assert.Null(body.Card);
        Assert.Empty(VisusHost.Permissions.All);
        Assert.Null(body.NoticeText);

        var other = page.RaisePermission("https://b.test", WebPermissionKind.Microphone);
        rig.Layout();
        visus.Session.Close(tab);
        Assert.Equal(WebPermissionDecision.Deny, other.Decision.Result);
        Assert.Empty(VisusHost.Permissions.All);
    });

    [Fact]
    public void Agent_tabs_never_ask_and_a_second_request_joins_the_first() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var agent = visus.Session.NewTab(VisusTabKind.Agent, "https://a.test/");
        rig.Layout();
        Assert.Equal(WebPermissionDecision.Deny, VisusRig.Live(agent).RaisePermission("https://a.test", WebPermissionKind.Camera).Decision.Result);
        Assert.Null(body.Card);

        visus.Session.NewTab();
        var tab = Go(visus, rig, "https://c.test/");
        var page = VisusRig.Live(tab);
        var one = page.RaisePermission("https://c.test", WebPermissionKind.Camera);
        var two = page.RaisePermission("https://c.test", WebPermissionKind.Camera);
        rig.Layout();
        Assert.Single(tab.Asks);
        visus.Time.Advance(VisusPromptCard.Shield);
        Press(CardOf<VisusPermissionPrompt>(body).AllowOnceButton);
        Assert.Equal((WebPermissionDecision.AllowOnce, WebPermissionDecision.AllowOnce), (one.Decision.Result, two.Decision.Result));
    });

    // ---- the keyboard path (10.1) ----

    [Fact]
    public void F6_lands_on_the_prompts_text_Tab_reaches_the_buttons_Space_is_shielded_and_F6_again_gives_the_keyboard_back() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var tab = Go(visus, rig, "https://a.test/");
        body.Toolbar.Address.Box.Focus();
        rig.Layout();
        var before = rig.Window.FocusManager?.GetFocusedElement();

        var request = VisusRig.Live(tab).RaisePermission("https://a.test", WebPermissionKind.Microphone);
        rig.Layout();
        var card = CardOf<VisusPermissionPrompt>(body);
        Assert.Same(before, rig.Window.FocusManager?.GetFocusedElement()); // it never takes focus by itself
        visus.Time.Advance(VisusPromptCard.Shield); // the appearance shield is over

        Assert.True(rig.Workspace.CycleFocus(forward: true));
        Assert.True(card.Lead.IsFocused); // the text, not a button
        Assert.True(card.IsShielded); // the shield runs again from the keyboard's entry

        rig.Key(Key.Tab);
        Assert.True(card.AllowOnceButton.IsFocused); // Tab reaches the buttons
        rig.Key(Key.Space);
        Assert.False(request.Decision.IsCompleted); // a Space within 400 ms of focus entering it is ignored

        rig.Key(Key.Enter);
        Assert.False(request.Decision.IsCompleted); // Enter does nothing

        rig.Key(Key.F6);
        Assert.Same(before, rig.Window.FocusManager?.GetFocusedElement()); // F6 again returns focus to where it was

        Assert.True(rig.Workspace.CycleFocus(forward: true));
        visus.Time.Advance(VisusPromptCard.Shield);
        card.BlockButton.Focus(NavigationMethod.Tab);
        rig.Key(Key.Space);
        Assert.Equal(WebPermissionDecision.Deny, request.Decision.Result);
        Assert.Null(body.Card);
        Assert.False(VisusPrompts.AnyIn(rig.Window));
    });

    // ---- downloads (5.4) ----

    [Fact]
    public void A_download_asks_and_Save_puts_it_in_Downloads_under_a_free_name_never_opening_it() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var tab = Go(visus, rig, "http://localhost:5173/");
        var page = VisusRig.Live(tab);
        visus.ExistingFiles.Add(VisusRig.DownloadsFolder + @"\plans-2026-10.csv");

        var request = page.RaiseDownload("http://localhost:5173/export", "plans-2026-10.csv", 12 * 1024, "text/csv", "http://localhost:5173");
        rig.Layout();
        var card = CardOf<VisusDownloadPrompt>(body);
        Assert.Equal("plans-2026-10.csv", card.Title);
        Assert.Equal("12 KB · CSV · from localhost:5173", card.Detail);
        Assert.Null(card.Warning);
        Assert.Equal(["Save", "Save as…", "Cancel"], card.Buttons.Select(Label));
        Assert.Equal("Save plans-2026-10.csv to Downloads", AutomationProperties.GetName(card.SaveButton));
        Assert.False(body.Toolbar.Downloads.IsVisible);

        visus.Time.Advance(VisusPromptCard.Shield);
        Press(card.SaveButton);
        Assert.Equal(new WebDownloadDecision(true, VisusRig.DownloadsFolder + @"\plans-2026-10 (1).csv"), request.Decision.Result);
        Assert.Null(body.Card);
        Pump(rig);
        var transfer = Assert.Single(page.Downloads);
        var download = Assert.Single(VisusHost.Downloads.Items);
        Assert.Equal(("plans-2026-10 (1).csv", "localhost:5173", VisusDownloadState.Running), (download.FileName, download.Source, download.State));
        Assert.True(body.Toolbar.Downloads.IsVisible);

        transfer.Progress(6 * 1024);
        Assert.Equal(0.5, body.Toolbar.DownloadsProgress);
        transfer.Complete();
        Assert.Equal("Saved to Downloads · plans-2026-10 (1).csv", body.NoticeText);
        Assert.DoesNotContain(page.Log, line => line.StartsWith("Open", StringComparison.Ordinal)); // never opened
        Assert.Empty(visus.Opened);

        var row = Assert.IsType<VisusMenuEntry>(body.Toolbar.DownloadEntries()[0]);
        Assert.Equal(("plans-2026-10 (1).csv", "12 KB · from localhost:5173", true), (row.Title, row.Shortcut, row.IsEnabled));
        row.Run();
        Assert.Equal([VisusRig.DownloadsFolder + @"\plans-2026-10 (1).csv"], visus.ShownInFolder); // Show in folder, not open
    });

    [Fact]
    public void A_risky_file_warns_and_Save_needs_a_second_click_within_3_s() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var page = VisusRig.Live(Go(visus, rig, "https://downloads.example.com/"));

        var request = page.RaiseDownload("https://downloads.example.com/orbit-setup.exe", "orbit-setup.exe", 84L * 1024 * 1024, "application/x-msdownload", "https://downloads.example.com");
        rig.Layout();
        var card = CardOf<VisusDownloadPrompt>(body);
        Assert.Equal("84 MB · Application · from downloads.example.com", card.Detail);
        Assert.Equal("This file can run code on your PC.", card.Warning!.Text);
        visus.Time.Advance(VisusPromptCard.Shield);

        Press(card.SaveButton);
        Assert.False(request.Decision.IsCompleted);
        Assert.Equal("Click again to save", card.SaveLabel);
        visus.Time.Advance(TimeSpan.FromSeconds(3));
        Press(card.SaveButton); // too late: it arms again
        Assert.False(request.Decision.IsCompleted);
        visus.Time.Advance(TimeSpan.FromSeconds(1));
        Press(card.SaveButton);
        Assert.True(request.Decision.Result.Save);
    });

    [Fact]
    public void Save_as_uses_the_system_dialog_and_a_closed_dialog_keeps_the_prompt() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var page = VisusRig.Live(Go(visus, rig, "https://a.test/"));
        var request = page.RaiseDownload("https://a.test/f.zip", "f.zip", 2048, "application/zip");
        rig.Layout();
        var card = CardOf<VisusDownloadPrompt>(body);
        visus.Time.Advance(VisusPromptCard.Shield);

        visus.NextSavePath = null;
        Press(card.SaveAsButton);
        Pump(rig);
        Assert.Equal([("f.zip", (string?)VisusRig.DownloadsFolder)], visus.SaveDialogs);
        Assert.False(request.Decision.IsCompleted);
        Assert.Same(card, body.Card);

        visus.NextSavePath = @"D:\elsewhere\f.zip";
        Press(card.SaveAsButton);
        Pump(rig);
        Assert.Equal(new WebDownloadDecision(true, @"D:\elsewhere\f.zip"), request.Decision.Result);
        Assert.Null(body.Card);
    });

    [Fact]
    public void Cancel_and_Esc_cancel_and_a_hidden_tabs_download_waits_for_its_tab() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var first = Go(visus, rig, "https://a.test/");
        var request = VisusRig.Live(first).RaiseDownload("https://a.test/f.zip", "f.zip");
        rig.Layout();
        visus.Time.Advance(VisusPromptCard.Shield);
        Press(CardOf<VisusDownloadPrompt>(body).CancelButton);
        Assert.Equal(WebDownloadDecision.Cancel, request.Decision.Result);
        Assert.Equal("Download cancelled", body.NoticeText);

        visus.Session.NewTab();
        var second = Go(visus, rig, "https://b.test/");
        var waiting = VisusRig.Live(first).RaiseDownload("https://a.test/g.zip", "g.zip");
        rig.Layout();
        Assert.Null(body.Card); // not on screen: it waits in its tab
        Assert.False(waiting.Decision.IsCompleted);
        var view = body.Strip.TabViews.First(item => ReferenceEquals(item.Tab, first));
        Assert.True(view.ShowsWaiting);
        Assert.EndsWith(", waiting for your answer", AutomationProperties.GetName(view), StringComparison.Ordinal);

        visus.Session.Select(first);
        rig.Layout();
        Assert.IsType<VisusDownloadPrompt>(body.Card);
        Assert.False(view.ShowsWaiting);
        visus.Session.Select(second);
        rig.Layout();
        Assert.Null(body.Card);
        Assert.False(waiting.Decision.IsCompleted); // choosing another tab does not answer a download
        visus.Session.Select(first);
        rig.Layout();
        Assert.True(VisusRig.Live(first).RaiseKey(Escape).Handled);
        Assert.Equal(WebDownloadDecision.Cancel, waiting.Decision.Result);
    });

    [Fact]
    public void A_model_started_download_waits_for_the_user_in_the_same_prompt() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var agent = visus.Session.NewTab(VisusTabKind.Agent, "https://a.test/");
        var page = VisusRig.Live(agent);
        page.RaiseNavigationCompleted("https://a.test/");
        rig.Layout();

        var request = page.RaiseDownload("https://a.test/report.csv", "report.csv", 100, "text/csv");
        rig.Layout();
        var card = CardOf<VisusDownloadPrompt>(body);
        Assert.False(request.Decision.IsCompleted);
        Assert.Contains(agent.View!.AgentNotes, note => note.Kind == WebAgentNoteKind.DownloadWaiting);
        visus.Time.Advance(VisusPromptCard.Shield);
        Press(card.SaveButton);
        Assert.True(request.Decision.Result.Save);
    });

    [Fact]
    public void Visus_downloads_join_Background_tasks_with_their_controls_and_private_ones_stay_out() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var page = VisusRig.Live(Go(visus, rig, "https://a.test/"));
        page.RaiseDownload("https://a.test/big.iso", "big.bin", 4096, "application/octet-stream");
        rig.Layout();
        visus.Time.Advance(VisusPromptCard.Shield);
        Press(CardOf<VisusDownloadPrompt>(body).SaveButton);
        Pump(rig);
        var transfer = Assert.Single(page.Downloads);

        var source = new VisusDownloadJobSource(() => VisusHost.Downloads);
        var job = Assert.Single(source.Snapshot(visus.Time.GetUtcNow()));
        Assert.Equal(("big.bin", BackgroundJobKind.Download, BackgroundJobState.Running, true), (job.Title, job.Kind, job.State, job.CanPause));
        Assert.True(source.Pause(job.Id));
        Assert.Equal(["Pause"], transfer.Calls);
        Assert.Equal(BackgroundJobState.Paused, Assert.Single(source.Snapshot(visus.Time.GetUtcNow())).State);
        Assert.True(source.Resume(job.Id));
        Assert.True(source.Stop(job.Id));
        Assert.Equal(VisusDownloadState.Cancelled, VisusHost.Downloads.Items[0].State);
        source.Dismiss(job.Id);
        Assert.Empty(VisusHost.Downloads.Items);

        visus.Session.NewTab(VisusTabKind.Private);
        var hidden = VisusRig.Live(Go(visus, rig, "https://a.test/"));
        hidden.RaiseDownload("https://a.test/p.txt", "p.txt", 10, "text/plain");
        rig.Layout();
        visus.Time.Advance(VisusPromptCard.Shield);
        Press(CardOf<VisusDownloadPrompt>(body).SaveButton);
        Pump(rig);
        Assert.Single(VisusHost.Downloads.Items);
        Assert.Empty(source.Snapshot(visus.Time.GetUtcNow())); // the model cannot see private tabs
    });

    // ---- HTTP auth and script dialogs (8.4) ----

    [Fact]
    public void Http_auth_is_nths_card_and_the_values_go_to_the_response_only() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var page = VisusRig.Live(Go(visus, rig, "https://a.test/"));

        var request = page.RaiseAuth("https://a.test/admin", "Basic realm=\"Staging\", charset=\"UTF-8\"");
        rig.Layout();
        var card = CardOf<VisusSignInCard>(body);
        Assert.Equal("a.test asks you to sign in", card.Title);
        Assert.Equal("Staging", card.Detail);
        Assert.Null(card.Warning);
        Assert.Equal(["Sign in", "Cancel"], card.Buttons.Select(Label));
        card.UserName.Text = "dana";
        card.Password.Text = "pw";
        visus.Time.Advance(VisusPromptCard.Shield);
        Press(card.SignInButton);
        Assert.Equal(new WebCredentials("dana", "pw"), request.Decision.Result);
        Assert.Equal(("", ""), (card.UserName.Text, card.Password.Text)); // nth keeps none of it

        var plain = page.RaiseAuth("http://intranet.example/", "Basic realm=\"x\"");
        rig.Layout();
        var second = CardOf<VisusSignInCard>(body);
        Assert.Equal(VisusSignInCard.PlainWarning, second.Warning!.Text);
        second.Dismiss();
        Assert.Null(plain.Decision.Result);
        Assert.True(VisusSignInCard.IsPlain("http://intranet.example/"));
        Assert.False(VisusSignInCard.IsPlain("http://localhost:5173/"));
        Assert.False(VisusSignInCard.IsPlain("http://127.0.0.1:8080/"));
        Assert.Equal("Staging", VisusSignInAsk.RealmOf("Basic realm=Staging"));
        Assert.Null(VisusSignInAsk.RealmOf("Bearer"));
    });

    [Fact]
    public void Enter_in_a_sign_in_field_signs_in_after_the_shield() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var page = VisusRig.Live(Go(visus, rig, "https://a.test/"));
        var request = page.RaiseAuth("https://a.test/");
        rig.Layout();
        var card = CardOf<VisusSignInCard>(body);
        card.UserName.Text = "u";
        card.Password.Text = "p";
        card.Password.Focus();
        rig.Key(Key.Enter);
        Assert.False(request.Decision.IsCompleted); // the keyboard just entered: shielded
        visus.Time.Advance(VisusPromptCard.Shield);
        rig.Key(Key.Enter);
        Assert.Equal(new WebCredentials("u", "p"), request.Decision.Result);
    });

    [Fact]
    public void A_page_dialog_is_a_card_labelled_with_the_site() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var page = VisusRig.Live(Go(visus, rig, "https://a.test/"));

        var alert = page.RaiseScriptDialog("https://a.test/", WebScriptDialogKind.Alert, "Saved.");
        rig.Layout();
        var card = CardOf<VisusDialogCard>(body);
        Assert.Equal(("a.test says", "Saved."), (card.Title, card.Detail));
        Assert.Equal(["OK"], card.Buttons.Select(Label));
        Assert.Null(card.Stop);
        visus.Time.Advance(VisusPromptCard.Shield);
        Press(card.AcceptButton);
        Assert.True(alert.Decision.Result.Accept);

        var prompt = page.RaiseScriptDialog("https://a.test/", WebScriptDialogKind.Prompt, "Your name?", "Dana");
        rig.Layout();
        var ask = CardOf<VisusDialogCard>(body);
        Assert.Equal("a.test asks", ask.Title);
        Assert.Equal("Dana", ask.Answer!.Text);
        Assert.NotNull(ask.Stop); // from the page's second dialog
        ask.Answer.Text = "Dana R";
        visus.Time.Advance(VisusPromptCard.Shield);
        Press(ask.AcceptButton);
        Assert.Equal(new WebScriptDialogDecision(true, "Dana R"), prompt.Decision.Result);

        var confirm = page.RaiseScriptDialog("https://a.test/", WebScriptDialogKind.Confirm, "Delete?");
        rig.Layout();
        var question = CardOf<VisusDialogCard>(body);
        question.Stop!.IsChecked = true;
        visus.Time.Advance(VisusPromptCard.Shield);
        Press(question.DismissButton!);
        Assert.Equal(WebScriptDialogDecision.Dismiss, confirm.Decision.Result);

        // "Don't let this page show more dialogs" holds until the page goes.
        Assert.Equal(WebScriptDialogDecision.Dismiss, page.RaiseScriptDialog("https://a.test/", WebScriptDialogKind.Alert, "spam").Decision.Result);
        Assert.Null(body.Card);
        page.RaiseNavigationStarting("https://a.test/next");
        page.RaiseScriptDialog("https://a.test/next", WebScriptDialogKind.Alert, "hello again");
        rig.Layout();
        Assert.IsType<VisusDialogCard>(body.Card);
    });

    [Fact]
    public void A_leave_page_dialog_offers_Stay_and_Esc_stays() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var page = VisusRig.Live(Go(visus, rig, "https://a.test/"));
        page.RaiseNavigationStarting("https://b.test/");
        var leave = page.RaiseScriptDialog("https://a.test/", WebScriptDialogKind.BeforeUnload, "ignored");
        rig.Layout();
        var card = CardOf<VisusDialogCard>(body);
        Assert.Equal(("Leave this page?", "Changes you made on a.test may not be saved."), (card.Title, card.Detail));
        Assert.Equal(["Leave", "Stay"], card.Buttons.Select(Label));
        card.Dismiss();
        Assert.Equal(WebScriptDialogDecision.Dismiss, leave.Decision.Result);
    });

    // ---- the rest of 8.4 ----

    [Fact]
    public void Fullscreen_is_contained_in_the_expanded_pane_and_Esc_or_a_restore_ends_it() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var tab = Go(visus, rig, "https://a.test/");
        var page = VisusRig.Live(tab);

        page.RaiseFullScreen(true);
        rig.Settle();
        Assert.Equal(PaneKind.Browser, rig.Workspace.State.Expanded);
        Assert.Same(tab, body.FullScreenTab);

        Assert.True(page.RaiseKey(Escape).Handled); // Esc is nth's while the page is in fullscreen
        Assert.Contains("Script " + WebFeatureGuard.ExitFullscreenScript, page.Log);
        page.RaiseFullScreen(false);
        rig.Settle();
        Assert.Null(rig.Workspace.State.Expanded);
        Assert.Null(body.FullScreenTab);
        Assert.False(page.RaiseKey(Escape).Handled); // Esc is the page's again

        page.RaiseFullScreen(true);
        rig.Settle();
        page.Log.Clear();
        rig.Workspace.RestoreExpanded(); // Ctrl+Shift+\ restores the pane
        rig.Settle();
        Assert.Contains("Script " + WebFeatureGuard.ExitFullscreenScript, page.Log);
    });

    [Fact]
    public void Pointer_lock_asks_like_a_permission_and_says_how_to_release_it() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var tab = Go(visus, rig, "https://game.test/");
        var page = VisusRig.Live(tab);

        page.EmitCdp("Runtime.bindingCalled", "{\"name\":\"__nthPointerLock\",\"payload\":\"{\\\"kind\\\":\\\"ask\\\",\\\"id\\\":5}\",\"executionContextId\":3}");
        rig.Layout();
        var card = CardOf<VisusPermissionPrompt>(body);
        Assert.Equal(SitePermissionKind.PointerLock, card.Permission.Permission);
        Assert.Equal("game.test wants to " + SitePermissions.Verb(SitePermissionKind.PointerLock), card.Title);
        visus.Time.Advance(VisusPromptCard.Shield);
        Press(card.AllowOnceButton);
        Assert.Contains(page.CdpCalls, call => call.Method == "Runtime.evaluate" && call.Params.Contains("__nthPointerLockAnswer(5, true)", StringComparison.Ordinal));

        page.EmitCdp("Runtime.bindingCalled", "{\"name\":\"__nthPointerLock\",\"payload\":\"{\\\"kind\\\":\\\"locked\\\"}\",\"executionContextId\":3}");
        rig.Layout();
        Assert.True(tab.IsPointerLocked);
        Assert.Equal(VisusTab.PointerLockNotice, body.NoticeText);
        Assert.Equal("Press Esc to release your pointer", VisusTab.PointerLockNotice);
        Assert.True(page.RaiseKey(Escape).Handled);
        Assert.Contains(page.CdpCalls, call => call.Method == "Runtime.evaluate" && call.Params.Contains("exitPointerLock", StringComparison.Ordinal));
    });

    [Fact]
    public void A_browse_pages_notification_is_nths_toast_naming_the_site() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, _) = Open(visus);
        using var __ = rig;
        var tab = Go(visus, rig, "https://example.com/");
        var page = VisusRig.Live(tab);
        Assert.Equal((VisusTabKind.Browse, WebProfileKind.Browse), (tab.Kind, tab.View!.Creation.Kind));
        var shown = page.RaiseNotification("https://example.com", "Build finished", "All green", silent: true);
        Assert.True(shown.Shown);
        Assert.Equal([("example.com", "Build finished", "All green", false)], visus.Notifications);

        // Notifications are blocked in preview (5.5): a dev server's page raises none.
        visus.Session.NewTab();
        var preview = Go(visus, rig, "http://localhost:5173/");
        Assert.Equal(VisusTabKind.Preview, preview.Kind);
        Assert.False(VisusRig.Live(preview).RaiseNotification("http://localhost:5173", "x").Shown);
        Assert.Equal(WebPermissionDecision.Deny, VisusRig.Live(preview).RaisePermission("http://localhost:5173", WebPermissionKind.Notifications).Decision.Result);

        visus.Session.NewTab(VisusTabKind.Private);
        var hidden = VisusRig.Live(Go(visus, rig, "https://example.com/"));
        Assert.False(hidden.RaiseNotification("https://example.com", "x").Shown);
        Assert.Single(visus.Notifications);
    });

    [Fact]
    public void An_agent_tabs_file_input_waits_for_the_users_Choose_in_the_guarded_card() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body) = Open(visus);
        using var _ = rig;
        var agent = visus.Session.NewTab(VisusTabKind.Agent, "https://a.test/");
        var page = VisusRig.Live(agent);
        page.RaiseNavigationCompleted("https://a.test/");
        rig.Layout();

        page.EmitCdp("Page.fileChooserOpened", "{\"frameId\":\"F\",\"mode\":\"selectSingle\",\"backendNodeId\":9}");
        rig.Layout();
        var card = CardOf<VisusFileCard>(body);
        Assert.Equal("The model's page on a.test wants a file from your PC", card.Title);
        Assert.Equal(VisusFileCard.Explanation, card.Detail);
        visus.Time.Advance(VisusPromptCard.Shield);

        visus.NextOpenFiles = null;
        Press(card.ChooseButton);
        Pump(rig);
        Assert.Equal([false], visus.OpenDialogs);
        Assert.DoesNotContain(page.CdpCalls, call => call.Method == "DOM.setFileInputFiles");
        Assert.Same(card, body.Card);

        visus.NextOpenFiles = [@"C:\Users\d\a.pdf", @"C:\Users\d\b.pdf"];
        Press(card.ChooseButton);
        Pump(rig);
        var call = page.CdpCalls.Last();
        Assert.Equal("DOM.setFileInputFiles", call.Method);
        Assert.Equal("{\"files\":[\"C:\\\\Users\\\\d\\\\a.pdf\"],\"backendNodeId\":9}", call.Params); // one file for a single input
        Assert.Null(body.Card);
        Assert.True(card.File.Given);
    });

    private static string Label(Button button) =>
        button.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text).FirstOrDefault(text => !string.IsNullOrEmpty(text)) ?? "";
}
