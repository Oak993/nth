// RR-48: a real OS delete failure must not remove the indexed import or leave a new copy.
using Marrow.Core.Port.Skills.Import;
using Marrow.Services.Port.Skills.Import;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Skills.Import;

public sealed class DesignGapsReplaceFilesystemTests
{
    [Fact]
    public void Replace_rolls_back_when_the_old_copy_cannot_be_deleted_by_the_OS()
    {
        using var home = new ForeignHome();
        home.Skill(".claude/skills", "notes", "notes", "Original");
        home.Skill(".codex/skills", "notes", "notes", "Replacement");
        var scan = new ForeignSkillScanner(home.Home).Scan().Skills;
        var library = new ImportedSkillsLibrary(home.SkillsRoot, new ManualTimeProvider());
        var first = Assert.Single(library.Import([scan.Single(s => s.App == ForeignApp.ClaudeCode)], ImportMode.Copy, _ => DuplicateChoice.Skip).Imported);
        var copies = Path.Combine(home.SkillsRoot, "imported", "copies");
        var old = Path.Combine(copies, first.Id);
        FileStream? held = null;
        if (OperatingSystem.IsWindows()) held = new FileStream(Path.Combine(old, "SKILL.md"), FileMode.Open, FileAccess.Read, FileShare.Read);
        else File.SetUnixFileMode(old, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var result = library.Import([scan.Single(s => s.App == ForeignApp.Codex)], ImportMode.Copy, _ => DuplicateChoice.Replace);
            Assert.Single(result.Failed);
            Assert.Empty(result.Imported);
            Assert.Equal(first.Id, Assert.Single(library.List()).Id);
            Assert.Equal(old, Assert.Single(Directory.EnumerateDirectories(copies)));
            Assert.Equal(first.Id, Assert.Single(new ImportedSkillsLibrary(home.SkillsRoot, new ManualTimeProvider()).List()).Id);
        }
        finally
        {
            held?.Dispose();
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(old, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
