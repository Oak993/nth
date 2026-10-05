// PORT-ONLY: SK-13 / DL-16 (skills-ONDEMAND-PLAN.md 3.8 and section 7; skills-PLAN.md WU-18 r3 (a)). The body of Velopack's before-uninstall hook, on a fake data root with the injected
// protection and deletion doubles. The real NTFS deny entry and a real held handle are in SkillsUninstallWindowsTests.
using Marrow.Services.Port.Skills.Store;
using Marrow.Services.Port.Skills.Uninstall;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Store.Fixtures;

namespace Marrow.Services.Tests.Port.Skills.Uninstall;

[Trait("Feature", "SK-13")]
[Trait("Spec", "DL-16")]
[Trait("Rule", "R-TEST-9")]
[Collection("Dev data root env")]
public sealed class SkillsUninstallTests
{
    [Fact]
    public void Everything_is_unlocked_before_anything_is_deleted()
    {
        using var f = new StoreFixture();
        var tree = new UninstallTree(f);
        var deletes = 0;
        f.Deletion.BeforeDelete = _ =>
        {
            deletes++;
            foreach (var entry in StoreTrees.Entries(f.Root).Prepend(f.Root))
            {
                Assert.False(f.Protection.IsLocked(entry), "A deny entry was still there at the first delete: " + entry);
                if (File.Exists(entry)) Assert.False(File.GetAttributes(entry).HasFlag(FileAttributes.ReadOnly), "A read-only file at a delete: " + entry);
            }
        };
        SkillsUninstall.BeforeUninstall(f.Root, f.Protection, f.Deletion);
        Assert.True(deletes > 0);
        Assert.All(tree.Locked, folder => Assert.False(Directory.Exists(folder)));
    }

    [Fact]
    public void It_deletes_addons_and_the_store_the_views_and_the_derived_files_and_keeps_enabled_json()
    {
        using var f = new StoreFixture();
        var tree = new UninstallTree(f);
        var report = SkillsUninstall.BeforeUninstall(f.Root, f.Protection, f.Deletion);
        Assert.False(Directory.Exists(f.Paths.Addons));
        Assert.False(Directory.Exists(f.Paths.Store));
        Assert.False(Directory.Exists(f.Paths.Views));
        Assert.False(File.Exists(f.Paths.CondemnedFile));
        Assert.False(File.Exists(Path.Combine(f.Paths.Skills, "chat-paths.json")));
        Assert.False(File.Exists(Path.Combine(f.Paths.Skills, "generation-launches.jsonl")));
        Assert.False(File.Exists(Path.Combine(f.Paths.Skills, "reset-pending")));
        Assert.Equal("enabled sentinel", File.ReadAllText(f.Paths.EnabledFile));
        Assert.Empty(report.Left);
        Assert.False(report.OutOfTime);
        Assert.True(File.Exists(tree.Other));
    }

    [Fact]
    public void It_leaves_the_providers_homes_and_their_transcripts_alone()
    {
        using var f = new StoreFixture();
        var tree = new UninstallTree(f);
        SkillsUninstall.BeforeUninstall(f.Root, f.Protection, f.Deletion);
        Assert.Equal("a provider transcript", File.ReadAllText(Path.Combine(tree.ProviderHome, "transcripts", "chat.jsonl")));
    }

    [Fact]
    public void A_held_folder_is_left_unlocked_and_a_plain_delete_works_once_the_handle_is_gone()
    {
        using var f = new StoreFixture();
        var tree = new UninstallTree(f);
        var held = tree.Locked[0];
        f.Deletion.Held.Add(held);
        var report = SkillsUninstall.BeforeUninstall(f.Root, f.Protection, f.Deletion);
        Assert.True(Directory.Exists(held));
        Assert.Contains(report.Left, p => p == held || held.StartsWith(p + Path.DirectorySeparatorChar, StringComparison.Ordinal) || p.StartsWith(held, StringComparison.Ordinal));
        foreach (var entry in StoreTrees.Entries(held).Prepend(held))
        {
            Assert.False(f.Protection.IsLocked(entry), "The held tree kept a deny entry: " + entry);
            if (File.Exists(entry)) Assert.False(File.GetAttributes(entry).HasFlag(FileAttributes.ReadOnly));
        }
        Assert.Equal(["enabled sentinel"], [File.ReadAllText(f.Paths.EnabledFile)]);
        f.Deletion.Held.Clear();
        Directory.Delete(held, recursive: true);
        Assert.False(Directory.Exists(held));
    }

    [Fact]
    public void One_entry_that_cannot_be_unlocked_does_not_stop_the_others_being_unlocked_and_deleted()
    {
        using var f = new StoreFixture();
        var tree = new UninstallTree(f);
        var stubborn = tree.FirstFile(tree.Locked[2]);
        var protection = new RefusingUnlockProtection(f.Protection, path => path == stubborn);
        SkillsUninstall.BeforeUninstall(f.Root, protection, f.Deletion);
        Assert.False(Directory.Exists(tree.Locked[0]));
        Assert.False(Directory.Exists(tree.Locked[6]));
        Assert.False(Directory.Exists(f.Paths.Tools));
    }

    [Fact]
    public void When_the_budget_runs_out_no_new_delete_starts_but_everything_is_already_unlocked()
    {
        using var f = new StoreFixture();
        var tree = new UninstallTree(f);
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero));
        var deleted = 0;
        f.Deletion.BeforeDelete = _ =>
        {
            deleted++;
            clock.Advance(TimeSpan.FromSeconds(12));
        };
        var report = SkillsUninstall.BeforeUninstall(f.Root, f.Protection, f.Deletion, TimeSpan.FromSeconds(20), clock);
        Assert.True(report.OutOfTime);
        Assert.NotEmpty(report.Left);
        Assert.True(deleted is >= 1 and <= 3, "deletes started after the budget: " + deleted);
        foreach (var entry in StoreTrees.Entries(f.Root).Prepend(f.Root))
            Assert.False(f.Protection.IsLocked(entry), "A deny entry survived: " + entry);
        Assert.Equal("enabled sentinel", File.ReadAllText(f.Paths.EnabledFile));
    }

    [Fact]
    public void A_data_root_that_does_not_exist_is_nothing_to_do()
    {
        var parent = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        Assert.False(string.IsNullOrWhiteSpace(parent));
        var missing = Path.Combine(parent!, "sk13 no such root " + Guid.NewGuid().ToString("N"));
        var report = SkillsUninstall.BeforeUninstall(missing, new FakeStoreProtection(), new FakeStoreDeletion());
        Assert.Empty(report.Left);
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public void A_data_root_that_is_not_absolute_is_refused_and_nothing_is_touched()
    {
        Assert.Throws<ArgumentException>(() => SkillsUninstall.BeforeUninstall("relative\\root", new FakeStoreProtection(), new FakeStoreDeletion()));
    }
}
