using Marrow.App.Assistant;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

// The decision toasts over a real DecisionQueue, a fake channel and a manual clock (AS-9). Nothing runs on a thread of its own: the
// toast's timer fires when the test advances the clock, and its main-thread hop runs inline.
internal sealed class DecisionToastRig : IDisposable
{
    public DecisionToastRig()
    {
        Queue = new DecisionQueue(Clock);
        Toasts = new DecisionToasts(Queue, Channel, new DecisionToastOptions
        {
            NameOf = id => id == "store" ? "Store" : "Dextra",
            HomeOnScreen = () => HomeOnScreen,
            OnMain = work => work(),
            Time = Clock,
        });
        Toasts.Faulted += Faults.Add;
    }

    public ManualTimeProvider Clock { get; } = new();

    public DecisionQueue Queue { get; }

    public FakeNotificationChannel Channel { get; } = new();

    public DecisionToasts Toasts { get; }

    /// <summary>The Assistant home is in front (the toast is not shown).</summary>
    public bool HomeOnScreen { get; set; }

    public List<string> Faults { get; } = [];

    public List<(DecisionItem Card, DecisionAnswer Answer)> Answers { get; } = [];

    /// <summary>A reversible provider approval of a mission's chat, as the runner raises one.</summary>
    public static DecisionItem Approval(string title = "Run npm test?", string assistant = "store", ActionClass actionClass = ActionClass.Run, string request = "") =>
        new(Guid.NewGuid(), DecisionKind.Approval, DecisionRules.Weigh(actionClass), title)
        {
            Inputs = new ValueList<DecisionInput>([new DecisionInput("Command", title)]),
            AssistantId = assistant,
            MissionId = Guid.Parse("60000000-0000-4000-8000-000000000006"),
            ThreadId = Guid.NewGuid(),
            Class = actionClass,
            RequestId = request.Length > 0 ? request : Guid.NewGuid().ToString("N"),
        };

    public DecisionItem Raise(DecisionItem card) => Queue.Raise(card, (item, answer) => Answers.Add((item, answer)));

    public void Pass(TimeSpan by) => Clock.Advance(by);

    public void Dispose() => Toasts.Dispose();
}
