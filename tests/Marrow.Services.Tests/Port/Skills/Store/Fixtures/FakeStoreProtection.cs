// PORT-ONLY: an explicit test double, never selected by production on Linux.
using Marrow.Services.Port.Skills.Store;

namespace Marrow.Services.Tests.Port.Skills.Store.Fixtures;

public sealed class FakeStoreProtection : IStoreProtection
{
    private readonly HashSet<string> locked = new(StringComparer.Ordinal);
    public List<string> Events { get; } = [];
    public HashSet<string> Streams { get; } = new(StringComparer.Ordinal);
    public bool Refuse { get; set; }
    public Action<string>? BeforeLock { get; set; }
    public void Lock(string folder)
    {
        BeforeLock?.Invoke(folder);
        if (Refuse) throw new IOException("Fixture protection unavailable");
        foreach (var path in StoreTrees.Entries(folder).Prepend(folder)) locked.Add(path);
        Events.Add("lock:" + folder);
    }
    public bool IsLocked(string path) => !Refuse && locked.Contains(path);
    public bool HasNamedStreams(string path)
    {
        if (Refuse) throw new IOException("Fixture stream inspection unavailable");
        return Streams.Contains(path);
    }
    public void Unlock(string path)
    {
        if (Refuse) throw new IOException("Fixture unlock unavailable");
        locked.Remove(path);
        Events.Add("unlock:" + path);
    }
}
