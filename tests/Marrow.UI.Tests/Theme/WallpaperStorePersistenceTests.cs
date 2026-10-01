using Marrow.UI.Theme;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Theme;

// Source: DroppyCode/UI/Theme/Wallpaper.swift (B3 52bc7926). The store's start-up read and the two defaults keys it owns.
[Collection("Headless")]
public sealed class WallpaperStorePersistenceTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-THEME-053")] // UI/Theme/Wallpaper.swift:15-17 one store, `static let shared`, reached through its private init
    public void There_is_one_way_to_make_a_store_and_a_fresh_one_holds_no_picture() => headless.OnUiThread(() =>
    {
        var constructors = typeof(WallpaperStore).GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        Assert.All(constructors, c => Assert.True(c.IsPrivate, "the store's initialiser is private (Wallpaper.swift:85), so Shared is the only store"));
        Assert.Single(constructors);

        using var rig = new WallpaperStoreRig();
        var store = rig.NewStore();
        Assert.Null(store.Image); // :32 nil when no wallpaper is set
        Assert.False(store.IsInstalling); // :34
        Assert.Null(store.LastError); // :36
        Assert.False(store.HasWallpaper); // :57
    });

    [Fact]
    [Trait("Req", "PR-THEME-056")] // UI/Theme/Wallpaper.swift:85-87 `stored ... as? Double ?? 0`, `min(1, max(0, stored))`
    [Trait("Req", "PR-SETTINGS-103")] // UI/Theme/Wallpaper.swift:85-93 softness clamped on read, default 0
    public void At_start_softness_is_the_stored_double_clamped_into_0_to_1_and_is_not_written_back() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        foreach (var (stored, expected) in new[] { (-1.0, 0.0), (0.5, 0.5), (2.0, 1.0), (0.0, 0.0), (1.0, 1.0) })
        {
            rig.Defaults.Set("wallpaperSoftness", stored);
            rig.Defaults.Writes.Clear();
            var store = rig.NewStore();
            Assert.Equal(expected, store.Softness);
            Assert.Empty(rig.Defaults.Writes); // "reads only; the clamped softness is not written back at start"
            Assert.Equal(stored, rig.Defaults.Object("wallpaperSoftness"));
        }
    });

    [Fact]
    [Trait("Req", "PR-THEME-056")] // UI/Theme/Wallpaper.swift:86 `as? Double ?? 0`: nothing stored, or not a Double, is 0
    [Trait("Req", "PR-SETTINGS-103")] // UI/Theme/Wallpaper.swift:86 default 0
    public void At_start_a_missing_or_non_double_softness_is_zero() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        Assert.Equal(0, rig.NewStore().Softness);

        rig.Defaults.Set("wallpaperSoftness", "0.7");
        Assert.Equal(0, rig.NewStore().Softness);
    });

    [Fact]
    [Trait("Req", "PR-THEME-056")] // UI/Theme/Wallpaper.swift:88-94 an existing stored file becomes the wallpaper and a full render starts
    [Trait("Req", "PR-SETTINGS-103")] // UI/Theme/Wallpaper.swift:88-93
    [Trait("Req", "PR-THEME-053")] // UI/Theme/Wallpaper.swift:57 hasWallpaper is true with a file before the render finishes
    public void A_stored_file_that_exists_is_the_wallpaper_at_once_and_its_render_arrives_later() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        var picture = WallpaperPictures.Png(rig.Sources, "kept.png", 300, 20);
        rig.PlaceInLibrary("wallpaper-KEPT.png", picture);

        var store = rig.NewStore(out var published);

        Assert.True(store.HasWallpaper); // :57 `image != nil || fileURL != nil`: the file is known, the picture is not rendered yet
        Assert.Null(store.Image);
        Assert.Null(store.LastError);
        WallpaperStoreRig.WaitFor(() => store.Image is not null, "the start-up render");
        Assert.Equal(300, store.Image!.Size.Width);
        Assert.Equal(20, store.Image.Size.Height);
        Assert.Single(published);
        Assert.Equal("wallpaper-KEPT.png", rig.Defaults.Object("wallpaperFile"));
    });

    [Fact]
    [Trait("Req", "PR-THEME-056")] // UI/Theme/Wallpaper.swift:90-93 `if FileManager.default.fileExists(...)`: a missing file is ignored
    [Trait("Req", "PR-SETTINGS-103")] // failure_cases: stored name present but the file is gone: no wallpaper, stale key left behind
    public void A_stored_name_whose_file_is_gone_leaves_no_wallpaper_no_error_and_keeps_the_key() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        rig.Defaults.Set("wallpaperFile", "wallpaper-GONE.png");
        rig.Defaults.Writes.Clear();

        var store = rig.NewStore();
        WallpaperStoreRig.Settle(100);

        Assert.False(store.HasWallpaper);
        Assert.Null(store.Image);
        Assert.Null(store.LastError);
        Assert.False(store.IsInstalling);
        Assert.Equal("wallpaper-GONE.png", rig.Defaults.Object("wallpaperFile")); // the stale key is NOT cleaned up
        Assert.Empty(rig.Defaults.Writes);
        Assert.Null(WallpaperStoreRig.RenderTask(store)); // no render was started
    });

    [Fact]
    [Trait("Req", "PR-THEME-056")] // UI/Theme/Wallpaper.swift:88 `!name.isEmpty`: an empty stored name is ignored
    public void An_empty_stored_file_name_is_ignored() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        _ = rig.Library; // the library folder exists, which an empty name would resolve to
        rig.Defaults.Set("wallpaperFile", "");
        rig.Defaults.Writes.Clear();

        var store = rig.NewStore();

        Assert.False(store.HasWallpaper);
        Assert.Null(WallpaperStoreRig.RenderTask(store));
    });

    [Fact]
    [Trait("Req", "PR-THEME-055")] // UI/Theme/Wallpaper.swift:222 `defaults.set(file.lastPathComponent, forKey: Key.file)`
    [Trait("Req", "PR-SETTINGS-103")] // UI/Theme/Wallpaper.swift:219-227 only the last path component is stored; the file lives in the library
    public void Installing_stores_only_the_file_name_under_wallpaperFile_and_a_restart_shows_the_same_picture() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        var picture = WallpaperPictures.Png(rig.Sources, "sky.png", 300, 20);
        var store = rig.NewStore();

        store.Install(picture);
        WallpaperStoreRig.WaitFor(() => !store.IsInstalling, "the install");

        var stored = Assert.IsType<string>(rig.Defaults.Object("wallpaperFile"));
        Assert.DoesNotContain('\\', stored);
        Assert.DoesNotContain('/', stored);
        Assert.Equal(Path.GetFileName(Assert.Single(Directory.GetFiles(rig.Library))), stored);
        Assert.Equal(1, rig.Defaults.Writes.Count(k => k == "wallpaperFile"));

        // The restart: the stored name plus the library reproduce the picture.
        var restarted = rig.NewStore();
        Assert.True(restarted.HasWallpaper);
        WallpaperStoreRig.WaitFor(() => restarted.Image is not null, "the restart render");
        Assert.Equal(300, restarted.Image!.Size.Width);
        Assert.Equal(20, restarted.Image.Size.Height);
    });

    [Fact]
    [Trait("Req", "PR-THEME-055")] // UI/Theme/Wallpaper.swift:52 `defaults.set(softness, forKey: Key.softness)`; the key is never removed
    [Trait("Req", "PR-SETTINGS-103")] // "wallpaperSoftness" as a Double, default 0
    public void Softness_is_stored_as_a_double_under_wallpaperSoftness_and_the_key_is_only_ever_overwritten() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        var store = rig.NewStore();

        store.Softness = 0.25;
        Assert.Equal(0.25, Assert.IsType<double>(rig.Defaults.Object("wallpaperSoftness")));
        store.Softness = 0;
        Assert.Equal(0.0, Assert.IsType<double>(rig.Defaults.Object("wallpaperSoftness"))); // overwritten with 0, not removed

        // A restart reads it back.
        store.Softness = 0.75;
        Assert.Equal(0.75, rig.NewStore().Softness);
    });
}
