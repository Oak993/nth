using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Browser;
using Marrow.Core.Models;
using Marrow.Core.Web;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Web;

namespace Marrow.App.Tests.Views.Browser;

// PORT-ONLY: web surfaces W11 (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.10 model access: the tabs a chat's model may use
// (its agent tabs and the tab the user shared, never a private tab), web_open opening an agent tab, "unknown URL means stop"
// for addresses the model invents, the cards' asks and their withdrawal, "User takes over", a crash during a model step (2.7),
// the turn and the chat's Stop, "Log: ... the full log is in Background tasks"; 2.3 "Let the model use this tab"; 13.2 headless
// tests over the fake engine; ED-NEW-web-w11-1). Visus as the web tools see it, with fake chats and the real hub and engine.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w11-1")]
public sealed class VisusAgentTests(HeadlessSession headless)
{
    private const string Thread = "6f1d3c1e-0b7a-4a43-9d1e-2b8c4b8f6a11";

    private static VisusAgentBound Bind(bool paneOpen = true)
    {
        var visus = new VisusRig();
        visus.Session.AgentThread = Thread;
        var chats = new VisusAgentChatsFake(visus.Session, Thread) { PaneOpen = paneOpen };
        var hub = new WebAgentHub(() => null, () => null);
        var asks = new VisusAgentAsks();
        var jobs = new PublishedJobSource("Published");
        var turns = new VisusAgentTurns();
        var binding = new VisusAgentBinding(chats, hub, asks, () => jobs, settings: () => WebAgentSettings.Default, watchTurnEnd: turns.Watch,
            now: () => visus.Time.GetUtcNow());
        return new VisusAgentBound(visus, chats, hub, asks, jobs, turns, binding);
    }

    private static WebToolResult Run(VisusAgentBound b, string tool, string json = "{}")
    {
        var call = b.Hub.Engine(Thread)!.Call(tool, JsonNode.Parse(json)!.AsObject(), new WebAgentCaller(Thread, "claude-opus-5-5", true), CancellationToken.None);
        HeadlessSession.RunDispatcherUntil(call, TimeSpan.FromSeconds(20));
        Dispatcher.UIThread.RunJobs();
        return call.Result;
    }

    // The fake engine has no platform view: a new view is attached as the pane's host would, and its page finishes loading.
    private static void AttachNewViews(VisusRig visus) => visus.Session.Changed += () =>
    {
        foreach (var tab in visus.Session.Tabs.Where(t => t.View is { Engine: null or { IsAttached: false } }))
        {
            var page = VisusRig.Live(tab);
            page.RaiseNavigationCompleted(tab.Url);
        }
    };

    [Fact]
    public void The_model_may_use_its_agent_tabs_and_the_one_shared_tab_never_a_private_one() => headless.OnUiThread(() =>
    {
        using var b = Bind();
        var browse = b.Visus.Page("https://mail.test/");
        var other = b.Visus.Page("https://news.test/");
        var secret = b.Visus.Page("https://bank.test/", VisusTabKind.Private);
        var agent = b.Visus.Page("https://a.test/", VisusTabKind.Agent);

        var before = b.Binding.Host.Tabs(Thread).Select(t => t.Id).ToList();
        b.Visus.Session.ShareWithModel(browse, true);
        b.Visus.Session.ShareWithModel(secret, true);
        var shared = b.Binding.Host.Tabs(Thread).Select(t => t.Id).ToList();
        b.Visus.Session.ShareWithModel(other, true);
        var moved = b.Binding.Host.Tabs(Thread).Select(t => t.Id).ToList();

        Assert.Equal(new[] { agent.Id }, before);
        Assert.Equal(new[] { browse.Id, agent.Id }, shared);
        Assert.False(secret.SharedWithModel);
        Assert.Equal(new[] { other.Id, agent.Id }, moved); // sharing another ends the first
        Assert.False(browse.SharedWithModel);
        Assert.Empty(b.Binding.Host.Tabs("someone-else"));
        Assert.True(VisusAgentHost.Adapter(b.Visus.Session, browse).IsClosed); // no longer shared: closed for the model
    });

    [Fact]
    public void The_menu_shares_a_browse_or_preview_tab_and_stops_sharing_it() => headless.OnUiThread(() =>
    {
        using var b = Bind();
        var browse = b.Visus.Page("https://mail.test/");
        var secret = b.Visus.Page("https://bank.test/", VisusTabKind.Private);

        var share = Assert.Single(b.Binding.ShareEntries(b.Visus.Session, browse));
        share.Run();
        var stop = Assert.Single(b.Binding.ShareEntries(b.Visus.Session, browse));

        Assert.Equal("Let the model use this tab", share.Title);
        Assert.Equal("Stop sharing with the model", stop.Title);
        Assert.True(browse.SharedWithModel);
        Assert.Empty(b.Binding.ShareEntries(b.Visus.Session, secret));
        Assert.Contains(VisusToolbar.MenuProviders, p => p.Method.Name == nameof(VisusAgentBinding.ShareEntries));
        stop.Run();
        Assert.False(browse.SharedWithModel);
    });

