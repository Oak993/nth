using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount.Thread;
using Marrow.Services.Port.MultiAccount;

namespace Marrow.App.Port.MultiAccount.Thread;

// PORT-ONLY: ED-NEW-inst-c7-5. Private checking is admitted before context/preparation, through completion.
internal sealed class InstallerLinkEngine(AppModel app, LinkEngine engine) : ILinkPort, IDisposable
{
    public event Action<Guid>? Changed { add => engine.Changed += value; remove => engine.Changed -= value; }
    public IReadOnlyList<Guid> Linked(Guid thread) => engine.Linked(thread);
    public IReadOnlyList<LinkCheck> Checks(Guid thread) => engine.Checks(thread);
    public void Link(Guid thread, Guid account) => engine.Link(thread, account);
    public void Unlink(Guid thread, Guid account) => engine.Unlink(thread, account);
    public Task CheckCompletedAnswerAsync(Guid thread, Guid answerId, Guid author, string message, string answer, CancellationToken ct = default) =>
        Check(thread, token => engine.CheckCompletedAnswerAsync(thread, answerId, author, message, answer, token), ct);
    public Task CheckAnswerAsync(Guid thread, Guid answerId, Guid author, string message, string answer, CancellationToken ct = default) =>
        Check(thread, token => engine.CheckAnswerAsync(thread, answerId, author, message, answer, token), ct);
    private Task Check(Guid thread, Func<CancellationToken, Task> body, CancellationToken ct) => MainActor.Invoke(async () =>
    {
        ct.ThrowIfCancellationRequested();
        using var admission = app.AdmitInstallerConversationWork(thread);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, admission.StopToken);
        await body(linked.Token);
    });
    public void Dispose() => engine.Dispose();
}
