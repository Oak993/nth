using Marrow.Core.Support;

namespace Marrow.Setup.Tests.Fakes;

/// <summary>An <see cref="ISetupRunningProbe"/> with an answer a test can change that counts how often it was asked (nth installer PLAN.md 5, unit U9).</summary>
public sealed class FakeSetupProbe(bool running) : ISetupRunningProbe
{
    private volatile bool answer = running;

    /// <summary>How many times a sweep asked.</summary>
    public int Asked => Volatile.Read(ref asked);

    private int asked;

    /// <summary>What the next question gets: a test ends the setup by setting this to false.</summary>
    public bool Running
    {
        get => answer;
        set => answer = value;
    }

    public bool IsSetupRunning()
    {
        Interlocked.Increment(ref asked);
        return answer;
    }
}
