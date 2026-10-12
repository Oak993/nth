using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

// PORT-ONLY (no Swift twin): OD-0072 / ED-247. The Marrow Setup bootstrapper decides, before Velopack's Setup.exe runs,
// what an installer does next to an existing installation: an older one is upgraded, the same version is reported as up
// to date (nothing reinstalled), a newer one makes the installer refuse and change nothing, otherwise a normal install.
// Velopack 1.2.161's Setup.exe cannot be told this itself (its overwrite dialog offers Repair and Downgrade, and the
// app's install hook runs only after the old install has already been moved aside), so the gate runs first. No test
// here installs anything: the host only records what the gate shows and what it would hand to Setup.exe.
public sealed class InstallerGateTests
{
    private const string Nuspec = """
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
        <metadata>
        <id>NthApp</id>
        <title>nth</title>
        <description>nth</description>
        <authors>nth</authors>
        <version>{0}</version>
        <channel>win</channel>
        <mainExe>nth.exe</mainExe>
        <os>win</os>
        <rid>win-x64</rid>
        </metadata>
        </package>
        """;

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData("1.6.2", "1.7.0")]
    [InlineData("1.7.0", "1.7.1")]
    [InlineData("1.7.0", "1.10.0")] // numeric, not text, order
    [InlineData("1.99.99", "2.0.0")]
    [InlineData("1.7.0-beta.1", "1.7.0")] // a pre-release is older than its release
    public void An_older_installed_version_is_upgraded(string installed, string installer)
        => Assert.Equal(InstallerAction.Upgrade, InstallerGate.Decide(installed, installer));

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData("1.7.0", "1.7.0")]
    [InlineData(" 1.7.0\n", "1.7.0")] // the manifest's text around the version
    [InlineData("1.7.0+21", "1.7.0")] // build metadata has no precedence (SemVer 2.0.0 section 10)
    [InlineData("2.0.0-rc.1", "2.0.0-rc.1")]
    public void The_same_version_is_already_up_to_date(string installed, string installer)
        => Assert.Equal(InstallerAction.AlreadyUpToDate, InstallerGate.Decide(installed, installer));

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData("1.8.0", "1.7.0")]
    [InlineData("1.7.1", "1.7.0")]
    [InlineData("1.10.0", "1.9.0")]
    [InlineData("2.0.0", "1.99.99")]
    [InlineData("1.7.0", "1.7.0-rc.1")]
    public void A_newer_installed_version_refuses_the_older_installer(string installed, string installer)
        => Assert.Equal(InstallerAction.RefuseNewerInstalled, InstallerGate.Decide(installed, installer));

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData(null)] // not installed
    [InlineData("")]
    [InlineData("1.7")] // Velopack reads the manifest with strict SemVer: an unreadable one is "no version"
    [InlineData("01.7.0")]
    [InlineData("v1.7.0")]
    [InlineData("not a version")]
    public void No_readable_installed_version_installs_normally(string? installed)
    {
        Assert.Equal(InstallerAction.Install, InstallerGate.Decide(installed, "1.7.0"));
        Assert.Equal(InstallerAction.Install, InstallerGate.Decide(installed, "0.0.1"));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Versions_follow_SemVer_precedence()
    {
        // SemVer 2.0.0 section 11's own example chain, each lower than the next
        string[] chain = ["1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0", "1.0.1", "1.1.0", "2.0.0"];
        for (var i = 0; i < chain.Length; i++)
            for (var j = 0; j < chain.Length; j++)
                Assert.Equal(Math.Sign(i.CompareTo(j)), Math.Sign(InstallerGate.CompareVersions(chain[i], chain[j])));
        Assert.Throws<FormatException>(() => InstallerGate.CompareVersions("1.7", "1.7.0"));
        Assert.Throws<FormatException>(() => InstallerGate.CompareVersions("1.7.0", "1.7.0-"));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_messages_are_the_owners_words()
    {
        Assert.Equal("A newer version of nth (1.8.0) is already installed.", InstallerGate.NewerInstalledMessage("nth", "1.8.0"));
        Assert.Equal("nth 1.7.0 is already installed and up to date.", InstallerGate.UpToDateMessage("nth", "1.7.0"));
        Assert.Equal("nth Setup", InstallerGate.Caption("nth")); // Velopack's own title-setup, en-US.ftl
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_installed_version_is_read_from_Velopacks_layout()
    {
        var root = NewRoot();
        try
        {
            Assert.Null(InstallerGate.ReadInstalledVersion(root)); // no folder
            Directory.CreateDirectory(root);
            Assert.Null(InstallerGate.ReadInstalledVersion(root)); // empty folder
            Install(root, "1.7.0");
            Assert.Equal("1.7.0", InstallerGate.ReadInstalledVersion(root));
            Assert.Equal("1.7.0", InstallerGate.ReadInstalledVersion(root + Path.DirectorySeparatorChar));

            // Velopack's locator needs Update.exe as well as the manifest
            File.Delete(Path.Combine(root, "Update.exe"));
            Assert.Null(InstallerGate.ReadInstalledVersion(root));
            Install(root, "1.7.0");

            // an uninstalled folder Velopack marked dead is empty to it
            File.WriteAllBytes(Path.Combine(root, ".dead"), []);
            Assert.Null(InstallerGate.ReadInstalledVersion(root));
            File.Delete(Path.Combine(root, ".dead"));

            File.WriteAllText(Path.Combine(root, "current", "sq.version"), "<package><metadata><version>1.7</version></metadata></package>");
            Assert.Null(InstallerGate.ReadInstalledVersion(root));
            File.WriteAllText(Path.Combine(root, "current", "sq.version"), "not xml");
            Assert.Null(InstallerGate.ReadInstalledVersion(root));
            File.Delete(Path.Combine(root, "current", "sq.version"));
            Assert.Null(InstallerGate.ReadInstalledVersion(root));
        }
        finally { Remove(root); }
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Not_installed_hands_every_argument_to_Setup_unchanged()
    {
        var root = NewRoot();
        var host = new RecordingInstallerHost(setupExitCode: 7);

        Assert.Equal(7, InstallerGate.Run(host, Identity(root, "1.7.0"), ["--verbose", "--", "--flag"]));

        Assert.Empty(host.Messages);
        Assert.Equal(["--verbose", "--", "--flag"], Assert.Single(host.SetupRuns));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void An_older_installation_hands_over_to_Setup_which_upgrades_it()
    {
        var root = NewRoot();
        try
        {
            Install(root, "1.6.2");
            var host = new RecordingInstallerHost();

            Assert.Equal(0, InstallerGate.Run(host, Identity(root, "1.7.0"), []));

            Assert.Empty(host.Messages);
            Assert.Empty(Assert.Single(host.SetupRuns));
        }
        finally { Remove(root); }
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_same_version_says_up_to_date_and_runs_nothing()
    {
        var root = NewRoot();
        try
        {
            Install(root, "1.7.0");
            var before = Snapshot(root);
            var host = new RecordingInstallerHost();

            Assert.Equal(0, InstallerGate.Run(host, Identity(root, "1.7.0"), []));

            Assert.Equal([("nth Setup", "nth 1.7.0 is already installed and up to date.", false)], host.Messages);
            Assert.Empty(host.SetupRuns);
            Assert.Equal(before, Snapshot(root));
        }
        finally { Remove(root); }
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_newer_installation_refuses_and_changes_nothing()
    {
        var root = NewRoot();
        try
        {
            Install(root, "1.8.0");
            var before = Snapshot(root);
            var host = new RecordingInstallerHost();

            var exit = InstallerGate.Run(host, Identity(root, "1.7.0"), []);

            Assert.Equal(InstallerGate.RefusedExitCode, exit);
            Assert.NotEqual(0, exit);
            Assert.Equal([("nth Setup", "A newer version of nth (1.8.0) is already installed.", true)], host.Messages);
            Assert.Empty(host.SetupRuns);
            Assert.Equal(before, Snapshot(root));
        }
        finally { Remove(root); }
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void An_unreadable_installation_goes_to_Setups_own_repair_prompt()
    {
        var root = NewRoot();
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "leftover.txt"), "x");
            var host = new RecordingInstallerHost();

            Assert.Equal(0, InstallerGate.Run(host, Identity(root, "1.7.0"), []));

            Assert.Empty(host.Messages);
            Assert.Single(host.SetupRuns);
        }
        finally { Remove(root); }
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData("--silent")]
    [InlineData("-s")]
    [InlineData("-vs")] // a cluster of short flags, as Setup's clap parser reads it
    public void Silent_shows_no_message_but_keeps_the_outcome(string silent)
    {
        var root = NewRoot();
        try
        {
            Install(root, "1.7.0");
            var same = new RecordingInstallerHost();
            Assert.Equal(0, InstallerGate.Run(same, Identity(root, "1.7.0"), [silent]));
            Assert.Empty(same.Messages);
            Assert.Empty(same.SetupRuns);

            var older = new RecordingInstallerHost();
            Assert.Equal(InstallerGate.RefusedExitCode, InstallerGate.Run(older, Identity(root, "1.6.2"), [silent]));
            Assert.Empty(older.Messages);
            Assert.Empty(older.SetupRuns);

            Assert.True(InstallerGate.IsSilent([silent]));
        }
        finally { Remove(root); }
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Arguments_after_the_separator_are_the_apps_not_Setups()
    {
        var root = NewRoot();
        try
        {
            Install(root, "1.7.0");
            var host = new RecordingInstallerHost();

            InstallerGate.Run(host, Identity(root, "1.7.0"), ["--", "--silent", "--installto", "elsewhere"]);

            Assert.Single(host.Messages);
            Assert.False(InstallerGate.IsSilent(["--", "--silent"]));
        }
        finally { Remove(root); }
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData("--installto", true)]
    [InlineData("--installto=", false)]
    [InlineData("-t", true)]
    [InlineData("-t", false)]
    [InlineData("-t=", false)]
    public void An_install_folder_given_to_Setup_is_the_one_checked(string option, bool separate)
    {
        var parent = NewRoot();
        var custom = Path.Combine(parent, "custom");
        try
        {
            Install(custom, "1.8.0");
            string[] args = separate ? [option, custom] : [option + custom];
            var host = new RecordingInstallerHost();

            // the default folder is empty; the folder Setup would install to holds a newer version
            Assert.Equal(InstallerGate.RefusedExitCode, InstallerGate.Run(host, Identity(NewRoot(), "1.7.0"), args));
            Assert.Equal("A newer version of nth (1.8.0) is already installed.", Assert.Single(host.Messages).Text);
        }
        finally { Remove(parent); }
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_relative_install_folder_is_resolved_where_Setup_resolves_it_and_passed_on_whole()
    {
        // Velopack's Setup moves its working folder to its own before it uses --installto, so a relative folder is
        // relative to the installer file; the gate's Setup.exe runs from a temporary copy, so it is given the full path.
        var launch = NewRoot();
        try
        {
            var host = new RecordingInstallerHost();
            var identity = Identity(NewRoot(), "1.7.0") with { LaunchDirectory = launch };

            InstallerGate.Run(host, identity, ["-vt", "sub", "--", "-t", "x"]);

            Assert.Equal(["--installto", Path.Combine(launch, "sub"), "-v", "--", "-t", "x"], Assert.Single(host.SetupRuns));

            Install(Path.Combine(launch, "sub"), "1.7.0");
            var same = new RecordingInstallerHost();
            Assert.Equal(0, InstallerGate.Run(same, identity, ["--installto=sub"]));
            Assert.Single(same.Messages);
            Assert.Empty(same.SetupRuns);
        }
        finally { Remove(launch); }
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Help_goes_straight_to_Setup(string help)
    {
        var root = NewRoot();
        try
        {
            Install(root, "1.8.0");
            var host = new RecordingInstallerHost();

            InstallerGate.Run(host, Identity(root, "1.7.0"), [help]);

            Assert.Empty(host.Messages);
            Assert.Equal([help], Assert.Single(host.SetupRuns));
        }
        finally { Remove(root); }
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void An_installer_without_a_valid_version_runs_nothing()
    {
        var host = new RecordingInstallerHost();
        Assert.Throws<FormatException>(() => InstallerGate.Run(host, Identity(NewRoot(), ""), []));
        Assert.Throws<FormatException>(() => InstallerGate.Run(host, Identity(NewRoot(), "1.7"), []));
        Assert.Empty(host.SetupRuns);
        Assert.Empty(host.Messages);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_package_script_ships_the_gate_named_Nth_beside_the_Electron_app()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(repository.FullName, "Marrow.sln"))) repository = repository.Parent!;
        var script = File.ReadAllText(Path.Combine(repository.FullName, "scripts", "package-windows.ps1"));
        var project = File.ReadAllText(Path.Combine(repository.FullName, "src", "Marrow.Setup", "Marrow.Setup.csproj"));

        // OD-0135: the app is "nth" and installs fresh as NthApp, never the Electron app's "Marrow" and never the data
        // folder's own name "nth" (Velopack deletes its install folder on uninstall)
        Assert.Contains("[string]$PackTitle = 'nth',", script, StringComparison.Ordinal);
        Assert.Contains("[string]$PackId = 'NthApp',", script, StringComparison.Ordinal);
        Assert.Contains("[string]$PackAuthors = 'nth',", script, StringComparison.Ordinal);
        Assert.Contains("<InstallerTitle Condition=\"'$(InstallerTitle)' == ''\">nth</InstallerTitle>", project, StringComparison.Ordinal);
        Assert.Contains("<InstallerAppId Condition=\"'$(InstallerAppId)' == ''\">NthApp</InstallerAppId>", project, StringComparison.Ordinal);
        Assert.Equal("NthApp", Marrow.Core.Port.SelfUpdate.MarrowReleaseFeed.PackageId);
        Assert.NotEqual(AppInfo.DataFolderName, Marrow.Core.Port.SelfUpdate.MarrowReleaseFeed.PackageId, StringComparer.OrdinalIgnoreCase);
        // nth installer PLAN.md override 3 (ED-NEW-inst-u7-2): the Electron app that owned Programs\Marrow.lnk no longer matters because our file is nth.lnk, so
        // Start shows Programs\nth.lnk (StartMenuRoot), not a folder named after the publisher (StartMenu). PackageWindowsNthTests holds the same, and more.
        Assert.Contains("--shortcuts StartMenuRoot `", script, StringComparison.Ordinal);
        Assert.DoesNotContain("--shortcuts StartMenu `", script, StringComparison.Ordinal);
        // the gate is built with the same title, id and version vpk pack was given, around the Setup.exe vpk produced
        Assert.Contains("--packId $PackId `", script, StringComparison.Ordinal);
        Assert.Contains("--packVersion $version `", script, StringComparison.Ordinal);
        Assert.Contains("--packTitle $PackTitle `", script, StringComparison.Ordinal);
        Assert.Contains("\"-p:InstallerTitle=$PackTitle\" \"-p:InstallerAppId=$PackId\" \"-p:InstallerVersion=$version\"", script, StringComparison.Ordinal);
        Assert.Contains("\"-p:VelopackSetupExe=$($velopackSetup.FullName)\"", script, StringComparison.Ordinal);
        // and the gate compiles this very decision code, nothing else of Marrow
        Assert.Contains("<Compile Include=\"..\\Marrow.Core\\Support\\InstallerGate.cs\"", project, StringComparison.Ordinal);
        Assert.DoesNotContain("<ProjectReference", project, StringComparison.Ordinal);
        Assert.Contains("LogicalName=\"VelopackSetup.exe\"", project, StringComparison.Ordinal);
        // nth installer PLAN.md 3 (unit U1): the gate moved beside the window unchanged; --silent and --help still go to it
        var program = File.ReadAllText(Path.Combine(repository.FullName, "src", "Marrow.Setup", "Program.cs"));
        Assert.Contains("if (InstallerGate.IsSilent(args) || InstallerGate.IsHelp(args)) return LegacyGateHost.Run(args);", program, StringComparison.Ordinal);
        var gate = File.ReadAllText(Path.Combine(repository.FullName, "src", "Marrow.Setup", "Port", "LegacyGateHost.cs"));
        Assert.Contains("private const string PayloadName = \"VelopackSetup.exe\";", gate, StringComparison.Ordinal);
        Assert.Contains("return InstallerGate.Run(host, Identity(), args);", gate, StringComparison.Ordinal);
    }

    private static InstallerIdentity Identity(string root, string version) =>
        new("nth", version, root, Path.GetTempPath());

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "marrow-gate-" + Guid.NewGuid().ToString("N"));

    // Velopack's installed layout: <root>\Update.exe and <root>\current\sq.version (the package's nuspec).
    private static void Install(string root, string version)
    {
        Directory.CreateDirectory(Path.Combine(root, "current"));
        File.WriteAllBytes(Path.Combine(root, "Update.exe"), []);
        File.WriteAllText(Path.Combine(root, "current", "sq.version"), Nuspec.Replace("{0}", version, StringComparison.Ordinal));
    }

    private static string Snapshot(string root) => string.Join("\n",
        Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(path => path + "|" + (File.Exists(path) ? File.ReadAllText(path) + "|" + File.GetLastWriteTimeUtc(path).Ticks : "dir")));

    private static void Remove(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }
}
