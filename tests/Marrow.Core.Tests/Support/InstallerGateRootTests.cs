using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md override 10 and section 3, unit U7 (ED-NEW-inst-u7-8; amends ED-247). A fresh install goes to
// %LOCALAPPDATA%\Programs\nth through --installto, and an existing install is always updated where it is: the folder the uninstall entry records,
// else %LOCALAPPDATA%\NthApp (where nth 1.0.0 put it), never copied to the new default. The gate's decisions and exit codes are InstallerGateTests'
// (unchanged); these prove only where it looks and what it hands to Setup.exe. Every folder is a temp folder; no installer runs.
public sealed class InstallerGateRootTests
{
    private const string Nuspec = "<?xml version=\"1.0\" encoding=\"utf-8\"?><package><metadata><id>NthApp</id><version>{0}</version></metadata></package>";

    private static (string Local, string Legacy, string Fresh) Folders()
    {
        var local = Directory.CreateTempSubdirectory("nth-gate-root-").FullName;
        return (local, Path.Combine(local, "NthApp"), Path.Combine(local, "Programs", "nth"));
    }

    // Velopack's installed layout: <root>\Update.exe and <root>\current\sq.version.
    private static void Install(string root, string version)
    {
        Directory.CreateDirectory(Path.Combine(root, "current"));
        File.WriteAllBytes(Path.Combine(root, "Update.exe"), []);
        File.WriteAllText(Path.Combine(root, "current", "sq.version"), string.Format(System.Globalization.CultureInfo.InvariantCulture, Nuspec, version));
    }

