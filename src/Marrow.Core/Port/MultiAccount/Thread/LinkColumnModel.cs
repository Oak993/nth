// PORT-ONLY: RR-49 / ED-NEW-ma-gaps-3. Head-only link-column projection; ordinary account rows retain every service.
namespace Marrow.Core.Port.MultiAccount.Thread;

public sealed class LinkColumnModel : IDisposable
{
    private readonly Guid thread;
    private readonly IThreadAccountPort current;
    private readonly IReadOnlyList<IThreadAccountPort> ports;
    private readonly ILinkPort links;
    private readonly Func<ThreadAccount, bool> hasRoom;

    public LinkColumnModel(Guid thread, IThreadAccountPort current, IReadOnlyList<IThreadAccountPort> ports,
        ILinkPort links, Func<ThreadAccount, bool>? hasRoom = null)
    {
        if (thread == Guid.Empty) throw new ArgumentException("A thread is required.", nameof(thread));
        this.thread = thread; this.current = current; this.ports = ports.Append(current).Distinct().ToArray();
        this.links = links; this.hasRoom = hasRoom ?? (a => !a.IsOut);
        foreach (var port in this.ports) port.Changed += Refresh;
        links.Changed += LinksChanged;
    }

    public event Action? Changed;
    public IReadOnlyList<ThreadAccount> Rows => ports.SelectMany(p => p.Accounts).DistinctBy(a => a.Id)
        .Where(a => a.Id != current.ActiveAccountId && a.Enabled && a.CanBeConversationHead && hasRoom(a)).ToArray();
    public IReadOnlyList<Guid> Linked => links.Linked(thread).Where(id => Rows.Any(a => a.Id == id)).ToArray();

    public void Toggle(Guid account)
    {
        // Re-read live capability/availability before honoring a stale UI tap.
        if (!Rows.Any(a => a.Id == account)) return;
        if (Linked.Contains(account)) links.Unlink(thread, account);
        else links.Link(thread, account);
    }

    private void Refresh() => Changed?.Invoke();
    private void LinksChanged(Guid changedThread) { if (changedThread == thread) Refresh(); }
    public void Dispose()
    {
        foreach (var port in ports) port.Changed -= Refresh;
        links.Changed -= LinksChanged;
    }
}
