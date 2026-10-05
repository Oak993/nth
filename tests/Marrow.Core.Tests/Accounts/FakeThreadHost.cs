using Marrow.Core.Accounts;

namespace Marrow.Core.Tests.Accounts;

/// <summary>A scripted <see cref="IThreadAccountHost"/>: no CLI, no model, no network. Every call lands in <see cref="Log"/>.</summary>
public sealed class FakeThreadHost : IThreadAccountHost
{
    public List<string> Log { get; } = [];

    public string ActiveAccountId { get; set; } = "";
    public Pill? Pill { get; set; }
    public int? StepIndex { get; set; }
    public bool Pinned { get; set; }
    public bool AtSafePoint { get; set; } = true;
    public bool ReplyInFlight { get; set; }

    public string? Briefing { get; set; } = "briefing";
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
        return new Standby(this);
    }

    public async Task<string?> RequestBriefingAsync(HandoffSummary summary, CancellationToken ct)
    {
        Log.Add("briefing");
        if (BriefingGate is not null) await BriefingGate.WaitAsync(ct);
        if (BriefingFails is not null) throw BriefingFails;
        return Briefing;
    }

    public Task SwitchAsync(IStandbySession standby, SwitchTarget target, bool resumeReply, CancellationToken ct)
    {
        Log.Add($"switch:{target.Account.Id}:{(resumeReply ? "resume" : "noresume")}:{(AtSafePoint ? "safe" : "UNSAFE")}");
        ActiveAccountId = target.Account.Id;
        StepIndex = target.StepIndex;
        ReplyInFlight = false;
        return Task.CompletedTask;
    }

    private sealed class Standby(FakeThreadHost host) : IStandbySession
    {
        public Task<string?> ReceiveAsync(HandoffPackage package, bool readBack, CancellationToken ct)
        {
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