    private static InstallerIdentity Identity((string Local, string Legacy, string Fresh) folders, string version, Func<string?>? registered = null, bool freshRoot = true) =>
        new("nth", version, folders.Legacy, folders.Local, freshRoot ? folders.Fresh : null, registered);

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Nothing_installed_installs_to_the_new_default_root_through_installto()
    {
        var folders = Folders();
        var host = new RecordingInstallerHost();

        Assert.Equal(0, InstallerGate.Run(host, Identity(folders, "1.0.0") with { CallerDirectory = folders.Local }, ["--silent", "--log", "setup.log"]));

        // ED-NEW-inst-fix-3: the relative log is handed on whole, resolved where the caller is (Setup.exe runs in the engine's folder)
        Assert.Equal(["--installto", folders.Fresh, "--silent", "--log", Path.Combine(folders.Local, "setup.log")], Assert.Single(host.SetupRuns));
        Assert.Empty(host.Messages);
        Assert.False(Directory.Exists(folders.Fresh)); // the gate writes nothing; Setup.exe does
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void An_install_at_the_old_default_root_is_updated_where_it_is_and_not_copied_to_the_new_one()
    {
        var folders = Folders();
        Install(folders.Legacy, "1.0.0");
        var host = new RecordingInstallerHost();

        Assert.Equal(0, InstallerGate.Run(host, Identity(folders, "1.1.0"), ["--silent"]));

        Assert.Equal(["--installto", folders.Legacy, "--silent"], Assert.Single(host.SetupRuns));
        Assert.False(Directory.Exists(folders.Fresh));
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void An_install_at_the_new_default_root_is_updated_where_it_is()
    {
        var folders = Folders();
        Install(folders.Fresh, "1.0.0");
        var host = new RecordingInstallerHost();

        InstallerGate.Run(host, Identity(folders, "1.0.1"), []);

        Assert.Equal(["--installto", folders.Fresh], Assert.Single(host.SetupRuns));
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_root_the_uninstall_entry_records_is_looked_at_first_wherever_it_is()
    {
        var folders = Folders();
        var elsewhere = Path.Combine(folders.Local, "My Apps", "nth");
        Install(elsewhere, "1.0.0");
        Install(folders.Legacy, "0.9.0");
        var host = new RecordingInstallerHost();

        InstallerGate.Run(host, Identity(folders, "1.1.0", () => elsewhere + Path.DirectorySeparatorChar), ["--silent"]);

        Assert.Equal(["--installto", elsewhere, "--silent"], Assert.Single(host.SetupRuns)); // no trailing separator, and the older copy elsewhere is left alone
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_same_version_installed_at_the_old_root_says_so_and_runs_nothing()
    {
        var folders = Folders();
        Install(folders.Legacy, "1.0.0");
        var host = new RecordingInstallerHost();

        Assert.Equal(0, InstallerGate.Run(host, Identity(folders, "1.0.0"), []));

        Assert.Empty(host.SetupRuns);
        Assert.Equal(InstallerGate.UpToDateMessage("nth", "1.0.0"), Assert.Single(host.Messages).Text);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_newer_version_installed_at_the_recorded_root_refuses_and_runs_nothing()
    {
        var folders = Folders();
        var elsewhere = Path.Combine(folders.Local, "elsewhere");
        Install(elsewhere, "2.0.0");
        var host = new RecordingInstallerHost();

        Assert.Equal(InstallerGate.RefusedExitCode, InstallerGate.Run(host, Identity(folders, "1.0.0", () => elsewhere), []));

        Assert.Empty(host.SetupRuns);
        Assert.Equal(InstallerGate.NewerInstalledMessage("nth", "2.0.0"), Assert.Single(host.Messages).Text);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void An_installto_the_user_gave_is_the_only_place_looked_at_and_is_not_said_twice()
    {
        var folders = Folders();
        Install(folders.Legacy, "1.0.0");
        var chosen = Path.Combine(folders.Local, "chosen");
        var host = new RecordingInstallerHost();

        InstallerGate.Run(host, Identity(folders, "1.1.0", () => folders.Legacy), ["--installto", chosen, "--silent"]);

        Assert.Equal(["--installto", chosen, "--silent"], Assert.Single(host.SetupRuns));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Help_goes_straight_to_Setup_exe_with_the_arguments_untouched()
    {
        var folders = Folders();
        var host = new RecordingInstallerHost();

        InstallerGate.Run(host, Identity(folders, "1.0.0"), ["--help"]);

        Assert.Equal(["--help"], Assert.Single(host.SetupRuns));
    }

    // VELOPACK_CHECK.md "Old branches" item 4 (ED-NEW-inst-fix-3): every spelling of the log Setup.exe reads goes on as a full path in
    // the caller's folder, a full path goes on as it is, and nth Setup's own --no-desktop-shortcut never reaches Setup.exe (its clap
    // parser refuses an argument it does not know), except after "--", where everything belongs to the app.
    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData("--silent|--log|setup.log", "--silent|--log|{caller}\\setup.log")]
    [InlineData("--silent|--log=logs\\setup.log", "--silent|--log|{caller}\\logs\\setup.log")]
    [InlineData("--silent|-l|setup.log", "--silent|-l|{caller}\\setup.log")]
    [InlineData("-lsetup.log|--silent", "-l|{caller}\\setup.log|--silent")]
    [InlineData("-sl|setup.log", "-s|-l|{caller}\\setup.log")]
    [InlineData("-s|-l=..\\setup.log", "-s|-l|{parent}\\setup.log")]
    [InlineData("--silent|--log|C:\\logs\\setup.log", "--silent|--log|C:\\logs\\setup.log")]
    [InlineData("--silent|--no-desktop-shortcut", "--silent")]
    [InlineData("--no-desktop-shortcut|--silent|--log|a.log", "--silent|--log|{caller}\\a.log")]
    [InlineData("--silent|--|--no-desktop-shortcut|--log|a.log", "--silent|--|--no-desktop-shortcut|--log|a.log")]
    public void A_relative_log_is_resolved_where_the_caller_is_and_the_shortcut_switch_stays_with_nth_Setup(string given, string expected)
    {
        var folders = Folders();
        var caller = Directory.CreateDirectory(Path.Combine(folders.Local, "caller")).FullName;
        var host = new RecordingInstallerHost();
        string[] Split(string text) => text.Replace("{caller}", caller, StringComparison.Ordinal).Replace("{parent}", folders.Local, StringComparison.Ordinal).Split('|');

        Assert.Equal(0, InstallerGate.Run(host, Identity(folders, "1.0.0") with { CallerDirectory = caller }, Split(given)));

        Assert.Equal(["--installto", folders.Fresh, .. Split(expected)], Assert.Single(host.SetupRuns));
        Assert.Equal(Split(expected), InstallerGate.Forwarded(Split(given), folders.Local, caller));
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_recorded_root_that_can_no_longer_be_read_is_repaired_where_it_is()
    {
        var folders = Folders();
        var damaged = Path.Combine(folders.Local, "damaged");
        Directory.CreateDirectory(damaged);
        File.WriteAllText(Path.Combine(damaged, "leftover.txt"), "no Update.exe here");
        var host = new RecordingInstallerHost();

        InstallerGate.Run(host, Identity(folders, "1.0.0", () => damaged), ["--silent"]);

        Assert.Equal(["--installto", damaged, "--silent"], Assert.Single(host.SetupRuns));
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_recorded_root_that_is_gone_is_ignored_and_a_fresh_install_goes_to_the_new_default()
    {
        var folders = Folders();
        var host = new RecordingInstallerHost();

        InstallerGate.Run(host, Identity(folders, "1.0.0", () => Path.Combine(folders.Local, "removed-long-ago")), []);

        Assert.Equal(["--installto", folders.Fresh], Assert.Single(host.SetupRuns));
    }

    [Theory]
    [Trait("Decision", "OD-0072")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("C:\\bad|path?*\"")]
    public void No_usable_recorded_root_means_the_old_default_then_the_new_one(string? recorded)
    {
        var folders = Folders();
        var host = new RecordingInstallerHost();

        InstallerGate.Run(host, Identity(folders, "1.0.0", () => recorded), []);

        Assert.Equal(["--installto", folders.Fresh], Assert.Single(host.SetupRuns));
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_registry_read_that_throws_does_not_stop_the_install()
    {
        var folders = Folders();
        Install(folders.Legacy, "1.0.0");
        var host = new RecordingInstallerHost();

        InstallerGate.Run(host, Identity(folders, "1.1.0", () => throw new UnauthorizedAccessException("denied")), []);

        Assert.Equal(["--installto", folders.Legacy], Assert.Single(host.SetupRuns));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_app_arguments_after_the_separator_stay_last_and_in_order()
    {
        var folders = Folders();
        var host = new RecordingInstallerHost();

        InstallerGate.Run(host, Identity(folders, "1.0.0"), ["--silent", "--", "--at-sign-in", "two words"]);

        Assert.Equal(["--installto", folders.Fresh, "--silent", "--", "--at-sign-in", "two words"], Assert.Single(host.SetupRuns));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Without_a_fresh_root_the_gate_is_exactly_what_it_was_Velopacks_default_root_and_no_installto_added()
    {
        var folders = Folders();
        Install(folders.Legacy, "1.0.0");
        var host = new RecordingInstallerHost();

        InstallerGate.Run(host, Identity(folders, "1.1.0", () => Path.Combine(folders.Local, "ignored"), freshRoot: false), ["--silent"]);
        InstallerGate.Run(host, Identity(folders, "1.1.0", freshRoot: false), []);

        Assert.Equal(["--silent"], host.SetupRuns[0]);
        Assert.Empty(host.SetupRuns[1]);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void FindInstall_reports_the_root_and_the_version_for_each_case()
    {
        var folders = Folders();
        Assert.Equal((folders.Fresh, (string?)null), InstallerGate.FindInstall(Identity(folders, "1.0.0"), null));
        Install(folders.Legacy, "1.0.0");
        Assert.Equal((folders.Legacy, (string?)"1.0.0"), InstallerGate.FindInstall(Identity(folders, "1.0.0"), null));
        Assert.Equal((folders.Legacy, (string?)"1.0.0"), InstallerGate.FindInstall(Identity(folders, "1.0.0", freshRoot: false), null));
        var other = Path.Combine(folders.Local, "other");
        Assert.Equal((other, (string?)null), InstallerGate.FindInstall(Identity(folders, "1.0.0"), other));
    }
}
