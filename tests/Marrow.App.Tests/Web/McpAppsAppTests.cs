using System.Reflection;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port.Web;
using Marrow.App.Port.Web.McpApps;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.App.Views.Panes;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.App.Tests.Web;

// PORT-ONLY: web surfaces W19 in the app (DESIGN.md 7.6, 13.2 "MCP Apps host", 14 W19 "inline, expanded, pop-out,
// fullscreen as Expand; live and snapshot in the thread; declared-domain consent; attributed ui/message; the context chip").
// A real timeline on a real chat, the fake web engine behind the one WebSurface, and a scripted MCP server: the call draws
// its card under its tool row; the one host moves between the card, the App pane and the pop-out window and back; an app's
// message is the app's row and reaches the model wrapped as untrusted; its context is a chip the user can read and remove;
// its cards sit in the chat's approval stack and only the user's click answers them.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w19-4")]
[Trait("Feature", "W-19")]
public sealed class McpAppsAppTests(HeadlessSession headless)
{
    private const string Resource = "ui://shipyard/release-board.html";
    private static readonly McpAppToolEntry Board = new("shipyard", "list_releases", Resource) { Title = "Release board" };
    private static readonly McpAppToolEntry Ledger = new("shipyard", "show_ledger", "ui://shipyard/ledger.html") { Title = "Ledger" };

    private sealed class Servers : IMcpAppServers
    {
        public Task<McpAppPage> ReadPageAsync(string server, string uri, CancellationToken cancellationToken) =>
            Task.FromResult(new McpAppPage(server, uri, "<!doctype html><title>Release board</title><p>v2.4.1</p>", McpUiCsp.None));

        public Task<IReadOnlyList<McpAppTool>> ToolsAsync(string server, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<McpAppTool>>([new McpAppTool("list_releases", "Release board", true)]);

        public Task<JsonObject> CallToolAsync(string server, string tool, JsonObject arguments, CancellationToken cancellationToken) =>
            Task.FromResult(new JsonObject { ["content"] = new JsonArray(), ["isError"] = false });

        public Task<JsonObject> ReadResourceAsync(string server, string uri, CancellationToken cancellationToken) =>
            Task.FromResult(new JsonObject { ["contents"] = new JsonArray() });
    }

    // The app layer over the fake engine; everything static it touched is put back after.
    private sealed class Apps : IDisposable
    {
        private readonly Func<WebSurface> factory = WebSurfaces.Factory;
        private readonly Func<IWebViewSurface, Control>? live = WebSurfaces.LiveFactory;

        public Apps(ThreadRuntime runtime)
        {
            WebSurfaces.Reset();
            McpAppSessions.Reset();
            WebSurfaces.Factory = static () => new WebSurface(new FakeWebEngine(), new WebProfiles(@"C:\nth-test\WebView2", "en-US"), post: static action => action());
            WebSurfaces.LiveFactory = static _ => new Border();
            Grants = new McpAppGrantStore(null);
            Ui = new McpAppHostUi(thread => thread == runtime.ThreadID ? runtime : null);
            Host = new McpAppsHost(new Servers(), Grants, Ui, static action => action());
            McpAppSessions.HostSource = () => Host;
            McpAppCatalog.Set([Board, Ledger]);
            AppPane.Register();
        }

        public McpAppGrantStore Grants { get; }

        public McpAppHostUi Ui { get; }

        public McpAppsHost Host { get; }

        public void Dispose()
        {
            foreach (var session in McpAppSessions.All()) session.Window?.CloseQuietly();
            McpAppSessions.Reset();
            McpAppSessions.HostSource = static () => McpAppsHost.Shared;
            McpAppCatalog.Set([]);
            WebSurfaces.Reset();
            WebSurfaces.Factory = factory;
            WebSurfaces.LiveFactory = live;
        }
    }

