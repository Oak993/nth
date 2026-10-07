using Marrow.Core.Accounts;
using Marrow.Testing;

namespace Marrow.Core.Tests.Accounts;

/// <summary>The deterministic rig every Handshake test builds: injected clock, in-memory MA-1/2/4 stand-ins, a scripted host.</summary>
public sealed class HandshakeRig : IAsyncDisposable
{
    public static readonly Guid Thread = new("00000000-0000-0000-0000-0000000000a5");

    public ManualTimeProvider Clock { get; } = new();
    public InMemoryAccountStore Store { get; }
    public InMemoryAccountLimits Limits { get; } = new();
    public InMemoryHandoffSummary Summaries { get; } = new();
    public InMemoryDecisionStore DecisionStore { get; } = new();
    public DecisionPolicy Decisions { get; }
    public FakeThreadHost Host { get; } = new();
    public HandshakeMachine Machine { get; }
    public List<HandshakeEvent> Events { get; } = [];
    public CancellationToken Ct => CancellationToken.None;

    public HandshakeRig(IEnumerable<AccountInfo>? accounts = null, HandshakeOptions? options = null, string active = "c1", Func<HandshakeOptions>? optionsSource = null)
    {
        Store = new InMemoryAccountStore(accounts ?? DefaultAccounts());
        Decisions = new DecisionPolicy(DecisionStore);
        Host.ActiveAccountId = active;
        foreach (var a in Store.Accounts) Limits.SetUsage(a.Id, new AccountUsage(10, null));
        Machine = new HandshakeMachine(Thread, Store, Limits, Summaries, Decisions, Host, Clock, options ?? new HandshakeOptions(), optionsSource);
        Machine.Changed += (_, e) => Events.Add(e);
    }

    public static List<AccountInfo> DefaultAccounts() =>
    [
        new("c1", "claude", "Claude Max 1", Plan: PlanTier.Large),
        new("c2", "claude", "Claude Max 2", Plan: PlanTier.Large),
        new("c3", "claude", "Claude Pro", Plan: PlanTier.Standard),
        new("x1", "codex", "Codex 1", Plan: PlanTier.Standard),
    ];

    /// <summary>Settings that answer every choice for a switch, so no card is raised.</summary>
    public void SettleAll(string onLimit = DecisionValues.Switch, string bring = DecisionValues.MarrowSummary, string midReply = DecisionValues.Finish)
    {
        DecisionStore.SetSetting(DecisionKind.OnLimit, onLimit);
        DecisionStore.SetSetting(DecisionKind.Bring, bring);
        DecisionStore.SetSetting(DecisionKind.MidReply, midReply);
    }

    public void SetUsed(string id, double percent, DateTimeOffset? resetsAt = null, bool isOut = false) =>
        Limits.SetUsage(id, new AccountUsage(percent, resetsAt, isOut));

    public IEnumerable<HandshakePhase> Phases() => Events.Where(e => e.Kind == HandshakeEventKind.PhaseChanged).Select(e => e.To);

    public ValueTask DisposeAsync() => Machine.DisposeAsync();
}
