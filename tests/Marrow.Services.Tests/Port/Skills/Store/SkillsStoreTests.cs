using System.Text.Json;
using Marrow.Core.Port.Skills.State;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Skills.Store;
using Marrow.Services.Tests.Port.Skills.Store.Fixtures;

namespace Marrow.Services.Tests.Port.Skills.Store;

// PORT-ONLY: DL-5, ONDEMAND 3.5/3.7/3.8, PLAN 2.1 leases and GC. No network or executable launch.
[Trait("Feature", "SK-5")]
[Trait("Rule", "R-TEST-9")]
[Trait("Spec", "DL-5")]
public sealed class SkillsStoreTests
{
    [Theory]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    [InlineData("fixture.exe")]
    [InlineData("fixture.DLL")]
    [InlineData("fixture.pyd")]
    [InlineData("fixture.node")]
    public void Every_launch_rechecks_the_binary_seal_and_reports_damaged_without_a_lease(string binary)
    {
        using var f = new StoreFixture();
        var item = f.Create();
        var store = f.Open(item);
        using (var first = store.Acquire("T1")) Assert.NotNull(first);
        f.Tamper(item, "sample/native/" + binary, [9, 9]);
        Assert.Null(store.Acquire("T1"));
        Assert.Equal(ItemState.Damaged, store.State.Get("T1").State);
        Assert.Equal(ItemState.Damaged, AddonsStateStore.Load(f.Paths.StateFile).Get("T1").State);
        Assert.Null(store.Acquire("T1"));
    }

    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public void Full_app_update_verification_detects_text_damage_before_use()
    {
        using var f = new StoreFixture();
        var item = f.Create();
        var store = f.Open(item);
        f.Tamper(item, "sample/SKILL.md", [9]);
        store.VerifyAfterAppUpdate();
        Assert.Equal(ItemState.Damaged, store.State.Get("T1").State);
        Assert.Null(store.Acquire("T1"));
    }

    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public async Task Repair_downloads_the_installed_pin_and_verifies_before_making_it_usable()
    {
        using var f = new StoreFixture();
        var item = f.Create();
        var store = f.Open(item);
        f.Tamper(item, "sample/native/fixture.exe", [9]);
        Assert.Null(store.Acquire("T1"));
        Assert.True(await store.RepairAsync("T1"));
        Assert.Equal("1.0", f.Downloader.Requested!.Pin);
        Assert.Equal(ItemState.Installed, store.State.Get("T1").State);
        using var lease = store.Acquire("T1");
        Assert.NotNull(lease);
        Assert.Equal(StoreFixture.Content["sample/native/fixture.exe"], File.ReadAllBytes(Path.Combine(lease!.Folder, "sample/native/fixture.exe")));
    }

    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public async Task Corrupt_repair_and_unconfigured_download_or_installer_seams_never_become_installed()
    {
        using var f = new StoreFixture();
        var item = f.Create();
        var store = f.Open(item);
        f.Tamper(item, "sample/native/fixture.exe", [9]);
        Assert.Null(store.Acquire("T1"));
        f.Downloader.Corrupt = true;
        await Assert.ThrowsAsync<IOException>(() => store.RepairAsync("T1"));
        Assert.Equal(ItemState.Failed, store.State.Get("T1").State);
        Assert.Null(store.Acquire("T1"));
        foreach (var pair in new[] { (false, true), (true, false) })
        {
            var closed = f.Open(item, pair.Item1, pair.Item2);
            Assert.Null(closed.Acquire("T1"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => closed.RepairAsync("T1"));
            Assert.Null(closed.Acquire("T1"));
        }
    }

    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public async Task A_live_lease_keeps_a_condemned_tree_until_repair_can_publish_and_restart_keeps_the_mark()
    {
        using var f = new StoreFixture();
        var item = f.Create();
        var store = f.Open(item);
        var lease = store.Acquire("T1")!;
        f.Tamper(item, "sample/native/fixture.exe", [9]);
        Assert.Null(store.Acquire("T1"));
        Assert.Contains(item.Folder, JsonSerializer.Deserialize<string[]>(File.ReadAllBytes(f.Paths.CondemnedFile))!);
        Assert.False(await store.RepairAsync("T1"));
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(Path.Combine(item.Folder, "sample/native/fixture.exe")));
        store.Collect();
        Assert.True(Directory.Exists(item.Folder));
        var restarted = f.Open(item);
        Assert.Null(restarted.Acquire("T1"));
        lease.Dispose();
        store.Collect();
        Assert.Equal(ItemState.Installed, store.State.Get("T1").State);
        using var repaired = store.Acquire("T1");
        Assert.NotNull(repaired);
        Assert.Equal(item.Folder, repaired!.Folder);
    }

    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public void GC_preserves_active_items_and_leases_and_deletes_old_tools_stores_and_generations()
    {
        using var f = new StoreFixture();
        var item = f.Create();
        var store = f.Open(item);
        using var lease = store.Acquire("T1");
        var old = new[] { Path.Combine(f.Paths.Tools, "old-version"), Path.Combine(f.Paths.Store, "old@abcd1234"),
            Path.Combine(f.Paths.Views, "claude", "spare-key-nonce"), Path.Combine(f.Paths.Views, "codex", "list-key-nonce"),
            Path.Combine(f.Paths.Views, "claude", "chat-ended"), Path.Combine(f.Paths.Tools, "tmp-crashed") };
        foreach (var path in old) { StoreFixture.WriteContent(path); f.Trees.Lock(path); }
        store.Collect();
        Assert.True(Directory.Exists(item.Folder));
        Assert.All(old, path => Assert.False(Directory.Exists(path)));
    }

    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public void A_running_probe_or_chat_leases_its_generation_until_session_end()
    {
        using var f = new StoreFixture();
        var item = f.Create();
        var store = f.Open(item);
        var generation = Path.Combine(f.Paths.Views, "claude", "chat-one");
        StoreFixture.WriteContent(generation);
        f.Trees.Lock(generation);
        var lease = store.AcquireGeneration(generation, item.Manifest)!;
        store.Collect();
        Assert.True(Directory.Exists(generation));
        lease.Dispose();
        lease.Dispose();
        store.Collect();
        Assert.False(Directory.Exists(generation));
    }

    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public void A_damaged_generation_is_condemned_never_released_to_a_second_session_and_survives_restart()
    {
        using var f = new StoreFixture();
        var item = f.Create();
        var generation = Path.Combine(f.Paths.Views, "claude", "chat-one");
        StoreFixture.WriteContent(generation);
        f.Trees.Lock(generation);
        var store = f.Open(item);
        var lease = store.AcquireGeneration(generation, item.Manifest)!;
        f.Trees.Unlock(generation);
        File.WriteAllBytes(Path.Combine(generation, "json.py"), [1]);
        f.Trees.Lock(generation);
        Assert.Null(store.AcquireGeneration(generation, item.Manifest));
        Assert.Null(f.Open(item).AcquireGeneration(generation, item.Manifest));
        store.Collect();
        Assert.True(Directory.Exists(generation));
        lease.Dispose();
        store.Collect();
        Assert.False(Directory.Exists(generation));
    }

    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public void A_sharing_failure_is_deferred_to_next_GC_without_waiting_or_losing_condemnation()
    {
        using var f = new StoreFixture();
        var item = f.Create();
        var store = f.Open(item);
        store.Retire(item.Folder);
        f.Deletion.Held.Add(item.Folder);
        store.Collect();
        Assert.True(Directory.Exists(item.Folder));
        Assert.False(f.Protection.IsLocked(item.Folder));
        Assert.Contains(item.Folder, JsonSerializer.Deserialize<string[]>(File.ReadAllBytes(f.Paths.CondemnedFile))!);
        f.Deletion.Held.Clear();
        store.Collect();
        Assert.False(Directory.Exists(item.Folder));
        Assert.DoesNotContain(item.Folder, JsonSerializer.Deserialize<string[]>(File.ReadAllBytes(f.Paths.CondemnedFile))!);
    }

    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public void Teardown_unlocks_everything_before_any_delete_and_keeps_enabled_and_accepted_terms()
    {
        using var f = new StoreFixture();
        var item = f.Create();
        var store = f.Open(item);
        var generation = Path.Combine(f.Paths.Views, "claude", "chat-one");
        StoreFixture.WriteContent(generation);
        f.Trees.Lock(generation);
        Directory.CreateDirectory(Path.GetDirectoryName(f.Paths.EnabledFile)!);
        File.WriteAllText(f.Paths.EnabledFile, "enabled sentinel");
        f.Deletion.BeforeDelete = _ =>
        {
            Assert.False(f.Protection.IsLocked(item.Folder));
            Assert.False(f.Protection.IsLocked(generation));
            Assert.All(StoreTrees.Entries(f.Root).Where(File.Exists), path => Assert.False(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly)));
        };
        store.Teardown();
        Assert.False(Directory.Exists(f.Paths.Tools));
        Assert.False(Directory.Exists(f.Paths.Store));
        Assert.False(Directory.Exists(f.Paths.Views));
        Assert.Equal("enabled sentinel", File.ReadAllText(f.Paths.EnabledFile));
    }

    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public void Teardown_leaves_a_held_tree_unlocked_and_later_GC_removes_it()
    {
        using var f = new StoreFixture();
        var item = f.Create();
        var store = f.Open(item);
        f.Deletion.Held.Add(f.Paths.Addons);
        f.Deletion.Held.Add(item.Folder);
        store.Teardown();
        Assert.False(f.Protection.IsLocked(item.Folder));
        Assert.True(Directory.Exists(item.Folder));
        f.Deletion.Held.Clear();
        store.Collect();
        Assert.False(Directory.Exists(item.Folder));
    }

    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public void Paths_and_condemnation_cannot_escape_the_data_root_or_follow_a_link_to_a_fake_home()
    {
        using var f = new StoreFixture();
        var item = f.Create();
        var store = f.Open(item);
        var home = Path.Combine(f.Root, "fake home");
        Directory.CreateDirectory(home);
        var sentinel = Path.Combine(home, "sentinel");
        File.WriteAllText(sentinel, "untouched");
        TestLinks.Directory(f.Root, Path.Combine(f.Paths.Tools, "old-link"), home);
        Assert.Throws<ArgumentException>(() => store.Retire(home));
        store.Collect();
        Assert.Equal("untouched", File.ReadAllText(sentinel));
        Assert.False(Directory.Exists(Path.Combine(f.Paths.Tools, "old-link")));
        Assert.Throws<ArgumentException>(() => new StorePaths("relative"));
    }

    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public void Invalid_condemnation_file_is_not_silently_replaced_and_outside_paths_are_refused()
    {
        using var f = new StoreFixture();
        var item = f.Create();
        Directory.CreateDirectory(Path.GetDirectoryName(f.Paths.CondemnedFile)!);
        File.WriteAllText(f.Paths.CondemnedFile, "garbage");
        Assert.ThrowsAny<Exception>(() => f.Open(item));
        Assert.Equal("garbage", File.ReadAllText(f.Paths.CondemnedFile));
        File.WriteAllText(f.Paths.CondemnedFile, "[\"../fake-home\"]");
        Assert.ThrowsAny<Exception>(() => f.Open(item));
    }

    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public async Task Two_repairs_share_no_download_and_GC_never_deletes_an_active_build()
    {
        using var f = new StoreFixture();
        var item = f.Create();
        var store = f.Open(item);
        f.Tamper(item, "sample/native/fixture.exe", [9]);
        Assert.Null(store.Acquire("T1"));
        f.Downloader.Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Downloader.Continue = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var repair = store.RepairAsync("T1");
        await f.Downloader.Started.Task;
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RepairAsync("T1"));
        store.Collect();
        Assert.NotEmpty(Directory.EnumerateDirectories(f.Paths.Tools, "tmp-*"));
        f.Downloader.Continue.SetResult();
        Assert.True(await repair);
        Assert.Equal(1, f.Downloader.Calls);
    }

    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public void No_production_ACL_seam_silently_succeeds_on_non_Windows()
    {
        using var f = new StoreFixture();
        var item = f.Create();
        var protection = new WindowsStoreProtection();
        if (!OperatingSystem.IsWindows()) Assert.Throws<PlatformNotSupportedException>(() => protection.Lock(item.Folder));
        Assert.False(new StoreVerifier(protection).Verify(item.Folder, item.Manifest).IsValid);
    }
}