    private static TimelineEntry Call(string title, string? detail, ToolCall.Status status = ToolCall.Status.Running, Guid? turn = null) =>
        new(new TimelineItem(turn, new TimelineItem.Content.Tool(new ToolCall { ToolCallKind = ToolCall.Kind.Mcp, Title = title, Detail = detail, ToolCallStatus = status })));

    private static TimelineEntry AppMessage(string text, Guid? turn = null) =>
        new(new TimelineItem(turn, new TimelineItem.Content.User(new UserMessage { Text = text, AppSource = "shipyard" })));

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static T[] Find<T>(Visual root) => root.GetVisualDescendants().OfType<T>().ToArray();

    private static void Pump(TimelineMount m, int milliseconds = 100) => m.Advance(milliseconds);

    // The App pane's content, shown in a window of its own as the workspace would.
    private static (Window Window, AppPane Pane, Views.Panes.PaneContent Content) OpenPane(ThreadRuntime runtime)
    {
        var content = PaneContents.Create(PaneKind.App, runtime)!;
        var pane = Assert.IsType<AppPane>(content.Body);
        var window = new Window { Width = 700, Height = 500, Content = pane };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return (window, pane, content);
    }

    [Fact]
    public void An_app_call_draws_its_card_under_its_tool_row_and_an_app_message_is_the_apps_own_row() => ThreadTimelineRig.Run(headless, m =>
    {
        using var apps = new Apps(m.Runtime);
        m.SetEntries([AppMessage("Promote v2.4.1 to production."), Call("shipyard · list_releases", "{\"env\":\"stag")]);
        Pump(m);

        var row = Assert.Single(Find<McpAppRow>(m.View));
        var card = Assert.IsType<InlineWebWidget>(row.Card);
        Assert.Equal("shipyard · Release board", card.TitleText);
        Assert.True(card.ShowsTrust); // wide enough: "Sandboxed · from shipyard"
        Assert.Null(card.PlaceholderText);
        Assert.Same(row.Session!.Host, card.Slot.Child);
        Assert.Equal(McpAppDisplayMode.Inline, card.PressedMode);
        Assert.Equal(McpAppSession.DefaultInlineHeight, card.Slot.Height, 1); // before the app says how tall it is
        Assert.Contains(Find<WorkGroup>(row), _ => true); // the tool row as every tool call draws it

        var message = Assert.Single(Find<AppMessageRow>(m.View));
        Assert.Equal("shipyard", message.Server);
        Assert.Equal("Promote v2.4.1 to production.", message.Message.Text);
        Assert.Contains(Find<TextBlock>(message), text => text.Text == "Message from shipyard");
    });

    [Fact]
    public void A_plain_mcp_call_stays_a_plain_tool_row() => ThreadTimelineRig.Run(headless, m =>
    {
        using var apps = new Apps(m.Runtime);
        m.SetEntries([Call("shipyard · delete_everything", "{}"), Call("other · list_releases", "{}")]);
        Pump(m);
        Assert.Empty(Find<McpAppRow>(m.View));
        Assert.Empty(Find<InlineWebWidget>(m.View));
    });

    [Fact]
    public void A_finished_turn_keeps_its_app_once_in_the_folded_answer() => ThreadTimelineRig.Run(headless, m =>
    {
        using var apps = new Apps(m.Runtime);
        var turn = Guid.NewGuid();
        m.SetEntries(
        [
            new TimelineEntry(new TimelineItem(turn, new TimelineItem.Content.User(new UserMessage { Text = "Show the release board." }))),
            Call("shipyard · list_releases", "{\"env\":\"staging\"}", ToolCall.Status.Completed, turn),
            new TimelineEntry(new TimelineItem(turn, new TimelineItem.Content.Assistant(new AssistantMessage { Text = "Here is the release board from shipyard." }))),
            new TimelineEntry(new TimelineItem(turn, new TimelineItem.Content.TurnEnd(new TurnSummary { TurnID = turn, Status = TurnStatus.Completed, Duration = 4, FilesChanged = 0, Additions = 0, Deletions = 0 }))),
        ]);
        Pump(m, 300);
        var row = Assert.Single(Find<McpAppRow>(m.View));
        Assert.NotNull(row.Card);
        Assert.Same(row.Session!.Host, row.Card!.Slot.Child);
    });

