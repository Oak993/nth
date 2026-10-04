// Source: DroppyCode/UI/Chrome/Chrome.swift (B3 52bc792), :171-182 (the glass frosts the app content under it). Review of ED-320 11-12.
// REVIEW: the material keeps the pixels it took (GlassMaterial.Backdrop) and takes new ones only inside the frame's dirty rect, read from Avalonia's
// SingleDirtyRectTracker in the surface's pixels. These pin what that claim has to hold through: a render scale that is not 1 (125 and 150 percent,
// and a scale change after the first frame, as a window moved to another monitor), a piece that moves (layout and a render transform), a window
// resize, and a piece inside a clipping ancestor whose sampled margin reaches past the clip: Avalonia adds a moved or appearing visual's bounds to
// the dirty region only inside its ancestors' clip, and does not draw a material whose bounds miss that clip, so pixels it kept outside the clip
// are not "unchanged" when content there changed while it was scrolled out.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;
using Pattern = Marrow.UI.Tests.Fixtures.GlassBackdropPattern;
using Scene = Marrow.UI.Tests.Fixtures.GlassScene;

namespace Marrow.UI.Tests.Port;

[Collection("Headless")]
public sealed class GlassMaterialKeptBackdropReviewTests(HeadlessSession headless)
{
    private static int Largest(Func<int, int, Color> a, Func<int, int, Color> b, PixelRect area, out PixelPoint at)
    {
        var most = 0;
        at = default;
        for (var y = area.Y; y < area.Bottom; y++)
            for (var x = area.X; x < area.Right; x++)
            {
                Color p = a(x, y), q = b(x, y);
                var d = Math.Max(Math.Abs(p.R - q.R), Math.Max(Math.Abs(p.G - q.G), Math.Abs(p.B - q.B)));
                if (d > most) { most = d; at = new PixelPoint(x, y); }
            }
        return most;
    }

    private static Func<int, int, Color> Whole(Scene scene)
    {
        foreach (var visual in scene.Window.GetVisualDescendants()) visual.InvalidateVisual();
        return scene.Pixels();
    }

    // The headless window's own scale (Avalonia.Headless.IHeadlessWindow.SetRenderScaling, internal), as PopoverWindowHitTestReviewTests sets it.
    private static void Scale(Window window, double scale)
    {
        var type = typeof(Avalonia.Headless.AvaloniaHeadlessPlatformOptions).Assembly.GetType("Avalonia.Headless.IHeadlessWindow", throwOnError: true)!;
        type.GetMethod("SetRenderScaling")!.Invoke(window.PlatformImpl, [scale]);
    }

    // The area of a control in the captured frame's pixels.
    private static PixelRect Area(Control control, Window window, double scale)
    {
        var origin = control.TranslatePoint(default, window)!.Value;
        return new PixelRect((int)Math.Ceiling(origin.X * scale), (int)Math.Ceiling(origin.Y * scale),
            (int)Math.Floor(control.Bounds.Width * scale) - 1, (int)Math.Floor(control.Bounds.Height * scale) - 1);
    }

