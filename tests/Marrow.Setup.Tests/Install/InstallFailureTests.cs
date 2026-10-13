using Marrow.Core.Support;
using Marrow.Setup.Install;
using Marrow.Setup.Tests.Fakes;
using Marrow.Setup.Text;

namespace Marrow.Setup.Tests.Install;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 4 ("Error reasons", "Copy details") and the canvas's Problem > After;
// unit U3 (ED-NEW-inst-u3-1). "Nothing was changed on your PC." only when it is true, read from the folders (Velopack
// 1.2.161 logs nothing when its restore fails): the old version back in the root and no new rollback folder beside it, or
// a fresh root still empty. Problem's sentences per reason, and Copy details with the user's folder written as
// %USERPROFILE%.
public sealed class InstallFailureTests : IDisposable
{
    private readonly string parent = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "nth-u3-fail-" + Guid.NewGuid().ToString("N")[..8], "Programs")).FullName;

    private string Root => Path.Combine(parent, "nth");

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(parent)!, recursive: true);

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_fresh_root_is_unchanged_only_while_it_is_missing_or_empty()
    {
        Assert.True(InstallFailure.Unchanged(Root, null, [], InstallerGate.ReadInstalledVersion));
        Directory.CreateDirectory(Root);
        Assert.True(InstallFailure.Unchanged(Root, null, [], InstallerGate.ReadInstalledVersion));
        File.WriteAllText(Path.Combine(Root, "Update.exe"), "x");
        Assert.False(InstallFailure.Unchanged(Root, null, [], InstallerGate.ReadInstalledVersion));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void An_upgrade_is_unchanged_when_the_old_version_is_back_and_no_new_rollback_folder_is_left()
    {
        InstallScenario.WriteOldInstall(Root);
        var before = InstallFailure.RollbackFolders(Root);
        Assert.True(InstallFailure.Unchanged(Root, InstallScenario.OldVersion, before, InstallerGate.ReadInstalledVersion));
        var stale = Directory.CreateDirectory(Path.Combine(parent, "nth.ZZZZZZZZZZZZZZZZ")).FullName;
        Assert.True(InstallFailure.Unchanged(Root, InstallScenario.OldVersion, [stale], InstallerGate.ReadInstalledVersion), "a rollback folder from before the run is not this run's");
        Assert.False(InstallFailure.Unchanged(Root, InstallScenario.OldVersion, before, InstallerGate.ReadInstalledVersion), "a new rollback folder means the restore did not happen");
        Directory.Delete(stale);
        File.Delete(Path.Combine(Root, "current", "sq.version"));
        Assert.False(InstallFailure.Unchanged(Root, InstallScenario.OldVersion, before, InstallerGate.ReadInstalledVersion));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Rollback_folders_are_the_root_s_name_with_16_letters_or_digits_and_never_the_root_itself()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Path.Combine(parent, "nth.A1b2C3d4E5f6G7h8"));
        Directory.CreateDirectory(Path.Combine(parent, "nth.short"));
        Directory.CreateDirectory(Path.Combine(parent, "nth.A1b2C3d4E5f6G7h-"));
        Directory.CreateDirectory(Path.Combine(parent, "nthA1b2C3d4E5f6G7h8x"));
        Assert.Equal([Path.Combine(parent, "nth.A1b2C3d4E5f6G7h8")], InstallFailure.RollbackFolders(Root));
    }

    [Theory]
    [InlineData(FailureReason.InUse, "Some of nth’s files are in use. Close any window showing nth’s folder, then retry.")]
    [InlineData(FailureReason.Blocked, "Security software stopped the install. Allow nth Setup, then retry.")]
    [InlineData(FailureReason.Other, "Something went wrong while installing.")]
    [Trait("Decision", "ED-247")]
    public void Each_reason_leads_with_its_sentence(FailureReason reason, string sentence)
    {
        var failure = new InstallFailure(reason, NothingChanged: true, 1, InstallStep.Copying, null, []);
        Assert.Equal(sentence, failure.Sentence);
        Assert.Equal(SetupStrings.ProblemSafe, failure.SecondLine);
        Assert.Equal(SetupStrings.ProblemLeft, (failure with { NothingChanged = false }).SecondLine);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void No_space_names_the_drive_the_size_and_what_is_free()
    {
        var space = new FreeSpaceVerdict(false, 470 * InstallSizes.Megabyte, 112 * InstallSizes.Megabyte, "C:", 420);
        var failure = new InstallFailure(FailureReason.NoSpace, true, null, InstallStep.Checking, space, []);
        Assert.Equal("There isn’t enough free space on drive C:. nth needs about 420 MB, and 112 MB is free.", failure.Sentence);
        Assert.Equal(SetupStrings.ProblemOther, (failure with { Space = null }).Sentence);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Copy_details_has_the_version_the_build_the_step_the_exit_code_and_the_log_with_the_user_s_folder_hidden()
    {
        const string profile = @"C:\Users\Ana-María Østergård-Fernández";
        var log = new[] { @"[setup:9] [12:00:00] INFO Installation Directory: ""c:\users\ana-maría østergård-fernández\AppData\Local\Programs\nth""", "[setup:9] [12:00:02] ERROR boom" };
        var failure = new InstallFailure(FailureReason.Other, false, 1, InstallStep.Copying, null, log);
        var details = failure.Details("1.1.0", "11", 26300, "x64", profile);
        Assert.StartsWith(SetupStrings.ProblemHeader("1.1.0", "11", 26300, "x64"), details, StringComparison.Ordinal);
        Assert.Contains("Step: Copying", details, StringComparison.Ordinal);
        Assert.Contains("Exit code: 1", details, StringComparison.Ordinal);
        Assert.Contains(@"""%USERPROFILE%\AppData\Local\Programs\nth""", details, StringComparison.Ordinal);
        Assert.DoesNotContain("ana-maría", details, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("ERROR boom", details, StringComparison.Ordinal);
        Assert.Contains("Exit code: none", new InstallFailure(FailureReason.Other, true, null, InstallStep.Checking, null, []).Details("1.1.0", "11", 1, "x64", null), StringComparison.Ordinal);
    }
}
