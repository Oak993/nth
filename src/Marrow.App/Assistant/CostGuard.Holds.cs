// PORT-ONLY: Dextra (DESIGN.md rev 5, 13.2 "Caps", 11.5; unit AS-15; ED-NEW-dextra-as15-4). No Swift twin.
// The pause and its card. When a turn would pass a cap the guard pauses that mission at the turn boundary (the runner's Pause with the
// spending rule as its cause: a running turn is never cut), raises a card with its options, and acts on the answer: let the work go
// over the cap this once, wait until tomorrow (a day's cap only; the day's figures start again at local midnight), or stop and give
// the owner what there is.
using System.Globalization;
using Marrow.App.Runtime;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

public sealed partial class CostGuard
{
    private sealed class HoldState
    {
        public Guid Card { get; set; }
        public required CostCap Cap { get; init; }
        public required CapReach Reach { get; init; }
        public required ReserveAccountRef Account { get; init; }
        public string? Model { get; init; }
        public string Purpose { get; init; } = "";

        /// <summary>What "allow it once" adds to the limit: whole points of the window, or whole dollars.</summary>
        public double Units { get; init; }

        public IReadOnlyList<CapOption> Options { get; set; } = [];
        public bool Waiting { get; set; }
        public DateTimeOffset WaitUntil { get; set; }
        public ITimer? Timer { get; set; }
    }

    /// <summary>The words of the card's option that lets the work go over the cap this once.</summary>
    public static string AllowOnceText(CapReach reach, double units) =>
        reach.IsDollars ? $"Allow ${units.ToString("0", CultureInfo.InvariantCulture)} more once" : $"Allow {units.ToString("0", CultureInfo.InvariantCulture)}% more once";

    public const string WaitText = "Wait until tomorrow";

    public const string StopText = "Stop and give me what you have";

    /// <summary>The hold on a task, if a cap holds it.</summary>
    public CapHold? HoldOf(Guid mission)
    {
        lock (gate)
        {
            if (!missions.TryGetValue(mission, out var m) || m.Hold is not { Card: var card } hold || card == Guid.Empty) return null;
            return new CapHold(mission, m.Title, hold.Cap, hold.Reach, card) { Options = hold.Options };
        }
    }

    // ---- raising ----

    // again: the pause that stands is this guard's own (it asks once more after a wait), so it is not another hold's.
    private void RaiseHold(MissionRecord record, CostCap cap, CapReach reach, ReserveAccountRef account, string? model, string purpose, string? note, bool again = false)
    {
        var hold = new HoldState { Cap = cap, Reach = reach, Account = account, Model = model, Purpose = purpose, Units = CostCapMath.AllowanceNeeded(reach) };
        lock (gate)
        {
            if (disposed) return;
            var m = MissionOf(record);
            if (m.Hold is not null) return;
            m.Hold = hold;
        }
        // Pause first, outside the lock. Another hold (the reserve's) or the owner's own pause stands: there is no second card.
        if (o.Runner.Find(record.Id) is not { } current || (!again && current.Mission.PausedBy is not null) || o.Runner.PauseForReserve(record.Id) is not null)
        {
            lock (gate)
            {
                if (missions.TryGetValue(record.Id, out var m) && ReferenceEquals(m.Hold, hold)) m.Hold = null;
            }
            return;
        }
        var options = new List<CapOption> { new(CapOptionKind.AllowOnce, AllowOnceText(reach, hold.Units)) };
        if (cap.IsDaily) options.Add(new CapOption(CapOptionKind.WaitForTomorrow, WaitText));
        options.Add(new CapOption(CapOptionKind.Stop, StopText));
        hold.Options = options;

        var inputs = new List<DecisionInput>
        {
            new("Cap", CostCapWords.Title(cap, o.AssistantName)),
            new("Where", CostCapWords.Where(reach, cap.IsDaily)),
            new("Used", Amount(reach, reach.Used)),
            new("Your cap", Amount(reach, reach.Limit)),
        };
        if (reach.Needs > 0) inputs.Add(new("The next step needs about", Amount(reach, reach.Needs)));
        inputs.Add(new("Where it stands", CostCapWords.Facts(cap, reach)));
        var item = new DecisionItem(Guid.NewGuid(), DecisionKind.Cap, DecisionWeight.Question, $"{record.Mission.Title} would pass your cap")
        {
            Inputs = new ValueList<DecisionInput>(inputs),
            Why = CostCapWords.Why(cap, o.AssistantName),
            Rule = "Your cap",
            AssistantId = record.Mission.AssistantId,
            MissionId = record.Id,
            ThreadId = record.LeadThread,
            Agent = "Lead",
            Options = new ValueList<string>(options.Select(x => x.Text)),
            Note = note is null ? ReserveGuard.PausedNote : note + " " + ReserveGuard.PausedNote,
        };
        var raised = o.Queue.Raise(item, (card, answer) => Answered(record.Id, card, answer));
        lock (gate) hold.Card = raised.Id;
        o.Runner.NoteWaiting(record.Id, LineOf(cap));
        Log(record.Mission.AssistantId, record.Id, "cap_hold", $"{purpose}: {CostCapWords.Where(reach, cap.IsDaily)}, {Amount(reach, reach.Used)} of {Amount(reach, reach.Limit)}");
        Held?.Invoke(new CapHold(record.Id, record.Mission.Title, cap, reach, raised.Id) { Options = options });
        Changed?.Invoke();
    }

