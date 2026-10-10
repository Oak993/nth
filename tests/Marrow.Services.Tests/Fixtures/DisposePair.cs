// PORT-ONLY: two scopes disposed together, the second first (PiSessionTests opens a pair of Ambient overrides on the loop's thread).
namespace Marrow.Services.Tests.Fixtures;

internal sealed class DisposePair(IDisposable first, IDisposable second) : IDisposable
{
    public void Dispose()
    {
        second.Dispose();
        first.Dispose();
    }
}
