// RR-48: metadata reads must remain available while an original skill is on a slow disk.
using Marrow.Core.Port.Skills.Import;
using Marrow.Services.Port.Skills.Import;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Skills.Import;

[Trait("Decision", "ED-NEW-designgaps-2")]
public sealed class DesignGapsImportLatencyTests
{
    [Fact]
    public async Task List_stays_available_while_copy_reads_a_slow_original()
    {
        using var home = new ForeignHome();
        home.Skill(".claude/skills", "notes", "notes", "Original");
        var skill = new ForeignSkillScanner(home.Home).Scan().Skills.Single();
        var library = new ImportedSkillsLibrary(home.SkillsRoot, new ManualTimeProvider());
        var read = library.ReadSkillTree;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        library.ReadSkillTree = path =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("test did not release disk read");
            return read(path);
        };
        var importing = Task.Run(() => library.Import([skill], ImportMode.Copy, _ => DuplicateChoice.Skip));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        var listing = Task.Run(library.List);
        try { Assert.Empty(await listing.WaitAsync(TimeSpan.FromSeconds(2))); }
        finally { release.Set(); }
        Assert.Single((await importing.WaitAsync(TimeSpan.FromSeconds(5))).Imported);
    }

    [Fact]
    public async Task List_and_switch_stay_available_while_a_linked_snapshot_reads_a_slow_original()
    {
        using var home = new ForeignHome();
        home.Skill(".claude/skills", "notes", "notes", "Original");
        var library = new ImportedSkillsLibrary(home.SkillsRoot, new ManualTimeProvider());
        var first = Assert.Single(library.Import(new ForeignSkillScanner(home.Home).Scan().Skills, ImportMode.Link, _ => DuplicateChoice.Skip).Imported);
        var read = library.ReadSkillTree;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        library.ReadSkillTree = path =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("test did not release disk read");
            return read(path);
        };
        var snapshot = Task.Run(library.Snapshot);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        var metadata = Task.Run(() => { library.SetEnabled(first.Id, false); return library.List(); });
        try { Assert.False(Assert.Single(await metadata.WaitAsync(TimeSpan.FromSeconds(2))).Enabled); }
        finally { release.Set(); }
        Assert.Empty(await snapshot.WaitAsync(TimeSpan.FromSeconds(5))); // no stale enable bit overwrites the switch
    }
}
