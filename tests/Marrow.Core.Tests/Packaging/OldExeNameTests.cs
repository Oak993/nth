using System.Diagnostics;
using System.Text.Json;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Packaging;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 7 and 10, unit U7 (ED-NEW-inst-u7-1; OD-0135). The published exe is nth.exe, so no file in the
// repository may name the SDK's apphost, except the decision ledgers (they record the rename) and the dated evidence records listed in
// scripts/lib/old-exe-name-allowlist.json (a record of a past run says what the file was called then). The name is written in two halves here
// so this file is not itself a hit.
public sealed class OldExeNameTests
{
    private static readonly string OldName = "Marrow.App" + ".exe";

    private static string Quote(string path) => path.Replace("'", "''", StringComparison.Ordinal);

    private static JsonElement AllowList() => JsonDocument.Parse(File.ReadAllText(Path.Combine(PackagingRig.Scripts, "lib", "old-exe-name-allowlist.json"))).RootElement;

    private static string Fixture()
    {
        var root = Directory.CreateTempSubdirectory("nth-oldname-").FullName;
        void Write(string path, string text)
        {
            var full = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
        }
        Write("porting/engineering_decisions.md", "the rename from " + OldName);        // a ledger: allowed
        Write("porting/probes/vt-0-publish/publish-listing.txt", OldName + "\t224768"); // evidence: allowed
        Write("scripts/uses-it.ps1", "& '" + OldName + "'");                             // a hit
        Write("src/App/Program.cs", "var exe = \"" + OldName + "\";");                    // a hit
        Write("tests/clean.cs", "var exe = \"nth.exe\";");                               // not a hit
        Write("bin/Debug/output.txt", OldName);                                          // a build folder: skipped without git
        Write("porting/notes/new-record.md", OldName);                                   // not listed: a hit
        return root;
    }

    [Fact]
    [Trait("Decision", "OD-0135")]
    public void The_scan_without_git_returns_only_files_that_are_neither_a_ledger_nor_listed_evidence()
    {
        var root = Fixture();

        var json = PackagingRig.RunJson($$"""
            Write-Output '#JSON'
            [pscustomobject]@{ hits = @(Find-NthOldExeNameReferences -Root '{{Quote(root)}}') } | ConvertTo-Json -Compress
            """);

        Assert.Equal(["porting/notes/new-record.md", "scripts/uses-it.ps1", "src/App/Program.cs"], json.GetProperty("hits").EnumerateArray().Select(e => e.GetString()!).ToArray());
    }

    [Fact]
    [Trait("Decision", "OD-0135")]
    public void The_scan_in_a_git_repository_finds_untracked_files_too_and_gives_the_same_answer()
    {
        var root = Fixture();
        using (var init = Process.Start(new ProcessStartInfo("git", ["-C", root, "init", "--quiet"]) { UseShellExecute = false, CreateNoWindow = true })!) init.WaitForExit();
        File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\n");

        var json = PackagingRig.RunJson($$"""
            Write-Output '#JSON'
            [pscustomobject]@{ hits = @(Find-NthOldExeNameReferences -Root '{{Quote(root)}}') } | ConvertTo-Json -Compress
            """);

        Assert.Equal(["porting/notes/new-record.md", "scripts/uses-it.ps1", "src/App/Program.cs"], json.GetProperty("hits").EnumerateArray().Select(e => e.GetString()!).ToArray());
    }

    [Fact]
    [Trait("Decision", "OD-0135")]
    public void No_file_in_the_repository_names_the_old_exe_outside_the_ledgers_and_the_listed_evidence()
    {
        var start = new ProcessStartInfo("git", ["-C", PackagingRig.Repository, "grep", "--untracked", "-l", "-I", "-F", "-e", OldName]) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        using var process = Process.Start(start)!;
        var listed = process.StandardOutput.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        process.WaitForExit();
        Assert.True(process.ExitCode is 0 or 1, "git grep failed");

        var allowed = AllowList().GetProperty("ledgers").EnumerateArray().Select(e => e.GetString()!)
            .Concat(AllowList().GetProperty("evidence").EnumerateArray().Select(e => e.GetProperty("path").GetString()!))
            .Concat(AllowList().GetProperty("legacy").EnumerateArray().Select(e => e.GetProperty("path").GetString()!)).ToHashSet(StringComparer.Ordinal);

        Assert.All(listed, path => Assert.Contains(path.Replace('\\', '/'), allowed));
    }

    [Fact]
    [Trait("Decision", "OD-0135")]
    public void Every_evidence_entry_still_names_the_old_exe_has_a_reason_and_is_not_source_or_a_script_input()
    {
        foreach (var entry in AllowList().GetProperty("evidence").EnumerateArray())
        {
            var path = entry.GetProperty("path").GetString()!;
            Assert.True(path.StartsWith("porting/", StringComparison.Ordinal), path + " is evidence only under porting/");
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("reason").GetString()), path + " has no reason");
            Assert.Contains(OldName, File.ReadAllText(Path.Combine(PackagingRig.Repository, path.Replace('/', Path.DirectorySeparatorChar))), StringComparison.Ordinal); // a stale entry would let a new hit in
        }
        foreach (var ledger in AllowList().GetProperty("ledgers").EnumerateArray())
            Assert.True(File.Exists(Path.Combine(PackagingRig.Repository, ledger.GetString()!.Replace('/', Path.DirectorySeparatorChar))), ledger.GetString() + " is not a file");
    }

    [Fact]
    [Trait("Decision", "OD-0135")]
    public void Every_legacy_entry_is_source_or_a_test_has_a_reason_and_still_names_the_old_exe()
    {
        // integration of units U4, U5 and U8 with U7 (ED-NEW-inst-integ-1): the installer must find and re-point what an install made before the rename left
        // (PLAN.md 3a, 7 and 8a), so this code names the old exe on purpose; each such file is listed with its reason, and a stale entry would let a new hit in
        var entries = AllowList().GetProperty("legacy").EnumerateArray().ToArray();
        Assert.NotEmpty(entries);
        foreach (var entry in entries)
        {
            var path = entry.GetProperty("path").GetString()!;
            Assert.True(path.StartsWith("src/", StringComparison.Ordinal) || path.StartsWith("tests/", StringComparison.Ordinal), path + " is a legacy entry only under src/ or tests/");
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("reason").GetString()), path + " has no reason");
            Assert.Contains(OldName, File.ReadAllText(Path.Combine(PackagingRig.Repository, path.Replace('/', Path.DirectorySeparatorChar))), StringComparison.Ordinal);
        }
        Assert.Equal(entries.Length, entries.Select(e => e.GetProperty("path").GetString()).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    [Trait("Decision", "OD-0135")]
    public void The_scan_through_the_library_finds_nothing_in_this_repository()
    {
        var json = PackagingRig.RunJson($$"""
            Write-Output '#JSON'
            [pscustomobject]@{ hits = @(Find-NthOldExeNameReferences -Root '{{Quote(PackagingRig.Repository)}}') } | ConvertTo-Json -Compress
            """);

        Assert.Empty(json.GetProperty("hits").EnumerateArray());
    }
}
