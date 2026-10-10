using System.Security.Cryptography;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Xunit;

namespace Marrow.App.Tests.Views.Composer;

// ED-NEW-launch-fix (1), (2): the GPU program cache is read before Boot B's first frame (an entry asked for before its folder
// is read comes from its file), survives a new build of the app (its folder is named for the GPU, its driver and ANGLE, not
// the build), and is kept under a size cap instead of being removed with every build.
[Collection("Headless")]
public sealed class GpuProgramCacheLaunchTests
{
    // the entry GpuBlobNativeCall's key (bytes 1..8) names
    private static readonly string Entry = Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })).ToLowerInvariant()[..40] + ".bin";

    [Fact, Trait("Decision", "ED-NEW-launchfix-1")] // traceability alias of ED-NEW-launch-fix
    public void A_store_not_yet_read_finds_an_entry_in_its_file()
    {
        using var cache = new GpuProgramCacheFolder();
        File.WriteAllBytes(Path.Combine(cache.Folder, Entry), [7, 8, 9]);
        var store = new GpuProgramBlobStore(cache.Folder);
        Assert.False(store.Loaded);
        Assert.Equal(new byte[] { 7, 8, 9 }, GpuBlobNativeCall.Take(store));
        File.Delete(Path.Combine(cache.Folder, Entry));
        Assert.Equal(new byte[] { 7, 8, 9 }, GpuBlobNativeCall.Take(store)); // read once, then from memory
    }

    [Fact, Trait("Decision", "ED-NEW-launchfix-1")] // traceability alias of ED-NEW-launch-fix
    public void Once_read_a_store_never_goes_to_the_disk_for_a_miss()
    {
        using var cache = new GpuProgramCacheFolder();
        var store = new GpuProgramBlobStore(cache.Folder);
        store.Load();
        Assert.True(store.Loaded);
        File.WriteAllBytes(Path.Combine(cache.Folder, Entry), [7, 8, 9]); // written after the read (another instance)
        var misses = GpuProgramCache.Misses;
        Assert.Empty(GpuBlobNativeCall.Take(store));
        Assert.True(GpuProgramCache.Misses > misses);
    }

    [Fact, Trait("Decision", "ED-NEW-launchfix-1")] // traceability alias of ED-NEW-launch-fix
    public void A_missing_entry_in_a_store_not_yet_read_is_a_miss()
    {
        using var cache = new GpuProgramCacheFolder();
        var store = new GpuProgramBlobStore(Path.Combine(cache.Folder, "not made yet"));
        Assert.Empty(GpuBlobNativeCall.Take(store));
    }

    [Fact, Trait("Decision", "ED-NEW-launchfix-1")] // traceability alias of ED-NEW-launch-fix
    public void The_folder_is_named_for_the_GPU_its_driver_and_ANGLE_and_not_for_the_build()
    {
        const string renderer = "ANGLE (NVIDIA, NVIDIA GeForce RTX 4080 (0x00002704) Direct3D11 vs_5_0 ps_5_0, D3D11-32.0.16.1692)";
        const string version = "OpenGL ES 3.0 (ANGLE 2.1.1 git hash: 1c89805903c1)";
        var name = GpuProgramCache.KeyFolderName(renderer, version);
        Assert.Matches("^[0-9a-f]{24}$", name);
        Assert.Equal(name, GpuProgramCache.KeyFolderName(renderer, version));
        Assert.NotEqual(name, GpuProgramCache.KeyFolderName(renderer.Replace("32.0.16.1692", "32.0.16.1700", StringComparison.Ordinal), version)); // a driver update
        Assert.NotEqual(name, GpuProgramCache.KeyFolderName(renderer, version.Replace("1c89805903c1", "0123456789ab", StringComparison.Ordinal))); // another ANGLE
        Assert.NotEqual(name, GpuProgramCache.KeyFolderName(renderer.Replace("RTX 4080", "RTX 4090", StringComparison.Ordinal), version));          // another GPU
    }

    [Fact, Trait("Decision", "ED-NEW-launchfix-1")] // traceability alias of ED-NEW-launch-fix
    public async Task A_new_build_reads_the_programs_the_last_build_kept_and_still_runs_its_own_warm_up()
    {
        var root = Path.Combine(Path.GetTempPath(), "jewel-build-" + Guid.NewGuid().ToString("N"));
        var source = GpuProgramCache.MachineSource;
        const string machine = "10de:2704:00000000:a1 Test GPU 32.0.15.6094";
        GpuProgramCache.MachineSource = () => machine;
        GpuProgramCache.FolderOverride = root;
        try
        {
            // the last build's warm-up completed: its marker, its key and its programs
            var folder = Path.Combine(root, GpuProgramCache.KeyFolderName("ANGLE (Test GPU)", "OpenGL ES 3.0 (ANGLE test)"));
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "key.txt"), string.Join("\n", "ANGLE (Test GPU)", "OpenGL ES 3.0 (ANGLE test)", "the last build", machine, ""));
            File.WriteAllBytes(Path.Combine(folder, Entry), [4, 5, 6]);
            for (var k = 0; k < GpuWarmup.Steps; k++) File.WriteAllBytes(Path.Combine(folder, k + ".bin"), [1]);
            File.WriteAllLines(Path.Combine(root, "warm.txt"), ["the last build", Path.GetFileName(folder), machine]);

            Assert.False(GpuWarmup.IsWarm); // OD-0096 unchanged: this build's warm-up has not completed
            GpuProgramCache.Prepare();
            var store = GpuProgramCache.StoreOf(folder);
            Assert.NotNull(store);
            Assert.True(store!.Loaded);
            Assert.Equal(new byte[] { 4, 5, 6 }, GpuBlobNativeCall.Take(store)); // linked from the last build's binary
            await store.Drain(); // its touch done before the folder goes

            // this build installs on the same GPU (the same folder; its key names this build now) and completes its warm-up
            File.WriteAllText(Path.Combine(folder, "key.txt"), string.Join("\n", "ANGLE (Test GPU)", "OpenGL ES 3.0 (ANGLE test)", GpuProgramCache.Build, machine, ""));
            GpuProgramCache.MarkWarm(folder);
            Assert.True(File.Exists(Path.Combine(folder, Entry)), "the programs the last build kept were removed");
            Assert.True(GpuWarmup.IsWarm);
        }
        finally
        {
            GpuProgramCache.MachineSource = source;
            GpuProgramCache.FolderOverride = null;
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact, Trait("Decision", "ED-NEW-launchfix-1")] // traceability alias of ED-NEW-launch-fix
    public void The_size_cap_removes_the_entries_used_longest_ago_and_keeps_keys_and_markers()
    {
        var root = Path.Combine(Path.GetTempPath(), "jewel-cap-" + Guid.NewGuid().ToString("N"));
        try
        {
            var a = Directory.CreateDirectory(Path.Combine(root, "aaaaaaaaaaaaaaaaaaaaaaaa")).FullName;
            var b = Directory.CreateDirectory(Path.Combine(root, "bbbbbbbbbbbbbbbbbbbbbbbb")).FullName;
            File.WriteAllText(Path.Combine(root, "warm.txt"), "marker");
            var at = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            string Entry(string folder, string name, int size, int day)
            {
                var file = Path.Combine(folder, name);
                File.WriteAllBytes(file, new byte[size]);
                File.SetLastWriteTimeUtc(file, at.AddDays(day));
                return file;
            }
            File.WriteAllText(Path.Combine(a, "key.txt"), "key a");
            File.WriteAllText(Path.Combine(b, "key.txt"), "key b");
            var oldest = Entry(a, "1.bin", 1000, 1);
            var older = Entry(b, "2.bin", 1000, 2);
            var newer = Entry(a, "3.bin", 1000, 3);
            var newest = Entry(b, "4.bin", 1000, 4);

            GpuProgramCache.Trim(root, 4000);
            Assert.All(new[] { oldest, older, newer, newest }, file => Assert.True(File.Exists(file))); // within the cap: all kept

            GpuProgramCache.Trim(root, 2500);
            Assert.False(File.Exists(oldest));
            Assert.False(File.Exists(older));
            Assert.True(File.Exists(newer));
            Assert.True(File.Exists(newest));
            Assert.True(File.Exists(Path.Combine(a, "key.txt")));
            Assert.True(File.Exists(Path.Combine(b, "key.txt")));
            Assert.True(File.Exists(Path.Combine(root, "warm.txt")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact, Trait("Decision", "ED-NEW-launchfix-1")] // traceability alias of ED-NEW-launch-fix
    public async Task An_entry_read_from_its_file_and_found_is_touched_so_the_cap_keeps_it()
    {
        using var cache = new GpuProgramCacheFolder();
        var file = Path.Combine(cache.Folder, Entry);
        File.WriteAllBytes(file, [4, 5, 6]);
        var old = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(file, old);
        var store = new GpuProgramBlobStore(cache.Folder);
        store.Load();
        Assert.Equal(new byte[] { 4, 5, 6 }, GpuBlobNativeCall.Take(store));
        await store.Drain();
        Assert.True(File.GetLastWriteTimeUtc(file) > old.AddYears(1), "a found entry was not touched");
    }

    [Fact, Trait("Decision", "ED-NEW-launchfix-1")] // traceability alias of ED-NEW-launch-fix
    public async Task Preparing_keeps_the_cache_under_its_cap_before_it_reads_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "jewel-prepcap-" + Guid.NewGuid().ToString("N"));
        var cap = GpuProgramCache.SizeCap;
        GpuProgramCache.FolderOverride = root;
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(root, "cccccccccccccccccccccccc")).FullName;
            var stale = Path.Combine(folder, "stale.bin");
            File.WriteAllBytes(stale, new byte[3000]);
            File.SetLastWriteTimeUtc(stale, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            File.WriteAllBytes(Path.Combine(folder, Entry), [1, 2]);
            GpuProgramCache.SizeCap = 1000;
            GpuProgramCache.Prepare();
            Assert.False(File.Exists(stale));
            Assert.Equal(new byte[] { 1, 2 }, GpuBlobNativeCall.Take(GpuProgramCache.StoreOf(folder)!));
            await GpuProgramCache.StoreOf(folder)!.Drain();
        }
        finally
        {
            GpuProgramCache.SizeCap = cap;
            GpuProgramCache.FolderOverride = null;
            Directory.Delete(root, recursive: true);
        }
    }
}