    private static (Border Piece, Border Wash) PieceWithWash()
    {
        var wash = new Border { Width = 40, Height = 16, Background = new SolidColorBrush(Color.FromArgb(77, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var piece = new Border { Background = Brushes.Transparent, Width = 160, Height = 120, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Child = wash };
        SwiftGlass.SetEffect(piece, GlassEffect.Regular(GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous)));
        return (piece, wash);
    }

    private static void RedrawWash(Scene scene, Border wash)
    {
        for (var i = 0; i < 4; i++)
        {
            wash.InvalidateVisual();
            _ = scene.Pixels();
        }
    }

    // At 125 and 150 percent (set after the first frame, as a window moved to another monitor is), and from 150 back to 125, a redraw of a control
    // over the glass that changes nothing leaves the glass as a whole drawing gives it: the dirty rect and the kept pixels are in the same pixels.
    [Theory]
    [InlineData(1.25, 0)]
    [InlineData(1.5, 0)]
    [InlineData(1.25, 1.5)]
    [Trait("Req", "PR-WIN-180")]
    public void REVIEW_At_a_render_scale_that_is_not_one_a_redraw_over_the_glass_leaves_it_as_a_whole_drawing(double scale, double first) => headless.OnUiThread(() =>
    {
        var (piece, wash) = PieceWithWash();
        using var scene = new Scene(new Pattern { Flat = false }, piece);
        _ = scene.Pixels();
        if (first > 0) { Scale(scene.Window, first); _ = scene.Pixels(); RedrawWash(scene, wash); }
        Scale(scene.Window, scale);
        var before = scene.Pixels();
        Assert.Equal(scale, scene.Window.RenderScaling);
        RedrawWash(scene, wash);
        var partial = scene.Pixels();
        var area = Area(piece, scene.Window, scale);
        var drift = Largest(before, partial, area, out var driftAt);
        var whole = Whole(scene);
        var apart = Largest(partial, whole, area, out var apartAt);
        Assert.True(drift <= 2 && apart <= 2,
            $"at {scale}: four redraws that changed nothing moved the glass {drift} levels (at {driftAt}); it is {apart} levels from a whole drawing (at {apartAt})");
    });

    // A block under the middle of the glass turns from black to white and only it is redrawn. The frost spreads it (the blur, 3 sigma up to 36 pt,
    // and the refraction): in a whole drawing the glass AROUND the block lightens too. Avalonia redraws only the block's dirty rect and clips every
    // drawing to it, so the glass just outside the block keeps the previous frame's frost: a seam along the block's edges (measured 54 to 82 levels
    // at 1, 1.25, 1.5 and 2; 0 inside the block, which is all GlassMaterialPartialRedrawTests measures). Pre-existing (the clip is Avalonia's), not
    // from the kept backdrop; nothing invalidates the glass within its sampling reach of a change under it.
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [Trait("Req", "PR-WIN-180")]
    public void REVIEW_A_change_under_the_glass_changes_the_frost_around_it_as_a_whole_drawing_does(double scale) => headless.OnUiThread(() =>
    {
        var block = new Border { Width = 40, Height = 30, Background = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center };
        var (piece, _) = PieceWithWash();
        using var scene = new Scene(new Pattern { Flat = true }, new Panel { Children = { block, piece } });
        _ = scene.Pixels();
        if (scale != 1.0) { Scale(scene.Window, scale); _ = scene.Pixels(); }
        block.Background = Brushes.White;
        var partial = scene.Pixels();
        var whole = Whole(scene);
        var apart = Largest(partial, whole, Area(piece, scene.Window, scale), out var apartAt);
        Assert.True(apart <= 2, $"at {scale} the glass around the block is {apart} levels from a whole drawing (at {apartAt})");
    });

    // The piece moves (by layout, then by a render transform), then a control over it is redrawn: the glass at its new place is a whole drawing's.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-WIN-180")]
    public void REVIEW_A_piece_that_moved_and_is_then_redrawn_over_is_drawn_as_a_whole_drawing(bool byTransform) => headless.OnUiThread(() =>
    {
        var (piece, wash) = PieceWithWash();
        using var scene = new Scene(new Pattern { Flat = false }, piece);
        _ = scene.Pixels();
        if (byTransform) piece.RenderTransform = new TranslateTransform(-23, 9);
        else piece.Margin = new Thickness(0, 0, 46, 0);
        _ = scene.Pixels();
        RedrawWash(scene, wash);
        var partial = scene.Pixels();
        var area = Area(piece, scene.Window, 1);
        var whole = Whole(scene);
        var apart = Largest(partial, whole, area, out var apartAt);
        Assert.True(apart <= 2, $"the moved glass is {apart} levels from a whole drawing (at {apartAt}, area {area})");
    });

    // The window is resized (the piece, centred, moves with it), then a control over the glass is redrawn.
    [Fact]
    [Trait("Req", "PR-WIN-180")]
    public void REVIEW_After_a_resize_a_redraw_over_the_glass_leaves_it_as_a_whole_drawing() => headless.OnUiThread(() =>
    {
        var (piece, wash) = PieceWithWash();
        using var scene = new Scene(new Pattern { Flat = false }, piece);
        _ = scene.Pixels();
        scene.Window.Width = 300;
        scene.Window.Height = 190;
        _ = scene.Pixels();
        RedrawWash(scene, wash);
        var partial = scene.Pixels();
        var area = Area(piece, scene.Window, 1);
        var whole = Whole(scene);
        var apart = Largest(partial, whole, area, out var apartAt);
        Assert.True(scene.Window.Bounds.Width == 300 && apart <= 2, $"after the resize the glass is {apart} levels from a whole drawing (at {apartAt})");
    });

    // A glass piece sits at the top of a clipping viewport (a scroll view's), under a header drawn before it: its blur and refraction near its top
    // edge read the header. It is moved out of the viewport (as scrolled away), the header changes (only the header is redrawn: the material,
    // outside the clip, is not drawn), and it is moved back. Avalonia adds the piece's bounds to the dirty region only inside the viewport's clip,
    // so the pixels it kept over the header are the old header's; a whole drawing reads the new one.
    [Fact]
    [Trait("Req", "PR-WIN-180")]
    public void REVIEW_A_piece_that_comes_back_into_a_clipped_viewport_reads_what_changed_beside_the_clip_while_it_was_away() => headless.OnUiThread(() =>
    {
        var header = new Border { Height = 40, VerticalAlignment = VerticalAlignment.Top, Background = Brushes.Black };
        var piece = new Border { Background = Brushes.Transparent, Width = 160, Height = 60, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top };
        SwiftGlass.SetEffect(piece, GlassEffect.Regular(GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous)));
        var shift = new TranslateTransform();
        piece.RenderTransform = shift;
        var viewport = new Border { Margin = new Thickness(0, 40, 0, 0), ClipToBounds = true, Child = new Panel { Children = { piece } } };
        using var scene = new Scene(new Pattern { Flat = true }, new Panel { Children = { header, viewport } });
        _ = scene.Pixels();
        shift.Y = 300; // scrolled away
        _ = scene.Pixels();
        header.Background = Brushes.White;
        _ = scene.Pixels();
        shift.Y = 0; // scrolled back
        var partial = scene.Pixels();
        var area = Area(piece, scene.Window, 1);
        var whole = Whole(scene);
        var apart = Largest(partial, whole, area, out var apartAt);
        Assert.True(apart <= 2, $"the glass that came back is {apart} levels from a whole drawing (at {apartAt})");
    });
}
