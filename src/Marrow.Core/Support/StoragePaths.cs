using Marrow.Core.Port;

namespace Marrow.Core.Support;

/// <summary>
/// PORT-ONLY seam for StoragePaths.swift's side-effecting <c>static let</c> roots (R-LIFE-2), styled on
/// <see cref="Marrow.Services.Store.IStorageLocations"/> (Services/Store/Storage.cs), which this file's
/// Swift source used to share a body with before B3 split it out (Storage.swift B2 :19-25, :35-36,
/// :54-58; N2 in porting/work/b3/B3_PLAN.md section 3). Unbound by default so a test can never create
/// real files under %LOCALAPPDATA%\Marrow Code: a production caller binds
/// <see cref="StoragePathsLocations.CreateProduction"/> at start-up, and a test binds
/// <see cref="StoragePathsLocations.Create(string)"/> under its own throwaway folder (R-TEST-9).
/// </summary>
public interface IStoragePathsLocations
{
    string Root { get; }
    string WallpaperDirectory { get; }
    string Directory(string name);
}

/// <summary>
/// The production shape of StoragePaths.swift's two roots and its folder helper (:6-22). Each of
/// <see cref="Root"/> and <see cref="WallpaperDirectory"/> is created with intermediates on first access
/// and cached for the life of the instance, matching Swift's <c>static let</c>; a creation failure is
/// swallowed (<c>try?</c>) exactly as the source swallows it, and the resolved path is still returned and
/// cached. <see cref="Directory"/> (:18-22) is Swift's plain <c>func</c>, not a <c>static let</c>: never
/// cached here either, recomputed and its folder re-created (idempotently) on every call, exactly as the
/// source recomputes it.
/// </summary>
public sealed class StoragePathsLocations : IStoragePathsLocations
{
    private readonly Lazy<string> root;
    private readonly Lazy<string> wallpaperDirectory;

    private StoragePathsLocations(Func<string> root)
    {
        this.root = CreateOnce(() =>
        {
            // :7-9. dead_code.md #44: the CaptureRun.storageRoot redirect (:7) is reachable only via
            // --website-captures / --tour-captures, never a normal launch, so it is not reproduced here
            // (matching Services.Store.StorageLocations, which never reproduced it either): the caller
            // supplies the already-resolved base folder directly (see CreateProduction and Create).
            var url = root();
            SwiftTry.Ignore(() => System.IO.Directory.CreateDirectory(url)); // :10 try? createDirectory(withIntermediateDirectories: true)
            return url;
        });
        wallpaperDirectory = CreateOnce(() => DirectoryCore("wallpaper")); // :15
    }

    public string Root => root.Value; // :6-12
    public string WallpaperDirectory => wallpaperDirectory.Value; // :15
    public string Directory(string name) => DirectoryCore(name); // :18-22, never cached (matches the Swift func)

    private string DirectoryCore(string name)
    {
        var url = SwiftPath.AppendingPathComponent(Root, name); // :19
        SwiftTry.Ignore(() => System.IO.Directory.CreateDirectory(url)); // :20
        return url;
    }

    private static Lazy<string> CreateOnce(Func<string> factory) => new(factory, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// DEVIATION OD-0008 C7 (mirrors Services.Store.StorageLocations.CreateProduction): %LOCALAPPDATA%\Marrow
    /// Code, a fixed literal rather than AppInfo.Name (upstream appends the display name, falling back to
    /// "Droppy Code", Text.swift:4; :9 <c>base.appendingPathComponent(AppInfo.name, ...)</c>), so the port
    /// never shares a folder with the owner's separate Electron "Marrow" (%LOCALAPPDATA%\Marrow).
    /// </summary>
    public static StoragePathsLocations CreateProduction() => new(() =>
        SwiftPath.AppendingPathComponent(DevDataRoot.LocalApplicationData(), AppInfo.DataFolderName)); // ED-212; OD-0135

    /// <summary>A test's own throwaway root (R-TEST-9): a real folder under MARROW_PORT_DATA_DIR, never production.</summary>
    public static StoragePathsLocations Create(string root) => new(() => root);

    /// <summary>For a caller (Services.Store.StorageLocations) that already owns its own root factory.</summary>
    public static StoragePathsLocations Create(Func<string> root) => new(root);
}

file sealed class UnboundStoragePathsLocations : IStoragePathsLocations
{
    private const string Message = "StoragePaths.Locations is not bound: a start-up path binds it; tests override it.";
    public string Root => throw new SwiftTrapException(Message);
    public string WallpaperDirectory => throw new SwiftTrapException(Message);
    public string Directory(string name) => throw new SwiftTrapException(Message);
}

/// <summary>
/// :3-4 Storage locations that views need too; they live in Core because UI can't reach
/// Services.Store.Storage. Services.Store.StorageLocations builds its own
/// <see cref="StoragePathsLocations"/> directly (its Root/ThreadsDirectory/AttachmentsDirectory/
/// WallpaperDirectory now call through it) rather than reading this Ambient, so this lane never needs to
/// touch Services.Store.Storage's public root/homeDirectory-driven construction or the 9 unrelated test
/// fixtures that call <c>StorageLocations.Create(root, homeDirectory)</c>. This Ambient is the seam a
/// caller that cannot reach Services (e.g. UI.Theme.Wallpaper, once ED-189/O5 moves it back to Marrow.UI)
/// binds and reads directly instead.
/// </summary>
public static class StoragePaths
{
    public static Ambient<IStoragePathsLocations> Locations { get; } = new(new UnboundStoragePathsLocations());

    public static string Root => Locations.Current.Root; // :6-12
    public static string WallpaperDirectory => Locations.Current.WallpaperDirectory; // :15
    public static string Directory(string name) => Locations.Current.Directory(name); // :18-22
}
