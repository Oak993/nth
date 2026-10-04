// PORT-ONLY: Linux has POSIX unlink semantics; inject a Windows sharing failure for portable GC tests.
using Marrow.Services.Port.Skills.Store;

namespace Marrow.Services.Tests.Port.Skills.Store.Fixtures;

public sealed class FakeStoreDeletion : IStoreDeletion
{
    public HashSet<string> Held { get; } = new(StringComparer.Ordinal);
    public Action<string>? BeforeDelete { get; set; }
    public void Delete(string folder)
    {
        BeforeDelete?.Invoke(folder);
        if (Held.Contains(folder)) throw new IOException("Fixture sharing violation");
        StoreDeletion.Instance.Delete(folder);
    }
}
