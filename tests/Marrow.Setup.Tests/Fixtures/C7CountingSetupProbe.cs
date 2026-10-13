using Marrow.Core.Support;

namespace Marrow.Setup.Tests.Fixtures;

internal sealed class C7CountingSetupProbe : ISetupRunningProbe
{
    public int Asked { get; private set; }
    public bool IsSetupRunning() { Asked++; return false; }
}
