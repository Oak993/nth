using Marrow.Core.Support;
using Marrow.Setup.Tests.Fixtures;

namespace Marrow.Setup.Tests.Support;

// Read-only C7 U9 review: all paths are disposable stand-ins for LocalAppData and Temp.
// No installer, application, child process, junction creation or ACL change is involved.
public sealed class C7U9ReviewReproTests : IDisposable
{
    private readonly SweepArea area = new();

    public void Dispose() => area.Dispose();

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_root_that_cannot_be_enumerated_is_reported_as_not_removed_instead_of_clean()
    {
        // File.GetAttributes / Exists can see this entry, but directory enumeration fails.
        // The entry must be preserved and the refusal must be visible in SweepResult.Kept.
        area.Put(area.SetupRoot, "foreign file occupying the reserved staging-folder path");

        var result = SetupSweeper.Sweep(area.LocalAppData, area.Temp);

        Assert.True(File.Exists(area.SetupRoot), "the foreign entry must survive");
        Assert.False(result.Clean, "a failed root enumeration cannot certify cleanup as complete");
        var kept = Assert.Single(result.Kept);
        Assert.Equal(area.SetupRoot, kept.Path);
        Assert.Equal(SweepKeptReason.NotRemoved, kept.Reason);
    }
}
