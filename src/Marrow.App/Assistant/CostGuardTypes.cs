// PORT-ONLY: Dextra (DESIGN.md rev 5, 13.2 "Caps"; unit AS-15; ED-NEW-dextra-as15-4). No Swift twin.
// The records around CostGuard: the options of a cap's card, what a hold says for the toast and the board, and what the guard is
// wired to read and do.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Port.Ignis;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

/// <summary>The three options of a cap's card (13.2): let it go over once, wait for tomorrow (a day's cap only), or stop.</summary>
public enum CapOptionKind { AllowOnce, WaitForTomorrow, Stop }

/// <summary>One option of a card: what the button says, and what it does.</summary>
public sealed record CapOption(CapOptionKind Kind, string Text);

/// <summary>What the guard raised when it paused a task at a cap: the facts the toast and the board show.</summary>
public sealed record CapHold(Guid Mission, string MissionTitle, CostCap Cap, CapReach Reach, Guid Card)
{
    /// <summary>"Paused at your cap. Claude 20x's 5-hour window: 5% used by this task; your cap is 5%."</summary>
    public string Toast => $"Paused at your cap. {CostCapWords.Standing(Cap, Reach)}";

    /// <summary>The board's line for the task: "Paused at your cap of 5% for this task".</summary>
    public string Line => CostGuard.LineOf(Cap);

    public IReadOnlyList<CapOption> Options { get; init; } = [];
}

/// <summary>The guard's wiring: what it reads and what it may do. Every delegate runs on the app's main thread.</summary>
public sealed class CostGuardOptions
{
    public required MissionRunner Runner { get; init; }

    public required DecisionQueue Queue { get; init; }

    /// <summary>The owner's standing caps (they live beside the reserve rules).</summary>
    public required ReserveSettings Settings { get; init; }

    /// <summary>What each assistant used today, per account and window, and the money spent on API keys.</summary>
    public required CostLedger Ledger { get; init; }

    public required UsageCalibrator Calibrator { get; init; }

    public required TurnCostHistory Turns { get; init; }

    /// <summary>The account a chat runs on, or null when it is not known: then nothing is counted for it.</summary>
    public required Func<Guid, ReserveAccountRef?> AccountOf { get; init; }

    /// <summary>The account a chat of this mission that does not exist yet would run on.</summary>
    public required Func<MissionRecord, ReserveAccountRef?> AccountForNew { get; init; }

    /// <summary>The usage windows an account reports, so a percent cap counts only those; null: the 5-hour and the weekly window.</summary>
    public Func<string, IReadOnlyList<ReserveWindowKind>>? WindowsOf { get; init; }

    /// <summary>
    /// The model's prices in dollars per million tokens, or null when they are not known (then
    /// <see cref="CostPricing.ConservativePerMillion"/> is counted). Asked only for API keys.
    /// </summary>
    public Func<ReserveAccountRef, string?, IgnisPrices?>? Prices { get; init; }

    /// <summary>
    /// The assistant a chat belongs to when it is not a mission's (the assistant's own home thread): its spend counts toward the
    /// assistant's day. Null: such a chat's spend is not counted.
    /// </summary>
    public Func<Guid, string?>? AssistantOf { get; init; }

    /// <summary>The name the owner knows an assistant by, for the cap's words; null: the id.</summary>
    public Func<string, string?>? AssistantName { get; init; }

    /// <summary>Writes one activity log line; a failure is reported through <see cref="MissionRunner.ReportFault"/>.</summary>
    public Action<AuditEntry>? Audit { get; init; }

    public TimeProvider Time { get; init; } = TimeProvider.System;
}
