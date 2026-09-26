using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// PA-0019, ED-205: SwiftUI's `.pointerStyle(_:)` (App/Views/Composer/FollowUpQueueTab.swift:384,
// App/Views/Settings/ModelsSettingsPage.swift:184, App/Views/Sidebar/SidebarView.swift:124,
// App/Views/Chat/TimelineMinimap.swift:211 - the four hits of a repo-wide grep for "pointerStyle" in DroppyCode at
// B3 52bc792) over Avalonia's Cursor. Avalonia's StandardCursorType has no open-hand/closed-hand member, so
// PA-0019 ships two rendered cursors (the Lucide icons hand.svg and hand-fist.svg, already at
// Assets/Icons/lucide/, ISC licence) instead; .link needs none, since StandardCursorType.Hand already exists.
// PORT-ONLY surface these tests fix, namespace Marrow.UI.Port, src/Marrow.UI/Port/PointerStyle.cs:
//   enum PointerStyle { GrabIdle, GrabActive, Link }
//   static class PointerStyleControl { T PointerStyle<T>(this T control, PointerStyle? style) where T : Control; }
//   static class PointerStyleCursors {
//       const double BaseSize;   int PixelSize(double scale);   PixelPoint Hotspot(double scale);
//       Cursor Get(PointerStyle style, double scale);   Bitmap Render(PointerStyle style, double scale); }
[Collection("Headless")]
public sealed class PointerStyleTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-205")]
    public void The_style_set_matches_every_pointerStyle_value_the_pinned_source_uses() =>
        Assert.Equal([PointerStyle.GrabIdle, PointerStyle.GrabActive, PointerStyle.Link], Enum.GetValues<PointerStyle>());

    [Fact]
    [Trait("Decision", "ED-205")]
    public void Grab_idle_and_grab_active_are_distinct_rendered_cursors_and_differ_from_link() => headless.OnUiThread(() =>
    {
        var control = new Border();
        control.PointerStyle(PointerStyle.GrabIdle);
        var idle = control.Cursor;
        Assert.NotNull(idle);

        control.PointerStyle(PointerStyle.GrabActive);
        var active = control.Cursor;
        Assert.NotNull(active);
        Assert.NotSame(idle, active);

        control.PointerStyle(PointerStyle.Link);
        var link = control.Cursor;
        Assert.NotNull(link);
        Assert.NotSame(idle, link);
        Assert.NotSame(active, link);
    });

    [Fact]
    [Trait("Decision", "ED-205")]
    public void The_two_grab_glyphs_render_different_pixels() => headless.OnUiThread(() =>
    {
        var idle = PointerStyleCursors.Render(PointerStyle.GrabIdle, 1);
        var active = PointerStyleCursors.Render(PointerStyle.GrabActive, 1);
        Assert.Equal(idle.PixelSize, active.PixelSize);
        using var idleBytes = new MemoryStream();
        using var activeBytes = new MemoryStream();
        idle.Save(idleBytes, new PngBitmapEncoderOptions());
        active.Save(activeBytes, new PngBitmapEncoderOptions());
        Assert.NotEqual(idleBytes.ToArray(), activeBytes.ToArray());
    });

    [Fact]
    [Trait("Decision", "ED-205")]
    public void Applying_null_clears_the_override_back_to_the_controls_normal_cursor() => headless.OnUiThread(() =>
    {
        var control = new Border();
        control.PointerStyle(PointerStyle.GrabActive);
        Assert.NotNull(control.Cursor);
        control.PointerStyle(null);
        Assert.Null(control.Cursor);
    });

    [Fact]
    [Trait("Decision", "ED-205")]
    public void Link_maps_to_the_standard_Hand_cursor_and_needs_no_rendered_art() => headless.OnUiThread(() =>
    {
        var first = PointerStyleCursors.Get(PointerStyle.Link, 1);
        var second = PointerStyleCursors.Get(PointerStyle.Link, 2); // scale-independent: the same StandardCursorType.Hand
        Assert.Same(first, second);
        Assert.Throws<ArgumentOutOfRangeException>(() => PointerStyleCursors.Render(PointerStyle.Link, 1));
    });

    [Theory]
    [Trait("Decision", "ED-205")]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void The_hotspot_is_the_centre_of_the_rendered_square(double scale)
    {
        var side = PointerStyleCursors.PixelSize(scale);
        var hotspot = PointerStyleCursors.Hotspot(scale);
        Assert.Equal(side / 2, hotspot.X);
        Assert.Equal(side / 2, hotspot.Y);
    }

    [Fact]
    [Trait("Decision", "ED-205")]
    public void The_base_cursor_size_is_32_pixels_at_100_percent() =>
        Assert.Equal(32, PointerStyleCursors.PixelSize(1));

    [Fact]
    [Trait("Decision", "ED-205")]
    public void The_cache_returns_the_same_cursor_instance_for_the_same_style_and_scale() => headless.OnUiThread(() =>
    {
        var a = PointerStyleCursors.Get(PointerStyle.GrabIdle, 1);
        var b = PointerStyleCursors.Get(PointerStyle.GrabIdle, 1);
        Assert.Same(a, b);

        var atOtherScale = PointerStyleCursors.Get(PointerStyle.GrabIdle, 2);
        Assert.NotSame(a, atOtherScale);

        var otherStyle = PointerStyleCursors.Get(PointerStyle.GrabActive, 1);
        Assert.NotSame(a, otherStyle);
    });

    [Fact]
    [Trait("Decision", "ED-205")]
    public void The_cache_returns_the_same_bitmap_instance_for_the_same_style_and_scale() => headless.OnUiThread(() =>
    {
        var a = PointerStyleCursors.Render(PointerStyle.GrabActive, 1.5);
        var b = PointerStyleCursors.Render(PointerStyle.GrabActive, 1.5);
        Assert.Same(a, b);
    });
}
