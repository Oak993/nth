using Marrow.Core.Accounts;
using Marrow.Testing;

namespace Marrow.Core.Tests.Accounts;

/// <summary>
/// One randomized (but seeded) conversation with the Handshake machine and an independent model of what OD-0116 allows.
/// The model judges every call that carries the conversation to another account at the instant it happens.
/// </summary>
public sealed class ConsentModel
{
    private readonly Random random;
    private readonly ManualTimeProvider clock = new();
    private readonly InMemoryAccountStore accounts = new(HandshakeRig.DefaultAccounts());
    private readonly InMemoryAccountLimits limits = new();
    private readonly InMemoryDecisionStore store = new();
    private readonly ConsentProbeHost host = new();
    private readonly Dictionary<DecisionKind, (string Value, Resolution Snapshot)> once = [];
    private readonly List<DecisionRequest> requests = [];
    private readonly List<string> script = [];
    private readonly DecisionPolicy policy;
    private bool handshake;
    private int mutateAt;
    private int mutation;
    private HandshakeReason reason;

    public ConsentModel(int seed)
    {
        random = new Random(seed);
        policy = new DecisionPolicy(store);
        Seed = seed;
    }

    public int Seed { get; }

    public int Switches => host.Log.Count(entry => entry.StartsWith("switch:", StringComparison.Ordinal));

    public List<string> Violations { get; } = [];

    public string Describe => "seed " + Seed + ": " + string.Join("; ", script) + " | calls " + string.Join(",", host.Log);

    private string? Pick(params string?[] values) => values[random.Next(values.Length)];

    private void Setting(DecisionKind kind, string? value)
    {
        if (value is null) return;
        if (random.Next(4) == 0) store.SetThread(HandshakeRig.Thread, kind, value);
        else store.SetSetting(kind, value);
        script.Add(kind + "=" + value);
    }

    public async Task RunAsync()
    {
        handshake = random.Next(2) == 0;
        Setting(DecisionKind.OnLimit, Pick(null, DecisionValues.Ask, DecisionValues.Switch, DecisionValues.Wait, DecisionValues.Switch));
        Setting(DecisionKind.OtherService, Pick(null, DecisionValues.Ask, DecisionValues.Switch, DecisionValues.Wait));
        Setting(DecisionKind.Bring, Pick(null, DecisionValues.Full, DecisionValues.MarrowSummary, DecisionValues.AiSummary));
        Setting(DecisionKind.MidReply, Pick(null, DecisionValues.Finish, DecisionValues.Stop));
        Setting(DecisionKind.EarlierAccount, Pick(null, DecisionValues.Ask, DecisionValues.GoBack, DecisionValues.Stay));
        host.Pinned = random.Next(6) == 0;
        host.ReplyInFlight = random.Next(2) == 0;
        mutateAt = random.Next(3) == 0 ? 0 : 1 + random.Next(6);
        mutation = random.Next(12);
        host.ActiveAccountId = Pick("c1", "c1", "c2", "c3", "x1")!;
        script.Add("handshake=" + handshake + " pinned=" + host.Pinned + " reply=" + host.ReplyInFlight + " active=" + host.ActiveAccountId
            + " mutation#" + mutateAt + "=" + mutation);
        foreach (var a in accounts.Accounts) Usage(a.Id);

        host.OnCall = Judge;
        await using var machine = new HandshakeMachine(HandshakeRig.Thread, accounts, limits, new InMemoryHandoffSummary(), policy, host, clock,
            new HandshakeOptions(), () => new HandshakeOptions(handshake));
        machine.Changed += (_, e) =>
        {
            reason = e.Reason;
            if (e.Kind == HandshakeEventKind.PhaseChanged && e.To == HandshakePhase.Idle
                && (e.From is HandshakePhase.Switch or HandshakePhase.Standby or HandshakePhase.Linking or HandshakePhase.InSync
                    || e.From == HandshakePhase.Waiting && e.Reason == HandshakeReason.ResetReached)) once.Clear();
            if (e.Request is { } request) requests.Add(request);
        };

        for (var round = 0; round < 14; round++)
        {
            var action = random.Next(12);
            script.Add("#" + action);
            switch (action)
            {
                case 0 or 1 or 2: await machine.TickAsync(CancellationToken.None); break;
                case 3: await machine.LimitHitAsync(host.ActiveAccountId, clock.GetUtcNow().AddHours(1), CancellationToken.None, waitOnce: random.Next(4) == 0); break;
                case 4 or 5: if (machine.Phase == HandshakePhase.Asking) await AnswerAsync(machine); break;
                case 6: ReplaceAny(); break;
                case 7: handshake = !handshake; break;
                case 8: host.Pinned = !host.Pinned; break;
                case 9: Usage(Pick("c1", "c2", "c3", "x1")!); break;
                case 10: host.AtSafePoint = !host.AtSafePoint; host.ReplyInFlight = random.Next(2) == 0; break;
                case 11: clock.Advance(TimeSpan.FromMinutes(random.Next(1, 200))); break;
            }
            script.Add("{" + machine.Phase + " " + string.Join(",", Enum.GetValues<DecisionKind>().Select(k => k.ToString()[..2] + "=" + policy.Resolve(HandshakeRig.Thread, k).Value)) + " " + string.Join(",", accounts.Accounts.Select(a => a.Id + (a.Enabled ? "" : "!") + ":" + limits.Usage(a.Id)?.PercentUsed + (limits.Usage(a.Id)?.IsExhaustedAt(clock.GetUtcNow()) == true ? "X" : ""))) + " act=" + host.ActiveAccountId + "}");
        }
        host.AtSafePoint = true;
        for (var round = 0; round < 4; round++)
        {
            if (machine.Phase == HandshakePhase.Asking) await AnswerAsync(machine);
            await machine.TickAsync(CancellationToken.None);
        }
    }

