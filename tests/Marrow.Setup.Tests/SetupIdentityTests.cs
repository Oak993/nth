using System.Reflection;
using System.Text.RegularExpressions;
using Marrow.Core.Support;
using Marrow.Setup.Port;

namespace Marrow.Setup.Tests;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 7 and 9, unit U7 (ED-NEW-inst-u7-4, ED-NEW-inst-u7-5, ED-NEW-inst-u7-8). The setup exe is named nth-Setup
// (so its original filename reads nth-Setup.exe), says the plan's section 7 about itself, carries the publisher and the default install root as
// metadata even when it is built outside the packaging script, and looks for an existing install at the old default root while a fresh install
// goes to %LOCALAPPDATA%\Programs\nth. The sizes and the notes are stamped only by the script (the build in the script is the gate's).
public sealed class SetupIdentityTests
{
    private static readonly Assembly Setup = typeof(ProcessVelopackRunner).Assembly;

    private static string Repository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Marrow.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Marrow.sln was not found above the test output folder.");
    }

    private static string? Metadata(string key) => Setup.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value;

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_setup_assembly_is_nth_Setup_and_says_what_plan_section_7_says_about_it()
    {
        var props = File.ReadAllText(Path.Combine(Repository(), "Directory.Release.props"));
        var publisher = Regex.Match(props, "<NthPublisher>([^<]+)</NthPublisher>", RegexOptions.CultureInvariant).Groups[1].Value;
        var info = Setup.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

        Assert.Equal("nth-Setup", Setup.GetName().Name);
        Assert.Equal("nth Setup", Setup.GetCustomAttribute<AssemblyTitleAttribute>()!.Title);
        Assert.Equal("nth", Setup.GetCustomAttribute<AssemblyProductAttribute>()!.Product);
        Assert.Equal(publisher, Setup.GetCustomAttribute<AssemblyCompanyAttribute>()!.Company);
        Assert.Equal($"© 2026 {publisher}. Includes Droppy Code by Jordy Spruit (Droppy), GNU AGPL v3.0.", Setup.GetCustomAttribute<AssemblyCopyrightAttribute>()!.Copyright);
        Assert.DoesNotContain('+', info);
        Assert.Matches(@"^\d+\.\d+\.\d+$", info);
        Assert.Equal(info + "." + Setup.GetCustomAttribute<AssemblyFileVersionAttribute>()!.Version.Split('.')[3], Setup.GetCustomAttribute<AssemblyFileVersionAttribute>()!.Version);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_setup_built_outside_the_script_carries_the_publisher_and_the_default_root_and_no_measured_numbers()
    {
        // ED-NEW-inst-cx-5: the owner-selected publisher supersedes the original nth fixture.
        Assert.Equal("oak993", Metadata("InstallerPublisher"));
        Assert.Equal(@"%LOCALAPPDATA%\Programs\nth", Metadata("InstallerDefaultRoot"));
        foreach (var key in new[] { "InstallerMainExe", "InstallerAumid", "InstallerPackageBytes", "InstallerAppBytes", "InstallerDiskBytes", "InstallerVelopackNeedBytes" })
            Assert.Null(Metadata(key));
        Assert.Null(Setup.GetManifestResourceNames().FirstOrDefault(n => n == "ReleaseNotes.md"));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_identity_the_setup_builds_looks_for_an_install_at_the_old_default_root_and_sends_a_fresh_one_to_Programs_nth()
    {
        var identity = (InstallerIdentity)Setup.GetType("Marrow.Setup.Program")!.GetMethod("Identity", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.Equal(Path.Combine(local, "NthApp"), identity.DefaultInstallRoot);
        Assert.Equal(Path.Combine(local, "Programs", "nth"), identity.FreshInstallRoot);
        Assert.NotNull(identity.RegisteredInstallRoot);
        Assert.Equal("nth", identity.Title);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_manifest_and_the_staging_folder_no_longer_carry_the_old_product_name()
    {
        var folder = Path.Combine(Repository(), "src", "Marrow.Setup");

        Assert.DoesNotContain("Marrow", File.ReadAllText(Path.Combine(folder, "app.manifest")), StringComparison.Ordinal);
        Assert.DoesNotContain("\"MarrowSetup-", File.ReadAllText(Path.Combine(folder, "Program.cs")), StringComparison.Ordinal);
        Assert.Contains("\"nth Setup-\"", File.ReadAllText(Path.Combine(folder, "Program.cs")), StringComparison.Ordinal);
    }
}