    [Fact]
    public void Web_tabs_through_the_app_lists_the_shared_tab_as_the_users() => headless.OnUiThread(() =>
    {
        using var b = Bind();
        var browse = b.Visus.Page("https://mail.test/", title: "Inbox");
        b.Visus.Session.ShareWithModel(browse, true);

        var result = Run(b, WebToolSchemas.Tabs);

        Assert.Null(result.Error);
        Assert.Contains("t1 (shared by the user) https://mail.test/ | Inbox", result.Text, StringComparison.Ordinal);
    });

    [Fact]
    public void Web_open_opens_an_agent_tab_beside_the_users_tab_without_taking_it_from_them() => headless.OnUiThread(() =>
    {
        using var b = Bind();
        var reading = b.Visus.Page("https://news.test/");
        AttachNewViews(b.Visus);
        b.Chats.Wrote.Add("Invite Dana on https://team.test/settings please");

        var result = Run(b, WebToolSchemas.Open, "{\"url\":\"https://team.test/settings\"}");

        Assert.Null(result.Error);
        var agent = Assert.Single(b.Visus.Session.Tabs, t => t.Kind == VisusTabKind.Agent);
        Assert.Equal("https://team.test/settings", agent.Url);
        Assert.Same(reading, b.Visus.Session.Selected); // the user's page stays in front
        Assert.Equal(0, b.Chats.PaneOpened);
        // ED-NEW-web-sec-res-3: the tab's address is inside the untrusted block.
        Assert.StartsWith("Opened t1", result.Text, StringComparison.Ordinal);
        Assert.Contains("<untrusted-page-content origin=\"https://team.test\" marker=\"", result.Text, StringComparison.Ordinal);
        Assert.Contains("\">\nAddress: https://team.test/settings\n", result.Text, StringComparison.Ordinal);
    });

    [Fact]
    public void Web_open_with_the_pane_closed_opens_it_and_chooses_the_new_tab() => headless.OnUiThread(() =>
    {
        using var b = Bind(paneOpen: false);
        b.Visus.Page("https://news.test/");
        AttachNewViews(b.Visus);
        b.Chats.Wrote.Add("open https://team.test/");

        var result = Run(b, WebToolSchemas.Open, "{\"url\":\"https://team.test/\"}");

        Assert.Null(result.Error);
        Assert.Equal(1, b.Chats.PaneOpened);
        Assert.Equal(VisusTabKind.Agent, b.Visus.Session.Selected!.Kind);
    });

    [Fact]
    public void An_address_only_the_models_own_answer_holds_is_unknown_and_opens_nothing() => headless.OnUiThread(() =>
    {
        using var b = Bind();
        b.Chats.Wrote.Add("Find the team page");

        var result = Run(b, WebToolSchemas.Open, "{\"url\":\"https://team.test/\"}");

        Assert.Equal(WebErrorCodes.UnknownUrl, result.Error);
        Assert.DoesNotContain(b.Visus.Session.Tabs, t => t.Kind == VisusTabKind.Agent);
        Assert.False(b.Binding.Host.InConversation(Thread, "https://team.test/settings"));
        b.Chats.Wrote.Add("it is https://team.test/settings");
        Assert.True(b.Binding.Host.InConversation(Thread, "https://team.test/settings"));
    });

    [Fact]
    public void The_cards_name_the_model_and_the_chat() => headless.OnUiThread(() =>
    {
        using var b = Bind();

        Assert.Equal("Opus 5.5", b.Binding.Host.ModelName("claude-opus-5-5"));
        Assert.Equal("gpt-x", b.Binding.Host.ModelName("gpt-x"));
        Assert.Equal("The model", b.Binding.Host.ModelName(null));
        Assert.Equal("Invite Dana", b.Binding.Host.ChatTitle(Thread));
    });

