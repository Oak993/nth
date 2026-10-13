using Marrow.App.Assistant;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Support;
using Marrow.Services.Assistant;
using Marrow.Services.Port;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-20 (DESIGN.md rev 5, 12.7, 15.5; ED-NEW-dextra-as20-10): the controller's seam. Without a purchase setup
// prepare_purchase says it is not part of this build; with one it raises the desk's card, and Pay from the Decisions tab goes
// through the controller's AnswerCard to the desk (Windows Hello, then the fill), never straight into the queue.
[Trait("Feature", "AS-20")]
[Trait("Decision", "ED-NEW-dextra-as20-10")]
public sealed class PurchaseControllerTests : IDisposable
{
    private readonly AssistantTempRoot temp = new();
    private readonly FakeMissionAgents agents = new();
    private readonly PurchaseRig pages = new();
    private readonly FakeHelloGate hello = new();

    public void Dispose()
    {
        pages.Dispose();
        temp.Dispose();
    }

    private AssistantController Controller(PurchaseSetup? purchases) => new(new AssistantControllerOptions
    {
        Agents = agents,
        Thread = _ => null,
        Projects = () => [],
        Profiles = () => [new AssistantProfile("dextra", "Dextra")],
        Jobs = new AgentJobs(new FakeAgentJobFactory()),
        Workspaces = new FakeWorkspaces(Path.Combine(temp.Folder, "worktrees")),
        Audit = _ => { },
        Memory = new FakeMemory(FakeMemory.Plenty),
        Purchases = purchases,
    });

    private PurchaseSetup Setup() => new()
    {
        Book = new PurchaseBook(() => null, _ => { }),
        Hello = hello,
        Vault = guard => new CardVault(new SecretStore(new InMemoryCredentialBackend()), new FakeCardSealer(), guard, "nth.dextra.card.controller"),
        Pages = pages,
        AllowLoopbackHttp = true,
    };

    private static AssistantToolCall Call(Guid thread) =>
        new(new AssistantChat(thread, "dextra", null, AssistantChatRole.Home, ProviderKind.Claude, "model-a"), AssistantToolCatalog.Find("prepare_purchase")!,
            JSONValue.Parse(PurchaseRig.Arguments())!, new Classification(ActionClass.Agents, "puts a purchase card in front of the owner"),
            new PolicyDecision(Verdict.Allow, PolicyRule.AlwaysConfirmed, "Allowed"), AssistantRoute.Mcp);

    [Fact]
    public async Task Without_a_purchase_setup_the_tool_is_not_part_of_this_build()
    {
        using var controller = Controller(null);
        Assert.Null(controller.Purchases);
        var result = await controller.Run(Call(Guid.NewGuid()), CancellationToken.None);
        Assert.True(result.IsError);
        Assert.Equal("Not run: prepare_purchase is not part of this build of Dextra yet.", result.Text);
    }

    [Fact]
    public async Task Pay_from_the_decisions_tab_goes_through_the_desk_and_windows_hello_and_never_settles_in_the_queue_alone()
    {
        using var controller = Controller(Setup());
        var desk = controller.Purchases!;
        using (var entry = new CardEntry())
        {
            foreach (var c in PurchaseRig.Number) entry.Type(CardPart.Number, c);
            foreach (var c in PurchaseRig.Expiry) entry.Type(CardPart.Expiry, c);
            foreach (var c in PurchaseRig.Code) entry.Type(CardPart.Code, c);
            Assert.Equal(CardVaultOutcome.Done, (await desk.SaveCard(entry)).Outcome);
        }
        Assert.Null(await desk.TurnOn());
        var thread = Guid.NewGuid();
        var page = pages.SameOrigin(thread: thread);

        var result = await controller.Run(Call(thread), CancellationToken.None);
        Assert.False(result.IsError);
        var card = Guid.Parse(result.DecisionId!);
        Assert.Equal(PurchaseWords.PayOnCard, controller.Queue.Answer(card, DecisionAnswer.DoIt));   // the queue alone does not pay

        var tab = new QueueDecisions(controller.Queue, controller.AnswerCard);
        Assert.Null(tab.Answer(card, DecisionAnswer.DoIt));
        Assert.True(SpinWait.SpinUntil(() => controller.Queue.Find(card)!.State == DecisionState.Answered, TimeSpan.FromSeconds(10)));
        Assert.Equal(["save the card", "turn purchases on", "pay $46.80 to shop.localhost"], hello.Reasons);
        Assert.Equal(PurchaseRig.Number, page.Filled["number"]);
        Assert.Equal(PurchaseRowState.Paid, Assert.Single(desk.State.History).State);
        Assert.Contains(agents.Refused, r => r.Thread == thread && r.Text.StartsWith("Paid $46.80 at shop.localhost after Windows Hello.", StringComparison.Ordinal));
    }
}
