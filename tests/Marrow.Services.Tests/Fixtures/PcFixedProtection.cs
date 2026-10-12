using Marrow.Services.Port.PcControl;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY: Tactus, PC control (ED-NEW-pcctl-17). Whether the foreground may be borrowed, as a test sets it.
public sealed class PcFixedProtection(PcForegroundProtection value) : IForegroundProtection
{
    public PcForegroundProtection Value { get; set; } = value;

    public PcForegroundProtection Read() => Value;
}
