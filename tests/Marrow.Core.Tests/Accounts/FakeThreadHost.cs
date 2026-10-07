using Marrow.Core.Accounts;

namespace Marrow.Core.Tests.Accounts;

/// <summary>A scripted <see cref="IThreadAccountHost"/>: no CLI, no model, no network. Every call lands in <see cref="Log"/>.</summary>
public sealed class FakeThreadHost : IThreadAccountHost, IIncomingBriefingHost, IDeferredDeliveryHost
{
    public HashSet<string> DeferredAccounts { get; } = [];

    public bool DeliversDeferred(SwitchTarget target) => DeferredAccounts.Contains(target.Account.Id);

    /// <summary>Destinations that load the history without a model turn (Codex, Pi): no read-back, the same in-sync tick (OD-0122).</summary>
    public HashSet<string> InjectedAccounts { get; } = [];
    /// <summary>Destinations whose deferral only shows after the host checks the CLI at run time.</summary>
    public HashSet<string> LateDeferredAccounts { get; } = [];
    /// <summary>An injected standby that discards its loaded history on receiving the package (a turn or item showed on the waiting thread).</summary>
    public bool InjectedDiscardsOnReceive { get; set; }

    public ValueTask<bool> ResolveDeliversDeferredAsync(SwitchTarget target, CancellationToken ct) =>
        ValueTask.FromResult(DeliversDeferred(target) || LateDeferredAccounts.Contains(target.Account.Id));

    public List<string> Log { get; } = [];

    public string ActiveAccountId { get; set; } = "";
    public Pill? Pill { get; set; }
    public int? StepIndex { get; set; }
    public bool Pinned { get; set; }
    public bool AtSafePoint { get; set; } = true;
    public bool ReplyInFlight { get; set; }

    public string? Briefing { get; set; } = "briefing";
    public string? IncomingBriefing { get; set; } = "incoming-light-briefing";
    public Exception? IncomingBriefingFails { get; set; }
    public Task<string?> RequestIncomingBriefingAsync(SwitchTarget target, HandoffSummary summary, CancellationToken ct)
    {
        Log.Add("light-briefing:" + target.Account.Id);
        if (IncomingBriefingFails is not null) throw IncomingBriefingFails;
        return Task.FromResult(IncomingBriefing);
    }
    public string ReadBack { get; set; } = "";
    public Exception? StandbyFails { get; set; }
    public Exception? BriefingFails { get; set; }
    public Task? BriefingGate { get; set; }
    public List<IReadOnlyList<string>> GapsSent { get; } = [];
    public List<HandoffPackage> Packages { get; } = [];
    public List<SwitchTarget> Targets { get; } = [];
    public int StandbysDisposed { get; private set; }

    public async Task<IStandbySession> StartStandbyAsync(SwitchTarget target, CancellationToken ct)
    {
        await Task.Yield();
        if (StandbyFails is not null) throw StandbyFails;
        Targets.Add(target);
        Log.Add($"standby:{target.Account.Id}");
        return new Standby(this, target.Account.Id);
    }

    public async Task<string?> RequestBriefingAsync(HandoffSummary summary, CancellationToken ct)
    {
        Log.Add("briefing");
        if (BriefingGate is not null) await BriefingGate.WaitAsync(ct);
        if (BriefingFails is not null) throw BriefingFails;
        return Briefing;
    }

    public Task? SwitchGate { get; set; }
    public async Task SwitchAsync(IStandbySession standby, SwitchTarget target, bool resumeReply, CancellationToken ct)
    {
        if (SwitchGate is not null) await SwitchGate.WaitAsync(ct);
        Log.Add($"switch:{target.Account.Id}:{(resumeReply ? "resume" : "noresume")}:{(AtSafePoint ? "safe" : "UNSAFE")}");
        ActiveAccountId = target.Account.Id;
        StepIndex = target.StepIndex;
        ReplyInFlight = false;
    }

    private sealed class Standby(FakeThreadHost host, string account) : IStandbySession
    {
        private bool discarded;

        public StandbyKind Kind => discarded ? StandbyKind.Deferred : host.InjectedAccounts.Contains(account) ? StandbyKind.Injected : StandbyKind.Primed;

        public Task<string?> ReceiveAsync(HandoffPackage package, bool readBack, CancellationToken ct)
        {
            if (Kind == StandbyKind.Injected && host.InjectedDiscardsOnReceive) discarded = true;
            host.Packages.Add(package);
            host.Log.Add(readBack ? "receive:readback" : "receive");
            return Task.FromResult<string?>(readBack ? host.ReadBack : null);
        }

        public Task FillGapsAsync(IReadOnlyList<string> gaps, CancellationToken ct)
        {
            host.GapsSent.Add(gaps);
            host.Log.Add("gaps:" + string.Join("|", gaps));
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            host.StandbysDisposed++;
            return ValueTask.CompletedTask;
        }
    }
}
