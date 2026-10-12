// PORT-ONLY: Dextra (DESIGN.md rev 5, 11.5; unit AS-13; ED-NEW-dextra-as13-9). No Swift twin.
// The pause and its card. When a projection crosses a floor the guard pauses that mission at the turn boundary (the runner's
// Pause with the reserve as its cause: a running turn is never cut), raises a card with four options, and acts on the answer:
// move the rest to an account with room, wait for the reset (the 60 s AutoContinue margin after it), use some of the reserve
// this once, or stop and give the owner what there is.
using System.Globalization;
using Marrow.App.Runtime;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

public sealed partial class ReserveGuard
{
    /// <summary>"Waiting for the reset": a reading older than the reset is asked for again this often, this many times at most.</summary>
    public static readonly TimeSpan WaitRecheck = TimeSpan.FromSeconds(30);

    public const int WaitTries = 40;

    public const string PausedNote = "Paused at a turn boundary. Nothing was cut off mid-turn.";

    private sealed class HoldState
    {
        public Guid Card { get; set; }
        public double Estimate { get; init; }
        public double Floor { get; init; }
        public double Needs { get; init; }
        public required ReserveAccountRef Account { get; init; }
        public required string WindowId { get; init; }
        public ReserveWindowKind Kind { get; init; }
        public DateTimeOffset? ResetsAt { get; init; }
        public double Allowance { get; init; }
        public IReadOnlyList<ReserveOption> Options { get; set; } = [];
        public ReserveMoveOffer? Offer { get; set; }
        public string? Model { get; init; }
        public double P90Tokens { get; init; }
        public string Purpose { get; init; } = "";
        public bool Waiting { get; set; }
        public DateTimeOffset WaitUntil { get; set; }
        public int Tries { get; set; }
        public ITimer? Timer { get; set; }
    }

    /// <summary>The hold on a task, if its reserve holds it.</summary>
    public ReserveHold? HoldOf(Guid mission)
    {
        lock (gate)
        {
            if (!missions.TryGetValue(mission, out var m) || m.Hold is not { Card: var card } hold || card == Guid.Empty) return null;
            return new ReserveHold(mission, m.Title, hold.Account, hold.WindowId, hold.Kind, hold.Estimate, hold.Floor, hold.Needs, hold.ResetsAt, card) { Options = hold.Options };
        }
    }

    // ---- raising ----