    [Fact]
    public void The_switch_moves_the_one_host_to_the_pane_and_the_window_and_Bring_back_returns_it() => ThreadTimelineRig.Run(headless, m =>
    {
        using var apps = new Apps(m.Runtime);
        m.SetEntries([Call("shipyard · list_releases", "{}", ToolCall.Status.Completed)]);
        Pump(m);
        var row = Assert.Single(Find<McpAppRow>(m.View));
        var card = row.Card!;
        var session = row.Session!;

        // Pane: the App pane opens (not filling the workspace), the card says where the app went.
        Click(card.SwitchButton(McpAppDisplayMode.Pane)!);
        Pump(m);
        Assert.Equal(McpAppDisplayMode.Pane, session.Mode);
        Assert.True(m.Runtime.Workspace.IsOpen(PaneKind.App));
        Assert.Null(m.Runtime.Workspace.Expanded);
        Assert.Same(session, McpAppSessions.Expanded(m.Runtime.ThreadID));
        Assert.Equal("Release board is open in a pane", card.PlaceholderText);
        Assert.Null(card.Slot.Child);

        var (paneWindow, pane, content) = OpenPane(m.Runtime);
        try
        {
            Assert.Same(session.Host, pane.Slot.Child);
            Assert.Equal("Sandboxed · from shipyard", pane.TrustText);
            Assert.Equal("App", content.Title);
            Assert.Equal("shipyard", content.Subtitle);
            Assert.Equal(["Pop out", "Back inline"], content.Actions.Select(action => action.Name).ToArray());
            Assert.Equal(McpAppDisplayMode.Pane, session.Mount.Context.DisplayMode);

            // Pop out from the pane's header: the pane closes, the window holds the host.
            content.Actions[0].Invoke();
            Pump(m);
            Assert.Equal(McpAppDisplayMode.PopOut, session.Mode);
            Assert.False(m.Runtime.Workspace.IsOpen(PaneKind.App));
            Assert.Null(McpAppSessions.Expanded(m.Runtime.ThreadID));
            var window = Assert.IsType<McpAppWindow>(session.Window);
            Assert.Same(session.Host, window.Slot.Child);
            Assert.Null(pane.Slot.Child);
            Assert.Equal("Release board is in its own window", card.PlaceholderText);
            Assert.True(window.IsOpening); // the snapshot holds while it springs in
            Assert.True(session.Host.IsHeld);
            window.Settle();
            Assert.False(window.IsOpening);
            Assert.True(window.Pinned);
            Assert.True(window.Window.Topmost);
            Click(window.PinButton);
            Assert.False(window.Window.Topmost);

            // Its close button (Back inline): the host comes home to the card.
            session.WindowClosed(window);
            Pump(m);
            Assert.Equal(McpAppDisplayMode.Inline, session.Mode);
            Assert.Null(session.Window);
            Assert.Null(card.PlaceholderText);
            Assert.Same(session.Host, card.Slot.Child);
            Assert.Equal(McpAppDisplayMode.Inline, card.PressedMode);
        }
        finally
        {
            paneWindow.Close();
        }
    });

    [Fact]
    public void The_apps_fullscreen_is_the_expanded_App_pane_and_its_close_brings_the_app_back_inline() => ThreadTimelineRig.Run(headless, m =>
    {
        using var apps = new Apps(m.Runtime);
        m.SetEntries([Call("shipyard · list_releases", "{}", ToolCall.Status.Completed)]);
        Pump(m);
        var row = Assert.Single(Find<McpAppRow>(m.View));
        var session = row.Session!;

        // The app asks for fullscreen (the bridge's request-display-mode goes through this handler): Expand, never the screen.
        Assert.Equal(McpAppDisplayMode.Pane, session.Mount.DisplayModeHandler!(McpAppDisplayMode.Pane));
        Pump(m);
        Assert.True(m.Runtime.Workspace.IsOpen(PaneKind.App));
        Assert.Equal(PaneKind.App, m.Runtime.Workspace.Expanded);

        // The pane's own close button closes the pane: the app goes back to its card.
        m.Runtime.Workspace = m.Runtime.Workspace.Close(PaneKind.App);
        Pump(m);
        Assert.Equal(McpAppDisplayMode.Inline, session.Mode);
        Assert.Same(session.Host, row.Card!.Slot.Child);
        Assert.Null(McpAppSessions.Expanded(m.Runtime.ThreadID));
    });

