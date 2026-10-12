using Marrow.Services.Port.PcControl.Native;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: PC control (OD-0140). DESIGN.md 4.2: high integrity and above are refused; medium and low are not.
[Trait("Decision", "ED-NEW-pcctl-1")]
public sealed class SecurityProbeTests
{
    [Theory]
    [InlineData(0x0000u, false)]
    [InlineData(0x1000u, false)]
    [InlineData(0x2000u, false)]
    [InlineData(0x2100u, false)]
    [InlineData(0x3000u, true)]
    [InlineData(0x4000u, true)]
    [InlineData(0x5000u, true)]
    public void High_system_and_protected_integrity_levels_count_as_high(uint rid, bool high)
    {
        Assert.Equal(high, SecurityProbe.IsHighIntegrity(rid));
    }

    [Fact]
    public void Display_scale_is_dpi_over_96()
    {
        Assert.Equal(1.0, Dpi.Scale(96));
        Assert.Equal(1.25, Dpi.Scale(120));
        Assert.Equal(1.5, Dpi.Scale(144));
        Assert.Equal(2.25, Dpi.Scale(216));
        Assert.Equal(1.0, Dpi.Scale(0));
    }
}
