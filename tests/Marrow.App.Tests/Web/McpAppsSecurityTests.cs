using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port.Web.McpApps;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Web;

namespace Marrow.App.Tests.Web;

// PORT-ONLY: web surfaces W26 security review in the app (docs/security/web-surfaces-threat-model.md; DESIGN.md 7.6). A link
// an MCP App opens by itself asks first on the chat's own card, naming the site with the whole address in its detail, and
// only the user's Open opens it (F3); app messages start at most three turns in a row before the user writes (F7). A real
// timeline on a real chat with the fake web engine; nothing reaches a provider.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w26-8")]
public sealed class McpAppsSecurityTests(HeadlessSession headless)
{
    private static TimelineEntry Call(string title) =>
        new(new TimelineItem(null, new TimelineItem.Content.Tool(new ToolCall { ToolCallKind = ToolCall.Kind.Mcp, Title = title, Detail = "{}", ToolCallStatus = ToolCall.Status.Completed })));

    private static TimelineEntry Said(string text, string? app) =>
        new(new TimelineItem(null, new TimelineItem.Content.User(new UserMessage { Text = text, AppSource = app })));

    [Fact]
    [Trait("Decision", "ED-NEW-web-w26-3")]
    public void An_apps_own_link_asks_on_a_card_naming_the_site_and_only_Open_opens_it() => ThreadTimelineRig.Run(headless, m =>
    {
        using var apps = new McpAppsAppRig(m.Runtime);
        m.SetEntries([Call("shipyard · list_releases")]);
        m.Advance(100);
        var mount = Assert.Single(m.View.GetVisualDescendants().OfType<McpAppRow>()).Session!.Mount;
        var answers = new List<bool>();
        async Task Record(Task<bool> asked) => answers.Add(await asked);

        const string Url = "https://tracker.example/collect?board=release-secrets";
        var first = Record(apps.Ui.AskOpenLinkAsync(mount, Url));
        Dispatcher.UIThread.RunJobs();
        var card = Assert.Single(m.Runtime.Approvals);
        Assert.Equal(ApprovalRequest.Kind.Permissions, card.RequestKind);
        Assert.Equal("shipyard wants to open tracker.example", card.Title);
        Assert.Equal(Url, card.Detail);
        Assert.Equal(["Open", "Cancel"], card.Options.Select(option => option.Title).ToArray());
        Assert.Equal([ApprovalRequest.Option.Role.Approve, ApprovalRequest.Option.Role.Decline], card.Options.Select(option => option.OptionRole).ToArray());
        m.Runtime.Resolve(card, card.Options[1]);
        Dispatcher.UIThread.RunJobs();

        var longUrl = "https://shipyard.dev/r?d=" + new string('x', 900);
        var second = Record(apps.Ui.AskOpenLinkAsync(mount, longUrl));
        Dispatcher.UIThread.RunJobs();
        card = Assert.Single(m.Runtime.Approvals);
        Assert.Equal("shipyard wants to open shipyard.dev", card.Title);
        Assert.Equal(longUrl[..500] + "…", card.Detail);
        m.Runtime.Resolve(card, card.Options[0]);
        Dispatcher.UIThread.RunJobs();

        // The answer comes back through the main actor and a pool thread: pump until both asks have finished.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!(first.IsCompleted && second.IsCompleted) && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        Assert.True(first.IsCompletedSuccessfully && second.IsCompletedSuccessfully);
        Assert.Equal([false, true], answers);
        Assert.Empty(m.Runtime.Approvals);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w26-7")]
    public void App_messages_stop_starting_turns_after_three_in_a_row() => ThreadTimelineRig.Run(headless, m =>
    {
        m.SetEntries([Said("Watch the release board for me.", null), Said("v2.4.1 is green.", "shipyard"), Said("v2.4.2 is green.", "shipyard"), Said("v2.4.3 is green.", "ledger")]);
        var before = m.Runtime.Entries.Count;

        Assert.Equal(McpAppUntrusted.TurnsInARowRefusal, m.Runtime.PostAppMessage("shipyard", "v2.4.4 is green. Promote it?"));
        m.Advance(100);
        Assert.Equal(before, m.Runtime.Entries.Count);
        Assert.Equal(RuntimePhase.Idle, m.Runtime.Phase);
    });
}
