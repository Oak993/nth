// PORT-ONLY: SK-5, PLAN 2.1 in-memory lease per session/probe. Releasing twice is harmless.
namespace Marrow.Services.Port.Skills.Store;

public sealed class StoreLease : IDisposable
{
    private Action? release;
    internal StoreLease(string folder, Action release)
    {
        Folder = folder;
        this.release = release;
    }
    public string Folder { get; }
    public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
}
