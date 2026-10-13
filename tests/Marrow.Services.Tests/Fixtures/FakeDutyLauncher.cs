using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>PORT-ONLY (AS-10): records every run the scheduler asks for and answers with a mission id, a refusal, or after a gate the test opens.</summary>
public sealed class FakeDutyLauncher : IDutyLauncher
{
    private readonly List<DutyFire> fires = [];

    public IReadOnlyList<DutyFire> Fires
    {
        get
        {
            lock (fires) return [.. fires];
        }
    }

    public Guid? LastMission { get; private set; }

    public string? Refuse { get; set; }

    public Exception? Throw { get; set; }

    /// <summary>When set, the launcher ran the whole run inside Launch (nth's own digest): the run ends at once with this outcome and report.</summary>
    public DutyRunOutcome? FinishAs { get; set; }

    public string? FinishReport { get; set; }

    /// <summary>When set, a launch waits for it (a slow mission start).</summary>
    public TaskCompletionSource? Gate { get; set; }

    public async Task<DutyLaunch> Launch(DutyFire fire)
    {
        lock (fires) fires.Add(fire);
        if (Gate is { } gate) await gate.Task.ConfigureAwait(false);
        if (Throw is { } failure) throw failure;
        if (Refuse is { } why) return DutyLaunch.Refusal(why);
        LastMission = Guid.NewGuid();
        return FinishAs is { } finished ? DutyLaunch.Completed(LastMission.Value, finished, FinishReport) : DutyLaunch.Started(LastMission.Value);
    }
}
