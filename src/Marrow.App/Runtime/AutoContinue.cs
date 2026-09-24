using Marrow.Core.Models;
using Marrow.Core.Port;

// Source: DroppyCode/App/Runtime/AutoContinue.swift (B2 1fbf81e), :1-159. Skeleton (c100 group 1): the constants
// (:12, :15), the LimitOutcome cases (:22-26) and the observed stored properties (:18, :28) are complete; the init
// (:38-40, which holds `app` weakly), the private wait state (:30-36) and every method are left to the implementing
// unit. LimitHandoff (:80) is NOT-PORTED (CLOUD_WORKER hard fences); handOff's use of it follows that evidence.

namespace Marrow.App.Runtime;

// :8-159. @MainActor @Observable -> MainActorModel (R-OBS-6).
public sealed class AutoContinue : MainActorModel
{
    // :12
    public const string ContinueMessage = "The usage limit has reset. Continue exactly where you left off and finish the task.";

    // :15. TimeInterval is double seconds.
    public const double Margin = 60;

    // :22-26 (R-ENUM-2). HandedOff's unlabeled UUID has no binding in the source; it is named Value.
    public abstract record LimitOutcome
    {
        private LimitOutcome() { }
        public sealed record Cancelled : LimitOutcome;              // :23
        public sealed record HandedOff(Guid Value) : LimitOutcome;  // :24
        public sealed record Resumed : LimitOutcome;                // :25
    }

    private ValueMap<Guid, DateTimeOffset> resumesAt = ValueMap<Guid, DateTimeOffset>.Empty;
    private ValueMap<Guid, LimitOutcome> outcomes = ValueMap<Guid, LimitOutcome>.Empty;

    // :18
    public ValueMap<Guid, DateTimeOffset> ResumesAt { get => Read(resumesAt); private set => Set(ref resumesAt, value); }

    // :28
    public ValueMap<Guid, LimitOutcome> Outcomes { get => Read(outcomes); private set => Set(ref outcomes, value); }

    // :38-40
    public AutoContinue(AppModel app) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/AutoContinue.swift:38");

    // :43-47. Swift: noteLimit(_:resetsAt:).
    public void NoteLimit(Guid threadID, DateTimeOffset? resetsAt) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/AutoContinue.swift:43");

    // :51-64. Swift: turnFinished(_:status:continues:).
    public bool TurnFinished(Guid threadID, TurnStatus status, bool continues) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/AutoContinue.swift:51");

    // :67-73. Swift: cancel(_:).
    public void Cancel(Guid threadID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/AutoContinue.swift:67");

    // :78-85. Swift: handOff(_:).
    public void HandOff(Guid threadID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/AutoContinue.swift:78");

    // :158
    public static string Format(DateTimeOffset date) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Runtime/AutoContinue.swift:158");
}
