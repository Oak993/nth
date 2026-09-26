using Marrow.Core.Support;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Support;

// Tests for src/Marrow.Core/Support/StoragePaths.cs, the port of
// DroppyCode/Core/Support/StoragePaths.swift (B3 52bc7926), :1-23. This is the new home of the root,
// wallpaperDirectory and directory(_:) code that used to live inline in Services/Store/Storage.swift
// (B2 1fbf81e, :19-25, :35-36, :54-58); Services.Store.StorageTests keeps its own test proving Storage.Root
// still resolves correctly through the move (Root_IsCreatedWithIntermediatesAndCachedForTheScope). Storage no
// longer has a WallpaperDirectory member at all (lane A10/ED-195): UI.Theme.Wallpaper reads
// StoragePaths.WallpaperDirectory (below) directly.
public sealed class StoragePathsTests
{
    // ---- PR-STORE-001 (root, moved to StoragePaths.swift:6-12) ----

    [Fact]
    [Trait("Req", "PR-STORE-001")] // Core/Support/StoragePaths.swift:6-12 (B3); moved from Storage.swift:19-25 (B2)
    public void Root_IsCreatedWithIntermediatesAndCachedForTheScope()
    {
        using var scratch = new StoragePathsScratchFolder();
        Assert.False(Directory.Exists(scratch.Root));

        var first = StoragePaths.Root;
        Assert.True(Directory.Exists(first));
        Assert.Equal(scratch.Root, first);

        Directory.Delete(first); // prove the second access is the cached value, not a fresh stat/mkdir
        Assert.Equal(first, StoragePaths.Root);
    }

    [Fact]
    [Trait("Req", "PR-STORE-001")] // Core/Support/StoragePaths.swift:6-10; try? swallows a creation failure
    public void Root_SwallowsACreationFailureAndStillCachesTheUrl()
    {
        // A file occupying the root's own path makes Directory.CreateDirectory throw; the source's `try?`
        // (:10) discards that error and the URL is still returned and cached, exactly like Storage's own root.
        var dataDirectory = TestDataDirectory.Read();
        Assert.False(string.IsNullOrEmpty(dataDirectory));
        var baseFolder = Path.Combine(dataDirectory!, "StoragePathsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseFolder);
        var blockedRoot = Path.Combine(baseFolder, "blocked-root");
        File.WriteAllText(blockedRoot, "occupied");
        using var scope = StoragePaths.Locations.Override(StoragePathsLocations.Create(blockedRoot));
        try
        {
            Assert.Equal(blockedRoot, StoragePaths.Root);
            Assert.Equal(blockedRoot, StoragePaths.Root); // cached: a second read never throws either
        }
        finally
        {
            try { Directory.Delete(baseFolder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    // ---- PR-STORE-005 (wallpaperDirectory, moved to StoragePaths.swift:15) ----

    [Fact]
    [Trait("Req", "PR-STORE-005")] // Core/Support/StoragePaths.swift:15; moved from Storage.swift:35-36 (B2)
    public void WallpaperDirectory_IsCreatedUnderTheRootAndCached()
    {
        using var scratch = new StoragePathsScratchFolder();
        var directory = StoragePaths.WallpaperDirectory;
        Assert.Equal(Path.Combine(scratch.Root, "wallpaper"), directory);
        Assert.True(Directory.Exists(directory));

        Directory.Delete(directory);
        Assert.Equal(directory, StoragePaths.WallpaperDirectory); // cached, like Root
    }

    // ---- directory(_:) (Core/Support/StoragePaths.swift:18-22; shared machinery behind PR-STORE-003/004/005) ----

    [Fact]
    [Trait("Req", "PR-STORE-003")] // Core/Support/StoragePaths.swift:18-22; moved from Storage.swift:54-58 (B2)
    public void Directory_CreatesTheNamedFolderUnderRootAndIsNeverCached()
    {
        using var scratch = new StoragePathsScratchFolder();
        var directory = StoragePaths.Directory("threads");
        Assert.Equal(Path.Combine(scratch.Root, "threads"), directory);
        Assert.True(System.IO.Directory.Exists(directory));

        System.IO.Directory.Delete(directory);
        Assert.False(System.IO.Directory.Exists(directory));
        Assert.Equal(directory, StoragePaths.Directory("threads")); // recomputed and recreated, never cached
        Assert.True(System.IO.Directory.Exists(directory));
    }

    // No test exercises StoragePathsLocations.CreateProduction().Root directly: doing so would create the
    // real %LOCALAPPDATA%\Marrow Code folder on the machine running the tests, which R-TEST-9 forbids
    // (Services.Store.StorageLocations.CreateProduction is untested for the identical reason). The literal
    // it uses (DEVIATION OD-0008 C7) is reviewed by inspection, matching Storage's own production factory.
}