    private void Usage(string id)
    {
        var pick = random.Next(5);
        var reset = random.Next(2) == 0 ? null : (DateTimeOffset?)clock.GetUtcNow().AddMinutes(random.Next(1, 300));
        limits.SetUsage(id, pick switch
        {
            0 or 1 => new AccountUsage(10, null),
            2 => new AccountUsage(97, reset),
            3 => new AccountUsage(99, reset),
            _ => new AccountUsage(100, reset, true),
        });
        script.Add(id + "usage" + pick);
        if (random.Next(10) == 0) accounts.Update(id, a => a with { Enabled = !a.Enabled });
    }

    private async Task AnswerAsync(HandshakeMachine machine)
    {
        if (requests.Count == 0) return;
        var request = requests[^1];
        var choices = new List<Choice>();
        foreach (var kind in request.Kinds)
        {
            if (random.Next(5) == 0) continue;
            var values = DecisionValues.Allowed(kind).Where(v => v != DecisionValues.Ask).ToArray();
            choices.Add(new Choice(kind, values[random.Next(values.Length)]));
        }
        var scope = (RememberScope)random.Next(3);
        script.Add("answer[" + string.Join(",", choices.Select(c => c.Kind + "=" + c.Value)) + "]@" + scope);
        var preset = choices.Any(c => c.Kind == DecisionKind.OnLimit && c.Value == DecisionValues.Switch);
        // Every answer governs the operation it was given for, whatever it remembers; its snapshot is the stored word right after it was recorded.
        var staged = new Dictionary<DecisionKind, string>();
        foreach (var choice in choices) staged[choice.Kind] = choice.Value;
        if (preset)
            foreach (var kind in request.Kinds.Where(k => k is DecisionKind.Bring or DecisionKind.MidReply && choices.All(c => c.Kind != k)))
                staged.TryAdd(kind, DecisionValues.Fallback(kind));
        try
        {
            await machine.AnswerAsync(choices, scope, CancellationToken.None, () =>
            {
                foreach (var pair in staged) once[pair.Key] = (pair.Value, policy.Resolve(HandshakeRig.Thread, pair.Key));
            });
        }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
        if (machine.Phase == HandshakePhase.Idle) once.Clear();
    }

