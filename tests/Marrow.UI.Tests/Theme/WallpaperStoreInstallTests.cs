using System.Text.RegularExpressions;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Theme;

// Source: DroppyCode/UI/Theme/Wallpaper.swift (B3 52bc7926), :113-156 install and remove, :219-232 finish and fail.
[Collection("Headless")]
public sealed class WallpaperStoreInstallTests(HeadlessSession headless)
{
    private static void Pick(WallpaperStore store, string path)
    {
        store.Install(path);
        WallpaperStoreRig.WaitFor(() => !store.IsInstalling, "the install");
    }

    [Fact]
    [Trait("Req", "PR-THEME-060")] // UI/Theme/Wallpaper.swift:118-121 isInstalling = true, lastError = nil, renderTask?.cancel()
    public void Installing_raises_the_flag_clears_the_error_and_cancels_the_render_in_flight() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        var store = rig.Loaded(out _, out _);
        store.Install(WallpaperPictures.Text(rig.Sources, "notes.txt"));
        Assert.Equal("That is not a picture.", store.LastError);
        var render = WallpaperStoreRig.RenderTask(store);
        Assert.NotNull(render);
        Assert.False(render!.IsCancelled); // a rejected pick leaves the render alone (:114-117 returns first)

        store.Install(WallpaperPictures.Png(rig.Sources, "good.png", 64, 8));

