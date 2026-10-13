using Marrow.Core.Models;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-12 (DESIGN.md 8.4 "watches those folders for new notes"): a change to a markdown file in a watched folder starts a short wait;
// when the changes stop, the import runs once, in Watch mode, so what is new waits for the owner's Keep. Driven by the injected clock.
[Trait("Decision", "ED-NEW-dextra-as12-15")]
[Trait("Feature", "AS-12")]
public sealed class MemoryImportWatchTests : IDisposable
{
    private readonly MemoryRig rig = new();
    private readonly string claude;

    public MemoryImportWatchTests()
    {
        claude = Path.Combine(rig.Temp.Folder, "claude-a");
        Directory.CreateDirectory(claude);
    }

    public void Dispose() => rig.Dispose();

    private MemoryImportSource Source() => new("acca", "Claude A", ProviderKind.Claude, claude);

    private MemoryImportWatch Watch(out List<ImportReport> reports, out List<string> faults, Func<IReadOnlyList<MemoryImportSource>>? sources = null)
    {
        var done = new List<ImportReport>();
        var failed = new List<string>();
        var watch = new MemoryImportWatch(new MemoryImport(rig.Memory, () => MemoryRig.Main), sources ?? (() => [Source()]), rig.Clock, runner: work => { work(); return Task.CompletedTask; });
        watch.Imported += done.Add;
        watch.Faulted += failed.Add;
        reports = done;
        faults = failed;
        return watch;
    }

    [Fact]
    public void Changes_that_come_in_a_burst_start_one_import_after_the_last_of_them()
    {
        using var watch = Watch(out var reports, out _);
        var file = Path.Combine(claude, "CLAUDE.md");
        File.WriteAllText(file, "- Deploy from the release branch only.\n- Check the invoices on Fridays.\n");
        watch.Notify(file);
        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        watch.Notify(file);
        rig.Clock.Advance(TimeSpan.FromSeconds(1.5));
        watch.Notify(file);
        Assert.Equal(1, rig.Clock.ActiveTimers);
        rig.Clock.Advance(TimeSpan.FromSeconds(1.9));
        Assert.Empty(reports);
        rig.Clock.Advance(TimeSpan.FromSeconds(0.2));
        var report = Assert.Single(reports);
        Assert.Equal((1, 2), (report.FilesRead, report.Quarantined));
        Assert.Equal(0, rig.Clock.ActiveTimers);
        // A new burst later runs again; the file is unchanged now, so it is not read.
        watch.Notify(file);
        rig.Clock.Advance(MemoryImportWatch.DefaultWait);
        Assert.Equal(2, reports.Count);
        Assert.Equal((0, 1), (reports[1].FilesRead, reports[1].FilesUnchanged));
    }

    [Fact]
    public void What_is_not_a_markdown_file_starts_nothing()
    {
        using var watch = Watch(out var reports, out _);
        watch.Notify(Path.Combine(claude, "session.jsonl"));
        watch.Notify(Path.Combine(claude, "notes.txt"));
        watch.Notify(Path.Combine(claude, "NOTES.MD.bak"));
        Assert.Equal(0, rig.Clock.ActiveTimers);
        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Empty(reports);
        watch.Notify(Path.Combine(claude, "NOTES.MD"));
        Assert.Equal(1, rig.Clock.ActiveTimers);
    }

    [Fact]
    public void An_import_that_throws_is_reported_and_the_watch_goes_on()
    {
        var calls = 0;
        using var watch = Watch(out var reports, out var faults, () => ++calls == 1 ? throw new InvalidOperationException("the accounts could not be read") : [Source()]);
        watch.Notify(Path.Combine(claude, "a.md"));
        rig.Clock.Advance(MemoryImportWatch.DefaultWait);
        Assert.Equal(["Notes could not be imported: the accounts could not be read"], faults);
        Assert.Empty(reports);
        watch.Notify(Path.Combine(claude, "a.md"));
        rig.Clock.Advance(MemoryImportWatch.DefaultWait);
        Assert.Single(reports);
    }

    [Fact]
    public void A_disposed_watch_does_nothing_and_holds_no_timer()
    {
        var watch = Watch(out var reports, out _);
        watch.Notify(Path.Combine(claude, "a.md"));
        Assert.Equal(1, rig.Clock.ActiveTimers);
        watch.Dispose();
        Assert.Equal(0, rig.Clock.ActiveTimers);
        watch.Notify(Path.Combine(claude, "a.md"));
        watch.Start();
        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Empty(reports);
        watch.Dispose();
    }

    [Fact]
    public void Starting_watches_only_the_folders_that_exist_and_can_be_started_again()
    {
        using var watch = Watch(out _, out var faults, () => [Source(), new MemoryImportSource("gone", "Claude Gone", ProviderKind.Claude, Path.Combine(rig.Temp.Folder, "not-there")) { ProjectFolders = [""] }]);
        watch.Start();
        watch.Start();
        Assert.Empty(faults);
    }
}
