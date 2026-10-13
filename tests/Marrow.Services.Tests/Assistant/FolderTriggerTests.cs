using System.Diagnostics;
using System.Text.Json.Nodes;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 7.2 "Folder change"; canvas 5; ED-NEW-dextra-as10-6): a folder trigger on real temp folders and a
// manual clock. A burst of events waits 2 s for the files to settle (30 s at most); a rescan every 10 minutes finds what a watcher
// missed; what arrived while nth was closed is found at the next start; a folder that is gone is a problem, never "every file
// was removed"; only a granted folder is watched; a junction inside it is not followed.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-6")]
public sealed class FolderTriggerTests : IDisposable
{
    private readonly AssistantTempRoot temp = new();
    private readonly ManualTimeProvider clock = new(new DateTimeOffset(2026, 10, 13, 6, 0, 0, TimeSpan.Zero));
    private readonly FakeFolderEvents events = new();
    private readonly List<FolderChange> found = [];
    private readonly List<string?> statuses = [];
    private readonly List<FolderTrigger> made = [];
    private readonly string folder;

    public FolderTriggerTests()
    {
        folder = Path.Combine(temp.Folder, "inbox");
        Directory.CreateDirectory(folder);
    }

    public void Dispose()
    {
        foreach (var trigger in made) trigger.Dispose();
        temp.Dispose();
    }

    private FolderTrigger Make(FolderEvents wanted = FolderEvents.New, string pattern = "*", bool subfolders = false, Func<string, bool>? granted = null,
        bool fake = true, int limit = 20_000)
    {
        var trigger = new FolderTrigger(new FolderTriggerOptions
        {
            Path = folder,
            Pattern = pattern,
            Events = wanted,
            Subfolders = subfolders,
            Time = clock,
            Granted = granted ?? (_ => true),
            Source = fake ? events : null,
            FileLimit = limit,
        }, change => found.Add(change), status => statuses.Add(status));
        made.Add(trigger);
        return trigger;
    }

