using Marrow.Core.Support;
using Marrow.Setup.Platform;

namespace Marrow.Setup.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3, 5 and 8, unit U9 (ED-NEW-inst-u9-1). The folders nth Setup writes outside
// the install root have one definition: the setup's own folder, a staging folder per version, a runtime folder per build, the
// uninstaller's temporary folder and the lock name. The sweeps use the same definition, so what is written is what is swept.
public sealed class SetupFoldersTests
{
    private const string Local = @"C:\Users\Ana-María Østergård-Fernández\AppData\Local";
    private const string Temp = @"C:\Users\Ana-María Østergård-Fernández\AppData\Local\Temp";

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_folders_are_the_ones_the_plan_names()
    {
        Assert.Equal(Local + @"\nth Setup", SetupFolders.Root(Local));
        Assert.Equal(Local + @"\nth Setup\runtime", SetupFolders.RuntimeRoot(Local));
        Assert.Equal(Local + @"\nth Setup\runtime\4f9c2a", SetupFolders.RuntimeFolder(Local, "4f9c2a"));
        Assert.Equal(Local + @"\nth Setup\1.0.0\nth Setup Engine.exe", SetupFolders.EnginePath(Local, "1.0.0"));
        Assert.Equal(Temp + @"\nth Uninstall", SetupFolders.UninstallTemp(Temp));
        Assert.Equal(@"Local\nth Setup", SetupFolders.SetupLockName);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_staging_folder_is_the_one_the_options_file_and_the_hook_use()
    {
        Assert.Equal(InstallOptions.StagingFolder(Local, "1.0.0"), Path.GetDirectoryName(SetupFolders.EnginePath(Local, "1.0.0")));
        Assert.Equal(InstallOptions.SetupFolderName, Path.GetFileName(SetupFolders.Root(Local)));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_setup_lock_is_one_name_for_the_lock_and_for_the_sweeps_that_look_for_it()
    {
        string lockName = SingleInstance.SetupLockName;
        string sweepsLookFor = SetupFolders.SetupLockName;

        Assert.Equal(sweepsLookFor, lockName);
        Assert.Equal(@"Local\nth Setup", lockName);
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData("")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData(@"..\nth")]
    [InlineData("a/b")]
    [InlineData("a b")]
    public void A_build_hash_that_cannot_name_one_folder_is_refused(string hash) =>
        Assert.Throws<ArgumentException>(() => SetupFolders.RuntimeFolder(Local, hash));

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData("")]
    [InlineData("nth")]
    [InlineData(@"relative\local")]
    public void A_folder_that_is_not_a_full_path_is_refused_so_nothing_is_ever_found_relative_to_the_working_folder(string path)
    {
        Assert.ThrowsAny<ArgumentException>(() => SetupFolders.Root(path));
        Assert.ThrowsAny<ArgumentException>(() => SetupFolders.UninstallTemp(path));
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData(@"C:\Users\Ana\AppData\Local\nth Setup", true)]
    [InlineData(@"C:\Users\Ana\AppData\Local\nth Setup\", true)]
    [InlineData(@"C:\Users\Ana\AppData\Local\NTH SETUP\1.0.0\x.exe", true)]
    [InlineData(@"C:\Users\Ana\AppData\Local\nth Setup\runtime\ab12", true)]
    [InlineData(@"C:\Users\Ana\AppData\Local\nth", false)]
    [InlineData(@"C:\Users\Ana\AppData\Local\nth Setup2", false)]
    [InlineData(@"C:\Users\Ana\AppData\Local\nth Setup\..\nth", false)]
    [InlineData(@"C:\Users\Ana\AppData\Local\NthApp\current", false)]
    [InlineData(@"nth Setup\1.0.0", false)]
    [InlineData("", false)]
    public void Only_the_setups_own_folder_and_what_is_inside_it_counts_as_inside(string path, bool inside) =>
        Assert.Equal(inside, SetupFolders.IsInsideRoot(@"C:\Users\Ana\AppData\Local", path));
}
