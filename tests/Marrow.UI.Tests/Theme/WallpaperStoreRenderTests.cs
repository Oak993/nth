using Marrow.UI.Theme;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Theme;

// Source: DroppyCode/UI/Theme/Wallpaper.swift (B3 52bc7926), :160-178 the full render and the checks that decide whether
// its result may land.
[Collection("Headless")]
public sealed class WallpaperStoreRenderTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-THEME-061")] // UI/Theme/Wallpaper.swift:166, :170 the base is at most 3200 px and blurred only when softness > 0
    public void A_full_render_is_the_3200_pixel_copy_and_is_blurred_only_above_zero_softness() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        var crisp = rig.Loaded(out _, out var file, width: 3300, height: 8);
        var plain = WallpaperStoreRig.Read(crisp.Image);
        Assert.Equal(3200, plain.Width); // 3300 reduced to the 3200 limit
        Assert.Equal(8, plain.Height);
        Assert.True(WallpaperStoreRig.SamePixels(plain, WallpaperStore.Thumbnail(file, 3200)!)); // softness 0: the base itself

        using var softRig = new WallpaperStoreRig();
        var soft = softRig.Loaded(out _, out var softFile, width: 3300, height: 8, softness: 0.05);
        var blurred = WallpaperStoreRig.Read(soft.Image);
        var thumbnail = WallpaperStore.Thumbnail(softFile, 3200)!;
        Assert.False(WallpaperStoreRig.SamePixels(blurred, thumbnail));
        Assert.True(WallpaperStoreRig.SamePixels(blurred, WallpaperStore.Blurred(thumbnail, 0.05)!));
    });

    [Fact]
    [Trait("Req", "PR-THEME-061")] // UI/Theme/Wallpaper.swift:166 the second copy of the same render is at most 1600 px (what a drag then blurs)
    public void The_render_also_keeps_a_1600_pixel_preview_copy_for_the_drag() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        var store = rig.Loaded(out var published, out var file, width: 3300, height: 8);

        store.IsAdjustingSoftness = true;

        WallpaperStoreRig.WaitFor(() => published.Count >= 1, "the press frame");
        var frame = WallpaperStoreRig.Read(published[0].Image);
        Assert.Equal(1600, frame.Width);
        Assert.True(WallpaperStoreRig.SamePixels(frame, WallpaperStore.Thumbnail(file, 1600)!)); // softness 0: the preview as it is
    });

    [Fact]
    [Trait("Req", "PR-THEME-061")] // UI/Theme/Wallpaper.swift:172 `!Task.isCancelled`: a cancelled render leaves the picture alone; failure_cases
    public void A_render_that_an_install_cancelled_publishes_nothing() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        var picture = WallpaperPictures.Png(rig.Sources, "kept.png", 400, 20);
        rig.PlaceInLibrary("wallpaper-KEPT.png", picture);
        var store = rig.NewStore(out var published); // its start-up render is on the pool, not yet back on main
        var render = WallpaperStoreRig.RenderTask(store);

        store.Install(WallpaperPictures.Broken(rig.Sources, "broken.png")); // cancels it (:120) and then fails
        WallpaperStoreRig.WaitFor(() => !store.IsInstalling && render!.Completion.IsCompleted, "the install and the render to end");
        WallpaperStoreRig.Settle(150);

        Assert.True(render!.IsCancelled);
        Assert.Null(store.Image);
        Assert.Empty(published);
        Assert.True(store.HasWallpaper); // the file is still the installed one
    });

    [Fact]
    [Trait("Req", "PR-THEME-061")] // UI/Theme/Wallpaper.swift:172 `self.fileURL == fileURL`: a render of a file that is no longer the installed one is dropped
    public void A_render_of_the_old_file_that_finishes_after_a_new_picture_was_installed_is_dropped() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        // The old picture is big, so a render of it is still busy off main while the small new one installs.
        var store = rig.Loaded(out var published, out _, width: 3300, height: 2000);
        var incoming = WallpaperPictures.Png(rig.Sources, "incoming.png", 8, 8, _ => ((byte)200, (byte)30, (byte)30));

        store.Install(incoming); // cancels the finished render and starts copying and decoding off main
        store.Softness = 0; // a re-render of the OLD file that nothing cancels (:180-187); it ends up on main after the swap
        Thread.Sleep(1500); // both background jobs are done and waiting for the main actor; nothing has been pumped
        WallpaperStoreRig.WaitFor(() => !store.IsInstalling, "the install");
        WallpaperStoreRig.Settle(300);

        Assert.Null(store.LastError);
        var current = WallpaperStoreRig.Read(store.Image);
        Assert.Equal(8, current.Width); // the new picture, not the old one's 3200-wide render
        Assert.Equal(8, WallpaperStoreRig.Read(published[^1].Image).Width);
        Assert.Equal(Path.GetFileName(Assert.Single(Directory.GetFiles(rig.Library))), rig.Defaults.Object("wallpaperFile"));
    });

    [Fact]
    [Trait("Req", "PR-THEME-061")] // failure_cases: the file cannot be decoded: nothing changes, no error is shown
    public void A_render_of_a_file_that_cannot_be_decoded_changes_nothing_and_shows_no_error() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        var broken = WallpaperPictures.Broken(rig.Sources, "broken.png");
        rig.PlaceInLibrary("wallpaper-BROKEN.png", broken);
        var store = rig.NewStore(out var published);
        var render = WallpaperStoreRig.RenderTask(store);

        WallpaperStoreRig.WaitFor(() => render!.Completion.IsCompleted, "the render to end");

        Assert.Null(store.Image);
        Assert.Null(store.LastError);
        Assert.False(store.IsInstalling);
        Assert.True(store.HasWallpaper);
        Assert.Empty(published);
    });
}
