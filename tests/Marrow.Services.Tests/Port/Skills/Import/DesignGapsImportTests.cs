// RR-48: UI reads use metadata; current linked bytes are read without the generation coordinator's lock.
using System.Reflection;
using Marrow.Core.Port.Skills.Import;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Import;
using Marrow.Services.Tests.Port.Skills.Generations;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Skills.Import;

public sealed class DesignGapsImportTests
{
    [Fact]
    public void List_does_not_read_a_linked_tree_and_snapshot_refreshes_its_metadata()
    {
        using var home = new ForeignHome();
        var folder = home.Skill(".claude/skills", "notes", "notes", "Original description");
        var library = new ImportedSkillsLibrary(home.SkillsRoot, new ManualTimeProvider());
        library.Import(new ForeignSkillScanner(home.Home).Scan().Skills, ImportMode.Link, _ => DuplicateChoice.Skip);
        File.WriteAllText(Path.Combine(folder, "SKILL.md"), ForeignHome.SkillText("notes", "Changed description"));
        Assert.Equal("Original description", Assert.Single(library.List()).Description);
        Assert.Equal("Changed description", Assert.Single(library.Snapshot()).Description);
        Assert.Equal("Changed description", Assert.Single(library.List()).Description);
    }

    [Fact]
    public async Task A_slow_imported_snapshot_does_not_hold_the_generation_coordinators_gate()
    {
        using var f = new GenerationFixture();
        var item = f.Add("A1", "pdf");
        f.Open();
        using var slow = new HeldSource();
        using var service = new SkillGenerations(f.Paths, f.Store!, f.Protection, _ => [], slow);
        var start = Task.Run(() => service.Start("codex", "one", [item], f.Switches((item, true)), f.Budget));
        Assert.True(slow.Entered.Wait(TimeSpan.FromSeconds(5)));
        var collect = Task.Run(service.Collect);
        try { await collect.WaitAsync(TimeSpan.FromSeconds(2)); }
        finally { slow.Release.Set(); }
        using var generation = await start.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(generation.Verify());
    }

    [Fact]
    public void Failed_replace_keeps_the_old_record_and_removes_the_new_copy()
    {
        using var home = new ForeignHome();
        home.Skill(".claude/skills", "notes", "notes", "Original description");
        home.Skill(".codex/skills", "notes", "notes", "Replacement description");
        var scanned = new ForeignSkillScanner(home.Home).Scan().Skills;
        var library = new ImportedSkillsLibrary(home.SkillsRoot, new ManualTimeProvider());
        var first = Assert.Single(library.Import([scanned.Single(s => s.App == ForeignApp.ClaudeCode)], ImportMode.Copy, _ => DuplicateChoice.Skip).Imported);
        // Deterministic stand-in for an OS delete failure (e.g. an old copied file held open without delete sharing).
        var seam = typeof(ImportedSkillsLibrary).GetProperty("DeleteDirectory", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(seam);
        seam.SetValue(library, (Action<string>)(path =>
        {
            if (Path.GetFileName(path) == first.Id) throw new IOException("old copy is held");
            Directory.Delete(path, recursive: true);
        }));
        var result = library.Import([scanned.Single(s => s.App == ForeignApp.Codex)], ImportMode.Copy, _ => DuplicateChoice.Replace);
        Assert.Single(result.Failed);
        Assert.Empty(result.Imported);
        Assert.Empty(result.Replaced);
        Assert.Equal(first.Id, Assert.Single(library.List()).Id);
        Assert.Equal(first.Id, Assert.Single(Directory.EnumerateDirectories(Path.Combine(home.SkillsRoot, "imported", "copies"))).Split(Path.DirectorySeparatorChar).Last());
        Assert.Equal(first.Id, Assert.Single(new ImportedSkillsLibrary(home.SkillsRoot, new ManualTimeProvider()).List()).Id);
    }

    private sealed class HeldSource : IImportedSkillSource, IDisposable
    {
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public IReadOnlyList<ImportedGenerationSkill> Snapshot()
        {
            Entered.Set();
            if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("test did not release the source");
            return [];
        }
        public void Dispose() { Entered.Dispose(); Release.Dispose(); }
    }
}
