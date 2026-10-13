// PORT-ONLY: Dextra AS-20 (DESIGN.md rev 5, 12.7; ED-NEW-dextra-as20-10). No Swift twin.
// The controller's purchases: the desk behind prepare_purchase, and the one door the Decisions tab answers through, so Pay on a
// purchase card goes to the desk (Windows Hello, then the fill) and never settles in the queue alone.
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Assistant;

public sealed partial class AssistantController
{
    /// <summary>The purchase desk (AS-20), when purchases are part of this start: prepare_purchase, the cards, the switch, the card, the limits.</summary>
    public PurchaseDesk? Purchases { get; private set; }

    private void StartPurchases()
    {
        if (o.Purchases is not { } setup) return;
        Purchases = new PurchaseDesk(new PurchaseDeskOptions
        {
            Setup = setup,
            Queue = Queue,
            Audit = o.Audit,
            Deliver = Deliver,
            Context = PolicyContext,
            Profiles = o.Profiles,
            AgentName = thread => o.Thread(thread)?.Title,
            Time = o.Time,
            Fault = Runner.ReportFault,
        });
    }

    /// <summary>The owner's answer from the Decisions tab: Pay on a purchase card goes to the desk, every other answer to the queue.</summary>
    public string? AnswerCard(Guid id, DecisionAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        return Purchases is { } desk && desk.Takes(id, answer) ? desk.Start(id) : Queue.Answer(id, answer);
    }
}
