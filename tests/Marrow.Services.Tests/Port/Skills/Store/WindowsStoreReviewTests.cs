using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Store;
using Marrow.Services.Tests.Port.Skills.Store.Fixtures;

namespace Marrow.Services.Tests.Port.Skills.Store;

// PORT-ONLY: integ-sk4-sk5 Windows review of DL-5 (ONDEMAND section 7; PLAN WU-4 repair-while-held and teardown)
// with the real NTFS deny entry and real held handles. Only the downloader and archive installer are fakes.
[Trait("Feature", "SK-5")]
[Trait("Platform", "WINDOWS-ONLY")]
[Trait("Rule", "R-TEST-9")]
[Collection("Dev data root env")]
public sealed class WindowsStoreReviewTests
{
    [WindowsOnlyFact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public async Task Under_the_real_ACL_a_damaged_item_is_never_leased_and_Repair_restores_the_same_pin_locked()
    {
        using var f = new StoreFixture(windows: true);
        var item = f.Create();
        var store = f.Open(item);
        f.Tamper(item, "sample/native/fixture.exe", [9, 9, 9, 9]);
        Assert.Null(store.Acquire("T1"));
        Assert.Equal(ItemState.Damaged, store.State.Get("T1").State);
        Assert.Null(store.Acquire("T1"));
        Assert.True(await store.RepairAsync("T1"));
        Assert.Equal(item.Pin, f.Downloader.Requested!.Pin);
        Assert.Equal(ItemState.Installed, store.State.Get("T1").State);
        Assert.Equal("1.0", store.State.Get("T1").InstalledPin);
        Assert.True(f.Protection.IsLocked(item.Folder));
        Assert.ThrowsAny<UnauthorizedAccessException>(() => File.WriteAllText(Path.Combine(item.Folder, "json.py"), "planted"));
        Assert.Empty(Directory.EnumerateDirectories(f.Paths.Tools, "tmp-*"));
        using var lease = store.Acquire("T1");
        Assert.NotNull(lease);
    }

    [WindowsOnlyFact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public async Task A_real_held_handle_in_the_damaged_folder_defers_Repair_promotion_to_the_next_GC()
    {
        using var f = new StoreFixture(windows: true);
        var item = f.Create();
        var store = f.Open(item);
        f.Tamper(item, "sample/native/fixture.exe", [9, 9, 9, 9]);
        Assert.Null(store.Acquire("T1"));
        using (new FileStream(Path.Combine(item.Folder, "LICENSE.txt"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.False(await store.RepairAsync("T1"));
            Assert.Null(store.Acquire("T1"));
            store.Collect();
            Assert.Null(store.Acquire("T1"));
        }
        store.Collect();
        Assert.Equal(ItemState.Installed, store.State.Get("T1").State);
        using var lease = store.Acquire("T1");
        Assert.NotNull(lease);
        Assert.Empty(Directory.EnumerateDirectories(f.Paths.Tools, "tmp-*"));
    }

    [WindowsOnlyFact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public void Teardown_with_a_real_held_handle_unlocks_everything_and_the_next_GC_removes_the_rest()
    {
        using var f = new StoreFixture(windows: true);
        var item = f.Create();
        var generation = Path.Combine(f.Paths.Views, "codex", "chat-1");
        StoreFixture.WriteContent(generation);
        f.Trees.Lock(generation);
        var store = f.Open(item);
        using (new FileStream(Path.Combine(item.Folder, "LICENSE.txt"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            store.Teardown();
            Assert.False(Directory.Exists(generation));
            Assert.True(Directory.Exists(item.Folder));
            foreach (var entry in StoreTrees.Entries(f.Root).Prepend(f.Root))
                Assert.False(f.Protection.IsLocked(entry), "A deny entry survived teardown: " + entry);
        }
        store.Collect();
        Assert.False(Directory.Exists(item.Folder));
        Assert.False(Directory.Exists(f.Paths.Tools));
    }
}
