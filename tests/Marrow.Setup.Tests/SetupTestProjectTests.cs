using Marrow.Core.Support;
using Marrow.Setup.Port;
using Marrow.Testing;

namespace Marrow.Setup.Tests;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 10 and 13, unit U0 (ED-NEW-inst-u0-1). This project is wired so later
// units can work in parallel: it is in Marrow.sln, it references nth Setup and nothing else of Marrow, nth Setup compiles
// the installer's Marrow.Core files as linked source (no project reference, like InstallerGate.cs), and the shared
// FakeShellLinks compiles against the setup's own copy of the contract, so the uninstaller's tests (unit U6) can hand it
// to setup code.
public sealed class SetupTestProjectTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_setup_tests_are_in_the_solution_and_reference_only_the_setup()
    {
        var repository = Repository();
        var solution = File.ReadAllText(Path.Combine(repository, "Marrow.sln"));
        var project = File.ReadAllText(Path.Combine(repository, "tests", "Marrow.Setup.Tests", "Marrow.Setup.Tests.csproj"));

        Assert.Contains("\"Marrow.Setup.Tests\", \"tests\\Marrow.Setup.Tests\\Marrow.Setup.Tests.csproj\"", solution, StringComparison.Ordinal);
        Assert.Equal(1, Count(project, "<ProjectReference "));
        Assert.Contains("<ProjectReference Include=\"..\\..\\src\\Marrow.Setup\\Marrow.Setup.csproj\" />", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Marrow.Core.csproj", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Marrow.Testing.csproj", project, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_setup_compiles_the_installer_contracts_as_linked_source_and_references_no_Marrow_project()
    {
        var setup = File.ReadAllText(Path.Combine(Repository(), "src", "Marrow.Setup", "Marrow.Setup.csproj"));

        Assert.Contains("<Compile Include=\"..\\Marrow.Core\\Support\\InstallerGate.cs\" Link=\"InstallerGate.cs\" />", setup, StringComparison.Ordinal);
        Assert.Contains("<Compile Include=\"..\\Marrow.Core\\Support\\InstallOptions.cs\" Link=\"InstallOptions.cs\" />", setup, StringComparison.Ordinal);
        Assert.Contains("<Compile Include=\"..\\Marrow.Core\\Support\\IShellLinks.cs\" Link=\"IShellLinks.cs\" />", setup, StringComparison.Ordinal);
        Assert.DoesNotContain("<ProjectReference", setup, StringComparison.Ordinal);
        Assert.Equal(typeof(ProcessVelopackRunner).Assembly, typeof(InstallOptions).Assembly);
        Assert.Equal(typeof(ProcessVelopackRunner).Assembly, typeof(IShellLinks).Assembly);
        Assert.Equal(typeof(ProcessVelopackRunner).Assembly, typeof(InstallerGate).Assembly);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_shared_fake_shell_is_a_shell_the_setup_code_accepts()
    {
        IShellLinks shell = new FakeShellLinks();

        Assert.Equal(typeof(ProcessVelopackRunner).Assembly, typeof(IShellLinks).Assembly);
        Assert.Equal(FakeShellLinks.OneDriveDesktop, shell.KnownFolder(ShellKnownFolder.Desktop));
        shell.WriteLink(FakeShellLinks.DesktopLink, new ShellLink(@"%LOCALAPPDATA%\Programs\nth\current\nth.exe", CreatedBy: ShellLinkKeys.CreatedByValue));
        Assert.True(shell.ReadLink(FakeShellLinks.DesktopLink).IsMadeByNth);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_tests_lock_file_names_the_setup_by_its_assembly_name_so_a_locked_restore_passes()
    {
        // ED-NEW-inst-merge-5: U7 renamed the setup assembly to nth-Setup; NuGet keys a project reference by that name, and
        // Directory.Build.props restores in locked mode, so a lock file that still names marrow.setup fails with NU1004.
        var repository = Repository();
        var setup = File.ReadAllText(Path.Combine(repository, "src", "Marrow.Setup", "Marrow.Setup.csproj"));
        var name = System.Text.RegularExpressions.Regex.Match(setup, "<AssemblyName>([^<]+)</AssemblyName>").Groups[1].Value;
        var lockFile = File.ReadAllText(Path.Combine(repository, "tests", "Marrow.Setup.Tests", "packages.lock.json"));

        Assert.Equal("nth-Setup", name);
        Assert.Matches("\"" + System.Text.RegularExpressions.Regex.Escape(name) + @""": \{\s*""type"": ""Project""", lockFile);
        Assert.DoesNotContain("\"marrow.setup\"", lockFile, StringComparison.OrdinalIgnoreCase);
        // the merged project keeps U5's unsafe code (generated COM and LibraryImport) and U1's AOT analyzers, once each
        Assert.Equal(1, Count(setup, "<AllowUnsafeBlocks>true</AllowUnsafeBlocks>"));
        Assert.Equal(1, Count(setup, "<IsAotCompatible>true</IsAotCompatible>"));
    }

    private static int Count(string text, string part)
    {
        var count = 0;
        for (var at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    private static string Repository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "Marrow.sln"))) directory = directory.Parent!;
        return directory.FullName;
    }
}
