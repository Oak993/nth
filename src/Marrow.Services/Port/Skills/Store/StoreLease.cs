// PORT-ONLY: SK-5, PLAN 2.1 in-memory lease per live session or probe.
namespace Marrow.Services.Port.Skills.Store;

public sealed class StoreLease : IDisposable
{
    public string Folder => throw new NotImplementedException();
    public void Dispose() => throw new NotImplementedException();
}
