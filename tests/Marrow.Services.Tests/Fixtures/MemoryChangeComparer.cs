using Marrow.Services.Assistant;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>Dextra AS-12: two memory changes are the same when their kind and ids are.</summary>
public sealed class MemoryChangeComparer : IEqualityComparer<MemoryChange>
{
    public bool Equals(MemoryChange? x, MemoryChange? y) => x?.Kind == y?.Kind && x!.Ids.SequenceEqual(y!.Ids);

    public int GetHashCode(MemoryChange obj) => (int)obj.Kind;
}
