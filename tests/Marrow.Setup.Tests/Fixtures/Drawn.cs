using Avalonia;

namespace Marrow.Setup.Tests.Fixtures;

/// <summary>One element's drawn place and opacity in one frame (the frame probe's capture).</summary>
public readonly struct Drawn(Rect box, double opacity)
{
    public Rect Box { get; } = box;

    public double Opacity { get; } = opacity;
}
