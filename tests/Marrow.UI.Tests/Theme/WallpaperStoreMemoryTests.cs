using System.Reflection;
using Marrow.Core.Port;
using Marrow.UI.Theme;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Theme;

// ED-NEW-perf-memory-2: the store used to keep the full-size decoded copy (about 23 MB for a 3200 px picture) for the whole run although it only read
// that copy's width, decoded the preview again on every re-render of the same file, and kept the previous picture's copies after a new one was installed
// or the picture removed. What the user sees (the full-size render, the live preview, their sizes and pixels) is unchanged.
[Collection("Headless")]
public sealed class WallpaperStoreMemoryTests(HeadlessSession headless)
{
    private static T Field<T>(WallpaperStore store, string name) =>
        (T)typeof(WallpaperStore).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(store)!;

    [Fact]
    [Trait("Req", "PR-THEME-061")] // UI/Theme/Wallpaper.swift:81-83, :173-174 the render keeps the preview and the width the live blur scales by
    public void After_a_render_the_store_keeps_the_preview_and_the_full_copys_width_but_not_the_full_copy() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        var store = rig.Loaded(out _, out var file, width: 3300, height: 8);

        Assert.Null(typeof(WallpaperStore).GetField("baseImage", BindingFlags.NonPublic | BindingFlags.Instance)); // the 23 MB copy has no field to live in
        Assert.Equal(3200, Field<int>(store, "baseWidth"));
        var preview = Field<DecodedBitmap>(store, "previewBase");
        Assert.Equal(1600, preview.Width);
        Assert.True(WallpaperStoreRig.SamePixels(preview, WallpaperStore.Thumbnail(file, 1600)!));
    });

    [Fact]
    [Trait("Req", "PR-THEME-061")] // UI/Theme/Wallpaper.swift:192-210 the live blur scales its radius by preview width / full width
    public void A_live_drag_still_scales_the_radius_by_the_previews_share_of_the_full_width() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        var store = rig.Loaded(out var published, out var file, width: 2000, height: 40);

        store.IsAdjustingSoftness = true;
        store.Softness = 0.5;

        WallpaperStoreRig.WaitFor(() => published.Count >= 1, "the live frames");
        WallpaperStoreRig.Settle();
        var frame = WallpaperStoreRig.Read(published[^1].Image);
        var expected = WallpaperStore.Blurred(WallpaperStore.Thumbnail(file, 1600)!, 0.5, 1600.0 / 2000.0)!; // 0.8, not a fixed 0.5
        Assert.True(WallpaperStoreRig.SamePixels(frame, expected));
    });

    [Fact]
    [Trait("Req", "PR-THEME-061")] // UI/Theme/Wallpaper.swift:160-178 a re-render of the same picture
    public void A_second_render_of_the_same_picture_reuses_the_preview_it_already_holds() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        var store = rig.Loaded(out var published, out _, width: 3300, height: 8);
        var before = Field<DecodedBitmap>(store, "previewBase");

        store.Softness = 0.05;

        WallpaperStoreRig.WaitFor(() => published.Count >= 1, "the re-render");
        WallpaperStoreRig.Pump();
        Assert.Same(before, Field<DecodedBitmap>(store, "previewBase"));
        Assert.Equal(3200, Field<int>(store, "baseWidth"));
    });

    [Fact]
    [Trait("Req", "PR-THEME-060")] // UI/Theme/Wallpaper.swift:147-156 removing the picture
    public void Removing_the_picture_lets_go_of_its_preview() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        var store = rig.Loaded(out _, out _, width: 3300, height: 8);
        Assert.NotNull(Field<DecodedBitmap?>(store, "previewBase"));

        store.Remove();

        Assert.Null(Field<DecodedBitmap?>(store, "previewBase"));
        Assert.Equal(0, Field<int>(store, "baseWidth"));
    });

    [Fact]
    [Trait("Req", "PR-THEME-060")] // UI/Theme/Wallpaper.swift:113-144 installing a new picture over the old one
    public void A_new_picture_does_not_inherit_the_old_ones_preview() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        var store = rig.Loaded(out var published, out _, width: 2000, height: 40);
        var other = WallpaperPictures.Png(rig.Sources, "other.png", 2400, 30, x => x % 2 == 0 ? ((byte)200, (byte)30, (byte)30) : ((byte)30, (byte)30, (byte)200));
        store.Install(other);
        WallpaperStoreRig.WaitFor(() => !store.IsInstalling, "the install");
        WallpaperStoreRig.Pump();
        Assert.Null(Field<DecodedBitmap?>(store, "previewBase")); // the old picture's copies went with the old file
        published.Clear();
        var installed = Directory.GetFiles(rig.Library).Single();

        store.IsAdjustingSoftness = true; // the press: nothing of the old picture to draw, so the full render runs for the new one

        WallpaperStoreRig.WaitFor(() => published.Count >= 1, "the press frame");
        Assert.Equal(2400, WallpaperStoreRig.Read(published[0].Image).Width); // softness 0: the full-size render of the new picture
        WallpaperStoreRig.WaitFor(() => Field<DecodedBitmap?>(store, "previewBase") is not null, "the new picture's copies");
        Assert.True(WallpaperStoreRig.SamePixels(Field<DecodedBitmap>(store, "previewBase"), WallpaperStore.Thumbnail(installed, 1600)!));
    });
}