        Assert.True(store.IsInstalling); // at once, so the card shows its progress indicator
        Assert.Null(store.LastError);
        Assert.True(render.IsCancelled);
        WallpaperStoreRig.WaitFor(() => !store.IsInstalling, "the install");
        Assert.Null(store.LastError);
    });

    [Fact]
    [Trait("Req", "PR-THEME-060")] // UI/Theme/Wallpaper.swift:127-131 "wallpaper-<uuid>.<ext>", ext lower-cased, directory created, item copied
    public void Install_copies_the_picture_into_the_library_as_wallpaper_uuid_with_its_lower_cased_extension() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        var store = rig.NewStore();
        var shout = WallpaperPictures.Png(rig.Sources, "Sky.PNG", 64, 8);
        Directory.Delete(rig.Library, recursive: true); // the library folder is made again with intermediates (:130)

        Pick(store, shout);

        Assert.Null(store.LastError);
        var first = Assert.Single(rig.LibraryFiles());
        Assert.Matches(new Regex(@"^wallpaper-[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}\.png$"), first);
        Assert.True(File.Exists(shout)); // a copy, not a move
        Assert.Equal(first, rig.Defaults.Object("wallpaperFile"));

        // A source with no extension gets "jpg" (:127); the old file goes once the new one is in.
        var bare = Path.Combine(rig.Sources, "bare");
        File.Copy(shout, bare);
        Pick(store, bare);
        Assert.Null(store.LastError);
        var second = Assert.Single(rig.LibraryFiles());
        Assert.Matches(new Regex(@"^wallpaper-[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}\.jpg$"), second);
        Assert.Equal(second, rig.Defaults.Object("wallpaperFile"));

        // The same source again is a new generated name: names never collide.
        Pick(store, shout);
        var third = Assert.Single(rig.LibraryFiles());
        Assert.NotEqual(first, third);
        Assert.EndsWith(".png", third, StringComparison.Ordinal);
    });

    [Fact]
    [Trait("Req", "PR-THEME-060")] // UI/Theme/Wallpaper.swift:121, :137, :264-269 decoded at up to 3200 px, blurred at the softness captured at the start; :223-225 spring 0.45, bounce 0.2
    public void Install_decodes_at_up_to_3200_pixels_blurred_at_the_softness_it_started_with_and_springs_in() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        var store = rig.NewStore(out var published);
        store.Softness = 0.05;
        var picture = WallpaperPictures.Png(rig.Sources, "wide.png", 3300, 8);

        store.Install(picture);
        store.Softness = 0.9; // changed after the install began: the install keeps the value it captured
        WallpaperStoreRig.WaitFor(() => !store.IsInstalling, "the install");

        Assert.Null(store.LastError);
        var installed = Path.Combine(rig.Library, Assert.Single(rig.LibraryFiles()));
        var picturePixels = WallpaperStoreRig.Read(store.Image);
        Assert.Equal(3200, picturePixels.Width); // 3300 reduced to the 3200 limit
        Assert.Equal(8, picturePixels.Height);
        var crisp = WallpaperStore.Thumbnail(installed, 3200)!;
        Assert.True(WallpaperStoreRig.SamePixels(picturePixels, WallpaperStore.Blurred(crisp, 0.05)!));
        Assert.False(WallpaperStoreRig.SamePixels(picturePixels, WallpaperStore.Blurred(crisp, 0.9)!));
        var arrival = Assert.Single(published);
        Assert.Equal(Animation.Spring(duration: 0.45, bounce: 0.2), arrival.Animation);
        Assert.False(store.IsInstalling);
    });

    [Fact]
    [Trait("Req", "PR-THEME-060")] // UI/Theme/Wallpaper.swift:220-222 finishInstall deletes the previous file, then records and persists the new one
    public void A_successful_install_replaces_the_previous_file_and_stored_name() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        var store = rig.Loaded(out _, out var previous);
        Assert.True(File.Exists(previous));

        Pick(store, WallpaperPictures.Png(rig.Sources, "next.png", 64, 8));

        Assert.False(File.Exists(previous));
        var current = Assert.Single(rig.LibraryFiles());
        Assert.NotEqual(Path.GetFileName(previous), current);
        Assert.Equal(current, rig.Defaults.Object("wallpaperFile"));
        Assert.Equal(64, store.Image!.Size.Width);
    });

    [Fact]
    [Trait("Req", "PR-THEME-062")] // UI/Theme/Wallpaper.swift:114-117 a non-picture: "That is not a picture." and no file work at all
    public void A_pick_that_is_not_a_picture_reports_its_sentence_and_changes_nothing_else() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        var store = rig.Loaded(out var published, out var file);
        var image = store.Image;
        var key = rig.Defaults.Object("wallpaperFile");
        var files = rig.LibraryFiles();

        store.Install(WallpaperPictures.Text(rig.Sources, "notes.txt"));

        Assert.Equal("That is not a picture.", store.LastError);
        Assert.False(store.IsInstalling);
        WallpaperStoreRig.Settle(150);
        Assert.Equal(files, rig.LibraryFiles());
        Assert.True(File.Exists(file));
        Assert.Equal(key, rig.Defaults.Object("wallpaperFile"));
        Assert.Empty(rig.Defaults.Writes);
        Assert.Same(image, store.Image);
        Assert.Empty(published);
    });

    [Fact]
    [Trait("Req", "PR-THEME-062")] // UI/Theme/Wallpaper.swift:129-136 the copy throws: destination deleted, failInstall()
    public void A_copy_that_fails_reports_could_not_use_that_picture_and_leaves_the_current_wallpaper() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        var store = rig.Loaded(out var published, out var file);
        var image = store.Image;
        var key = rig.Defaults.Object("wallpaperFile");
        var files = rig.LibraryFiles();

        // A name with a picture's extension is accepted without being read (:99-103), then the copy finds nothing.
        Pick(store, Path.Combine(rig.Sources, "gone.png"));

        Assert.Equal("Could not use that picture.", store.LastError);
        Assert.False(store.IsInstalling);
        Assert.Equal(files, rig.LibraryFiles());
        Assert.True(File.Exists(file));
        Assert.Equal(key, rig.Defaults.Object("wallpaperFile"));
        Assert.Empty(rig.Defaults.Writes);
        Assert.Same(image, store.Image);
        Assert.Empty(published);
    });

    [Fact]
    [Trait("Req", "PR-THEME-062")] // UI/Theme/Wallpaper.swift:137-141 decode returns nothing: the copy is deleted, failInstall()
    [Trait("Req", "PR-THEME-060")] // UI/Theme/Wallpaper.swift:221-222 the name is persisted once the decode succeeded, never before
    public void A_picture_that_cannot_be_decoded_leaves_no_copy_and_no_stored_name() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        var store = rig.NewStore(out var published);

        Pick(store, WallpaperPictures.Broken(rig.Sources, "broken.png"));

        Assert.Equal("Could not use that picture.", store.LastError);
        Assert.False(store.IsInstalling);
        Assert.Empty(rig.LibraryFiles()); // the half-made copy is gone
        Assert.Null(rig.Defaults.Object("wallpaperFile"));
        Assert.DoesNotContain("wallpaperFile", rig.Defaults.Writes);
        Assert.False(store.HasWallpaper);
        Assert.Empty(published);

        // With a wallpaper installed, the same failure leaves it, its file and its stored name alone.
        Pick(store, WallpaperPictures.Png(rig.Sources, "good.png", 64, 8));
        Assert.Null(store.LastError);
        var image = store.Image;
        var key = rig.Defaults.Object("wallpaperFile");
        var files = rig.LibraryFiles();
        Pick(store, WallpaperPictures.Broken(rig.Sources, "broken2.png"));
        Assert.Equal("Could not use that picture.", store.LastError);
        Assert.Equal(files, rig.LibraryFiles());
        Assert.Equal(key, rig.Defaults.Object("wallpaperFile"));
        Assert.Same(image, store.Image);
        Assert.True(store.HasWallpaper);
    });

    [Fact]
    [Trait("Req", "PR-THEME-062")] // UI/Theme/Wallpaper.swift:130-136 creating the library directory throws: the same failure
    public void A_library_folder_that_cannot_be_made_fails_the_install_the_same_way() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        var store = rig.NewStore();
        var library = rig.Library;
        Directory.Delete(library, recursive: true);
        File.WriteAllText(library, "a file where the folder should be");

        Pick(store, WallpaperPictures.Png(rig.Sources, "good.png", 64, 8));

        Assert.Equal("Could not use that picture.", store.LastError);
        Assert.False(store.IsInstalling);
        Assert.False(store.HasWallpaper);
        Assert.Null(rig.Defaults.Object("wallpaperFile"));
    });

    [Fact]
    [Trait("Req", "PR-THEME-063")] // UI/Theme/Wallpaper.swift:147-155 cancel, delete the file, remove the key, ease-out 0.25 to nil, clear the error
    public void Remove_cancels_the_render_deletes_the_file_clears_the_name_and_fades_out_over_a_quarter_second() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        var store = rig.Loaded(out var published, out var file, softness: 0.3);
        store.Install(WallpaperPictures.Text(rig.Sources, "notes.txt"));
        Assert.Equal("That is not a picture.", store.LastError);
        var render = WallpaperStoreRig.RenderTask(store);
        published.Clear();
        rig.Defaults.Writes.Clear();

        store.Remove();

        Assert.True(render!.IsCancelled);
        Assert.False(File.Exists(file));
        Assert.Null(rig.Defaults.Object("wallpaperFile")); // removed entirely, not blanked
        Assert.Equal(new[] { "wallpaperFile" }, rig.Defaults.Writes); // the softness key was not touched
        Assert.Equal(0.3, rig.Defaults.Object("wallpaperSoftness"));
        Assert.Equal(0.3, store.Softness);
        Assert.Null(store.Image);
        Assert.False(store.HasWallpaper);
        Assert.Null(store.LastError);
        var fade = Assert.Single(published);
        Assert.Null(fade.Image);
        Assert.Equal(Animation.EaseOut(0.25), fade.Animation);

        // Nothing comes back at the next start.
        Assert.False(rig.NewStore().HasWallpaper);
    });

    [Fact]
    [Trait("Req", "PR-THEME-063")] // failure_cases: the file cannot be deleted: ignored, the wallpaper is still cleared from the UI and the defaults
    public void Remove_clears_the_wallpaper_even_when_its_file_cannot_be_deleted() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        var store = rig.Loaded(out _, out var file);
        using var hold = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None); // a delete of this throws

        store.Remove();

        Assert.True(File.Exists(file));
        Assert.Null(rig.Defaults.Object("wallpaperFile"));
        Assert.Null(store.Image);
        Assert.False(store.HasWallpaper);
        Assert.Null(store.LastError);
    });
}
