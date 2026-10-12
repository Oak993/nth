using Marrow.Setup.Install;
using Marrow.Setup.Tests.Fakes;

namespace Marrow.Setup.Tests.Install;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 4 ("Free space" and the one size, T_disk) and VELOPACK_CHECK.md "Also
// found" (Velopack's own threshold); unit U3 (ED-NEW-inst-u3-1). The threshold, the message word for word, a sentence that
// never contradicts itself, an unknown answer, the drive's name, the sizes stamped by the packaging script, and the real
// GetDiskFreeSpaceEx on a folder that does not exist yet.
public sealed class FreeSpaceCheckTests
{
    private const long Mb = InstallSizes.Megabyte;
    private static readonly InstallSizes Sizes = new(118 * Mb, 302 * Mb, 420 * Mb, 0, Measured: true);
    private const string Root = @"C:\Users\Ana-María Østergård-Fernández\AppData\Local\Programs\nth";

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_fresh_install_needs_T_disk_plus_10_percent_and_an_update_T_pkg_plus_T_app_plus_10_percent()
    {
        Assert.Equal((long)Math.Ceiling(420 * Mb * 1.1), FreeSpaceCheck.Needed(Sizes, update: false));
        var bigger = Sizes with { DiskBytes = 440 * Mb };
        Assert.Equal((long)Math.Ceiling(440 * Mb * 1.1), FreeSpaceCheck.Needed(bigger, update: false));
        Assert.Equal((long)Math.Ceiling((118 + 302) * Mb * 1.1), FreeSpaceCheck.Needed(bigger, update: true));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Velopack_s_own_threshold_wins_when_it_is_larger()
    {
        var sizes = Sizes with { VelopackNeedBytes = 470 * Mb };
        Assert.Equal(470 * Mb, FreeSpaceCheck.Needed(sizes, update: false));
        Assert.Equal(470 * Mb, FreeSpaceCheck.Needed(sizes, update: true));
        Assert.False(FreeSpaceCheck.Check(sizes, false, Root, new FakeDiskSpace(465 * Mb)).Enough);
        Assert.True(FreeSpaceCheck.Check(sizes, false, Root, new FakeDiskSpace(470 * Mb)).Enough);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Not_enough_says_the_plan_s_sentence_with_T_disk_and_the_free_megabytes_rounded_down()
    {
        var verdict = FreeSpaceCheck.Check(Sizes, update: false, Root, new FakeDiskSpace(112 * Mb + Mb - 1));
        Assert.False(verdict.Enough);
        Assert.Equal("There isn’t enough free space on drive C:. nth needs about 420 MB, and 112 MB is free.", verdict.Message);
        Assert.Null(FreeSpaceCheck.Check(Sizes, false, Root, new FakeDiskSpace(500 * Mb)).Message);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void When_T_disk_is_free_but_not_the_threshold_the_sentence_names_the_threshold_so_it_never_contradicts_itself()
    {
        var sizes = Sizes with { VelopackNeedBytes = 470 * Mb };
        var verdict = FreeSpaceCheck.Check(sizes, update: false, Root, new FakeDiskSpace(440 * Mb));
        Assert.Equal("There isn’t enough free space on drive C:. nth needs about 470 MB, and 440 MB is free.", verdict.Message);
        var close = FreeSpaceCheck.Check(sizes, update: false, Root, new FakeDiskSpace(470 * Mb - 1));
        Assert.True(close.ShownMegabytes > close.FreeMegabytes);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void When_Windows_cannot_say_the_install_goes_ahead_and_Velopack_checks_again()
    {
        var verdict = FreeSpaceCheck.Check(Sizes, update: false, Root, new FakeDiskSpace(null));
        Assert.True(verdict.Enough);
        Assert.Null(verdict.FreeBytes);
    }

    [Theory]
    [InlineData(@"C:\Users\ana\AppData\Local\Programs\nth", "C:")]
    [InlineData(@"D:\nth", "D:")]
    [InlineData(@"E:\", "E:")]
    [InlineData(@"\\server\share\Apps\nth", @"\\server\share")]
    [Trait("Decision", "ED-247")]
    public void The_drive_is_named_as_Explorer_names_it(string root, string drive) => Assert.Equal(drive, FreeSpaceCheck.Drive(root));

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_stamped_sizes_are_read_and_a_build_without_them_uses_the_plan_s_figures()
    {
        var stamped = new Dictionary<string, string?>
        {
            [InstallSizes.PackageKey] = "123725000",
            [InstallSizes.AppKey] = "316669952",
            [InstallSizes.DiskKey] = "440500000",
            [InstallSizes.VelopackNeedKey] = "490000000",
        };
        var sizes = InstallSizes.FromMetadata(k => stamped.GetValueOrDefault(k));
        Assert.True(sizes.Measured);
        Assert.Equal(123725000 + 316669952, sizes.CopyBytes);
        Assert.Equal(420, sizes.DiskMegabytes);
        Assert.Equal(490000000, sizes.VelopackNeedBytes);
        Assert.Equal(InstallSizes.Estimate, InstallSizes.FromMetadata(_ => null));
        Assert.Equal(InstallSizes.Estimate, InstallSizes.FromMetadata(k => k == InstallSizes.DiskKey ? "1" : stamped.GetValueOrDefault(k)));
        Assert.Equal(InstallSizes.Estimate, InstallSizes.FromMetadata(k => k == InstallSizes.AppKey ? "-5" : stamped.GetValueOrDefault(k)));
        Assert.Equal(0, InstallSizes.FromMetadata(k => k == InstallSizes.VelopackNeedKey ? null : stamped.GetValueOrDefault(k)).VelopackNeedBytes);
        Assert.False(InstallSizes.Estimate.Measured);
        Assert.Equal(420, InstallSizes.Estimate.DiskMegabytes);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_project_passes_each_measured_size_through_as_assembly_metadata()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Marrow.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var project = File.ReadAllText(Path.Combine(directory.FullName, "src", "Marrow.Setup", "Marrow.Setup.csproj"));
        foreach (var key in new[] { InstallSizes.PackageKey, InstallSizes.AppKey, InstallSizes.DiskKey, InstallSizes.VelopackNeedKey })
            Assert.Contains($"<AssemblyMetadata Include=\"{key}\" Value=\"$({key})\" />", project, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Megabytes_are_Windows_megabytes_rounded_to_the_nearest_and_free_space_rounds_down()
    {
        Assert.Equal(1_048_576, InstallSizes.Megabyte);
        Assert.Equal(420, InstallSizes.Megabytes(420 * Mb + Mb / 2 - 1));
        Assert.Equal(421, InstallSizes.Megabytes(420 * Mb + Mb / 2));
        Assert.Equal(420, InstallSizes.MegabytesDown(421 * Mb - 1));
        Assert.Equal(0, InstallSizes.Megabytes(-5));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public void The_real_check_asks_the_nearest_folder_that_exists_and_reads_nothing_else()
    {
        var missing = Path.Combine(Path.GetTempPath(), "nth-u3-" + Guid.NewGuid().ToString("N"), "Programs", "nth");
        var free = new WindowsDiskSpace().FreeBytes(missing);
        Assert.NotNull(free);
        Assert.True(free > 0);
        Assert.False(Directory.Exists(missing));
    }
}
