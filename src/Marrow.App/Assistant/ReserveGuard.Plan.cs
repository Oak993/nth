// PORT-ONLY: Dextra (DESIGN.md rev 5, 11.4, 11.5; unit AS-13; ED-NEW-dextra-as13-9). No Swift twin.
// The guard's plan and its report. Before a big task: the plan (one line, the reading, a bar per account) built from the
// planner's steps, with the p90 checked against every floor. While it runs: a re-plan when spend runs more than 25% over the
// estimate. After it: what each account used against the plan, the use outside nth on its own, and the state of each reserve.
using System.Globalization;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

public sealed partial class ReserveGuard
{
    // ---- the plan ----

    /// <summary>
    /// The plan for a task about to start (11.4): the steps' cost per account, converted with the calibrated rates, set against the
    /// floors that would bind it. <paramref name="taskRules"/> are the rules the owner spoke for this task. Nothing is registered:
    /// <see cref="Begin"/> does that when the task starts.
    /// </summary>
    public ReservePlan Plan(MissionRecord? mission, IReadOnlyList<PlanStep> steps, IReadOnlyList<ReserveRule>? taskRules = null,
        int agents = 1, TimeSpan? duration = null, string team = "", decimal apiDollars = 0)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var now = clock.GetUtcNow();
        var task = (taskRules ?? []).Where(r => r.Lifetime == ReserveLifetime.Task).Select(r => r.Normalized()).ToList();
        lock (gate)
        {
            var scratch = new MissionReserve(mission?.Id ?? Guid.Empty, mission?.Mission.Title ?? "", mission?.Mission.AssistantId ?? "", now);
            scratch.TaskRules.AddRange(task);
            var lines = new List<PlanAccountLine>();
            var reserves = new List<PlanReserve>();
            var covering = new List<ReserveRule>();
            foreach (var group in steps.GroupBy(s => s.Account.Key))
            {
                var account = group.First().Account;
                var model = group.Select(s => s.Model).FirstOrDefault(x => x is not null);
                var tokens50 = group.Sum(s => Math.Max(0, s.TokensP50));
                var tokens90 = group.Sum(s => Math.Max(Math.Max(0, s.TokensP50), s.TokensP90));
                var line = accounts.TryGetValue(account.Key, out var state) && state.Windows.Count > 0
                    ? BindingLine(scratch, account, state, model, tokens50, tokens90, now)
                    : Unread(account, model, tokens50, tokens90);
                lines.Add(line);
                var subject = new ReserveSubject(account.Key, account.Name, account.ProviderId, mission?.Mission.AssistantId);
                foreach (var rule in o.Settings.Rules.Concat(task))
                {
                    if (!rule.Scope.Covers(subject) || rule.KeepDollars is not null || !rule.AppliesTo(line.Kind)) continue;
                    if (line.Floor is null) continue;
                    reserves.Add(new PlanReserve(account.Name, rule.Normalized().KeepPercent, rule.Lifetime));
                    if (rule.Lifetime == ReserveLifetime.Standing && !covering.Contains(rule)) covering.Add(rule);
                }
            }
            return new ReservePlan(lines)
            {
                Reading = ReserveWords.Reading(task, covering),
                Reserves = [.. reserves.Distinct()],
                Team = team,
                Duration = duration,
                Agents = agents,
                ApiDollars = apiDollars,
                AlwaysAsk = o.Settings.AlwaysAskBeforeStarting,
            };
        }
    }

    // The account's bar in a plan: the window the tighter reserve binds (least room after the p90), else the 5-hour window.
    private PlanAccountLine BindingLine(MissionReserve scratch, ReserveAccountRef account, ReserveAccount state, string? model, double tokens50, double tokens90, DateTimeOffset now)
    {
        PlanAccountLine? best = null;
        foreach (var window in state.Windows.OrderBy(w => w.Kind))
        {
            var rate = o.Calibrator.RateFor(account.Key, account.ProviderId, model, window.Kind);
            var used = state.Estimate(window.Id, now);
            var floor = FloorFor(scratch, account, state, window, now, withAllowance: false)?.Percent;
            var line = new PlanAccountLine(account.Key, account.Name, window.Kind, window.Title, used, rate.Percent(tokens50), rate.PercentP90(tokens90), floor, rate.Rough);
            if (best is null || (line.Room is { } room && (best.Room is not { } bestRoom || room < bestRoom))) best = line;
        }
        return best!;
    }

    // An account nobody has read yet: its plan is the conservative default, marked rough, with no floor to show.
    private PlanAccountLine Unread(ReserveAccountRef account, string? model, double tokens50, double tokens90)
    {
        var rate = o.Calibrator.RateFor(account.Key, account.ProviderId, model, ReserveWindowKind.FiveHour);
        return new PlanAccountLine(account.Key, account.Name, ReserveWindowKind.FiveHour, "", 0, rate.Percent(tokens50), rate.PercentP90(tokens90), null, true);
    }

    /// <summary>
    /// The task starts with this plan (11.5): its steps' tokens are what spend is measured against ("more than 25% over" re-plans),
    /// and its task rules bind from now on.
    /// </summary>
    public void Begin(MissionRecord mission, ReservePlan plan, IReadOnlyList<PlanStep> steps, IReadOnlyList<ReserveRule>? taskRules = null)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(steps);
        lock (gate)
        {
            var m = MissionOf(mission);
            m.Plan = plan;
            m.Planned.Clear();
            foreach (var group in steps.GroupBy(s => s.Account.Key))
                m.Planned[group.Key] = (group.Sum(s => Math.Max(0, s.TokensP50)), group.Sum(s => Math.Max(Math.Max(0, s.TokensP50), s.TokensP90)));
            if (taskRules is not null)
            {
                m.TaskRules.Clear();
                m.TaskRules.AddRange(taskRules.Where(r => r.Lifetime == ReserveLifetime.Task).Select(r => r.Normalized()));
            }
        }
    }

    // Spend that runs more than 25% over the estimate re-plans (11.5): the estimate becomes what was spent, and the p90 grows with it.
    private string? CheckOverEstimate(MissionReserve m, ReserveAccountRef account, ReserveAccount state, DateTimeOffset now)
    {
        if (!m.Planned.TryGetValue(account.Key, out var planned) || !m.Used.TryGetValue(account.Key, out var use)) return null;
        if (!ReserveMath.RunsOver(use.Tokens, planned.P50)) return null;
        var over = use.Tokens / planned.P50 - 1;
        var p90 = Math.Max(planned.P90, use.Tokens * (1 + ReserveMath.OverEstimate));
        m.Planned[account.Key] = (use.Tokens, p90);
        var remaining = Math.Max(0, p90 - use.Tokens);
        var reaches = false;
        foreach (var window in state.Windows)
        {
            if (FloorFor(m, account, state, window, now) is not { } floor) continue;
            var cost = o.Calibrator.RateFor(account.Key, account.ProviderId, null, window.Kind).PercentP90(remaining);
            if (ReserveMath.Crosses(state.Estimate(window.Id, now) + cost, floor.Percent)) reaches = true;
        }
        var percent = (Math.Round(over * 100)).ToString("0", CultureInfo.InvariantCulture);
        return $"{account.Name} is running {percent}% over its estimate · re-planned" +
            (reaches ? ": its p90 now reaches your floor, so the next step will pause before it" : "");
    }

    // ---- the Reserves view ----

    /// <summary>
    /// Every account the guard has read, with each window's used percent now and the floor of the owner's standing rules (easing
    /// near a reset unless he holds reserves), for the bars of Settings > Reserves.
    /// </summary>
    public IReadOnlyList<ReserveAccountBar> Bars()
    {
        var now = clock.GetUtcNow();
        lock (gate)
        {
            var bars = new List<ReserveAccountBar>();
            foreach (var (key, state) in accounts.OrderBy(a => refs[a.Key].Name, StringComparer.OrdinalIgnoreCase))
            {
                var account = refs[key];
                var subject = new ReserveSubject(key, account.Name, account.ProviderId);
                var rules = o.Settings.Rules.Where(r => r.Scope.Covers(subject)).ToList();
                var windows = state.Windows.OrderBy(w => w.Kind).Select(window =>
                {
                    var floor = ReserveMath.EffectiveFloor(rules.Select(r => new ReserveFloorInput(r, 0)), window, now, o.Settings.HoldReserves);
                    var title = window.Title.Length > 0 ? window.Title : ReserveWindowKinds.Words(window.Kind);
                    return new ReserveWindowBar(window.Id, window.Kind, title, state.Estimate(window.Id, now), floor?.Percent, floor?.Unadjusted, floor?.Eased ?? false, window.ResetsAt);
                }).ToList();
                var kind = windows.Count > 0 ? windows[0].Kind : ReserveWindowKind.FiveHour;
                bars.Add(new ReserveAccountBar(key, account.Name, windows, state.OutsideSince(now - TimeSpan.FromHours(24)),
                    !o.Calibrator.RateFor(key, account.ProviderId, null, kind).Calibrated));
            }
            return bars;
        }
    }

    // ---- the after-report ----

    private ReserveAfterReport BuildReport(MissionReserve m, DateTimeOffset now)
    {
        var rows = new List<ReportRow>();
        var outside = new List<ReportOutside>();
        var reserves = new List<ReportReserve>();
        foreach (var (key, use) in m.Used)
        {
            if (!accounts.TryGetValue(key, out var state) || state.Windows.Count == 0) continue;
            var name = use.Account.Name;
            var kind = state.Windows.Any(w => w.Kind == ReserveWindowKind.FiveHour) ? ReserveWindowKind.FiveHour : state.Windows.OrderBy(w => w.Kind).First().Kind;
            var planned = m.Plan?.Accounts.FirstOrDefault(a => a.AccountKey == key)?.PlannedP50 ?? 0;
            rows.Add(new ReportRow(name, planned, use.Points.GetValueOrDefault(kind), ReserveWindowKinds.Words(kind)));
            foreach (var found in state.OutsideUses.Where(x => x.At > m.Began).GroupBy(x => x.WindowId))
            {
                var window = state.Window(found.Key);
                outside.Add(new ReportOutside(name, ReserveWindowKinds.Words(window?.Kind ?? kind), found.Sum(x => x.Points),
                    found.Min(x => x.Since) is var since && since > m.Began ? since : m.Began));
            }
            foreach (var window in state.Windows)
            {
                if (FloorFor(m, use.Account, state, window, now, withAllowance: false) is not { } floor || floor.Percent >= 100) continue;
                var dipped = Math.Max(0, state.Estimate(window.Id, now) - floor.Percent);
                var once = m.Allowance.GetValueOrDefault((key, window.Id)) > 0;
                reserves.Add(new ReportReserve(name, ReserveWords.Title(floor.Rule), dipped, once && dipped > 0));
            }
        }
        return new ReserveAfterReport(m.Title, rows, outside, reserves);
    }
}
