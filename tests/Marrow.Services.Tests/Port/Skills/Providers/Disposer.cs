// PORT-ONLY test fixture (SK-11f). Runs one cleanup when disposed, so a test helper can hand back a single thing to put in a using.
namespace Marrow.Services.Tests.Port.Skills.Providers;

public sealed class Disposer(Action cleanup) : IDisposable
{
    private Action? pending = cleanup;
    public void Dispose() => Interlocked.Exchange(ref pending, null)?.Invoke();
}
