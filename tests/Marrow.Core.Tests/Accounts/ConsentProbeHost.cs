using Marrow.Core.Accounts;

namespace Marrow.Core.Tests.Accounts;

/// <summary>A host that tells the test, at the moment of every call, what the machine is doing, so consent can be judged at the actual transfer boundary and changed mid-flight.</summary>
public sealed class ConsentProbeHost : IThreadAccountHost, IIncomingBriefingHost
{
    public Action<string, SwitchTarget?, HandoffPackage?, bool> OnCall { get; set; } = static (_, _, _, _) => { };
    public int Calls { get; private set; }
    public List<string> Log { get; } = [];
    public string ActiveAccountId { get; set; } = "";
    public Pill? Pill => null;
    public int? StepIndex => null;
    public bool Pinned { get; set; }
    public bool AtSafePoint { get; set; } = true;
    public bool ReplyInFlight { get; set; }

    private async Task Enter(string name, SwitchTarget? target = null, HandoffPackage? package = null, bool resume = false)
    {
        Calls++;
        Log.Add(name + (target is null ? "" : ":" + target.Account.Id));
        OnCall(name, target, package, resume);
        await Task.Yield();
    }

    public async Task<IStandbySession> StartStandbyAsync(SwitchTarget target, CancellationToken ct)
    {
        await Enter("standby", target);
        return new Standby(this, target);
    }

    public async Task<string?> RequestBriefingAsync(HandoffSummary summary, CancellationToken ct)
    {
        await Enter("briefing");
        return "briefing";
    }

    public async Task<string?> RequestIncomingBriefingAsync(SwitchTarget target, HandoffSummary summary, CancellationToken ct)
    {
        await Enter("light-briefing", target);
        return "light";
    }

    public async Task SwitchAsync(IStandbySession standby, SwitchTarget target, bool resumeReply, CancellationToken ct)
    {
        await Enter("switch", target, null, resumeReply);
        ActiveAccountId = target.Account.Id;
        ReplyInFlight = false;
    }

    private sealed class Standby(ConsentProbeHost host, SwitchTarget target) : IStandbySession
    {
        public async Task<string?> ReceiveAsync(HandoffPackage package, bool readBack, CancellationToken ct)
        {
            await host.Enter("receive", target, package);
            return readBack ? "plan" : null;
        }

        public async Task FillGapsAsync(IReadOnlyList<string> gaps, CancellationToken ct) => await host.Enter("gaps", target);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