    private static string Amount(CapReach reach, double value) => reach.IsDollars ? CostCapWords.Money(value) : ReserveMath.Percent(value);

    // ---- the owner's answer ----

    private void Answered(Guid missionId, DecisionItem card, DecisionAnswer answer)
    {
        HoldState hold;
        MissionCost m;
        lock (gate)
        {
            if (!missions.TryGetValue(missionId, out var found) || found.Hold is not { } h || h.Card != card.Id) return;
            m = found;
            hold = h;
        }
        var chosen = hold.Options.FirstOrDefault(x => string.Equals(x.Text, answer.Text, StringComparison.Ordinal));
        if (chosen is null) return; // the queue lets only a card's own options through
        switch (chosen.Kind)
        {
            case CapOptionKind.AllowOnce:
                lock (gate)
                {
                    if (!m.Allowed.TryGetValue(hold.Cap.Id, out var allow) || (hold.Cap.IsDaily && allow.Day != o.Ledger.Today))
                        m.Allowed[hold.Cap.Id] = allow = new Allow(o.Ledger.Today);
                    if (hold.Reach.Window is { } kind) allow.Points[(hold.Reach.AccountKey, kind)] = allow.Points.GetValueOrDefault((hold.Reach.AccountKey, kind)) + hold.Units;
                    else allow.Dollars += (decimal)hold.Units;
                }
                Log(m.AssistantId, missionId, "cap_answer", chosen.Text);
                Release(m, hold);
                o.Runner.ResumeAfterReserve(missionId);
                break;
            case CapOptionKind.WaitForTomorrow:
                lock (gate)
                {
                    hold.Waiting = true;
                    hold.WaitUntil = NextLocalMidnight() + TimeSpan.FromSeconds(AutoContinue.Margin);
                    ArmWait(missionId, hold, hold.WaitUntil - clock.GetUtcNow());
                }
                o.Runner.NoteWaiting(missionId, $"Waiting for tomorrow, then your cap of {CostCapWords.Amount(hold.Cap.Normalized())} a day starts again");
                Log(m.AssistantId, missionId, "cap_answer", chosen.Text);
                break;
            case CapOptionKind.Stop:
                Log(m.AssistantId, missionId, "cap_answer", chosen.Text);
                Release(m, hold);
                o.Runner.Stop(missionId);
                break;
        }
        Changed?.Invoke();
    }

    // The hold is over: the task is no longer waited for.
    private void Release(MissionCost m, HoldState hold)
    {
        lock (gate)
        {
            CancelWait(hold);
            if (ReferenceEquals(m.Hold, hold)) m.Hold = null;
        }
    }

    // ---- waiting for tomorrow ----

    /// <summary>The next local midnight: when a day's figures start again.</summary>
    private DateTimeOffset NextLocalMidnight()
    {
        var zone = clock.LocalTimeZone;
        var next = clock.GetLocalNow().Date.AddDays(1);
        while (zone.IsInvalidTime(next)) next = next.AddHours(1);
        return new DateTimeOffset(next, zone.GetUtcOffset(next));
    }

    // Under the lock.
    private void ArmWait(Guid missionId, HoldState hold, TimeSpan due)
    {
        hold.Timer?.Dispose();
        hold.Timer = clock.CreateTimer(_ => o.Runner.Post(() => TryRelease(missionId)), null, due > TimeSpan.Zero ? due : TimeSpan.FromMilliseconds(1), Timeout.InfiniteTimeSpan);
    }

    // Under the lock.
    private static void CancelWait(HoldState hold)
    {
        hold.Timer?.Dispose();
        hold.Timer = null;
        hold.Waiting = false;
    }

    // The new day has begun: the mission goes on when its caps allow, else the owner is asked again.
    private void TryRelease(Guid missionId)
    {
        var now = clock.GetUtcNow();
        MissionCost m;
        HoldState hold;
        Crossing? still;
        lock (gate)
        {
            if (disposed || !missions.TryGetValue(missionId, out var found) || found.Hold is not { Waiting: true } h || now < h.WaitUntil) return;
            m = found;
            hold = h;
            still = Evaluate(m, hold.Account, hold.Model, withTurn: true);
        }
        var record = o.Runner.Find(missionId);
        if (record is null) return;
        Release(m, hold);
        if (still is null)
        {
            Log(m.AssistantId, missionId, "cap_wait", "a new day, resumed");
            o.Runner.ResumeAfterReserve(missionId);
        }
        else
        {
            Log(m.AssistantId, missionId, "cap_wait", "a new day, a cap still holds");
            RaiseHold(record, still.Cap, still.Reach, still.Account, hold.Model, hold.Purpose, "It is a new day, but a cap still holds.", again: true);
        }
        Changed?.Invoke();
    }

    // ---- the log ----

    private void Log(string assistant, Guid mission, string tool, string result)
    {
        if (o.Audit is not { } audit) return;
        try
        {
            audit(new AuditEntry
            {
                Assistant = assistant,
                Mission = mission.ToString("D"),
                Tool = tool,
                ActionClass = ActionClass.Agents.ToString(),
                Result = result,
            });
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            o.Runner.ReportFault("The activity log could not be written: " + error.Message);
        }
    }
}