    private void RaiseHold(MissionRecord record, ReserveAccountRef account, ReserveCheck check, double p90Tokens, string? model, string purpose, string? note)
    {
        var binding = check.Binding!;
        var now = clock.GetUtcNow();
        var rate = o.Calibrator.RateFor(account.Key, account.ProviderId, model, binding.Window.Kind);
        var needs = Math.Max(1, Math.Ceiling(rate.PercentP90(p90Tokens)));
        var allowance = ReserveMath.AllowanceNeeded(binding.Projected, binding.Floor.Percent);
        var hold = new HoldState
        {
            Account = account,
            Estimate = binding.Estimate,
            Floor = binding.Floor.Percent,
            Needs = needs,
            WindowId = binding.Window.Id,
            Kind = binding.Window.Kind,
            ResetsAt = binding.Window.ResetsAt,
            Allowance = allowance,
            Model = model,
            P90Tokens = p90Tokens,
            Purpose = purpose,
        };
        IReadOnlyList<ReserveCandidate> candidates;
        string facts;
        lock (gate)
        {
            var m = MissionOf(record);
            if (m.Hold is not null) return;
            m.Hold = hold;
            candidates = Candidates(m, account, p90Tokens, model, now);
            facts = Facts(m, account, binding, needs, now);
        }
        // Pause first, outside the lock. If the owner's own pause or stop stands, there is no card to raise: his hold is the stronger.
        if (o.Runner.PauseForReserve(record.Id) is not null)
        {
            lock (gate)
            {
                if (missions.TryGetValue(record.Id, out var m) && ReferenceEquals(m.Hold, hold)) m.Hold = null;
            }
            return;
        }
        var offer = o.Mover is { } mover && !o.IsPinned(record) ? mover.Offer(record, candidates) : null;
        var options = new List<ReserveOption>();
        if (offer is not null) options.Add(new ReserveOption(ReserveOptionKind.Move, $"Move the rest to {offer.Target.Name} (≈{Whole(offer.CostPercent)})"));
        if (binding.Window.ResetsAt is { } reset && reset > now)
            options.Add(new ReserveOption(ReserveOptionKind.Wait, $"Wait for the reset at {Clock(reset)}"));
        options.Add(new ReserveOption(ReserveOptionKind.UseOnce, $"Use {allowance.ToString("0", CultureInfo.InvariantCulture)}% of the reserve once"));
        options.Add(new ReserveOption(ReserveOptionKind.Stop, "Stop and give me what you have"));
        hold.Options = options;
        hold.Offer = offer;

        var rule = binding.Floor.Rule;
        var item = new DecisionItem(Guid.NewGuid(), DecisionKind.Reserve, DecisionWeight.Question, $"{record.Mission.Title} would touch your {account.Name} reserve")
        {
            Inputs = new ValueList<DecisionInput>([
                new("Account", account.Name),
                new("Window", ReserveWindowKinds.Words(binding.Window.Kind)),
                new("Used now", ReserveMath.Percent(binding.Estimate)),
                new("Your floor", ReserveMath.Percent(binding.Floor.Percent)),
                new("The next step needs about", ReserveMath.Percent(needs)),
                new("Where it stands", facts),
            ]),
            Why = (rule.Lifetime == ReserveLifetime.Standing ? "Standing rule: " : "Your rule for this task: ") + LowerFirst(ReserveWords.Title(rule)),
            Rule = "Your reserve",
            AssistantId = record.Mission.AssistantId,
            MissionId = record.Id,
            ThreadId = record.LeadThread,
            Agent = "Lead",
            Options = new ValueList<string>(options.Select(x => x.Text)),
            Note = note is null ? PausedNote : note + " " + PausedNote,
        };
        var raised = o.Queue.Raise(item, (card, answer) => Answered(record.Id, card, answer));
        lock (gate) hold.Card = raised.Id;
        o.Runner.NoteWaiting(record.Id, $"Paused before your {account.Name} reserve");
        Log(record.Mission.AssistantId, record.Id, "reserve_hold", $"{purpose}: {account.Name} at {ReserveMath.Percent(binding.Estimate)}, floor {ReserveMath.Percent(binding.Floor.Percent)}");
        Held?.Invoke(new ReserveHold(record.Id, record.Mission.Title, account, binding.Window.Id, binding.Window.Kind, binding.Estimate, binding.Floor.Percent, needs, binding.Window.ResetsAt, raised.Id) { Options = options });
        Changed?.Invoke();
    }

    // "Claude 20x is at 68.6% of its 5-hour window; your floor is 70%. It was 52% at 08:00: this task used 5.2%, and 8% was used
    // outside nth. The next step needs about 4% more."
    private string Facts(MissionReserve m, ReserveAccountRef account, ReserveWindowCheck binding, double needs, DateTimeOffset now)
    {
        var text = $"{account.Name} is at {ReserveMath.Percent(binding.Estimate)} of its {ReserveWindowKinds.Words(binding.Window.Kind)}; your floor is {ReserveMath.Percent(binding.Floor.Percent)}.";
        var used = m.Used.TryGetValue(account.Key, out var use) ? use.Points.GetValueOrDefault(binding.Window.Kind) : 0;
        var outside = accounts.TryGetValue(account.Key, out var state) ? state.OutsideSince(m.Began, binding.Window.Id) : 0;
        if (used >= 0.05 || outside > 0)
        {
            var start = m.Baseline.TryGetValue((account.Key, binding.Window.Id), out var b) ? b : Math.Max(0, binding.Estimate - used);
            text += $" It was {ReserveMath.Percent(start)} at {Clock(m.Began)}: this task used {ReserveMath.Percent(used)}";
            if (outside > 0) text += $", and {ReserveMath.Percent(outside)} was used outside nth";
            text += ".";
        }
        return text + $" The next step needs about {ReserveMath.Percent(needs)} more.";
    }

    // The other accounts that could take the held work: those with room for it, the roomiest first.
    private List<ReserveCandidate> Candidates(MissionReserve m, ReserveAccountRef held, double p90Tokens, string? model, DateTimeOffset now)
    {
        var list = new List<ReserveCandidate>();
        foreach (var (key, state) in accounts)
        {
            if (key == held.Key || state.Windows.Count == 0) continue;
            var other = refs[key];
            var check = state.Check(window => FloorFor(m, other, state, window, now), model, p90Tokens, now);
            if (!check.Go) continue;
            var kind = check.Binding?.Window.Kind ?? state.Windows.OrderBy(w => w.Kind).First().Kind;
            var cost = o.Calibrator.RateFor(key, other.ProviderId, model, kind).PercentP90(p90Tokens);
            list.Add(new ReserveCandidate(other, cost, check.Binding?.Room ?? 100));
        }
        return [.. list.OrderByDescending(c => c.Room)];
    }

