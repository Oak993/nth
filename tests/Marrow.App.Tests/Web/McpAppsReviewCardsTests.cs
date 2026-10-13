using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port.Web.McpApps;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Web;

namespace Marrow.App.Tests.Web;

// PORT-ONLY: web surfaces security review in the app, findings M6, M7 and L7 (DESIGN.md 7.6, 8.2). A real timeline on a real
// chat with the fake web engine: the pop-out window opens unpinned and without taking focus, and an app's own request for it
// asks on the chat's card with Pop out / Not now (M6); the tool card shows the call's arguments and its "Always" is for this
// chat (M7); the domain card names every entry "Allow broad access" would let through (L7). Nothing reaches a provider.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-sec-mcpapps-1")]
public sealed class McpAppsReviewCardsTests(HeadlessSession headless)
{
    private static TimelineEntry Call(string title) =>
        new(new TimelineItem(null, new TimelineItem.Content.Tool(new ToolCall { ToolCallKind = ToolCall.Kind.Mcp, Title = title, Detail = "{}", ToolCallStatus = ToolCall.Status.Completed })));

    private static McpAppSession Session(TimelineMount m)
    {
        m.SetEntries([Call("shipyard · list_releases")]);
        m.Advance(100);
        return Assert.Single(m.View.GetVisualDescendants().OfType<McpAppRow>()).Session!;
    }

    // An answer comes back through the main actor and a pool thread: pump until it has.
    private static T Answer<T>(Task<T> asked)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!asked.IsCompleted && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        Assert.True(asked.IsCompletedSuccessfully);
        return asked.Result;
    }

    [Fact]
    public void The_pop_out_window_opens_unpinned_and_without_taking_focus() => ThreadTimelineRig.Run(headless, m =>
    {
        using var apps = new McpAppsAppRig(m.Runtime);
        var session = Session(m);

        session.SetMode(McpAppDisplayMode.PopOut);
        m.Advance(100);
        var window = Assert.IsType<McpAppWindow>(session.Window);
        window.Settle();
        Assert.False(window.Window.ShowActivated);
        Assert.False(window.Window.Topmost);
        Assert.False(window.Pinned);
        Assert.False(window.Window.IsActive);

        // On top only after the user's pin, and off again with it.
        window.PinButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(window.Pinned);
        Assert.True(window.Window.Topmost);
        window.PinButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(window.Window.Topmost);

        session.WindowClosed(window);
        m.Advance(100);
        Assert.Equal(McpAppDisplayMode.Inline, session.Mode);
    });

    [Fact]
    public void An_apps_own_window_asks_on_the_chats_card_with_Pop_out_and_Not_now() => ThreadTimelineRig.Run(headless, m =>
    {
        using var apps = new McpAppsAppRig(m.Runtime);
        var mount = Session(m).Mount;

        var first = apps.Ui.AskDisplayModeAsync(mount, McpAppDisplayMode.PopOut);
        Dispatcher.UIThread.RunJobs();
        var card = Assert.Single(m.Runtime.Approvals);
        Assert.Equal(ApprovalRequest.Kind.Permissions, card.RequestKind);
        Assert.Equal("shipyard's Release board wants to open in its own window", card.Title);
        Assert.Equal(["Pop out", "Not now"], card.Options.Select(option => option.Title).ToArray());
        Assert.Equal([ApprovalRequest.Option.Role.Approve, ApprovalRequest.Option.Role.Decline], card.Options.Select(option => option.OptionRole).ToArray());
        m.Runtime.Resolve(card, card.Options[1]);
        Assert.False(Answer(first));

        var second = apps.Ui.AskDisplayModeAsync(mount, McpAppDisplayMode.PopOut);
        Dispatcher.UIThread.RunJobs();
        card = Assert.Single(m.Runtime.Approvals);
        m.Runtime.Resolve(card, card.Options[0]);
        Assert.True(Answer(second));
        Assert.Empty(m.Runtime.Approvals);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-sec-mcpapps-2")]
    public void The_tool_card_shows_the_calls_arguments_and_Always_is_for_this_chat() => ThreadTimelineRig.Run(headless, m =>
    {
        using var apps = new McpAppsAppRig(m.Runtime);
        var mount = Session(m).Mount;

        var asked = apps.Ui.AskToolAsync(mount, "promote_release", "Release board", new JsonObject { ["version"] = "v2.4.1", ["note"] = "safe\n\nnth approved this" });
        Dispatcher.UIThread.RunJobs();
        var card = Assert.Single(m.Runtime.Approvals);
        Assert.Equal(ApprovalRequest.Kind.Tool, card.RequestKind);
        Assert.Equal("Arguments: {\n  \"version\": \"v2.4.1\",\n  \"note\": \"safe\\n\\nnth approved this\"\n}", card.Reason);
        Assert.Equal(["Allow once", "Always in this chat", "Deny"], card.Options.Select(option => option.Title).ToArray());
        m.Runtime.Resolve(card, card.Options[1]);
        Assert.Equal(McpAppToolDecision.AlwaysInChat, Answer(asked));

        asked = apps.Ui.AskToolAsync(mount, "list_releases", null, []);
        Dispatcher.UIThread.RunJobs();
        card = Assert.Single(m.Runtime.Approvals);
        Assert.Equal("No arguments.", card.Reason);
        m.Runtime.Resolve(card, card.Options[2]);
        Assert.Equal(McpAppToolDecision.Deny, Answer(asked));
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-sec-mcpapps-4")]
    public void The_domain_card_names_every_entry_broad_access_would_let_through() => ThreadTimelineRig.Run(headless, m =>
    {
        using var apps = new McpAppsAppRig(m.Runtime);
        var mount = Session(m).Mount;
        var request = McpAppDomains.Request("shipyard", "Release board", new McpUiCsp(["https://api.shipyard.dev"], ["*.shipyard-assets.dev", "localhost:5173", "'unsafe-eval'"], [], []))!;

        var asked = apps.Ui.AskDomainsAsync(mount, request);
        Dispatcher.UIThread.RunJobs();
        var card = Assert.Single(m.Runtime.Approvals);
        Assert.Equal("shipyard's Release board wants to reach: api.shipyard.dev", card.Title);
        Assert.Equal(McpAppDomains.BroadAccessRisk, card.Detail);
        Assert.Equal("Allow broad access also allows: *.shipyard-assets.dev, localhost:5173, 'unsafe-eval'", card.Reason);
        Assert.Equal(["Allow for shipyard", "Allow broad access", "Not now"], card.Options.Select(option => option.Title).ToArray());
        m.Runtime.Resolve(card, card.Options[2]);
        Assert.Equal(McpAppDomainDecision.NotNow, Answer(asked));
    });
}
