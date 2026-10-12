// PORT-ONLY: Dextra (DESIGN.md rev 5, 11.5; unit AS-13; ED-NEW-dextra-as13-9). No Swift twin.
// The seam between the mission runner and the usage reserve. The runner asks before every turn it sends and tells the gate
// when turns start and end; the gate (ReserveGuard) answers yes, or pauses the mission at the turn boundary, raises the
// owner's card and answers no. A runner with no gate sends as before.
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

/// <summary>Why the runner is about to send a turn (the activity log's word for it).</summary>
public static class ReserveTurns
{
    public const string Start = "start";
    public const string Review = "review";
    public const string Redirect = "redirect";
    public const string Resume = "resume";
    public const string Head = "head";
}

/// <summary>
/// What a mission carries from its plan into the reserve (11.4, 11.5): the rules the owner spoke for it, the plan he saw and the
/// steps it was made from, so spend is measured against the estimate and the after-report against the plan.
/// </summary>
public sealed record ReserveStart(IReadOnlyList<ReserveRule> TaskRules, ReservePlan? Plan, IReadOnlyList<PlanStep> Steps);

/// <summary>The reserve's gate before a turn (11.5). Called on the app's main thread.</summary>
public interface IReserveGate
{
    /// <summary>A mission was made (before its lead is launched): its task rules and its plan, when it has them, bind from now on.</summary>
    void MissionBegan(MissionRecord mission, ReserveStart? start);

    /// <summary>
    /// True when the turn may be sent now. False: the mission is held (paused for its reserve, or the owner's pause stands) and
    /// the runner sends nothing; the words wait with the mission. <paramref name="thread"/> is the chat the turn goes to, or null
    /// when the chat is yet to be made.
    /// </summary>
    bool BeforeTurn(MissionRecord mission, Guid? thread, ProviderKind provider, string? model, string purpose);

    /// <summary>A turn began on the chat: its p90 counts in every margin on its account until it ends.</summary>
    void TurnStarted(Guid mission, Guid thread, string? model);

    void TurnEnded(Guid thread);

    /// <summary>The mission was stopped: nothing it waited for (a reset) is waited for any more.</summary>
    void MissionStopped(Guid mission);

    /// <summary>The mission is over: its after-report is made and its bookkeeping dropped.</summary>
    void MissionEnded(Guid mission);
}
