using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Core.Tests.Support;

// OD-0135: the product is named nth. On first launch the old "Marrow Code" data folder and the old .marrow / .marrow-dev state
// folder are COPIED once to "nth" and .nth / .nth-dev; the old folders are never moved, changed or deleted. Every test works in
// its own folder under MARROW_PORT_DATA_DIR (R-TEST-9): no real user folder is read or written.
[Trait("Decision", "OD-0135")]
public sealed class LegacyDataCopyTests : IDisposable
{
    private readonly string scratch;

    public LegacyDataCopyTests()
    {
        var data = TestDataDirectory.Read();
        Assert.False(string.IsNullOrEmpty(data));
        scratch = Path.Combine(data!, "LegacyDataCopyTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
    }

    public void Dispose()
    {
        try { Directory.Delete(scratch, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static Dictionary<string, string> Snapshot(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(file => Path.GetRelativePath(root, file), File.ReadAllText, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void The_old_data_folder_is_copied_whole_once_and_left_as_it_was()
    {
        var local = Path.Combine(scratch, "LocalAppData");
        var old = Path.Combine(local, "Marrow Code");
        Write(Path.Combine(old, "library.json"), "{\"projects\":[]}");
        Write(Path.Combine(old, "UserDefaults.json"), "{\"theme\":\"dark\"}");
        Write(Path.Combine(old, "threads", "a.json"), "thread a");
        Write(Path.Combine(old, "models", "whisper", "model.bin"), "weights");
        Directory.CreateDirectory(Path.Combine(old, "attachments"));
        var before = Snapshot(old);

        var (data, state) = LegacyDataCopy.CopyProductFolders(local, Path.Combine(scratch, "Home"), isDevelopment: false);

        Assert.Equal(LegacyDataCopyOutcome.Copied, data.Outcome);
        Assert.Empty(data.Skipped);
        Assert.Equal(LegacyDataCopyOutcome.NothingToCopy, state.Outcome);
        var copied = Path.Combine(local, "nth");
        var after = Snapshot(copied);
        Assert.True(after.Remove(LegacyDataCopy.MarkerName));
        Assert.Equal(before, after);
        Assert.True(Directory.Exists(Path.Combine(copied, "attachments")));
        Assert.Equal(before, Snapshot(old)); // the old folder is untouched
        Assert.False(Directory.Exists(copied + LegacyDataCopy.StagingSuffix));
        Assert.Contains(old, File.ReadAllText(Path.Combine(copied, LegacyDataCopy.MarkerName)), StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_folder_with_the_marker_of_an_earlier_copy_is_never_read_or_written()
    {
        var local = Path.Combine(scratch, "LocalAppData");
        Write(Path.Combine(local, "Marrow Code", "library.json"), "old");
        Write(Path.Combine(local, "Marrow Code", "threads", "a.json"), "thread a");
        Write(Path.Combine(local, "nth", "library.json"), "new");
        Write(Path.Combine(local, "nth", LegacyDataCopy.MarkerName), "copied earlier");
        var before = Snapshot(Path.Combine(local, "nth"));

        var result = LegacyDataCopy.CopyOnce(Path.Combine(local, "Marrow Code"), Path.Combine(local, "nth"));

        Assert.Equal(LegacyDataCopyOutcome.AlreadyThere, result.Outcome);
        Assert.Equal(before, Snapshot(Path.Combine(local, "nth")));
        Assert.False(Directory.Exists(Path.Combine(local, "nth" + LegacyDataCopy.StagingSuffix)));
    }

    [Fact]
    public void A_new_folder_made_by_something_else_keeps_every_file_it_has_and_gains_the_rest()
    {
        // Seen on a real PC: a test installing the Command Code mod wrote Nth\CommandCode\marrow-approvals.ts before the
        // first launch under the new name. That must not cost the user their data, and nothing it holds is overwritten.
        var local = Path.Combine(scratch, "LocalAppData");
        var old = Path.Combine(local, "Marrow Code");
        Write(Path.Combine(old, "library.json"), "library");
        Write(Path.Combine(old, "threads", "a.json"), "thread a");
        Write(Path.Combine(old, "CommandCode", "marrow-approvals.ts"), "old mod");
        Write(Path.Combine(old, "CommandCode", "settings.json"), "old settings");
        var oldBefore = Snapshot(old);
        var root = Path.Combine(local, "nth");
        Write(Path.Combine(root, "CommandCode", "marrow-approvals.ts"), "new mod");
        Directory.CreateDirectory(Path.Combine(root, "CommandCode", "approvals"));

        var result = LegacyDataCopy.CopyOnce(old, root);

        Assert.Equal(LegacyDataCopyOutcome.Merged, result.Outcome);
        Assert.Equal("new mod", File.ReadAllText(Path.Combine(root, "CommandCode", "marrow-approvals.ts"))); // kept, not overwritten
        Assert.True(Directory.Exists(Path.Combine(root, "CommandCode", "approvals")));
        Assert.Equal("old settings", File.ReadAllText(Path.Combine(root, "CommandCode", "settings.json")));
        Assert.Equal("library", File.ReadAllText(Path.Combine(root, "library.json")));
        Assert.Equal("thread a", File.ReadAllText(Path.Combine(root, "threads", "a.json")));
        Assert.True(File.Exists(Path.Combine(root, LegacyDataCopy.MarkerName)));
        Assert.False(Directory.Exists(root + LegacyDataCopy.StagingSuffix));
        Assert.Equal(oldBefore, Snapshot(old)); // the old folder is untouched

        // Marked now: a second launch reads and writes nothing.
        Write(Path.Combine(old, "late.json"), "late");
        Assert.Equal(LegacyDataCopyOutcome.AlreadyThere, LegacyDataCopy.CopyOnce(old, root).Outcome);
        Assert.False(File.Exists(Path.Combine(root, "late.json")));
    }

    [Fact]
    public void A_folder_an_interim_build_copied_under_the_capitalised_name_is_the_same_folder_and_is_left_alone()
    {
        // OD-0135 amendment 1: the interim build named the folder "Nth"; the name is now "nth". Windows compares folder
        // names without case, so the marker the interim copy left is found and nothing is read or written.
        var local = Path.Combine(scratch, "LocalAppData");
        Write(Path.Combine(local, "Marrow Code", "library.json"), "old");
        Write(Path.Combine(local, "Nth", "library.json"), "interim");
        Write(Path.Combine(local, "Nth", LegacyDataCopy.MarkerName), "copied by the interim build");
        var before = Snapshot(Path.Combine(local, "Nth"));

        var (data, _) = LegacyDataCopy.CopyProductFolders(local, Path.Combine(scratch, "Home"), isDevelopment: false);

        Assert.Equal(LegacyDataCopyOutcome.AlreadyThere, data.Outcome);
        Assert.Equal(before, Snapshot(Path.Combine(local, AppInfo.DataFolderName)));
        Assert.Equal(["Marrow Code", "Nth"], Directory.GetDirectories(local).Select(directory => Path.GetFileName(directory)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_capitalised_folder_without_the_marker_keeps_its_files_gains_the_rest_and_is_marked_once()
    {
        // The interim "Nth" folder made by something else (the stray test of the interim build): it is merged into, never
        // replaced or duplicated beside a second "nth" folder.
        var local = Path.Combine(scratch, "LocalAppData");
        Write(Path.Combine(local, "Marrow Code", "library.json"), "library");
        Write(Path.Combine(local, "Marrow Code", "CommandCode", "marrow-approvals.ts"), "old mod");
        Write(Path.Combine(local, "Nth", "CommandCode", "marrow-approvals.ts"), "interim mod");

        var (data, _) = LegacyDataCopy.CopyProductFolders(local, Path.Combine(scratch, "Home"), isDevelopment: false);

        Assert.Equal(LegacyDataCopyOutcome.Merged, data.Outcome);
        var root = Path.Combine(local, AppInfo.DataFolderName);
        Assert.Equal("interim mod", File.ReadAllText(Path.Combine(root, "CommandCode", "marrow-approvals.ts")));
        Assert.Equal("library", File.ReadAllText(Path.Combine(root, "library.json")));
        Assert.True(File.Exists(Path.Combine(root, LegacyDataCopy.MarkerName)));
        Assert.Equal(["Marrow Code", "Nth"], Directory.GetDirectories(local).Select(directory => Path.GetFileName(directory)).Order(StringComparer.Ordinal));
        Assert.Equal(LegacyDataCopyOutcome.AlreadyThere,
            LegacyDataCopy.CopyProductFolders(local, Path.Combine(scratch, "Home"), isDevelopment: false).Data.Outcome);
    }

    [Fact]
    public void The_copy_runs_only_once_even_when_the_old_folder_changes_later()
    {
        var local = Path.Combine(scratch, "LocalAppData");
        Write(Path.Combine(local, "Marrow Code", "library.json"), "first");
        Assert.Equal(LegacyDataCopyOutcome.Copied, LegacyDataCopy.CopyOnce(Path.Combine(local, "Marrow Code"), Path.Combine(local, "nth")).Outcome);
        Write(Path.Combine(local, "Marrow Code", "library.json"), "changed by the old app");

        Assert.Equal(LegacyDataCopyOutcome.AlreadyThere, LegacyDataCopy.CopyOnce(Path.Combine(local, "Marrow Code"), Path.Combine(local, "nth")).Outcome);
        Assert.Equal("first", File.ReadAllText(Path.Combine(local, "nth", "library.json")));
    }

    [Fact]
    public void A_fresh_install_writes_nothing()
    {
        var local = Path.Combine(scratch, "LocalAppData");
        var home = Path.Combine(scratch, "Home");
        Directory.CreateDirectory(local);
        Directory.CreateDirectory(home);

        var (data, state) = LegacyDataCopy.CopyProductFolders(local, home, isDevelopment: false);

        Assert.Equal(LegacyDataCopyOutcome.NothingToCopy, data.Outcome);
        Assert.Equal(LegacyDataCopyOutcome.NothingToCopy, state.Outcome);
        Assert.Empty(Directory.EnumerateFileSystemEntries(local));
        Assert.Empty(Directory.EnumerateFileSystemEntries(home));
    }

    [Theory]
    [InlineData(false, ".marrow", ".nth")]
    [InlineData(true, ".marrow-dev", ".nth-dev")]
    public void The_state_folder_of_the_build_channel_is_copied_and_the_other_channel_is_left_alone(bool development, string legacy, string current)
    {
        var home = Path.Combine(scratch, "Home");
        Write(Path.Combine(home, legacy, "patches", "p.diff"), "patch");
        Write(Path.Combine(home, legacy, "worktrees", "repo", "file.txt"), "work");
        var otherLegacy = development ? ".marrow" : ".marrow-dev";
        Write(Path.Combine(home, otherLegacy, "patches", "other.diff"), "other");

        var (_, state) = LegacyDataCopy.CopyProductFolders(Path.Combine(scratch, "LocalAppData"), home, development);

        Assert.Equal(LegacyDataCopyOutcome.Copied, state.Outcome);
        Assert.Equal("patch", File.ReadAllText(Path.Combine(home, current, "patches", "p.diff")));
        Assert.Equal("work", File.ReadAllText(Path.Combine(home, current, "worktrees", "repo", "file.txt")));
        Assert.Equal("patch", File.ReadAllText(Path.Combine(home, legacy, "patches", "p.diff")));
        Assert.False(Directory.Exists(Path.Combine(home, development ? ".nth" : ".nth-dev")));
    }

    [Fact]
    public void A_staging_folder_left_by_an_interrupted_copy_is_replaced_and_the_copy_completes()
    {
        var local = Path.Combine(scratch, "LocalAppData");
        Write(Path.Combine(local, "Marrow Code", "library.json"), "library");
        Write(Path.Combine(local, "nth" + LegacyDataCopy.StagingSuffix, "half.json"), "half");

        var result = LegacyDataCopy.CopyOnce(Path.Combine(local, "Marrow Code"), Path.Combine(local, "nth"));

        Assert.Equal(LegacyDataCopyOutcome.Copied, result.Outcome);
        Assert.Equal("library", File.ReadAllText(Path.Combine(local, "nth", "library.json")));
        Assert.False(File.Exists(Path.Combine(local, "nth", "half.json")));
        Assert.False(Directory.Exists(Path.Combine(local, "nth" + LegacyDataCopy.StagingSuffix)));
    }

    [Fact]
    public void A_file_the_old_app_holds_open_is_skipped_and_the_rest_is_copied()
    {
        var local = Path.Combine(scratch, "LocalAppData");
        var old = Path.Combine(local, "Marrow Code");
        Write(Path.Combine(old, "library.json"), "library");
        Write(Path.Combine(old, "Logs", "held.log"), "held");
        using (new FileStream(Path.Combine(old, "Logs", "held.log"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var result = LegacyDataCopy.CopyOnce(old, Path.Combine(local, "nth"));

            Assert.Equal(LegacyDataCopyOutcome.Copied, result.Outcome);
            Assert.Equal([Path.Combine(old, "Logs", "held.log")], result.Skipped);
        }
        Assert.Equal("library", File.ReadAllText(Path.Combine(local, "nth", "library.json")));
        Assert.Equal("held", File.ReadAllText(Path.Combine(old, "Logs", "held.log")));
    }

    [Fact]
    public void A_link_inside_the_old_folder_is_skipped_and_never_followed()
    {
        var local = Path.Combine(scratch, "LocalAppData");
        var old = Path.Combine(local, "Marrow Code");
        var outside = Path.Combine(scratch, "Outside");
        Write(Path.Combine(outside, "secret.txt"), "outside");
        Write(Path.Combine(old, "library.json"), "library");
        var link = Path.Combine(old, "linked");
        try { Directory.CreateSymbolicLink(link, outside); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Without the symbolic-link privilege a junction stands in: it is a reparse point all the same.
            var junction = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", ["/c", "mklink", "/J", link, outside])
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
            junction.WaitForExit();
            Assert.Equal(0, junction.ExitCode);
        }

        var result = LegacyDataCopy.CopyOnce(old, Path.Combine(local, "nth"));

        Assert.Equal(LegacyDataCopyOutcome.Copied, result.Outcome);
        Assert.Equal([link], result.Skipped);
        Assert.False(Directory.Exists(Path.Combine(local, "nth", "linked")));
        Assert.Equal("outside", File.ReadAllText(Path.Combine(outside, "secret.txt")));
    }
}
