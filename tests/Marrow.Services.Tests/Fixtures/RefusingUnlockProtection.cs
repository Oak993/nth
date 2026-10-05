// PORT-ONLY: SK-13 / DL-16. A protection double whose unlock fails for chosen paths and passes the rest on to the wrapped one.
using Marrow.Services.Port.Skills.Store;

namespace Marrow.Services.Tests.Fixtures;

internal sealed class RefusingUnlockProtection(IStoreProtection inner, Func<string, bool> refuse) : IStoreProtection
{
    public List<string> Unlocked { get; } = [];

    public void Lock(string folder) => inner.Lock(folder);

    public bool IsLocked(string path) => inner.IsLocked(path);

    public bool HasNamedStreams(string path) => inner.HasNamedStreams(path);

    public void Unlock(string path)
    {
        if (refuse(path)) throw new UnauthorizedAccessException("Fixture refuses to unlock " + path);
        inner.Unlock(path);
        Unlocked.Add(path);
    }
}
