// PORT-ONLY: MA-3b-5, page :2715, :2751-2756 and OD-0120. Only job-0 ILinkPort; E1 owns checking work.
using Marrow.Core.Port.MultiAccount.Thread;
using Marrow.Core.Port.MultiAccount.Opinions;
namespace Marrow.Core.Port.MultiAccount.Link;
public sealed class LinkUpModel : IDisposable
{
    private readonly Guid thread;
    private readonly IThreadAccountPort current;
    private readonly IReadOnlyList<IThreadAccountPort> accounts;
    private readonly ILinkPort port;
    private readonly OtherServiceConsentStore consent;
    private Guid? flight;
    private bool remember;
    public LinkUpModel(Guid thread, IThreadAccountPort current, IReadOnlyList<IThreadAccountPort> accounts, ILinkPort port, OtherServiceConsentStore consent)
    {
        this.thread = thread; this.current = current; this.accounts = accounts.Append(current).Distinct().ToArray(); this.port = port; this.consent = consent;
        foreach (var account in this.accounts) account.Changed += Refresh; port.Changed += LinksChanged;
    }
    public event Action? Changed;
    public IReadOnlyList<ThreadAccount> Accounts => accounts.SelectMany(p => p.Accounts).DistinctBy(a => a.Id).ToArray();
    public IReadOnlyList<ThreadAccount> Linked => port.Linked(thread).Select(id => Accounts.FirstOrDefault(a => a.Id == id)).Where(a => a is not null && a.Id != current.ActiveAccountId).Select(a => a!).ToArray();
    public ThreadAccount? Active => Accounts.FirstOrDefault(a => a.Id == current.ActiveAccountId);
    public ThreadAccount? Pending { get; private set; }
    public bool DontAskAgain { get; set; }
    public Guid? FlyingAccountId => flight;
    public bool Busy => flight is not null || Pending is not null;
    public bool CanLink(Guid id) => Accounts.FirstOrDefault(a => a.Id == id) is { Enabled: true, IsOut: false } && id != current.ActiveAccountId;
    public bool IsLinked(Guid id) => port.Linked(thread).Contains(id);
    public string Aria(Guid id) => Accounts.FirstOrDefault(a => a.Id == id) is not { } a ? "" : IsLinked(id) ? "Unlink " + a.Name : "Link " + a.Name + " to check this thread’s work";
    public bool RequestToggle(Guid id)
    {
        if (Busy || !CanLink(id)) return false;
        if (IsLinked(id)) { port.Unlink(thread, id); return false; }
        var a = Accounts.Single(a => a.Id == id); remember = false; DontAskAgain = false;
        if (a.Service != current.Service && !consent.Permits(ConsentPurpose.Link, a.Service)) { Pending = a; Changed?.Invoke(); return false; }
        flight = id; Changed?.Invoke(); return true;
    }
    public Guid? Confirm(bool accepted)
    {
        if (Pending is not { } a) return null;
        Pending = null;
        if (!accepted || !CanLink(a.Id)) { DontAskAgain = false; Changed?.Invoke(); return null; }
        remember = DontAskAgain; flight = a.Id; Changed?.Invoke(); return a.Id;
    }
    public void Complete(Guid id)
    {
        if (flight != id) return;
        flight = null;
        if (!CanLink(id)) { remember = false; Changed?.Invoke(); return; }
        var a = Accounts.Single(a => a.Id == id);
        if (remember) consent.Remember(ConsentPurpose.Link, a.Service);
        remember = false; port.Link(thread, id); Changed?.Invoke();
    }
    public void CancelFlight(Guid id) { if (flight == id) CancelFlight(); }
    public void CancelFlight() { flight = null; remember = false; Changed?.Invoke(); }
    public void Unlink(Guid id) { if (IsLinked(id)) port.Unlink(thread, id); }
    private void LinksChanged(Guid changed) { if (changed == thread) Changed?.Invoke(); }
    private void Refresh()
    {
        if (Pending is { } a && !CanLink(a.Id)) { Pending = null; DontAskAgain = false; }
        if (flight is { } id && !CanLink(id)) { flight = null; remember = false; }
        Changed?.Invoke();
    }
    public void Dispose() { foreach (var account in accounts) account.Changed -= Refresh; port.Changed -= LinksChanged; }
}
