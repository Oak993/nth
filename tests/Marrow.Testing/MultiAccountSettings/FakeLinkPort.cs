using Marrow.Core.Port.MultiAccount.Thread;

namespace Marrow.Testing.MultiAccountSettings;

/// <summary>Link up for tests: remembers who is linked to which thread and records every call; nothing checks any answer (MA-10 fixtures).</summary>
public sealed class FakeLinkPort : ILinkPort
{
    private readonly Dictionary<Guid, List<Guid>> links = [];

    public event Action<Guid>? Changed;

    /// <summary>Every call in order: "link" or "unlink", the thread, the account.</summary>
    public List<(string Call, Guid Thread, Guid Account)> Calls { get; } = [];

    public IReadOnlyList<Guid> Linked(Guid thread) => links.TryGetValue(thread, out var list) ? [.. list] : [];

    public void Link(Guid thread, Guid account)
    {
        Calls.Add(("link", thread, account));
        if (!links.TryGetValue(thread, out var list)) links[thread] = list = [];
        if (list.Contains(account)) return;
        list.Add(account);
        Changed?.Invoke(thread);
    }

    public void Unlink(Guid thread, Guid account)
    {
        Calls.Add(("unlink", thread, account));
        if (links.GetValueOrDefault(thread)?.Remove(account) != true) return;
        Changed?.Invoke(thread);
    }
}