    [Fact]
    public void The_user_acting_on_a_tab_from_Visus_chrome_is_a_take_over_of_that_tab_only() => headless.OnUiThread(() =>
    {
        using var b = Bind();
        var agent = b.Visus.Page("https://a.test/", VisusTabKind.Agent);
        var other = b.Visus.Page("https://b.test/", VisusTabKind.Agent);
        var adapter = VisusAgentHost.Adapter(b.Visus.Session, agent);
        var inputs = 0;
        adapter.UserInput += () => inputs++;

        b.Visus.Session.Select(agent);
        b.Visus.Session.Perform(VisusCommand.Reload);
        b.Visus.Session.Select(other);
        b.Visus.Session.Perform(VisusCommand.Reload);
        b.Visus.Session.Select(agent);
        b.Visus.Session.Go("https://c.test/");

        Assert.Equal(2, inputs);
    });

    [Fact]
    public void A_crash_reaches_the_step_of_its_own_tab_only() => headless.OnUiThread(() =>
    {
        using var b = Bind();
        var agent = b.Visus.Page("https://a.test/", VisusTabKind.Agent);
        var other = b.Visus.Page("https://b.test/", VisusTabKind.Agent);
        var adapter = VisusAgentHost.Adapter(b.Visus.Session, agent);
        var reasons = new List<string>();
        adapter.StepFailed += reasons.Add;

        using (VisusAgentHost.Adapter(b.Visus.Session, other).BeginModelStep())
            VisusRig.Live(other).RaiseProcessFailed(WebProcessFailureKind.RenderProcessExited);
        using (adapter.BeginModelStep())
        {
            Assert.True(agent.View!.IsModelStepRunning); // never suspended or discarded meanwhile
            VisusRig.Live(agent).RaiseProcessFailed(WebProcessFailureKind.RenderProcessExited);
        }

        Assert.Equal(new[] { WebSurface.StepFailedCrashed }, reasons);
        Assert.False(agent.View!.IsModelStepRunning);
    });

    [Fact]
    public void A_tab_comes_live_for_a_step_and_a_blank_one_has_nothing_to_drive() => headless.OnUiThread(() =>
    {
        using var b = Bind();
        var agent = b.Visus.Page("https://a.test/", VisusTabKind.Agent);
        var blank = b.Visus.Session.NewTab(VisusTabKind.Agent);

        var live = VisusAgentHost.Adapter(b.Visus.Session, agent).MakeLiveAsync(CancellationToken.None);
        var nothing = VisusAgentHost.Adapter(b.Visus.Session, blank).MakeLiveAsync(CancellationToken.None);

        Assert.True(live.IsCompletedSuccessfully && live.Result);
        Assert.True(nothing.IsCompletedSuccessfully && !nothing.Result);
    });

    [Fact]
    public void An_ask_waits_for_its_card_and_maps_the_choice() => headless.OnUiThread(() =>
    {
        var asks = new VisusAgentAsks();
        var shared = asks.AskSite(new WebSiteAsk(Thread, "t1", "https://mail.test", "Opus 5.5", Shared: true) { TabId = "x" }, CancellationToken.None);
        var pair = asks.AskNavigate(new WebNavigateAsk(Thread, "t1", ["https://a.test"], "https://b.test/?q=1", [], "Opus 5.5"), CancellationToken.None);
        var guard = asks.Confirm(new WebGuardAsk(Thread, "t1", "https://a.test", WebGuardCategory.Send, "send", "Send invite", "Opus 5.5"), CancellationToken.None);

        Assert.Equal(3, asks.Pending.Count);
        Assert.Equal(VisusAgentAskKind.Site, asks.Oldest!.Kind);
        asks.Pending[0].Answer(VisusAgentChoice.Always); // a shared tab is never "Always": the narrower grant
        asks.Pending[0].Answer(VisusAgentChoice.Always);
        asks.Pending[0].Answer(VisusAgentChoice.Once);

        Assert.Equal(WebSiteAnswer.AllowOnce, shared.Result);
        Assert.Equal(WebNavigateAnswer.AlwaysForPair, pair.Result);
        Assert.True(guard.Result);
        Assert.Empty(asks.Pending);
    });

    [Fact]
    public void A_withdrawn_ask_goes_and_a_late_answer_does_nothing() => headless.OnUiThread(() =>
    {
        var asks = new VisusAgentAsks();
        using var stop = new CancellationTokenSource();
        var changes = 0;
        asks.Changed += () => changes++;
        var guard = asks.Confirm(new WebGuardAsk(Thread, "t1", "https://a.test", WebGuardCategory.Send, "send", "Send invite", "Opus 5.5"), stop.Token);
        var item = asks.Oldest!;

        stop.Cancel();
        item.Answer(VisusAgentChoice.Once);

        Assert.True(guard.IsCanceled);
        Assert.True(item.IsDone);
        Assert.Empty(asks.Pending);
        Assert.Equal(2, changes);
    });

