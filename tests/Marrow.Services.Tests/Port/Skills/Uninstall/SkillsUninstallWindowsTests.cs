// PORT-ONLY: SK-13 / DL-16 (WINDOWS-ONLY; skills-PLAN.md WU-18 r3 (a)). The hook body on a fake data root with the real NTFS deny entry and a real handle held open: it finishes well inside
// the 10 s the plan asks for (Velopack's limit is 30), leaves no deny entry and no read-only file, deletes everything not held and keeps enabled.json. No installer or Velopack runs.
using System.Diagnostics;
using Marrow.Services.Port.Skills.Store;
using Marrow.Services.Port.Skills.Uninstall;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Store.Fixtures;

namespace Marrow.Services.Tests.Port.Skills.Uninstall;

[Trait("Feature", "SK-13")]
[Trait("Spec", "DL-16")]
[Trait("Platform", "WINDOWS-ONLY")]
[Trait("Rule", "R-TEST-9")]
[Collection("Dev data root env")]
public sealed class SkillsUninstallWindowsTests
{
    [WindowsOnlyFact]
    public void Under_the_real_ACL_with_a_file_held_open_the_hook_finishes_inside_10_seconds_and_leaves_only_unlocked_files()
    {
        using var f = new StoreFixture(windows: true);
        var tree = new UninstallTree(f, filesPerFolder: 150);
        var held = tree.FirstFile(tree.Locked[0]);
        var neverHeld = tree.FirstFile(tree.Locked[4]);
        // The fixture really is locked: a plain delete is refused before the hook runs.
        Assert.ThrowsAny<UnauthorizedAccessException>(() => File.Delete(neverHeld));
        Assert.True(f.Protection.IsLocked(tree.Locked[0]));

        UninstallReport report;
        var clock = Stopwatch.StartNew();
        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            report = SkillsUninstall.BeforeUninstall(f.Root);
            clock.Stop();
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "The hook took " + clock.Elapsed);
            Assert.False(report.OutOfTime);
            Assert.Equal([held], report.Left);
            Assert.True(File.Exists(held));
            foreach (var entry in StoreTrees.Entries(f.Root).Prepend(f.Root))
            {
                Assert.False(f.Protection.IsLocked(entry), "A deny entry survived the hook: " + entry);
                if (File.Exists(entry)) Assert.False(File.GetAttributes(entry).HasFlag(FileAttributes.ReadOnly), "A read-only file survived the hook: " + entry);
            }
        }

        // Everything not held is gone; the switches stay; the other Marrow data and the provider home are untouched.
        Assert.False(Directory.Exists(tree.Locked[1]));
        Assert.False(Directory.Exists(tree.Locked[2]));
        Assert.False(Directory.Exists(tree.Locked[3]));
        Assert.False(Directory.Exists(tree.Locked[4]));
        Assert.False(File.Exists(neverHeld));
        Assert.Equal("enabled sentinel", File.ReadAllText(f.Paths.EnabledFile));
        Assert.True(File.Exists(tree.Other));
        Assert.Equal("a provider transcript", File.ReadAllText(Path.Combine(tree.ProviderHome, "transcripts", "chat.jsonl")));
        Assert.False(File.Exists(f.Paths.CondemnedFile));

        // With the handle closed, a plain delete of what is left works: the held folder was left unlocked.
        Directory.Delete(f.Paths.Addons, recursive: true);
        Assert.False(Directory.Exists(f.Paths.Addons));
    }

    [WindowsOnlyFact]
    public void Under_the_real_ACL_with_nothing_held_the_hook_removes_every_locked_folder()
    {
        using var f = new StoreFixture(windows: true);
        var tree = new UninstallTree(f, filesPerFolder: 40);
        var report = SkillsUninstall.BeforeUninstall(f.Root);
        Assert.Empty(report.Left);
        Assert.All(tree.Locked, folder => Assert.False(Directory.Exists(folder)));
        Assert.False(Directory.Exists(f.Paths.Addons));
        Assert.False(Directory.Exists(f.Paths.Store));
        Assert.False(Directory.Exists(f.Paths.Views));
        Assert.Equal("enabled sentinel", File.ReadAllText(f.Paths.EnabledFile));
    }
}