    [Fact]
    public void A_thread_has_one_App_pane_and_expanding_another_app_sends_the_first_back_to_its_card() => ThreadTimelineRig.Run(headless, m =>
    {
        using var apps = new Apps(m.Runtime);
        m.SetEntries([Call("shipyard · list_releases", "{}", ToolCall.Status.Completed), Call("shipyard · show_ledger", "{}", ToolCall.Status.Completed)]);
        Pump(m);
        var rows = Find<McpAppRow>(m.View);
        Assert.Equal(2, rows.Length);
        var (board, ledger) = (rows[0].Session!, rows[1].Session!);
        var (paneWindow, pane, content) = OpenPane(m.Runtime);
        try
        {
            board.SetMode(McpAppDisplayMode.Pane);
            Pump(m);
            ledger.SetMode(McpAppDisplayMode.Pane);
            Pump(m);
            Assert.Equal(McpAppDisplayMode.Inline, board.Mode);
            Assert.Equal(McpAppDisplayMode.Pane, ledger.Mode);
            Assert.True(m.Runtime.Workspace.IsOpen(PaneKind.App)); // the pane stayed open for the second app
            Assert.Same(ledger, pane.Shown);
            Assert.Same(ledger.Host, pane.Slot.Child);
            Assert.Same(board.Host, rows[0].Card!.Slot.Child);
            Assert.Equal("Ledger is open in a pane", rows[1].Card!.PlaceholderText);

            // Back inline from the pane's header.
            content.Actions[1].Invoke();
            Pump(m);
            Assert.Equal(McpAppDisplayMode.Inline, ledger.Mode);
            Assert.False(m.Runtime.Workspace.IsOpen(PaneKind.App));
        }
        finally
        {
            paneWindow.Close();
        }
    });

    [Fact]
    public void The_App_pane_holds_the_apps_newest_frame_until_the_workspace_stands_still() => ThreadTimelineRig.Run(headless, m =>
    {
        using var apps = new Apps(m.Runtime);
        m.SetEntries([Call("shipyard · list_releases", "{}", ToolCall.Status.Completed)]);
        Pump(m);
        var session = Assert.Single(Find<McpAppRow>(m.View)).Session!;
        session.SetMode(McpAppDisplayMode.Pane);
        Pump(m);
        Assert.False(session.Host.IsHeld); // nowhere to show: no hold left on it

        var (paneWindow, pane, _) = OpenPane(m.Runtime);
        try
        {
            // The app arrives in the pane on its snapshot (the pane springs in with opacity and scale, which a page cannot follow)...
            Assert.Same(session.Host, pane.Slot.Child);
            Assert.True(pane.HoldsForMotion);
            Assert.True(session.Host.IsHeld);

            // ...and goes live two frames after nothing moves.
            Pump(m);
            Assert.False(pane.HoldsForMotion);
            Assert.False(session.Host.IsHeld);

            // Its size changing while still (no workspace motion) holds nothing.
            paneWindow.Width = 640;
            paneWindow.UpdateLayout();
            Assert.False(pane.HoldsForMotion);
        }
        finally
        {
            paneWindow.Close();
        }

        Assert.False(session.Host.IsHeld); // the pane left: its hold went with it
    });

