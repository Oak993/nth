// PORT-ONLY: Tactus (ED-NEW-pcctl-24). A glass for the pill host that records what it was asked and answers as it is told.
using Marrow.Core.Port.PcControl.Pill;
using Marrow.UI.Port.PcControl;

namespace Marrow.App.Tests.Fixtures;

public sealed class PcFakePillGlass(bool available) : IPillGlass
{
    public bool Available { get; set; } = available;

    public string? Failure => Available ? null : "fake: no composition";

    public List<(PillRect Drawn, double Radius)> Shown { get; } = [];

    public int Hides { get; private set; }

    public bool Disposed { get; private set; }

    public bool Show(PillRect drawn, double radius)
    {
        Shown.Add((drawn, radius));
        return Available;
    }

    public void Hide() => Hides++;

    public void Dispose() => Disposed = true;
}
