using Marrow.Services.Port.Skills.Store;
using Marrow.Services.Tests.Port.Skills.Store.Fixtures;

namespace Marrow.Services.Tests.Port.Skills.Store;

// PORT-ONLY: DL-5 and PLAN 2.1 Windows ACL, stream, lock and held-handle requirements.
[Trait("Feature", "SK-5")]
[Trait("Platform", "WINDOWS-ONLY")]
[Trait("Rule", "R-TEST-9")]
public sealed class WindowsStoreProtectionTests
{
    [WindowsOnlyFact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public void The_exact_inherited_deny_refuses_create_write_append_attribute_rename_and_delete()
    {
        using var f = new StoreFixture(windows: true);
        var item = f.Create();
        var file = Path.Combine(item.Folder, "LICENSE.txt");
        Assert.True(new StoreVerifier(f.Protection).Verify(item.Folder, item.Manifest).IsValid);
        Assert.ThrowsAny<UnauthorizedAccessException>(() => File.WriteAllText(Path.Combine(item.Folder, "json.py"), "planted"));
        Assert.ThrowsAny<UnauthorizedAccessException>(() => Directory.CreateDirectory(Path.Combine(item.Folder, "empty")));
        Assert.ThrowsAny<UnauthorizedAccessException>(() => File.WriteAllText(file, "changed"));
        Assert.ThrowsAny<UnauthorizedAccessException>(() => File.AppendAllText(file, "changed"));
        Assert.ThrowsAny<UnauthorizedAccessException>(() => File.SetAttributes(file, FileAttributes.Normal));
        Assert.ThrowsAny<UnauthorizedAccessException>(() => File.Move(file, file + ".renamed"));
        Assert.ThrowsAny<UnauthorizedAccessException>(() => File.Delete(file));
        Assert.ThrowsAny<UnauthorizedAccessException>(() => Directory.Delete(item.Folder, recursive: true));
    }

    [WindowsOnlyFact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public void Reset_ACLs_then_clear_read_only_then_delete_removes_the_whole_locked_fixture()
    {
        using var f = new StoreFixture(windows: true);
        var item = f.Create();
        Assert.ThrowsAny<UnauthorizedAccessException>(() => Directory.Delete(item.Folder, recursive: true));
        f.Trees.Unlock(item.Folder);
        StoreDeletion.Instance.Delete(item.Folder);
        Assert.False(Directory.Exists(item.Folder));
    }

    [WindowsOnlyFact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public void A_real_named_stream_is_damage_even_when_the_main_stream_matches()
    {
        using var f = new StoreFixture(windows: true);
        var item = f.Create();
        f.Trees.Unlock(item.Folder);
        File.WriteAllText(Path.Combine(item.Folder, "LICENSE.txt") + ":planted", "alternate data");
        f.Trees.Lock(item.Folder);
        Assert.False(new StoreVerifier(f.Protection).Verify(item.Folder, item.Manifest).IsValid);
    }

    [WindowsOnlyFact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public void A_real_handle_without_delete_sharing_defers_GC_until_the_next_pass()
    {
        using var f = new StoreFixture(windows: true);
        var item = f.Create();
        var store = new SkillsStore(f.Paths, [item], f.Open(item).State, f.Protection);
        store.Retire(item.Folder);
        using (var handle = new FileStream(Path.Combine(item.Folder, "LICENSE.txt"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            store.Collect();
            Assert.True(Directory.Exists(item.Folder));
            Assert.False(f.Protection.IsLocked(item.Folder));
        }
        store.Collect();
        Assert.False(Directory.Exists(item.Folder));
    }

    [WindowsOnlyFact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public void Removing_the_deny_or_renaming_a_locked_tree_is_checked_on_the_next_use()
    {
        using var f = new StoreFixture(windows: true);
        var item = f.Create();
        var renamed = item.Folder + "-renamed";
        Directory.Move(item.Folder, renamed);
        Assert.True(f.Protection.IsLocked(renamed));
        Assert.ThrowsAny<UnauthorizedAccessException>(() => File.WriteAllText(Path.Combine(renamed, "json.py"), "planted"));
        Assert.False(new StoreVerifier(f.Protection).Verify(item.Folder, item.Manifest).IsValid);
        f.Protection.Unlock(renamed);
        Assert.False(new StoreVerifier(f.Protection).Verify(renamed, item.Manifest).IsValid);
    }
}
