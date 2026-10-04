// PORT-ONLY: SK-9 background builds exercise SK-5's intentionally simple test double through serialized individual ACL operations.
using Marrow.Services.Port.Skills.Store;

namespace Marrow.Services.Tests.Port.Skills.Generations;

public sealed class SerializedGenerationProtection(IStoreProtection inner) : IStoreProtection
{
    private readonly object gate = new();
    public void Lock(string path) { lock (gate) inner.Lock(path); }
    public void Unlock(string path) { lock (gate) inner.Unlock(path); }
    public bool IsLocked(string path) { lock (gate) return inner.IsLocked(path); }
    public bool HasNamedStreams(string path) { lock (gate) return inner.HasNamedStreams(path); }
}