    [Fact]
    public void The_pop_out_window_resizes_with_Splitter_grips_within_280_by_180_and_remembers_the_size() => ThreadTimelineRig.Run(headless, m =>
    {
        using var apps = new Apps(m.Runtime);
        m.SetEntries([Call("shipyard · list_releases", "{}", ToolCall.Status.Completed)]);
        Pump(m);
        var session = Assert.Single(Find<McpAppRow>(m.View)).Session!;
        session.SetMode(McpAppDisplayMode.PopOut);
        var window = session.Window!;
        window.Settle();
        Assert.Equal(new Size(McpAppWindow.DefaultWidth, McpAppWindow.DefaultHeight), window.Size);

        var grips = window.Splitters.ToArray();
        Assert.Equal(5, grips.Length);
        Assert.Equal(2, grips.Count(grip => grip.Label == "Resize the shipyard window width"));
        Assert.Single(grips, grip => grip.Label == "Resize the shipyard window height");
        Assert.Equal(2, grips.Count(grip => grip.Label == "Resize the shipyard window"));

        // The bottom-right corner dragged far up-left stops at the minimum.
        var corner = grips.Last();
        corner.Target.Begin();
        corner.Target.Move(new Vector(-900, -900));
        corner.Target.End(true);
        Assert.Equal(new Size(McpAppWindow.MinimumWidth, McpAppWindow.MinimumHeight), window.Size);
        Assert.Equal((McpAppWindow.MinimumWidth, McpAppWindow.MinimumHeight), apps.Grants.Get("shipyard").WindowSize);

        // Grown by keyboard steps from the right edge; the next window for shipyard opens at the size it was left at.
        var right = grips[1];
        right.Target.Step(new Vector(120, 0));
        Assert.Equal(McpAppWindow.MinimumWidth + 120, window.Size.Width, 1);
        session.SetMode(McpAppDisplayMode.Inline);
        Assert.Null(session.Window);
        session.SetMode(McpAppDisplayMode.PopOut);
        session.Window!.Settle();
        Assert.Equal(new Size(McpAppWindow.MinimumWidth + 120, McpAppWindow.MinimumHeight), session.Window.Size);
        Assert.Equal(new Size(McpAppWindow.MinimumWidth, McpAppWindow.MinimumHeight), McpAppWindow.Fit(new Size(10, 10), new Size(1920, 1080)));
        Assert.Equal(new Size(1872, 1032), McpAppWindow.Fit(new Size(5000, 5000), new Size(1872, 1032)));
    });

    [Fact]
    public void Scrolling_holds_every_inline_app_on_its_snapshot_and_one_goes_live_after_the_thread_is_still() => ThreadTimelineRig.Run(headless, m =>
    {
        using var apps = new Apps(m.Runtime);
        m.SetEntries([Call("shipyard · list_releases", "{}", ToolCall.Status.Completed)]);
        Pump(m);
        var card = Assert.Single(Find<McpAppRow>(m.View)).Card!;
        var coordinator = InlineWebCoordinator.For(m.Scroll);
        coordinator.Evaluate();

        coordinator.Scrolled();
        Assert.True(coordinator.Scrolling);
        Assert.True(coordinator.IsHeld(card));
        Assert.True(card.App!.Host.IsHeld);
        Assert.Null(coordinator.Live);
        Assert.Equal(InlineWebWidget.SnapshotText, card.BadgeText); // the header's pill says so (canvas .live.snap)

        // Still again: the card is live only if it lies wholly inside the clear part of the thread.
        coordinator.GetType().GetProperty(nameof(InlineWebCoordinator.Scrolling))!.SetValue(coordinator, false);
        coordinator.Evaluate();
        var clear = coordinator.ClearArea();
        var at = card.Slot.TranslatePoint(default, m.Scroll)!.Value;
        var inside = new InlineRect(at.X, at.Y, card.Slot.Bounds.Width, card.Slot.Bounds.Height).IsInside(clear);
        Assert.Equal(inside, coordinator.Live == card);
        Assert.Equal(!inside, coordinator.IsHeld(card));
        Assert.Equal(inside ? InlineWebWidget.LiveText : InlineWebWidget.SnapshotText, card.BadgeText);

        // An app moved to the pane leaves no hold behind on its host.
        card.App!.SetMode(McpAppDisplayMode.Pane);
        Pump(m);
        Assert.False(coordinator.IsHeld(card));
        Assert.False(card.App.Host.IsHeld);
    });

