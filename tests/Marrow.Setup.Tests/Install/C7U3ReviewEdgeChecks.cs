using Marrow.Setup.Install;
using Marrow.Setup.Tests.Fakes;

namespace Marrow.Setup.Tests.Install;

public sealed class C7U3ReviewEdgeChecks
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void Totals_above_four_GB_preserve_counts_and_free_space_on_the_requested_path()
    {
        var mb = InstallSizes.Megabyte;
        var sizes = new InstallSizes(2048 * mb, 6144 * mb, 8200 * mb, 9500 * mb, true);
        var progress = new InstallProgress(false, sizes, true);
        var evidence = new ProgressEvidence { SetupStarted = true };
        var first = progress.Advance(evidence, TimeSpan.Zero);
        Assert.Equal("0 of 8200 MB", first.Steps[1].Detail);
        Assert.Equal("8200 of 8200 MB", first.Steps[1].DetailLongest);
        evidence.CopiedBytes = sizes.CopyBytes / 2;
        Assert.Equal("4100 of 8200 MB", progress.Advance(evidence, TimeSpan.FromSeconds(1)).Steps[1].Detail);
        evidence.CopiedBytes = sizes.CopyBytes;
        Assert.Equal("8200 of 8200 MB", progress.Advance(evidence, TimeSpan.FromSeconds(2)).Steps[1].Detail);
        var root = @"E:\Users\Ana-María Østergård-Fernández\" + string.Join("\\", Enumerable.Repeat("long folder name", 20)) + @"\nth";
        var disk = new FakeDiskSpace(10000 * mb);
        Assert.True(FreeSpaceCheck.Check(sizes, false, root, disk).Enough);
        Assert.Equal([root], disk.Asked);
        Assert.Equal(9500 * mb, FreeSpaceCheck.Needed(sizes, true));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Zero_and_unknown_sizes_do_not_produce_NaN_or_negative_counts()
    {
        var zero = new InstallSizes(0, 0, 0, 0, false);
        var frame = new InstallProgress(false, zero, true).Advance(new ProgressEvidence { SetupStarted = true, CopiedBytes = 0 }, TimeSpan.Zero);
        Assert.Equal("0 of 0 MB", frame.Steps[1].Detail);
        Assert.Equal(4, frame.Target);
        Assert.False(double.IsNaN(frame.Target));
        Assert.Equal(InstallSizes.Estimate, InstallSizes.FromMetadata(_ => null));
    }
}
