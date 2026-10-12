using Marrow.Core.Support;

namespace Marrow.Setup.Tests.Fakes;

/// <summary>An <see cref="ISetupRunningProbe"/> with a fixed answer that counts how often it was asked (nth installer PLAN.md 5, unit U9).</summary>
public sealed class FakeSetupProbe(bool running) : ISetupRunningProbe
{
    /// <summary>How many times a sweep asked.</summary>
    public int Asked { get; private set; }

    public bool IsSetupRunning()
    {
        Asked++;
        return running;
    }
}
