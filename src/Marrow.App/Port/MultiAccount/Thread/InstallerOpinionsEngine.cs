using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount.Thread;
using Marrow.Services.Port.MultiAccount;

namespace Marrow.App.Port.MultiAccount.Thread;

// PORT-ONLY: ED-NEW-inst-c7-5. App admission spans context preparation and the entire private round.
internal sealed class InstallerOpinionsEngine(AppModel app, OpinionsEngine engine) : IOpinionsPort, IDisposable
{
    public event Action<Guid>? Changed { add => engine.Changed += value; remove => engine.Changed -= value; }
    public IReadOnlyList<Guid> EveryoneWithRoom(Guid thread) => engine.EveryoneWithRoom(thread);
    public IReadOnlyList<OpinionRound> Rounds(Guid thread) => engine.Rounds(thread);
    public IReadOnlyDictionary<Guid, string> Errors(Guid thread, Guid round) => engine.Errors(thread, round);
    public Task WaitAsync(Guid thread, Guid round) => engine.WaitAsync(thread, round);
    public void Cancel(Guid thread, Guid round) => engine.Cancel(thread, round);
    public Task<Guid> AskAsync(Guid thread, string message, IReadOnlyList<Guid> accounts, CancellationToken ct = default) =>
        AskAsync(thread, message, accounts, null, ct);
    public Task<Guid> AskAsync(Guid thread, string message, IReadOnlyList<Guid> accounts, Action<Guid>? started, CancellationToken ct = default) =>
        MainActor.Invoke(async () =>
        {
            ct.ThrowIfCancellationRequested();
            var admission = app.AdmitInstallerConversationWork(thread);
            var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, admission.StopToken);
            try
            {
                var round = await engine.AskAsync(thread, message, accounts, started, linked.Token);
                _ = ReleaseAfterRound(engine.WaitAsync(thread, round), admission, linked);
                return round;
            }
            catch { linked.Dispose(); admission.Dispose(); throw; }
        });
    private static async Task ReleaseAfterRound(Task completion, InstallerTurnLease admission, CancellationTokenSource linked)
    {
        try { await completion; }
        catch { /* The original WaitAsync still exposes round failure to its caller. */ }
        finally { linked.Dispose(); admission.Dispose(); }
    }
    public Task CombineAsync(Guid thread, Guid round, CancellationToken ct = default) => MainActor.Invoke(async () =>
    {
        ct.ThrowIfCancellationRequested();
        using var admission = app.AdmitInstallerConversationWork(thread);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, admission.StopToken);
        await engine.CombineAsync(thread, round, linked.Token);
    });
    public void Dispose() => engine.Dispose();
}
