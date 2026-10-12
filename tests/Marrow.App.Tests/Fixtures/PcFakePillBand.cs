// PORT-ONLY: Tactus (ED-NEW-pcctl-23). One fake band: fixed handles, counts of re-asserts, closed or not.
using Marrow.App.Port.PcControl.Overlay;

namespace Marrow.App.Tests.Fixtures;

public sealed class PcFakePillBand(nint input, nint shadow) : IPillBand
{
    public nint Input { get; } = input;

    public nint Shadow { get; } = shadow;

    public int Reasserts { get; private set; }

    public bool Closed { get; private set; }

    public void Reassert() => Reasserts++;

    public void Close() => Closed = true;
}