    [Fact]
    public void An_apps_context_is_a_chip_capped_at_4000_characters_that_the_user_can_read_and_remove() => ThreadTimelineRig.Run(headless, m =>
    {
        using var apps = new Apps(m.Runtime);
        m.SetEntries([Call("shipyard · list_releases", "{}", ToolCall.Status.Completed)]);
        Pump(m);
        var session = Assert.Single(Find<McpAppRow>(m.View)).Session!;
        var chips = new AppContextChips(m.Runtime);
        var window = new Window { Width = 600, Height = 200, Content = chips };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.False(chips.IsVisible);

            apps.Ui.SetModelContext(session.Mount, new string('a', 1200));
            Dispatcher.UIThread.RunJobs();
            Assert.True(chips.IsVisible);
            Assert.Equal(["shipyard context · 1.2k"], chips.ChipTexts);

            apps.Ui.SetModelContext(session.Mount, new string('b', 5000)); // last write wins, cut to 4,000
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["shipyard context · 4k"], chips.ChipTexts);
            var entry = Assert.Single(m.Runtime.AppContexts.Entries);
            Assert.True(entry.Truncated);
            Assert.Equal(McpAppModelContext.MaxCharacters, entry.Text.Length);
            Assert.Equal("Cut to 4,000 of 5,000 characters", entry.TruncatedNote);
            var chip = chips.ChipOf("shipyard")!;
            Assert.Contains("Untrusted", Avalonia.Automation.AutomationProperties.GetName(Find<Button>(chip).Concat(chip is Button b ? [b] : []).First()), StringComparison.Ordinal);

            m.Runtime.AppContexts.Remove("shipyard");
            Dispatcher.UIThread.RunJobs();
            Assert.False(chips.IsVisible);
            Assert.Empty(chips.ChipTexts);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void An_app_message_reaches_the_model_wrapped_as_untrusted_never_as_a_command_and_spends_the_context_chips() => ThreadTimelineRig.Run(headless, m =>
    {
        var prompt = typeof(ThreadRuntime).GetMethod("AppPrompt", BindingFlags.Instance | BindingFlags.NonPublic)!;
        string Prompt(string? app, string text, bool command) => (string)prompt.Invoke(m.Runtime, [app, text, command])!;

        var wrapped = Prompt("shipyard", "/compact the thread", false);
        Assert.Equal(McpAppUntrusted.WrapMessage("shipyard", "/compact the thread"), wrapped);
        Assert.False(wrapped.StartsWith('/'));

        m.Runtime.AppContexts.Set("shipyard", "Release board", "3 releases waiting");
        var command = Prompt(null, "/review", true);
        Assert.Equal("/review", command); // a slash command leaves the chips for the next message
        Assert.Single(m.Runtime.AppContexts.Entries);

        var sent = Prompt(null, "Which one is safe to promote?", false);
        Assert.StartsWith(McpAppUntrusted.WrapContexts(
            [new McpAppContextEntry("shipyard", "Release board", "3 releases waiting", false, 18)]), sent, StringComparison.Ordinal);
        Assert.EndsWith("Which one is safe to promote?", sent, StringComparison.Ordinal);
        Assert.Empty(m.Runtime.AppContexts.Entries); // spent with the message

        Assert.Equal("The message is empty.", m.Runtime.PostAppMessage("shipyard", "  "));
        typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Phase))!.SetValue(m.Runtime, RuntimePhase.Running);
        Assert.Equal(ThreadRuntime.AppMessageBusy, m.Runtime.PostAppMessage("shipyard", "Promote it"));
        typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Phase))!.SetValue(m.Runtime, RuntimePhase.Idle);
    });

    [Fact]
    public void An_apps_card_is_in_the_chats_approval_stack_answered_only_by_the_user_and_outlives_the_turns_end() => ThreadTimelineRig.Run(headless, m =>
    {
        var session = new RecordingSession();
        typeof(ThreadRuntime).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(m.Runtime, session);
        var allow = new ApprovalRequest.Option("allow", "Allow", ApprovalRequest.Option.Role.Approve);
        var notNow = new ApprovalRequest.Option("not-now", "Not now", ApprovalRequest.Option.Role.Decline);

        var (id, answer) = m.Runtime.AskForApp(ApprovalRequest.Kind.Permissions, McpAppUntrusted.MessagePermission("shipyard"), [allow, notNow]);
        var card = Assert.Single(m.Runtime.Approvals);
        Assert.Equal(id, card.Id);
        Assert.Equal("Let shipyard post messages in this chat?", card.Title);
        Assert.False(answer.IsCompleted);

        // The turn ends: the provider's cards go, the app's stays.
        var keep = (ValueList<ApprovalRequest>)typeof(ThreadRuntime).GetMethod("AppPromptsOnly", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(m.Runtime, null)!;
        Assert.Equal([id], keep.Select(item => item.Id).ToArray());

        m.Runtime.Resolve(card, allow);
        Assert.True(answer.IsCompletedSuccessfully);
        Assert.Same(allow, answer.Result);
        Assert.Null(session.Resolved); // the provider never hears of it
        Assert.Empty(m.Runtime.Approvals);

        // The app's page went away: its card leaves and answers as dismissed.
        var (second, pending) = m.Runtime.AskForApp(ApprovalRequest.Kind.Tool, "shipyard wants to run promote_release", [allow, notNow]);
        m.Runtime.WithdrawAppPrompts([second]);
        Assert.Empty(m.Runtime.Approvals);
        Assert.True(pending.IsCompletedSuccessfully);
        Assert.Null(pending.Result);
    });

    [Fact]
    public void The_hosts_cards_carry_the_designs_words_and_tools_ask_unless_the_chat_has_full_access() => ThreadTimelineRig.Run(headless, m =>
    {
        using var apps = new Apps(m.Runtime);
        m.SetEntries([Call("shipyard · list_releases", "{}", ToolCall.Status.Completed)]);
        Pump(m);
        var mount = Assert.Single(Find<McpAppRow>(m.View)).Session!.Mount;

        var tool = apps.Ui.AskToolAsync(mount, "promote_release", "Release board");
        Dispatcher.UIThread.RunJobs();
        var card = Assert.Single(m.Runtime.Approvals);
        Assert.Equal("shipyard wants to run promote_release", card.Title);
        Assert.Equal("From its Release board, in this chat.", card.Detail);
        Assert.Equal(["Allow once", "Always for shipyard", "Deny"], card.Options.Select(option => option.Title).ToArray());
        m.Runtime.Resolve(card, card.Options[1]);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(McpAppToolDecision.AlwaysForServer, tool.GetAwaiter().GetResult());

        var request = new McpAppDomainRequest("shipyard", "Release board", ["api.shipyard.dev"], [], [], "f1");
        var domains = apps.Ui.AskDomainsAsync(mount, request);
        Dispatcher.UIThread.RunJobs();
        card = Assert.Single(m.Runtime.Approvals);
        Assert.Equal("shipyard's Release board wants to reach: api.shipyard.dev", card.Title);
        Assert.Equal(["Allow for shipyard", "Not now"], card.Options.Select(option => option.Title).ToArray()); // no wildcards: no broad access
        m.Runtime.Resolve(card, card.Options[1]);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(McpAppDomainDecision.NotNow, domains.GetAwaiter().GetResult());

        Assert.True(apps.Ui.ToolsNeedApproval(m.Runtime.ThreadID));
        Assert.True(apps.Ui.ToolsNeedApproval(Guid.NewGuid())); // a chat that is not open never runs a tool unasked
    });
}
