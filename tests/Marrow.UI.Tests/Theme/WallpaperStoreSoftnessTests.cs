using Marrow.UI.Theme;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Theme;

// Source: DroppyCode/UI/Theme/Wallpaper.swift (B3 52bc7926), :38-71 softness and the drag flag, :180-217 the re-render
// branch and the live render loop. The test picture is 2000 by 40: its full copy is 2000 wide (under the 3200 limit) and
// its preview 1600 wide, so the radius scale a live render must use is 1600 / 2000 = 0.8 and not a fixed 0.5.
[Collection("Headless")]
public sealed class WallpaperStoreSoftnessTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-THEME-057")] // UI/Theme/Wallpaper.swift:47-52 `clamped == softness` else write back and return: one persist per assignment
    [Trait("Req", "PR-SETTINGS-103")] // verification: values -1, 0.5 and 2 each assert exactly one write
    public void Assigning_softness_clamps_into_0_to_1_and_persists_exactly_once_each_time() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        var store = rig.NewStore();
        foreach (var (assigned, expected) in new[] { (-1.0, 0.0), (0.5, 0.5), (2.0, 1.0), (1.5, 1.0), (0.0, 0.0) })
        {
            rig.Defaults.Writes.Clear();
            store.Softness = assigned;
            Assert.Equal(expected, store.Softness);
            Assert.Equal(new[] { "wallpaperSoftness" }, rig.Defaults.Writes); // exactly one write, never a recursion's worth
            Assert.Equal(expected, rig.Defaults.Object("wallpaperSoftness"));
        }
    });

    [Fact]
    [Trait("Req", "PR-THEME-057")] // UI/Theme/Wallpaper.swift:52-53 persist, then scheduleRerender(): an in-range value re-renders
    [Trait("Req", "PR-THEME-059")] // UI/Theme/Wallpaper.swift:180-187 with a file and no drag: a full render
    [Trait("Req", "PR-THEME-061")] // UI/Theme/Wallpaper.swift:164-176 the full render blurs at the softness and swaps in unanimated
    public void With_a_wallpaper_a_softness_change_re_renders_at_full_size_unanimated() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        var store = rig.Loaded(out var published, out var file);
        var before = WallpaperStoreRig.RenderTask(store);
        Assert.NotNull(before);

        store.Softness = 0.1;

        var after = WallpaperStoreRig.RenderTask(store);
        Assert.NotSame(before, after); // a new full render
        Assert.True(before!.IsCancelled); // "A newer render cancels this one"
        WallpaperStoreRig.WaitFor(() => published.Count >= 1, "the re-render");
        WallpaperStoreRig.Settle(150);
        var single = Assert.Single(published);
        Assert.Null(single.Animation); // ":175 Deliberately no animation here"
        var pixels = WallpaperStoreRig.Read(single.Image);
        Assert.Equal(2000, pixels.Width);
        var crisp = WallpaperStore.Thumbnail(file, 3200)!;
        Assert.False(WallpaperStoreRig.SamePixels(pixels, crisp), "softness 0.1 blurs the picture");
        Assert.True(WallpaperStoreRig.SamePixels(pixels, WallpaperStore.Blurred(crisp, 0.1)!));
    });

    [Fact]
    [Trait("Req", "PR-THEME-057")] // UI/Theme/Wallpaper.swift:47-51 an out-of-range value re-renders once, at the clamped value
    public void An_out_of_range_softness_re_renders_at_the_clamped_value() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        var store = rig.Loaded(out var published, out var file, width: 400);

        store.Softness = 2;

        Assert.Equal(1, store.Softness);
        WallpaperStoreRig.WaitFor(() => published.Count >= 1, "the re-render");
        WallpaperStoreRig.Settle(150);
        var single = Assert.Single(published);
        var crisp = WallpaperStore.Thumbnail(file, 3200)!;
        Assert.True(WallpaperStoreRig.SamePixels(WallpaperStoreRig.Read(single.Image), WallpaperStore.Blurred(crisp, 1.0)!));
    });

    [Fact]
    [Trait("Req", "PR-THEME-059")] // UI/Theme/Wallpaper.swift:181 `guard fileURL != nil`: with no installed file nothing happens
    public void With_no_wallpaper_a_softness_change_starts_no_render_of_either_kind() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        var store = rig.NewStore(out var published);

        store.Softness = 0.5;
        store.IsAdjustingSoftness = true;
        store.Softness = 0.6;
        store.IsAdjustingSoftness = false;
        store.Softness = 0.7;

        Assert.Null(WallpaperStoreRig.RenderTask(store));
        Assert.Null(WallpaperStoreRig.LiveTask(store));
        WallpaperStoreRig.Settle(100);
        Assert.Empty(published);
        Assert.Equal(0.7, rig.Defaults.Object("wallpaperSoftness")); // the softness itself is still saved
    });

    [Fact]
    [Trait("Req", "PR-THEME-059")] // UI/Theme/Wallpaper.swift:181 `guard fileURL != nil`, with both cached copies still held
    public void After_the_wallpaper_is_removed_a_softness_change_during_a_drag_brings_nothing_back() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        var store = rig.Loaded(out var published, out _);
        store.IsAdjustingSoftness = true;
        WallpaperStoreRig.WaitFor(() => published.Count >= 1 && WallpaperStoreRig.LiveTask(store) is null, "the press frame");
        store.Remove();
        published.Clear();

        store.Softness = 0.5;

        Assert.Null(WallpaperStoreRig.LiveTask(store)); // the cached copies would otherwise start a live render
        WallpaperStoreRig.Settle(200);
        Assert.Empty(published);
        Assert.Null(store.Image);
    });

    [Fact]
    [Trait("Req", "PR-THEME-058")] // UI/Theme/Wallpaper.swift:63-69 press: renderLive(); release: liveRequested = nil, render()
    public void A_softness_drag_renders_the_half_size_copy_live_and_ends_with_one_full_size_render() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        var store = rig.Loaded(out var published, out var file);
        var full = WallpaperStoreRig.RenderTask(store);

        // Press: the preview path. No full-size render starts, and the first frame is the 1600-wide copy.
        store.IsAdjustingSoftness = true;
        Assert.Same(full, WallpaperStoreRig.RenderTask(store));
        Assert.NotNull(WallpaperStoreRig.LiveTask(store));
        WallpaperStoreRig.WaitFor(() => published.Count >= 1, "the press frame");
        Assert.Equal(1600, WallpaperStoreRig.Read(published[0].Image).Width);

        // During the drag every change renders from the preview copy, never at full size.
        published.Clear();
        store.Softness = 0.1;
        WallpaperStoreRig.WaitFor(() => published.Count >= 1, "a drag frame");
        var preview = WallpaperStore.Thumbnail(file, 1600)!;
        var frame = WallpaperStoreRig.Read(published[^1].Image);
        Assert.Equal(1600, frame.Width);
        Assert.True(WallpaperStoreRig.SamePixels(frame, WallpaperStore.Blurred(preview, 0.1, 0.8)!));
        Assert.Same(full, WallpaperStoreRig.RenderTask(store));

        // Release: exactly one full-size render at the final softness.
        published.Clear();
        store.IsAdjustingSoftness = false;
        Assert.NotSame(full, WallpaperStoreRig.RenderTask(store));
        WallpaperStoreRig.WaitFor(() => published.Count >= 1, "the release render");
        WallpaperStoreRig.Settle(200);
        var single = Assert.Single(published);
        var released = WallpaperStoreRig.Read(single.Image);
        Assert.Equal(2000, released.Width);
        Assert.True(WallpaperStoreRig.SamePixels(released, WallpaperStore.Blurred(WallpaperStore.Thumbnail(file, 3200)!, 0.1)!));
    });

    [Fact]
    [Trait("Req", "PR-THEME-058")] // UI/Theme/Wallpaper.swift:63 `guard isAdjustingSoftness != oldValue`: a repeated value does nothing
    public void Assigning_the_same_drag_flag_again_starts_no_render() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        var store = rig.Loaded(out var published, out _);
        var render = WallpaperStoreRig.RenderTask(store);

        store.IsAdjustingSoftness = false; // it already is

        Assert.Same(render, WallpaperStoreRig.RenderTask(store));
        Assert.Null(WallpaperStoreRig.LiveTask(store));
        WallpaperStoreRig.Settle(150);
        Assert.Empty(published);
    });

    [Fact]
    [Trait("Req", "PR-THEME-058")] // UI/Theme/Wallpaper.swift:63 `fileURL != nil`: with no wallpaper installed the flag changes nothing
    public void With_no_wallpaper_installed_the_drag_flag_starts_no_render() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        var store = rig.Loaded(out var published, out _);
        store.Remove(); // the cached copies stay, the file does not
        var render = WallpaperStoreRig.RenderTask(store);
        published.Clear();

        store.IsAdjustingSoftness = true;
        Assert.Null(WallpaperStoreRig.LiveTask(store));
        store.IsAdjustingSoftness = false;
        Assert.Same(render, WallpaperStoreRig.RenderTask(store));

        WallpaperStoreRig.Settle(200);
        Assert.Empty(published);
    });

    [Fact]
    [Trait("Req", "PR-THEME-064")] // UI/Theme/Wallpaper.swift:197-198, :201, :213-217 one live render at a time, the newest value wins
    public void A_burst_of_softness_values_in_a_drag_renders_once_and_that_render_is_the_newest_value() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        var store = rig.Loaded(out var published, out var file);
        store.IsAdjustingSoftness = true;
        WallpaperStoreRig.WaitFor(() => published.Count >= 1 && WallpaperStoreRig.LiveTask(store) is null, "the press frame");
        published.Clear();

        var values = Enumerable.Range(1, 10).Select(i => i * 0.02).ToArray(); // 0.02 ... 0.2
        foreach (var value in values) store.Softness = value; // no frame can run between these: the loop needs the main actor

        WallpaperStoreRig.WaitFor(() => published.Count >= 1, "the live frame");
        WallpaperStoreRig.Settle(250);
        var single = Assert.Single(published); // ten values, one render
        var frame = WallpaperStoreRig.Read(single.Image);
        var preview = WallpaperStore.Thumbnail(file, 1600)!;
        var newest = WallpaperStore.Blurred(preview, values[^1], 0.8)!;
        Assert.True(WallpaperStoreRig.SamePixels(frame, newest), "the frame is the newest value at the radius scale 1600 / 2000");
        Assert.False(WallpaperStoreRig.SamePixels(frame, WallpaperStore.Blurred(preview, values[0], 0.8)!));
        // The radius scale comes from the real widths: neither 0.5 nor 1 gives this picture.
        Assert.False(WallpaperStoreRig.SamePixels(frame, WallpaperStore.Blurred(preview, values[^1], 0.5)!));
        Assert.False(WallpaperStoreRig.SamePixels(frame, WallpaperStore.Blurred(preview, values[^1], 1)!));
        Assert.Equal(values[^1], store.Softness);
    });

    [Fact]
    [Trait("Req", "PR-THEME-064")] // UI/Theme/Wallpaper.swift:193-196 without both cached copies the store falls back to a full render
    public void A_drag_started_before_any_render_has_cached_the_copies_falls_back_to_a_full_render() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        var picture = WallpaperPictures.Png(rig.Sources, "fresh.png", 2000, 40);
        var store = rig.NewStore(out var published);
        store.Install(picture); // an install caches nothing (finishInstall, :219-227)
        WallpaperStoreRig.WaitFor(() => !store.IsInstalling, "the install");
        Assert.Null(store.LastError);
        published.Clear();

        store.IsAdjustingSoftness = true;

        Assert.NotNull(WallpaperStoreRig.RenderTask(store)); // a full render, not a live one
        Assert.Null(WallpaperStoreRig.LiveTask(store));
        WallpaperStoreRig.WaitFor(() => published.Count >= 1, "the fallback render");
        Assert.Equal(2000, WallpaperStoreRig.Read(published[0].Image).Width);

        // It cached the copies, so the next change in the same drag is live.
        WallpaperStoreRig.Settle(150);
        published.Clear();
        store.Softness = 0.1;
        WallpaperStoreRig.WaitFor(() => published.Count >= 1, "a live frame");
        Assert.Equal(1600, WallpaperStoreRig.Read(published[^1].Image).Width);
    });

    [Fact]
    [Trait("Req", "PR-THEME-064")] // UI/Theme/Wallpaper.swift:205 `self.isAdjustingSoftness else { break }`: a frame finished after the drag ended is not published
    [Trait("Req", "PR-THEME-058")] // UI/Theme/Wallpaper.swift:67-68 release clears the pending request and runs the full render
    public void A_live_frame_that_finishes_after_the_drag_ended_is_never_published() => headless.OnUiThread(() =>
    {
        using var rig = new WallpaperStoreRig();
        // Tall enough that a strong blur of the preview takes a good fraction of a second off the main thread.
        var store = rig.Loaded(out var published, out _, width: 2000, height: 400);
        store.IsAdjustingSoftness = true;
        WallpaperStoreRig.WaitFor(() => published.Count >= 1 && WallpaperStoreRig.LiveTask(store) is null, "the press frame");
        published.Clear();

        store.Softness = 0.9; // the loop takes this value on the next main-actor turn, then blurs off main
        Thread.Sleep(150);
        WallpaperStoreRig.Pump(); // the loop has taken the request; the blur is running
        store.IsAdjustingSoftness = false; // released before that frame finished

        WallpaperStoreRig.WaitFor(() => published.Any(p => WallpaperStoreRig.Read(p.Image).Width == 2000), "the full-size render");
        WallpaperStoreRig.Settle(500);
        Assert.All(published, p => Assert.Equal(2000, WallpaperStoreRig.Read(p.Image).Width)); // no preview-size frame came after the release
        Assert.Single(published);
    });
}