    private void Write(string name, string text = "x")
    {
        var path = Path.Combine(folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void The_first_scan_is_a_baseline_and_a_new_file_is_found_once_the_files_have_settled()
    {
        Write("a.pdf");
        var trigger = Make();
        Assert.Null(trigger.Start(null));
        Assert.Empty(found); // what was there already is not new
        Write("b.pdf");
        events.Poke();
        clock.Advance(TimeSpan.FromMilliseconds(1900));
        Assert.Empty(found);
        clock.Advance(TimeSpan.FromMilliseconds(100));
        var change = Assert.Single(found);
        Assert.Equal(["b.pdf"], change.New);
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Single(found); // the same file is not new twice
    }

    [Fact]
    public void A_burst_keeps_waiting_until_it_goes_quiet_but_never_past_thirty_seconds()
    {
        var trigger = Make();
        Assert.Null(trigger.Start(null));
        for (var second = 0; second < 45; second++)
        {
            Write($"part-{second}.pdf");
            events.Poke();
            clock.Advance(TimeSpan.FromSeconds(1));
            if (second < 28) Assert.Empty(found); // each poke moved the scan 2 s on
        }
        // The first poke was at t = 0: the scan could wait at most until t = 30.
        Assert.NotEmpty(found);
        Assert.True(found[0].New.Count is >= 29 and <= 31, $"the scan at 30 s saw {found[0].New.Count} files");
    }

    [Fact]
    public void Changed_and_removed_files_are_found_when_the_duty_wants_them_and_not_when_it_does_not()
    {
        Write("a.pdf", "one");
        Write("c.pdf", "gone soon");
        var all = Make(FolderEvents.All);
        Assert.Null(all.Start(null));
        Write("a.pdf", "a longer text");
        File.Delete(Path.Combine(folder, "c.pdf"));
        events.Poke();
        clock.Advance(TimeSpan.FromSeconds(2));
        var change = Assert.Single(found);
        Assert.Equal(["a.pdf"], change.Changed);
        Assert.Equal(["c.pdf"], change.Removed);
        Assert.Empty(change.New);

        found.Clear();
        using var newOnly = new FolderTrigger(new FolderTriggerOptions { Path = folder, Time = clock, Granted = _ => true, Source = new FakeFolderEvents() }, change => found.Add(change), _ => { });
        Assert.Null(newOnly.Start(null));
        Write("a.pdf", "changed again, longer still");
        newOnly.Scan();
        Assert.Empty(found);
    }

    [Fact]
    public void A_rescan_finds_what_the_watcher_missed()
    {
        var trigger = Make();
        Assert.Null(trigger.Start(null));
        Write("late.pdf"); // no event arrives
        clock.Advance(TimeSpan.FromMinutes(9));
        Assert.Empty(found);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(["late.pdf"], Assert.Single(found).New);
    }

    [Fact]
    public void Files_that_arrived_while_nth_was_closed_are_found_at_the_next_start()
    {
        Write("a.pdf");
        var first = Make();
        Assert.Null(first.Start(null));
        var saved = first.State();
        first.Dispose();
        Write("while-closed.pdf");
        var second = Make();
        Assert.Null(second.Start(saved));
        Assert.Equal(["while-closed.pdf"], Assert.Single(found).New);
    }

    [Fact]
    public void A_snapshot_of_another_folder_or_a_newer_format_is_not_trusted_and_the_scan_is_a_baseline()
    {
        Write("a.pdf");
        var other = new JsonObject { ["version"] = 1, ["folder"] = @"C:\somewhere\else", ["files"] = new JsonObject() };
        var newer = new JsonObject { ["version"] = 2, ["folder"] = folder, ["files"] = new JsonObject() };
        Assert.Null(Make().Start(other));
        Assert.Null(Make().Start(newer));
        Assert.Empty(found);
    }

    [Fact]
    public void A_folder_that_is_gone_is_a_problem_to_report_never_every_file_removed()
    {
        Write("a.pdf");
        var trigger = Make(FolderEvents.All);
        Assert.Null(trigger.Start(null));
        var away = folder + "-away";
        Directory.Move(folder, away); // the drive went away
        events.Poke();
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Empty(found);
        Assert.Contains(statuses, s => s is not null && s.Contains("is not there", StringComparison.Ordinal));
        Directory.Move(away, folder); // and came back with the same files
        trigger.Scan();
        Assert.Empty(found); // nothing changed
        Assert.Null(statuses[^1]); // and the owner hears it is fine again
    }

    [Fact]
    public void A_folder_that_was_not_granted_is_never_watched()
    {
        var trigger = Make(granted: _ => false);
        var refused = trigger.Start(null);
        Assert.Contains("not a folder you granted", refused, StringComparison.Ordinal);
        Assert.False(events.Started);
        Assert.Empty(found);
    }

    [Fact]
    public void A_relative_path_or_a_missing_folder_is_refused_with_words()
    {
        var relative = new FolderTrigger(new FolderTriggerOptions { Path = "inbox", Time = clock, Granted = _ => true, Source = events }, _ => { }, _ => { });
        Assert.Equal("Give the folder's full path.", relative.Start(null));
        var missing = new FolderTrigger(new FolderTriggerOptions { Path = Path.Combine(temp.Folder, "nope"), Time = clock, Granted = _ => true, Source = events }, _ => { }, _ => { });
        Assert.Contains("is not there", missing.Start(null), StringComparison.Ordinal);
    }

    [Fact]
    public void Only_files_that_match_the_pattern_count()
    {
        var trigger = Make(pattern: "*.pdf");
        Assert.Null(trigger.Start(null));
        Write("notes.txt");
        Write("INV-1.PDF");
        trigger.Scan();
        Assert.Equal(["INV-1.PDF"], Assert.Single(found).New);
    }

    [Fact]
    public void Subfolders_count_only_when_asked_and_are_named_relative_to_the_folder()
    {
        var flat = Make();
        Assert.Null(flat.Start(null));
        var deep = Make(subfolders: true);
        Assert.Null(deep.Start(null));
        Write(Path.Combine("sub", "x.pdf"));
        flat.Scan();
        Assert.Empty(found);
        deep.Scan();
        Assert.Equal(["sub/x.pdf"], Assert.Single(found).New);
        Assert.True(events.Subfolders);
    }

    [Fact]
    public void A_junction_inside_the_folder_is_not_followed()
    {
        var elsewhere = Path.Combine(temp.Folder, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        File.WriteAllText(Path.Combine(elsewhere, "secret.pdf"), "x");
        var link = Path.Combine(folder, "link");
        using (var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{elsewhere}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true }))
        {
            mklink!.WaitForExit(10_000);
        }
        Assert.True(Directory.Exists(link), "the test could not make a junction");
        var trigger = Make(subfolders: true);
        Assert.Null(trigger.Start(null));
        File.WriteAllText(Path.Combine(elsewhere, "another.pdf"), "x");
        trigger.Scan();
        Assert.Empty(found);
        Write("own.pdf");
        trigger.Scan();
        Assert.Equal(["own.pdf"], Assert.Single(found).New);
    }

    [Fact]
    public void A_folder_with_more_files_than_the_limit_is_refused_with_words()
    {
        for (var i = 0; i < 5; i++) Write($"f{i}.pdf");
        var trigger = Make(limit: 3);
        Assert.Null(trigger.Start(null));
        Assert.Contains(statuses, s => s is not null && s.Contains("holds more than 3 files", StringComparison.Ordinal));
        Assert.Empty(found);
    }

    [Fact]
    public void When_windows_will_not_watch_live_the_rescan_still_looks()
    {
        events.StartFails = new IOException("The network name is no longer available.");
        var trigger = Make();
        Assert.Null(trigger.Start(null));
        Assert.Contains(statuses, s => s is not null && s.Contains("looks every 10 minutes", StringComparison.Ordinal));
        Write("a.pdf");
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(["a.pdf"], Assert.Single(found).New);
    }

    [Fact]
    public void A_status_is_reported_when_it_begins_and_when_it_ends_not_at_every_scan()
    {
        var trigger = Make();
        Assert.Null(trigger.Start(null));
        for (var i = 0; i < 5; i++) trigger.Scan();
        Assert.DoesNotContain(statuses, s => s is not null);
        Assert.True(statuses.Count <= 1);
    }

    [Fact]
    public void A_stopped_trigger_looks_no_more_and_frees_its_watcher_and_timers()
    {
        var trigger = Make();
        Assert.Null(trigger.Start(null));
        trigger.Dispose();
        Assert.True(events.Disposed);
        Write("late.pdf");
        events.Poke();
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Empty(found);
        Assert.Equal(0, clock.ActiveTimers);
    }

    [Fact]
    public void The_real_watcher_notices_a_new_file_and_the_scan_follows_the_settle_time()
    {
        var trigger = Make(fake: false);
        Assert.Null(trigger.Start(null));
        Write("fresh.pdf");
        // The watcher says so on its own thread; the settle timer shows on the manual clock when it has.
        Assert.True(SpinWait.SpinUntil(() => clock.ActiveTimers >= 2, TimeSpan.FromSeconds(15)), "Windows did not report the new file");
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(["fresh.pdf"], Assert.Single(found).New);
    }
}
