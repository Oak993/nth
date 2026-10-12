using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Packaging;

public sealed class C4cHiddenSmokeTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void Smoke_CreateProcess_requests_the_hidden_first_GUI_show_state()
    {
        var source = File.ReadAllText(Path.Combine(PackagingRig.Scripts, "lib", "SmokeProcess.cs"));
        Assert.Contains("private const uint StartFUseShowWindow = 0x00000001;", source, StringComparison.Ordinal);
        Assert.Contains("private const ushort SwHide = 0;", source, StringComparison.Ordinal);
        Assert.Contains("Flags = StartFUseShowWindow, ShowWindow = SwHide", source, StringComparison.Ordinal);
        Assert.Contains("ref startup, out child", source, StringComparison.Ordinal);
        Assert.Contains("CreateSuspended | CreateNoWindow", source, StringComparison.Ordinal);
    }
}
