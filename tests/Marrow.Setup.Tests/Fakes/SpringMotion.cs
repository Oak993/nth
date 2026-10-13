using Marrow.Setup.Motion;

namespace Marrow.Setup.Tests.Fakes;

/// <summary>A spring driven by the window's loop, recording the steps it was given.</summary>
public sealed class SpringMotion(Spring spring) : IMotion
{
    public int Calls { get; private set; }

    public double LastStep { get; private set; }

    public bool Advance(double seconds)
    {
        Calls++;
        LastStep = seconds;
        return spring.Advance(seconds);
    }
}