    // ---- the owner's answer ----

    private void Answered(Guid missionId, DecisionItem card, DecisionAnswer answer)
    {
        HoldState hold;
        MissionReserve m;
        lock (gate)
        {
            if (!missions.TryGetValue(missionId, out var found) || found.Hold is not { } h || h.Card != card.Id) return;
            m = found;
            hold = h;
        }
        var chosen = hold.Options.FirstOrDefault(x => string.Equals(x.Text, answer.Text, StringComparison.Ordinal));
        if (chosen is null) return; // the queue lets only a card's own options through
        var record = o.Runner.Find(missionId);
        switch (chosen.Kind)
        {
            case ReserveOptionKind.Move:
            {
                // A live chat changes account at its next safe point (AS-14), so a mover may need time: the task stays held until it is
                // done. A mover that does not is asked, as before, and answers at once.
                if (record is not null && hold.Offer is not null && o.Mover is { MovesAsync: true } slow)
                {
                    _ = FinishMoveLater(slow, missionId, record, hold.Offer, m, hold, chosen);
                    break;
                }
                var refusal = record is null || hold.Offer is null || o.Mover is null ? "that move is not available now" : o.Mover.Move(record, hold.Offer);
                FinishMove(missionId, m, hold, record, chosen, refusal);
                break;
            }
            case ReserveOptionKind.Wait:
            {
                var reset = hold.ResetsAt ?? clock.GetUtcNow();
                lock (gate)
                {
                    hold.Waiting = true;
                    hold.Tries = 0;
                    hold.WaitUntil = reset + TimeSpan.FromSeconds(AutoContinue.Margin);
                    ArmWait(missionId, hold, hold.WaitUntil - clock.GetUtcNow());
                }
                o.Runner.NoteWaiting(missionId, $"Waiting for the {hold.Account.Name} reset at {Clock(reset)}");
                Log(m.AssistantId, missionId, "reserve_answer", chosen.Text);
                break;
            }
            case ReserveOptionKind.UseOnce:
            {
                lock (gate)
                {
                    var key = (hold.Account.Key, hold.WindowId);
                    m.Allowance[key] = m.Allowance.GetValueOrDefault(key) + hold.Allowance;
                }
                Log(m.AssistantId, missionId, "reserve_answer", chosen.Text);
                Release(m, hold);
                o.Runner.ResumeAfterReserve(missionId);
                break;
            }
            case ReserveOptionKind.Stop:
                Log(m.AssistantId, missionId, "reserve_answer", chosen.Text);
                Release(m, hold);
                o.Runner.Stop(missionId);
                break;
        }
        Changed?.Invoke();
    }

    // A move's result: the log says that it did not work; why is the mover's own text, for the card the owner reads, not for a log line.
    private void FinishMove(Guid missionId, MissionReserve m, HoldState hold, MissionRecord? record, ReserveOption chosen, string? refusal)
    {
        // The task ended, or the owner's Stop came while the move ran: the hold is no longer this one's to release.
        lock (gate)
        {
            if (!ReferenceEquals(m.Hold, hold)) return;
        }
        Log(m.AssistantId, missionId, "reserve_answer", refusal is null ? chosen.Text : $"{chosen.Text}: did not work");
        if (refusal is null)
        {
            Release(m, hold);
            o.Runner.ResumeAfterReserve(missionId);
        }
        else Ask(m, hold, record, $"Moving the rest did not work: {refusal}.");
    }

    // The slow move: awaited, then finished on the app's main thread. What a mover throws is for the fault line, never for the card or the log.
    private async Task FinishMoveLater(IReserveMover mover, Guid missionId, MissionRecord record, ReserveMoveOffer offer, MissionReserve m, HoldState hold, ReserveOption chosen)
    {
        string? refusal;
        try { refusal = await mover.MoveAsync(record, offer); }
        catch (OperationCanceledException) { refusal = "that move was cancelled"; }
        catch (Exception error)
        {
            o.Runner.ReportFault("A move to another account failed (" + error.GetType().Name + ").");
            refusal = "that move failed";
        }
        o.Runner.Post(() =>
        {
            try
            {
                FinishMove(missionId, m, hold, record, chosen, refusal);
                Changed?.Invoke();
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                o.Runner.ReportFault("The answer to the reserve card could not be carried out (" + error.GetType().Name + ").");
            }
        });
    }

