// PORT-ONLY: the fake desktop's quiet peek (ED-NEW-pcctl-46): ending it runs its end once.
namespace Marrow.Services.Tests.Fixtures;

public sealed class PcFakePeek(Action end) : IDisposable
{
    private int ended;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref ended, 1) == 0) end();
    }
}
