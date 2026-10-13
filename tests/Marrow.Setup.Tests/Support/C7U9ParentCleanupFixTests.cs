using Marrow.Core.Support;
using Marrow.Setup.Tests.Fixtures;

namespace Marrow.Setup.Tests.Support;

public sealed class C7U9ParentCleanupFixTests : IDisposable
{
    private readonly SweepArea area = new();
    public void Dispose() => area.Dispose();

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Removing_a_missing_runtime_build_does_not_delete_foreign_empty_parents_through_a_root_junction()
    {
        var foreign = Path.Combine(area.Base, "foreign");
        var runtime = Directory.CreateDirectory(Path.Combine(foreign, "runtime")).FullName;
        C7DisposableJunction.Create(area.Base, area.SetupRoot, foreign);
        try
        {
            _ = SetupSweeper.RemoveFolder(area.LocalAppData, Path.Combine(area.SetupRoot, "runtime", "missing-build"));
            Assert.True(Directory.Exists(runtime), "parent cleanup must honor every ancestor link even for an absent build");
        }
        finally { Directory.Delete(area.SetupRoot, recursive: false); }
    }
}
