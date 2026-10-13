using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Support;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-9 (DESIGN.md rev 5, 12.4 "a card the owner may raise himself", 13.1; ED-NEW-dextra-as9-1): a call the policy asks about is a card that needs
// the owner; a call refused because its chat read untrusted content is a card he may raise himself, which waits in the queue and makes no toast.
[Trait("Feature", "AS-9")]
[Trait("Decision", "ED-NEW-dextra-as9-1")]
public sealed class DecisionPriorityTests : IDisposable
{
    private readonly AssistantTempRoot temp = new();
    private readonly FakeMissionAgents agents = new();
    private readonly Dictionary<Guid, ChatThread> chats = [];
    private readonly MissionProject project;
    private readonly FakeWorkspaces workspaces;

    public DecisionPriorityTests()
    {
        project = new MissionProject(MissionRig.Project, "App", Path.Combine(temp.Folder, "owner"));
        workspaces = new FakeWorkspaces(Path.Combine(temp.Folder, "worktrees"));
    }

    public void Dispose() => temp.Dispose();

    private AssistantController Controller() => new(new AssistantControllerOptions
    {
        Agents = agents,
        Thread = id => chats.GetValueOrDefault(id),
        Projects = () => [project],
        Profiles = () => [new AssistantProfile("dextra", "Dextra")],
        Jobs = new AgentJobs(new FakeAgentJobFactory()),
        Workspaces = workspaces,
        Audit = _ => { },
        Memory = new FakeMemory(FakeMemory.Plenty),
    });

    private static AssistantToolCall Send(Guid thread) =>
        new(new AssistantChat(thread, "dextra", null, AssistantChatRole.Home, ProviderKind.Claude, "model-a"), AssistantToolCatalog.Find("deliver")!,
            JSONValue.Parse("""{"text":"The weekly update"}""")!, new Classification(ActionClass.Outward, "the tool's class"),
            new PolicyDecision(Verdict.Ask, PolicyRule.AlwaysConfirmed, "Asks: the owner's call"), AssistantRoute.Mcp);

    [Fact]
    public async Task A_call_the_policy_asks_about_is_a_card_that_needs_the_owner()
    {
        using var controller = Controller();
        _ = await controller.Ask(Send(Guid.NewGuid()), CancellationToken.None);
        var card = Assert.Single(controller.Queue.Waiting);
        Assert.Equal(DecisionPriority.NeedsYou, card.Priority);
        Assert.NotNull(DecisionNotices.For(card, "Dextra"));
    }

    [Fact]
    public void A_call_refused_for_a_tainted_chat_is_a_card_the_owner_may_raise_himself_and_makes_no_toast()
    {
        using var controller = Controller();
        controller.Offer(Send(Guid.NewGuid()));
        var card = Assert.Single(controller.Queue.Waiting);
        Assert.Equal(DecisionPriority.Offered, card.Priority);
        Assert.Null(DecisionNotices.For(card, "Dextra"));
        Assert.False(DecisionNotices.NeedsYou(card));
    }
}