    [Fact]
    public void An_ask_opens_the_chats_Visus_pane_when_it_is_closed() => headless.OnUiThread(() =>
    {
        using var b = Bind(paneOpen: false);

        _ = b.Asks.AskSite(new WebSiteAsk(Thread, "t1", "https://mail.test", "Opus 5.5", Shared: true), CancellationToken.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, b.Chats.PaneOpened);
    });

    [Fact]
    public void The_turn_end_lets_the_next_turn_use_Visus_after_the_chats_Stop() => headless.OnUiThread(() =>
    {
        using var b = Bind();
        b.Visus.Page("https://a.test/", VisusTabKind.Agent);
        Assert.Null(Run(b, WebToolSchemas.Tabs).Error);

        b.Binding.ChatStopped(Thread);
        var stopped = Run(b, WebToolSchemas.Tabs);
        b.Turns.Watched[Thread]();
        var next = Run(b, WebToolSchemas.Tabs);

        Assert.Equal(WebErrorCodes.Stopped, stopped.Error);
        Assert.Null(next.Error);
        Assert.Contains(b.Hub.Engines, e => e.Thread == Thread && !e.StoppedForTurn);
    });

    [Fact]
    public void Without_Tactus_the_stop_line_names_no_chord() => headless.OnUiThread(() =>
    {
        using var b = Bind();
        b.Visus.Page("https://a.test/", VisusTabKind.Agent);
        var lines = new List<WebAgentActivity>();
        b.Hub.Activity += lines.Add;
        Run(b, WebToolSchemas.Tabs);

        b.Binding.ChatStopped(Thread);

        Assert.Contains(lines, a => a.Phase == WebAgentPhase.Stopped && a.Step == "Every web step ended.");
        Assert.Null(b.Hub.StopChord);
    });

    [Fact]
    public void Each_chats_web_session_is_a_Background_tasks_job_with_Pause_and_Stop() => headless.OnUiThread(() =>
    {
        using var b = Bind();
        b.Visus.Page("https://a.test/", VisusTabKind.Agent);
        AttachNewViews(b.Visus);
        b.Chats.Wrote.Add("https://team.test/");
        Run(b, WebToolSchemas.Open, "{\"url\":\"https://team.test/\"}");
        Dispatcher.UIThread.RunJobs();
        var job = Assert.Single(b.Jobs.Snapshot(b.Visus.Time.GetUtcNow()));

        Assert.Equal(VisusAgentBinding.JobPrefix + Thread, job.Id);
        Assert.Equal(BackgroundJobKind.BrowserSession, job.Kind);
        Assert.Equal("Invite Dana", job.Title);
        Assert.Equal("Visus \u00B7 agent tab", job.KindText);
        Assert.Equal(BackgroundJobState.Running, job.State);
        Assert.Equal("Opening team.test", job.Activity);
        Assert.True(job.CanPause && job.CanStop);
        Assert.Equal(Guid.Parse(Thread), job.TranscriptThreadId);

        Assert.True(b.Jobs.Pause(job.Id));
        Assert.True(b.Hub.IsPaused);
        Assert.True(b.Jobs.Resume(job.Id));
        Assert.False(b.Hub.IsPaused);
        Assert.True(b.Jobs.Stop(job.Id));
        Dispatcher.UIThread.RunJobs();
        var stopped = Assert.Single(b.Jobs.Snapshot(b.Visus.Time.GetUtcNow()));
        b.Turns.Watched[Thread]();
        Dispatcher.UIThread.RunJobs();
        var after = Assert.Single(b.Jobs.Snapshot(b.Visus.Time.GetUtcNow()));

        Assert.Equal(BackgroundJobState.Stopped, stopped.State);
        Assert.NotNull(stopped.FinishedAt);
        Assert.False(stopped.CanStop);
        Assert.Equal(BackgroundJobState.Stopped, after.State); // the turn's end keeps "Stopped"
    });

    [Fact]
    public void A_card_never_opens_under_the_pointer() => headless.OnUiThread(() =>
    {
        var card = new PixelRect(100, 100, 300, 200);

        Assert.Equal(0, VisusAgentCard.PointerShift(card, new PixelPoint(50, 50), 16));
        Assert.Equal(0, VisusAgentCard.PointerShift(card, null, 16));
        Assert.Equal(36, VisusAgentCard.PointerShift(card, new PixelPoint(120, 150), 16)); // left edge past the pointer
        Assert.Equal(-36, VisusAgentCard.PointerShift(card, new PixelPoint(380, 150), 16)); // right edge before it
        Assert.Equal(14, VisusAgentCard.PointerShift(card, new PixelPoint(98, 150), 16)); // in the margin
    });
}
