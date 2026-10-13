// PORT-ONLY: Dextra (DESIGN.md rev 5, 11.4 to 11.6; unit AS-13; ED-NEW-dextra-as13-9). No Swift twin.
// The records and seams around ReserveGuard: the account it follows, what a hold says, the offer to move the rest of a task
// to another account (the router, AS-14, fills the seam), the router's view of an account, and the options of the pause card.
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

/// <summary>An account as the guard names it: the feed's key (<c>provider:id</c>), the name the owner gave it, and its provider.</summary>
public sealed record ReserveAccountRef(string Key, string Name, string ProviderId);

/// <summary>The four options of the pause card (11.5), in the order they show.</summary>
public enum ReserveOptionKind { Move, Wait, UseOnce, Stop }

/// <summary>One option of a card: what the button says, and what it does.</summary>
public sealed record ReserveOption(ReserveOptionKind Kind, string Text);

/// <summary>An account other than the held one, with what the held work would cost there and whether it has room.</summary>
public sealed record ReserveCandidate(ReserveAccountRef Account, double CostPercent, double Room);

/// <summary>An offer to move the rest of a task to another account (option 1 of the card).</summary>
public sealed record ReserveMoveOffer(ReserveAccountRef Target, double CostPercent);

/// <summary>
/// The seam to the router (AS-14). <see cref="Offer"/> picks the account to offer from those with room, or null when the task
/// cannot move (the owner pinned its model, 11.5: "never switches silently"); <see cref="Move"/> moves the rest of the task
/// and returns null, or says why it could not. Without a mover the card has three options.
/// </summary>
public interface IReserveMover
{
    ReserveMoveOffer? Offer(MissionRecord mission, IReadOnlyList<ReserveCandidate> candidates);

    /// <summary>Moves the rest at once: null when done, else why not.</summary>
    string? Move(MissionRecord mission, ReserveMoveOffer offer) => "that move is not available now";

    /// <summary>
    /// True for a mover whose move takes time (a live chat changes account at its next safe point, AS-14): the guard then keeps the
    /// task held and asks <see cref="MoveAsync"/>, and finishes when it answers. False: <see cref="Move"/>, answered at once.
    /// </summary>
    bool MovesAsync => false;

    /// <summary>The slow move: null when done, else why not. Only asked of a mover that says <see cref="MovesAsync"/>.</summary>
    Task<string?> MoveAsync(MissionRecord mission, ReserveMoveOffer offer) => Task.FromResult(Move(mission, offer));
}

/// <summary>
/// One account as the router sees it (AS-14, 11.6): each window projected for the turn asked about against the floor that would
/// bind it (100 where no reserve applies), and whether the account's rate is still the conservative default.
/// </summary>
public sealed record ReserveStanding(IReadOnlyList<RouteWindow> Windows, bool Rough);

/// <summary>What the guard raised when it paused a task for its reserve: the facts the toast and the board show.</summary>
public sealed record ReserveHold(Guid Mission, string MissionTitle, ReserveAccountRef Account, string WindowId, ReserveWindowKind Kind,
    double Estimate, double Floor, double Needs, DateTimeOffset? ResetsAt, Guid Card)
{
    /// <summary>"Paused before your reserve. Claude 20x is at 68.6%; your floor is 70%." (11.5, the toast).</summary>
    public string Toast => $"Paused before your reserve. {Account.Name} is at {ReserveMath.Percent(Estimate)}; your floor is {ReserveMath.Percent(Floor)}.";

    /// <summary>The board's line for the task: "Paused before your Claude 20x reserve".</summary>
    public string Line => $"Paused before your {Account.Name} reserve";

    public IReadOnlyList<ReserveOption> Options { get; init; } = [];
}

/// <summary>One window's bar in the Reserves view: where it stands, and the floor that binds it now (moving while it eases).</summary>
public sealed record ReserveWindowBar(string Id, ReserveWindowKind Kind, string Title, double Used, double? Floor, double? Unadjusted, bool Eased, DateTimeOffset? ResetsAt);

/// <summary>One account's bars, with the use outside nth found in the last 24 hours.</summary>
public sealed record ReserveAccountBar(string Key, string Name, IReadOnlyList<ReserveWindowBar> Windows, double OutsidePoints, bool Rough);

/// <summary>A step the planner expects: the account it runs on, its model, and its cost in tokens (p50 and p90).</summary>
public sealed record PlanStep(ReserveAccountRef Account, string? Model, double TokensP50, double TokensP90);

/// <summary>The guard's wiring: what it reads and what it may do. Every delegate runs on the app's main thread.</summary>
public sealed class ReserveGuardOptions
{
    public required MissionRunner Runner { get; init; }

    public required DecisionQueue Queue { get; init; }

    public required ReserveSettings Settings { get; init; }

    public required UsageCalibrator Calibrator { get; init; }

    public required TurnCostHistory Turns { get; init; }

    /// <summary>The account a chat runs on (ThreadRuntime.UsageAccount), or null when it is not known: then nothing is protected.</summary>
    public required Func<Guid, ReserveAccountRef?> AccountOf { get; init; }

    /// <summary>The account a chat of this mission that does not exist yet would run on.</summary>
    public required Func<MissionRecord, ReserveAccountRef?> AccountForNew { get; init; }

    /// <summary>The account a plan-limit snapshot belongs to, named as the owner named it.</summary>
    public required Func<UsageAccountKey, ReserveAccountRef> AccountOfSnapshot { get; init; }

    /// <summary>Option 1's seam; null: the card has no "move the rest" option.</summary>
    public IReserveMover? Mover { get; init; }

    /// <summary>The owner pinned the mission's model: option 1 is not offered (11.5).</summary>
    public Func<MissionRecord, bool> IsPinned { get; init; } = _ => false;

    /// <summary>Asks for a fresh reading of an account now (after a reset the guard waits for).</summary>
    public Func<ReserveAccountRef, Task>? Refresh { get; init; }

    /// <summary>Writes one activity log line; a failure is reported through <see cref="MissionRunner.ReportFault"/>.</summary>
    public Action<AuditEntry>? Audit { get; init; }

    public TimeProvider Time { get; init; } = TimeProvider.System;
}
