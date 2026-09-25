using Marrow.App.Runtime;

namespace Marrow.App.Tests.Fixtures;

internal sealed record RuntimeFixture(AppModelHarness Harness, ThreadRuntime Runtime) : IDisposable
{
    public void Dispose() => Harness.Dispose();
}
