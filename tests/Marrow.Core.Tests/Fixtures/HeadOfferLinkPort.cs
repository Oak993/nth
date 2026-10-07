using Marrow.Core.Port.MultiAccount.Thread;

namespace Marrow.Core.Tests.Port.MultiAccount;

internal sealed class HeadOfferLinkPort(IReadOnlyList<Guid> initial) : ILinkPort
{
    private readonly List<Guid> linked = initial.ToList();
    public List<(Guid Id, bool Link)> Actions { get; } = [];
    public event Action<Guid>? Changed;
    public IReadOnlyList<Guid> Linked(Guid thread) => linked.ToArray();
    public void Link(Guid thread, Guid id) { linked.Add(id); Actions.Add((id, true)); Changed?.Invoke(thread); }
    public void Unlink(Guid thread, Guid id) { linked.Remove(id); Actions.Add((id, false)); Changed?.Invoke(thread); }
}
