using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 8 and VELOPACK_CHECK.md checks 5, 7 and 14, unit U5 (ED-NEW-inst-u5-1).
// The install root's fixed names, finding the root from current\, telling a path inside the root from a look-alike, and the
// uninstaller copy (current\nth-Setup.exe to <root>\nth Uninstall.exe, replaced in one move, never half-written). The copy
// runs in a temporary folder only.
public sealed class InstallLayoutTests : IDisposable
{
    private readonly string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "nth-tests", Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); }
        catch (IOException) { } // a temporary folder; Windows cleans %TEMP%
    }

    private string Source => Path.Combine(root, "current", "nth-Setup.exe");

    private string Target => Path.Combine(root, "nth Uninstall.exe");

    private void WriteSource(byte fill, int length = 4096)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Source)!);
        File.WriteAllBytes(Source, Enumerable.Repeat(fill, length).ToArray());
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_root_names_are_Velopacks_and_nths()
    {
        Assert.Equal("NthApp", InstallLayout.AppId);
        Assert.Equal("nth Uninstall.exe", InstallLayout.RootUninstaller);
        Assert.Equal("nth-Setup.exe", InstallLayout.PayloadFreeSetup);
        Assert.Equal("nth", InstallLayout.OldStartFolder);
        Assert.Equal(@"C:\R\current", InstallLayout.CurrentDirectory(@"C:\R"));
        Assert.Equal(@"C:\R\current\nth.exe", InstallLayout.CurrentExe(@"C:\R"));
        Assert.Equal(@"C:\R\nth Uninstall.exe", InstallLayout.UninstallerPath(@"C:\R"));
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData(@"C:\Users\Ana\AppData\Local\Programs\nth\current", @"C:\Users\Ana\AppData\Local\Programs\nth")]
    [InlineData(@"C:\Users\Ana\AppData\Local\Programs\nth\current\", @"C:\Users\Ana\AppData\Local\Programs\nth")]
    [InlineData(@"D:\nth\current", @"D:\nth")]
    public void The_root_is_the_folder_above_current(string baseDirectory, string expected)
    {
        Assert.Equal(expected, InstallLayout.RootOf(baseDirectory));
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData(@"C:\R\nth", @"C:\R\nth\current\nth.exe", true)]
    [InlineData(@"C:\R\nth", @"C:\R\NTH\Marrow.App.exe", true)]
    [InlineData(@"C:\R\nth\", @"C:\R\nth\nth.exe", true)]
    [InlineData(@"C:\R\nth", @"C:\R\nth", true)]
    [InlineData(@"C:\R\nth", @"C:\R\nth\..\nth-old\nth.exe", false)]
    [InlineData(@"C:\R\nth", @"C:\R\nth-old\nth.exe", false)]
    [InlineData(@"C:\R\nth", @"C:\R\nthx", false)]
    [InlineData(@"C:\R\nth", @"nth\nth.exe", false)]
    [InlineData(@"C:\R\nth", @"%LOCALAPPDATA%\Programs\nth\nth.exe", false)]
    [InlineData(@"C:\R\nth", "", false)]
    public void Inside_the_root_means_the_root_or_below_it(string rootPath, string path, bool inside)
    {
        Assert.Equal(inside, InstallLayout.IsInside(rootPath, path));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_uninstaller_is_copied_from_current_to_the_root_and_replaced_by_a_newer_one()
    {
        WriteSource(1);

        Assert.Equal(new UninstallerCopyResult(UninstallerCopyOutcome.Copied), InstallLayout.CopyUninstaller(root));
        Assert.Equal(File.ReadAllBytes(Source), File.ReadAllBytes(Target));

        WriteSource(2, 8192); // the next version's setup
        Assert.Equal(UninstallerCopyOutcome.Copied, InstallLayout.CopyUninstaller(root).Outcome);
        Assert.Equal(File.ReadAllBytes(Source), File.ReadAllBytes(Target));
        Assert.Equal(new[] { "current", "nth Uninstall.exe" }, new DirectoryInfo(root).EnumerateFileSystemInfos().Select(entry => entry.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_package_without_nth_Setup_copies_nothing()
    {
        Directory.CreateDirectory(Path.Combine(root, "current"));

        Assert.Equal(new UninstallerCopyResult(UninstallerCopyOutcome.NoSource), InstallLayout.CopyUninstaller(root));
        Assert.False(File.Exists(Target));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_target_that_cannot_be_replaced_fails_with_its_HRESULT_and_leaves_no_temporary_file()
    {
        WriteSource(3);
        File.WriteAllBytes(Target, [9, 9, 9]);
        using (new FileStream(Target, FileMode.Open, FileAccess.Read, FileShare.None)) // the running uninstaller
        {
            var result = InstallLayout.CopyUninstaller(root);

            Assert.Equal(UninstallerCopyOutcome.Failed, result.Outcome);
            Assert.NotNull(result.HResult);
            Assert.NotEqual(0, result.HResult);
        }
        Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(Target));
        Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
    }
}
