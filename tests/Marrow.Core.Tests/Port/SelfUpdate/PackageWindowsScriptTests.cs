using System.Text.RegularExpressions;
using Marrow.Core.Port.SelfUpdate;

namespace Marrow.Core.Tests.Port.SelfUpdate;

// UE-1 (OD-0058, OD-0062, OD-0100): scripts/package-windows.ps1 writes the release folder `vpk upload github` takes and NEVER uploads: nothing
// that runs publishes, signs, pushes or fetches. Read as text; the script is never run here.
public sealed class PackageWindowsScriptTests
{
    private static string[] Lines()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Marrow.sln"))) dir = dir.Parent;
        var text = File.ReadAllText(Path.Combine(dir!.FullName, "scripts", "package-windows.ps1"));
        text = Regex.Replace(text, @"<#.*?#>", "", RegexOptions.Singleline | RegexOptions.CultureInvariant);   // the help block
        return text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
    }

    // a line that runs: not blank, not a comment, not only text for Write-Host to print
    private static IEnumerable<string> Executable() =>
        Lines().Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#') && !l.TrimStart().StartsWith("Write-Host", StringComparison.Ordinal));

    [Theory]
    [Trait("Feature", "UE-1")]
    [Trait("Rule", "R-TEST-4")]
    [InlineData("vpk upload")]
    [InlineData("vpk publish")]
    [InlineData("vpk download")]
    [InlineData("gh release")]
    [InlineData("git push")]
    [InlineData("Invoke-WebRequest")]
    [InlineData("Invoke-RestMethod")]
    [InlineData("curl")]
    [InlineData("--signParams")]
    public void Nothing_the_script_runs_uploads_publishes_pushes_downloads_or_signs(string forbidden) =>
        Assert.DoesNotContain(Executable(), l => l.Contains(forbidden, StringComparison.OrdinalIgnoreCase));

    [Fact]
    [Trait("Feature", "UE-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_upload_command_appears_only_as_text_it_prints_for_the_owner()
    {
        var mentions = Lines().Where(l => l.Contains("vpk upload github", StringComparison.Ordinal) && !l.TrimStart().StartsWith('#')).ToArray();
        var printed = Assert.Single(mentions);
        Assert.StartsWith("Write-Host", printed.TrimStart(), StringComparison.Ordinal);
        Assert.Contains("--repoUrl $feedRepoUrl", printed, StringComparison.Ordinal);
        Assert.Contains("--outputDir `\"$releasesDir`\"", printed, StringComparison.Ordinal);   // PowerShell's own escape for the quotes in a string
    }

    [Fact]
    [Trait("Feature", "UE-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_scripts_feed_is_the_apps_feed_and_the_pack_goes_to_the_folder_the_upload_takes()
    {
        var feed = Executable().Single(l => l.StartsWith("$feedRepoUrl", StringComparison.Ordinal));
        Assert.Contains("'" + MarrowReleaseFeed.Url + "'", feed, StringComparison.Ordinal);
        Assert.Contains(Executable(), l => l.Contains("$releasesDir = Join-Path $root 'artifacts/releases'", StringComparison.Ordinal));
        Assert.Contains(Executable(), l => l.Contains("--outputDir $releasesDir `", StringComparison.Ordinal));
        Assert.Contains(Executable(), l => l.Contains("--packId $PackId `", StringComparison.Ordinal));
        Assert.Contains(Executable(), l => l.Contains("[string]$PackId = 'MarrowCode'", StringComparison.Ordinal));
        Assert.Equal("MarrowCode", MarrowReleaseFeed.PackageId);
    }

    [Fact]
    [Trait("Feature", "UE-1")]
    [Trait("Rule", "R-TEST-4")]
    public void Release_notes_reach_the_pack_only_when_a_file_is_named_and_it_exists()
    {
        var lines = Executable().ToArray();
        var at = Array.FindIndex(lines, l => l.Contains("if ($ReleaseNotesFile)", StringComparison.Ordinal));
        Assert.True(at >= 0);
        Assert.Contains("Test-Path -LiteralPath $ReleaseNotesFile", lines[at + 1], StringComparison.Ordinal);
        Assert.Contains("'--releaseNotes'", lines[at + 2], StringComparison.Ordinal);
        Assert.Single(lines, l => l.Contains("--releaseNotes", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Trim() == "@notesArgs");                                    // empty unless a file was named
        Assert.Contains(lines, l => l.Trim() == "$notesArgs = @()");
    }
}