    private string? Effective(DecisionKind kind)
    {
        if (once.TryGetValue(kind, out var held) && held.Snapshot == policy.Resolve(HandshakeRig.Thread, kind)) return held.Value;
        return policy.Resolve(HandshakeRig.Thread, kind).Value;
    }

    private void Judge(string call, SwitchTarget? target, HandoffPackage? package, bool resume)
    {
        if (call is not ("standby" or "briefing") && target is not null)
        {
            void Violate(string why) => Violations.Add(call + " -> " + target.Account.Id + ": " + why + " [" + string.Join(",", Enum.GetValues<DecisionKind>().Select(k => k + "=" + Effective(k) + (once.ContainsKey(k) ? "(once)" : ""))) + " reason=" + reason + " from=" + host.ActiveAccountId + "]");
            var from = accounts.Accounts.First(a => a.Id == host.ActiveAccountId);
            var row = accounts.Accounts.First(a => a.Id == target.Account.Id);
            if (host.Pinned) Violate("the thread is pinned");
            if (!row.Enabled) Violate("the target is off");
            if (limits.Usage(row.Id)?.IsExhaustedAt(clock.GetUtcNow()) == true) Violate("the target is out");
            if (reason == HandshakeReason.EarlierAccountBack)
            {
                if (Effective(DecisionKind.EarlierAccount) != DecisionValues.GoBack) Violate("going back to an earlier account was not agreed");
            }
            else
            {
                if (Effective(DecisionKind.OnLimit) != DecisionValues.Switch) Violate("switching was not agreed");
                if (from.Service != row.Service && Effective(DecisionKind.OtherService) != DecisionValues.Switch) Violate("another service was not agreed");
                if (reason == HandshakeReason.NearLimit && !handshake) Violate("the Handshake is off");
            }
            if (resume && Effective(DecisionKind.MidReply) == DecisionValues.Stop) Violate("the user chose Stop for a reply in flight");
            if (package is not null && Effective(DecisionKind.Bring) is { } bring && DecisionValues.ToBring(bring) != package.Bring)
                Violate("the package is " + package.Bring + " but the choice is " + bring);
        }
        Mutate();
    }

    private void Mutate()
    {
        if (mutateAt == 0 || host.Calls != mutateAt) return;
        script.Add("mutation " + mutation + " after call " + mutateAt);
        switch (mutation)
        {
            case 0: Replace(DecisionKind.OnLimit, DecisionValues.Wait); break;
            case 1: Replace(DecisionKind.OnLimit, DecisionValues.Ask); break;
            case 2: Replace(DecisionKind.OnLimit, null); break;
            case 3: handshake = false; break;
            case 4: host.Pinned = true; break;
            case 5: accounts.Update("c2", a => a with { Enabled = false }); break;
            case 6: limits.SetUsage("c2", new AccountUsage(100, clock.GetUtcNow().AddHours(5), true)); break;
            case 7: Replace(DecisionKind.OtherService, DecisionValues.Wait); break;
            case 8: Replace(DecisionKind.Bring, DecisionValues.Full); break;
            case 9: Replace(DecisionKind.MidReply, DecisionValues.Stop); break;
            case 10: Replace(DecisionKind.EarlierAccount, DecisionValues.Stay); break;
            case 11: host.ReplyInFlight = true; break;
        }
    }

    private void ReplaceAny()
    {
        var kind = (DecisionKind)random.Next(5);
        var values = DecisionValues.Allowed(kind);
        Replace(kind, random.Next(5) == 0 ? null : values[random.Next(values.Count)]);
    }

    private void Replace(DecisionKind kind, string? value)
    {
        store.SetSetting(kind, value);
        store.SetThread(HandshakeRig.Thread, kind, null);
    }
}