    // The hold is over: the task is no longer waited for.
    private void Release(MissionReserve m, HoldState hold)
    {
        lock (gate)
        {
            CancelWait(hold);
            if (ReferenceEquals(m.Hold, hold)) m.Hold = null;
        }
    }

    // After an answer that did not work, ask again with the facts as they are now.
    private void Ask(MissionReserve m, HoldState old, MissionRecord? record, string note)
    {
        var now = clock.GetUtcNow();
        ReserveAccountRef account = old.Account;
        ReserveCheck check;
        lock (gate)
        {
            CancelWait(old);
            if (ReferenceEquals(m.Hold, old)) m.Hold = null;
            if (!accounts.TryGetValue(account.Key, out var state)) return;
            check = state.Check(window => FloorFor(m, account, state, window, now), old.Model, old.P90Tokens, now);
        }
        if (record is null) return;
        if (check.Go) o.Runner.ResumeAfterReserve(record.Id);
        else RaiseHold(record, account, check, old.P90Tokens, old.Model, old.Purpose, note);
    }

    // ---- waiting for a reset ----

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

    private void ReleaseWaiting(string accountKey)
    {
        List<Guid> due;
        var now = clock.GetUtcNow();
        lock (gate)
        {
            due = [.. missions.Values.Where(x => x.Hold is { Waiting: true } h && h.Account.Key == accountKey && now >= h.WaitUntil).Select(x => x.Id)];
        }
        foreach (var id in due) TryRelease(id);
    }

    // The reset time has passed: if a reading shows the new window, the mission goes on when its floor allows, else it asks again.
    private void TryRelease(Guid missionId)
    {
        var now = clock.GetUtcNow();
        MissionReserve m;
        HoldState hold;
        ReserveCheck? check = null;
        bool stale;
        lock (gate)
        {
            if (disposed || !missions.TryGetValue(missionId, out var found) || found.Hold is not { Waiting: true } h || now < h.WaitUntil) return;
            m = found;
            hold = h;
            var state = accounts.GetValueOrDefault(h.Account.Key);
            var window = state?.Window(h.WindowId);
            stale = state is null || window is null || (h.ResetsAt is { } reset && window.ObservedAt < reset);
            if (!stale) check = state!.Check(w => FloorFor(m, h.Account, state, w, now), h.Model, h.P90Tokens, now);
        }
        var record = o.Runner.Find(missionId);
        if (record is null) return;
        if (stale)
        {
            bool giveUp;
            lock (gate)
            {
                hold.Tries++;
                giveUp = hold.Tries > WaitTries;
                if (!giveUp) ArmWait(missionId, hold, WaitRecheck);
            }
            if (!giveUp)
            {
                if (o.Refresh is { } refresh) _ = RefreshQuietly(refresh, hold.Account);
                return;
            }
            Log(m.AssistantId, missionId, "reserve_wait", "no reading after the reset");
            Ask(m, hold, record, $"{hold.Account.Name}'s usage could not be read after the reset.");
            return;
        }
        if (check!.Go)
        {
            Release(m, hold);
            Log(m.AssistantId, missionId, "reserve_wait", "window reset, resumed");
            o.Runner.ResumeAfterReserve(missionId);
            Replanned?.Invoke(missionId, $"{hold.Account.Name}'s {ReserveWindowKinds.Words(hold.Kind)} reset · re-planned · resumed");
            Changed?.Invoke();
        }
        else
        {
            Log(m.AssistantId, missionId, "reserve_wait", "window reset, floor still holds");
            Ask(m, hold, record, $"{hold.Account.Name}'s {ReserveWindowKinds.Words(hold.Kind)} reset, but your floor still holds.");
        }
    }

    private async Task RefreshQuietly(Func<ReserveAccountRef, Task> refresh, ReserveAccountRef account)
    {
        try { await refresh(account).ConfigureAwait(false); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            o.Runner.ReportFault($"{account.Name}'s usage could not be refreshed: {error.Message}");
        }
    }

    // ---- words and the log ----

    private static string LowerFirst(string text) => text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];

    private static string Whole(double percent) => Math.Max(1, Math.Ceiling(percent - 1e-9)).ToString("0", CultureInfo.InvariantCulture) + "%";

    private static string Clock(DateTimeOffset at) => at.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

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
