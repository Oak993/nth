using Marrow.Core.Accounts;
using Marrow.Core.Port;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;

namespace Marrow.App.Runtime;

// PORT-ONLY: ED-NEW-inst-c7-5. The primed standby's later private turns need their own admission.
internal sealed class InstallerStandbySession(AppModel app, Guid thread, IAdoptableStandby inner) : IAdoptableStandby
{
    internal IAdoptableStandby Inner { get; } = inner;
    public StandbyKind Kind => Inner.Kind;
    public SessionConfiguration Configuration => Inner.Configuration;
    public string SessionId => Inner.SessionId;
    public HandoffPackage? ReceivedPackage => Inner.ReceivedPackage;
    public bool ReadBackRequested => Inner.ReadBackRequested;
    public Task PrepareForAdoptionAsync(CancellationToken ct) => Inner.PrepareForAdoptionAsync(ct);
    public ProviderSession TakeSession() => Inner.TakeSession();
    public Task<string?> ReceiveAsync(HandoffPackage package, bool readBack, CancellationToken ct) => MainActor.Invoke(async () =>
    {
        ct.ThrowIfCancellationRequested();
        using var admission = Kind == StandbyKind.Primed ? app.AdmitInstallerConversationWork(thread) : null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, admission?.StopToken ?? CancellationToken.None);
        return await Inner.ReceiveAsync(package, readBack, linked.Token);
    });
    public Task FillGapsAsync(IReadOnlyList<string> gaps, CancellationToken ct) => MainActor.Invoke(async () =>
    {
        ct.ThrowIfCancellationRequested();
        using var admission = Kind == StandbyKind.Primed ? app.AdmitInstallerConversationWork(thread) : null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, admission?.StopToken ?? CancellationToken.None);
        await Inner.FillGapsAsync(gaps, linked.Token);
    });
    public ValueTask DisposeAsync() => Inner.DisposeAsync();
}
