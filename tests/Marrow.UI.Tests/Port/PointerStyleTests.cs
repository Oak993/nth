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

    // Coordinator review: PixelSize(scale) is already scale-multiplied, so arranging the glyph at that size in
    // DIPs and ALSO setting the RenderTargetBitmap's dpi to 96*scale double-applied the scale factor - at 2x the
    // render target only had room for the glyph's top-left quarter (alpha bbox (10,6,64,64) of a 64x64 square,
    // running off the right and bottom edges). This theory catches any recurrence: the alpha bounding box of every
    // (style, scale) render must sit fully inside its square with a real margin, at the same proportions the 100%
    // render has.
    [Theory]
    [Trait("Decision", "ED-205")]
    [InlineData(PointerStyle.GrabIdle, 1.0)]
    [InlineData(PointerStyle.GrabIdle, 1.25)]
    [InlineData(PointerStyle.GrabIdle, 1.5)]
    [InlineData(PointerStyle.GrabIdle, 2.0)]
    [InlineData(PointerStyle.GrabActive, 1.0)]
    [InlineData(PointerStyle.GrabActive, 1.25)]
    [InlineData(PointerStyle.GrabActive, 1.5)]
    [InlineData(PointerStyle.GrabActive, 2.0)]
    public void The_whole_glyph_stays_inside_the_square_with_a_margin_and_the_same_proportions_at_every_scale(PointerStyle style, double scale) =>
        headless.OnUiThread(() =>
        {
            var side = PointerStyleCursors.PixelSize(scale);
            var (left, top, right, bottom) = AlphaBoundingBox(PointerStyleCursors.Render(style, scale));
            var width = right - left + 1;
            var height = bottom - top + 1;

            // At least 1 px of transparent margin on every side.
            Assert.True(left >= 1, $"{style}@{scale}x: left margin was {left}px of a {side}px side (box {left},{top},{right},{bottom})");
            Assert.True(top >= 1, $"{style}@{scale}x: top margin was {top}px of a {side}px side (box {left},{top},{right},{bottom})");
            Assert.True(right <= side - 2, $"{style}@{scale}x: right edge at {right} of a {side}px side (box {left},{top},{right},{bottom})");
            Assert.True(bottom <= side - 2, $"{style}@{scale}x: bottom edge at {bottom} of a {side}px side (box {left},{top},{right},{bottom})");

            // The same composition at every scale, not a fraction of it: width/height relative to the square are
            // within 10% of the 100% render's own ratios.
            var baselineSide = PointerStyleCursors.PixelSize(1);
            var (baseLeft, baseTop, baseRight, baseBottom) = AlphaBoundingBox(PointerStyleCursors.Render(style, 1));
            var baselineWidthRatio = (double)(baseRight - baseLeft + 1) / baselineSide;
            var baselineHeightRatio = (double)(baseBottom - baseTop + 1) / baselineSide;
            var widthRatio = (double)width / side;
            var heightRatio = (double)height / side;
            Assert.InRange(widthRatio, baselineWidthRatio * 0.9, baselineWidthRatio * 1.1);
            Assert.InRange(heightRatio, baselineHeightRatio * 0.9, baselineHeightRatio * 1.1);
        });

    // Same pixel-readback idiom as LoadingMarkHeadlessTests.Alpha/Copy: CopyPixels a RenderTargetBitmap into a
    // WriteableBitmap, then Lock() and read the raw bytes. Alpha is byte offset 3 regardless of Rgba8888 vs
    // Bgra8888 channel order, so no format branch is needed here (unlike a test that reads R/G/B). Returns the
    // smallest rectangle (inclusive pixel indices) enclosing every non-transparent pixel.
    private static (int Left, int Top, int Right, int Bottom) AlphaBoundingBox(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        using var copy = new WriteableBitmap(size, bitmap.Dpi,
            bitmap.Format ?? Avalonia.Platform.PixelFormat.Rgba8888, Avalonia.Platform.AlphaFormat.Premul);
        using (var buffer = copy.Lock())
            ((RenderTargetBitmap)bitmap).CopyPixels(new PixelRect(size), buffer.Address, buffer.RowBytes * buffer.Size.Height, buffer.RowBytes);

        int left = size.Width, top = size.Height, right = -1, bottom = -1;
        using (var buffer = copy.Lock())
        {
            var row = new byte[buffer.RowBytes];
            for (var y = 0; y < size.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(buffer.Address + y * buffer.RowBytes, row, 0, buffer.RowBytes);
                for (var x = 0; x < size.Width; x++)
                {
                    if (row[x * 4 + 3] == 0) continue;
                    if (x < left) left = x;
                    if (x > right) right = x;
                    if (y < top) top = y;
                    if (y > bottom) bottom = y;
                }
            }
        }
        Assert.True(right >= 0, "the glyph rendered nothing");
        return (left, top, right, bottom);
    }
}
