using System.Text.RegularExpressions;

namespace Marrow.App.Tests;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 10, unit U5 (ED-NEW-inst-u5-1). The real shell and the real HKCU are
// bound in two places only: the app's and the setup's Program.cs. Every other piece of source takes IShellLinks and
// IUserRegistry, so a test can never reach the real Desktop, Start menu or registry by constructing a step. A source scan
// over src/ fails on any other construction of ShellLinks or RegistryUserHive, or any other use of Registry.CurrentUser
// in the installer files.
public sealed class ShellLinksBindingTests
{
    private static readonly string[] Bindings = [@"src\Marrow.App\Program.cs", @"src\Marrow.Setup\Program.cs"];

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Marrow.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Marrow.sln was not found above the test output folder.");
    }

    private static List<(string File, int Line)> Matches(string pattern)
    {
        var root = RepositoryRoot();
        var separator = Path.DirectorySeparatorChar;
        var found = new List<(string, int)>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{separator}obj{separator}", StringComparison.Ordinal) && !f.Contains($"{separator}bin{separator}", StringComparison.Ordinal)))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimStart();
                if (line.StartsWith("//", StringComparison.Ordinal)) continue;
                if (Regex.IsMatch(line, pattern)) found.Add((Path.GetRelativePath(root, file), i + 1));
            }
        }
        return found;
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void ShellLinks_is_constructed_only_in_the_two_Program_files()
    {
        var found = Matches(@"new\s+ShellLinks\s*\(|ShellLinks\s+\w+\s*=\s*new\s*\(");

        Assert.All(found, at => Assert.Contains(at.File, Bindings));
        Assert.Equal(Bindings.Order(StringComparer.Ordinal), found.Select(at => at.File).Distinct().Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void RegistryUserHive_is_constructed_only_in_the_two_Program_files_over_the_current_user()
    {
        var found = Matches(@"new\s+RegistryUserHive\s*\(");

        Assert.All(found, at => Assert.Contains(at.File, Bindings));
        Assert.Equal(Bindings.Order(StringComparer.Ordinal), found.Select(at => at.File).Distinct().Order(StringComparer.Ordinal));
        var root = RepositoryRoot();
        foreach (var (file, line) in found)
            Assert.Contains("RegistryUserHive(Registry.CurrentUser)", File.ReadAllLines(Path.Combine(root, file))[line - 1].Replace("Microsoft.Win32.", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_installer_steps_never_reach_the_registry_or_the_shell_directly()
    {
        var root = RepositoryRoot();
        string[] steps =
        [
            @"src\Marrow.Core\Support\DesktopShortcut.cs",
            @"src\Marrow.Core\Support\InstallHooks.cs",
            @"src\Marrow.Core\Support\UninstallEntry.cs",
            @"src\Marrow.Core\Support\InstallLayout.cs",
            @"src\Marrow.Setup\Support\AfterSetupExit.cs",
            @"src\Marrow.App\Port\NthInstallHook.cs",
            // unit U8 (ED-NEW-inst-u8-1 and -2): Settings > General > "On this PC" gets the shell and the registry from the platform bound in Program.cs
            @"src\Marrow.Core\Support\DesktopShortcutProbe.cs",
            @"src\Marrow.Core\Support\SignInEntry.cs",
            @"src\Marrow.App\Port\OnThisPcModel.cs",
            @"src\Marrow.App\Views\Settings\OnThisPcSettingsSection.cs",
        ];
        foreach (var step in steps)
        {
            // code only: the summaries may name the production bindings
            var text = string.Join('\n', File.ReadAllLines(Path.Combine(root, step)).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
            Assert.DoesNotContain("Microsoft.Win32", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Registry.", text, StringComparison.Ordinal);
            Assert.DoesNotContain("DllImport", text, StringComparison.Ordinal);
            Assert.DoesNotContain("LibraryImport", text, StringComparison.Ordinal);
        }
    }
}
