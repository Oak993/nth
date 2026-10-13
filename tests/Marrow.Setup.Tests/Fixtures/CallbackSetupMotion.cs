using Marrow.Setup.Motion;

namespace Marrow.Setup.Tests.Fixtures;

public sealed class CallbackSetupMotion(Func<double, bool> advance) : IMotion
{
    public bool Advance(double seconds) => advance(seconds);
}
