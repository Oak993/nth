using System.Runtime.CompilerServices;

namespace Marrow.App.Tests;

// PORT-ONLY (no Swift twin): in DroppyCode, StoragePaths.wallpaperDirectory/.root are plain `nonisolated static
// let` globals that just work; the port's equivalents are Ambient<T>s (R-LIFE-2) a production caller must bind
// at start-up, or every reader traps. Program.cs already binds Storage.Locations this way (Storage.swift:19-48's
// roots); ED-195 (lane A10) split a sibling Ambient, Marrow.Core.Support.StoragePaths.Locations, out of Storage
// at B3, and lane L2e's review (2026-09-26) found Program.cs was never updated to bind it too: the first
// production read of StoragePaths.WallpaperDirectory (Marrow.UI.Theme.Wallpaper.cs:154, :187, :191, reached the
// moment Settings > Appearance constructs WallpaperStore.Shared, or RootView's backdrop at every launch once
// PR-THEME-053 lands) throws SwiftTrapException("StoragePaths.Locations is not bound..."). Program.Main cannot
// run inside a unit test host (STAThread, the single-instance lock and the full Avalonia lifetime all need a
// real process, and it would touch the real %LOCALAPPDATA%\Marrow Code folder, forbidden by R-TEST-9 the same
// way StoragePathsLocations.CreateProduction() itself is never exercised in StoragePathsTests.cs): this proves
// the wiring by reading Program.cs's own source and asserting both bindings are present side by side, the same
// technique ED-076's planned per-layer source-scan tests use for other must-not-regress patterns.
public sealed class ProgramStartupBindingTests
{
    [Fact]
    [Trait("Decision", "ED-195")]
    public void Main_binds_StoragePathsLocations_alongside_StorageLocations_before_any_window_opens()
    {
        var source = File.ReadAllText(ProgramCsPath());

        Assert.Contains(
            "Storage.Locations.SetProcessDefault(StorageLocations.CreateProduction());",
            source, StringComparison.Ordinal);
        Assert.Contains(
            "StoragePaths.Locations.SetProcessDefault(StoragePathsLocations.CreateProduction());",
            source, StringComparison.Ordinal);
    }

    private static string ProgramCsPath([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", "src", "Marrow.App", "Program.cs"));
}
