using System.Text.Json;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Store;
using Marrow.Services.Tests.Port.Skills.Store.Fixtures;

namespace Marrow.Services.Tests.Port.Skills.Store;

// PORT-ONLY: adversarial cases for PLAN 2.1/DL-5, added before the boundary repairs.
[Trait("Spec", "DL-5")]
public sealed class StoreLifecycleReviewTests
{
    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public void Full_store_verification_precedes_every_view_build()
    {
        using var f = new StoreFixture();
        var item = f.Create();
        var store = f.Open(item);
        f.Tamper(item, "sample/SKILL.md", new byte[StoreFixture.Content["sample/SKILL.md"].Length]);
        Assert.Null(store.AcquireForViewBuild("T1"));
        Assert.Equal(ItemState.Damaged, store.State.Get("T1").State);
        Assert.Null(store.Acquire("T1"));
    }

    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public void Teardown_deletes_unleased_trees_now_and_leased_trees_after_session_end_while_retaining_terms()
    {
        using var f = new StoreFixture();
        var item = f.Create();
        var term = new AcceptedTerm(new string('a', 64), "T1", DateTimeOffset.Parse("2026-10-04T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var state = AddonsState.Restore([new ItemRecord("T1", ItemState.Installed, "1.0", null, null)], [term], [], []);
        var store = new SkillsStore(f.Paths, [item], state, f.Protection, f.Deletion);
        var generations = new[] { "chat-live", "chat-ended", "spare-key-one" }.Select(n => Path.Combine(f.Paths.Views, "claude", n)).ToArray();
        foreach (var path in generations) { StoreFixture.WriteContent(path); f.Trees.Lock(path); }
        var lease = store.AcquireGeneration(generations[0], item.Manifest)!;
        store.Teardown();
        Assert.True(Directory.Exists(generations[0]));
        Assert.False(f.Protection.IsLocked(generations[0]));
        Assert.False(Directory.Exists(generations[1]));
        Assert.False(Directory.Exists(generations[2]));
        Assert.Equal(term, Assert.Single(AddonsStateStore.Load(f.Paths.StateFile).AcceptedTerms));
        lease.Dispose();
        store.Collect();
        Assert.False(Directory.Exists(generations[0]));
    }

    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public async Task A_cancelled_repair_returns_to_damaged_without_changing_the_old_pin()
    {
        using var f = new StoreFixture();
        var item = f.Create();
        var store = f.Open(item);
        f.Tamper(item, "sample/native/fixture.exe", [9]);
        Assert.Null(store.Acquire("T1"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.RepairAsync("T1", cancellation.Token));
        Assert.Equal(ItemState.Damaged, store.State.Get("T1").State);
        Assert.Equal("1.0", store.State.Get("T1").InstalledPin);
        Assert.Null(store.Acquire("T1"));
        Assert.Empty(Directory.EnumerateDirectories(f.Paths.Tools, "tmp-*"));
    }

    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public async Task A_held_old_folder_defers_repair_promotion_until_next_GC()
    {
        using var f = new StoreFixture();
        var item = f.Create();
        var store = f.Open(item);
        f.Tamper(item, "sample/native/fixture.exe", [9]);
        Assert.Null(store.Acquire("T1"));
        f.Deletion.Held.Add(item.Folder);
        Assert.False(await store.RepairAsync("T1"));
        Assert.Null(store.Acquire("T1"));
        f.Deletion.Held.Clear();
        store.Collect();
        using var lease = store.Acquire("T1");
        Assert.NotNull(lease);
    }

    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public async Task Tampering_with_a_pending_repair_is_detected_before_promotion()
    {
        using var f = new StoreFixture();
        var item = f.Create();
        var store = f.Open(item);
        var live = store.Acquire("T1")!;
        f.Tamper(item, "sample/native/fixture.exe", [9]);
        Assert.Null(store.Acquire("T1"));
        Assert.False(await store.RepairAsync("T1"));
        var prepared = Assert.Single(Directory.EnumerateDirectories(f.Paths.Tools, "tmp-*"));
        f.Trees.Unlock(prepared);
        File.WriteAllBytes(Path.Combine(prepared, "LICENSE.txt"), [9]);
        f.Trees.Lock(prepared);
        live.Dispose();
        store.Collect();
        Assert.Equal(ItemState.Failed, store.State.Get("T1").State);
        Assert.Null(store.Acquire("T1"));
    }

    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public async Task Plain_file_set_repair_uses_the_same_catalogue_files_and_keeps_the_licence_beside_them()
    {
        using var f = new StoreFixture();
        var item = f.Create("store", "source@12345678", ItemKind.Skillset);
        var store = new SkillsStore(f.Paths, [item], f.Open(item).State, f.Protection, f.Deletion, f.Downloader);
        f.Tamper(item, "sample/native/fixture.exe", [9]);
        Assert.Null(store.Acquire("T1"));
        Assert.True(await store.RepairAsync("T1"));
        Assert.Equal(StoreFixture.Content["LICENSE.txt"], File.ReadAllBytes(Path.Combine(item.Folder, "LICENSE.txt")));
    }

    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public void An_unpinned_set_missing_licence_or_replaced_file_inventory_is_refused()
    {
        using var f = new StoreFixture();
        var item = f.Create("store", kind: ItemKind.Skillset);
        Assert.Throws<ArgumentException>(() => new StoreItem(item.Catalogue with { PinState = PinState.Unpinned }, item.Folder, item.Manifest));
        Assert.Throws<ArgumentException>(() => new StoreItem(item.Catalogue with { LicenceFile = null }, item.Folder, item.Manifest));
        Assert.Throws<ArgumentException>(() => new StoreItem(item.Catalogue, item.Folder, new StoreManifest(item.Manifest.Files.Skip(1))));
        Assert.Throws<ArgumentException>(() => new StoreItem(item.Catalogue, item.Folder, item.Manifest, "newer-pin"));
    }

    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public void GC_never_deletes_a_descendant_of_an_active_installed_tree()
    {
        using var f = new StoreFixture();
        var item = f.Create();
        var store = f.Open(item);
        store.Retire(Path.Combine(item.Folder, "sample"));
        store.Collect();
        Assert.True(File.Exists(Path.Combine(item.Folder, "sample/SKILL.md")));
        Assert.Null(store.Acquire("T1")); // Any condemned descendant makes the whole registered item unusable.
    }

    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public void A_symlink_in_the_managed_ancestor_or_root_never_reaches_the_fake_home()
    {
        using var f = new StoreFixture();
        var home = Path.Combine(f.Root, "fake home");
        Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "sentinel"), "untouched");
        Directory.CreateDirectory(f.Paths.Addons);
        TestLinks.Directory(f.Root, f.Paths.Tools, home);
        Assert.Throws<IOException>(() => new SkillsStore(f.Paths, [], AddonsState.Empty, f.Protection).Collect());
        Assert.Equal("untouched", File.ReadAllText(Path.Combine(home, "sentinel")));
    }

    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public async Task Teardown_during_a_download_cannot_resurrect_the_store_or_publish_a_repair()
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
        store.Teardown();
        f.Downloader.Continue.SetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => repair);
        Assert.Null(store.Acquire("T1"));
        Assert.False(Directory.Exists(item.Folder));
        Assert.False(Directory.Exists(f.Paths.Tools));
    }
    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public void Two_chat_generations_are_copies_and_tampering_one_never_changes_the_other_or_store()
    {
        using var f = new StoreFixture();
        var item = f.Create();
        var store = f.Open(item);
        var a = Path.Combine(f.Paths.Views, "claude", "chat-a");
        var b = Path.Combine(f.Paths.Views, "claude", "chat-b");
        foreach (var path in new[] { a, b }) { StoreFixture.WriteContent(path); f.Trees.Lock(path); }
        using var leaseA = store.AcquireGeneration(a, item.Manifest);
        using var leaseB = store.AcquireGeneration(b, item.Manifest);
        Assert.NotNull(leaseA);
        Assert.NotNull(leaseB);
        f.Trees.Unlock(a);
        File.WriteAllBytes(Path.Combine(a, "sample/SKILL.md"), [9]);
        f.Trees.Lock(a);
        var verifier = new StoreVerifier(f.Protection);
        Assert.False(verifier.Verify(a, item.Manifest).IsValid);
        Assert.True(verifier.Verify(b, item.Manifest).IsValid);
        Assert.True(verifier.Verify(item.Folder, item.Manifest).IsValid);
    }

    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public async Task Multi_pin_sets_keep_their_per_skill_pins_and_explicit_installed_key_during_Repair()
    {
        using var f = new StoreFixture();
        var original = f.Create("store", "source@12345678", ItemKind.Skillset);
        var descriptor = original.Catalogue with { Pin = null, Skills = [original.Catalogue.Skills[0] with { Pin = new string('b', 40) }] };
        var item = new StoreItem(descriptor, original.Folder, original.Manifest, "explicit-multi-pin-key");
        var state = AddonsState.Restore([new ItemRecord("T1", ItemState.Installed, item.Pin, null, null)], [], [], []);
        var store = new SkillsStore(f.Paths, [item], state, f.Protection, f.Deletion, f.Downloader);
        f.Tamper(item, "sample/native/fixture.exe", [9]);
        Assert.Null(store.Acquire("T1"));
        Assert.True(await store.RepairAsync("T1"));
        Assert.Null(f.Downloader.Requested!.Pin);
        Assert.Equal(new string('b', 40), f.Downloader.Requested.Skills[0].Pin);
        Assert.Equal("explicit-multi-pin-key", store.State.Get("T1").InstalledPin);
    }

    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public void Missing_inherited_deny_on_one_file_is_damage_even_with_a_locked_root()
    {
        using var f = new StoreFixture();
        var item = f.Create();
        f.Fake.Unlock(Path.Combine(item.Folder, "LICENSE.txt"));
        Assert.True(f.Protection.IsLocked(item.Folder));
        Assert.False(new StoreVerifier(f.Protection).Verify(item.Folder, item.Manifest).IsValid);
    }

    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public void A_restart_loads_real_SK_7_state_and_never_releases_the_condemned_pin()
    {
        using var f = new StoreFixture();
        var item = f.Create();
        var store = f.Open(item);
        f.Tamper(item, "sample/native/fixture.exe", [9]);
        Assert.Null(store.Acquire("T1"));
        var restarted = new SkillsStore(f.Paths, [item], AddonsStateStore.Load(f.Paths.StateFile), f.Protection, f.Deletion);
        Assert.Null(restarted.Acquire("T1"));
        Assert.Equal(ItemState.Damaged, restarted.State.Get("T1").State);
    }

    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public void Successful_teardown_removes_condemnation_and_empty_addons_metadata()
    {
        using var f = new StoreFixture();
        var item = f.Create();
        var store = f.Open(item);
        store.Retire(item.Folder);
        store.Teardown();
        Assert.False(Directory.Exists(f.Paths.Tools));
        Assert.False(Directory.Exists(f.Paths.Store));
        Assert.False(Directory.Exists(f.Paths.Views));
        Assert.False(File.Exists(f.Paths.CondemnedFile));
        Assert.False(Directory.Exists(f.Paths.Addons));
    }

    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public async Task A_failure_after_repair_rename_is_unusable_and_can_be_retried_without_a_stale_pending_repair()
    {
        using var f = new StoreFixture();
        var item = f.Create();
        var store = f.Open(item);
        f.Tamper(item, "sample/native/fixture.exe", [9]);
        Assert.Null(store.Acquire("T1"));
        f.Fake.BeforeLock = folder => { if (folder == item.Folder) throw new IOException("Fixture final lock failure"); };
        Assert.False(await store.RepairAsync("T1"));
        Assert.Equal(ItemState.Failed, store.State.Get("T1").State);
        Assert.Null(store.Acquire("T1"));
        f.Fake.BeforeLock = null;
        Assert.True(await store.RepairAsync("T1"));
        using var lease = store.Acquire("T1");
        Assert.NotNull(lease);
    }

    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public void A_held_shim_outside_tools_is_unlocked_and_removed_by_the_next_teardown_GC()
    {
        using var f = new StoreFixture();
        var item = f.Create();
        var store = f.Open(item);
        var bin = Path.Combine(f.Paths.Addons, "bin");
        StoreFixture.WriteContent(bin);
        f.Trees.Lock(bin);
        f.Deletion.Held.Add(Path.Combine(bin, "LICENSE.txt"));
        store.Teardown();
        Assert.True(Directory.Exists(bin));
        Assert.False(f.Protection.IsLocked(bin));
        f.Deletion.Held.Clear();
        store.Collect();
        Assert.False(Directory.Exists(bin));
        Assert.False(Directory.Exists(f.Paths.Addons));
    }

}
