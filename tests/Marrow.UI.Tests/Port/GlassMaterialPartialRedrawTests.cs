// Source: DroppyCode/UI/Chrome/Chrome.swift (B3 52bc792), :171-182 (the glass frosts the app content under it, and only that).
// ED-NEW-glass-firstchild 11: a partial redraw of the window draws the glass as a whole drawing does. The material keeps the pixels under it and takes
// new ones only where Avalonia redraws (the review's GlassMaterialPartialRedrawReviewTests: a redraw over the glass); these two pin the other halves:
// a change under the glass inside the redrawn region is taken, and a piece that appears beside content drawn over it reads none of that content
// (its render bounds reach as far as it samples, SampledMargin).
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
public sealed class GlassMaterialPartialRedrawTests(HeadlessSession headless)
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

    private static Border Piece(bool visible = true)
    {
        var piece = new Border { Background = Brushes.Transparent, Width = 160, Height = 120, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, IsVisible = visible };
        SwiftGlass.SetEffect(piece, GlassEffect.Regular(GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous)));
        return piece;
    }

    // A block under the middle of the glass turns from black to white; only it is redrawn. The glass over it shows the white (the frost is lighter
    // there than before) and is what a whole drawing gives.
    [Fact]
    [Trait("Req", "PR-WIN-180")]
    public void A_change_under_the_glass_that_only_it_redraws_shows_through_the_glass_as_in_a_whole_drawing() => headless.OnUiThread(() =>
    {
        var block = new Border { Width = 40, Height = 30, Background = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center };
        var piece = Piece();
        using var scene = new Scene(new Pattern { Flat = true }, new Panel { Children = { block, piece } });
        var before = scene.Pixels();
        block.Background = Brushes.White;
        var partial = scene.Pixels();
        var whole = Whole(scene);
        var origin = block.TranslatePoint(default, scene.Window)!.Value;
        var area = new PixelRect((int)origin.X, (int)origin.Y, (int)block.Bounds.Width, (int)block.Bounds.Height);
        var centre = new PixelPoint(area.X + area.Width / 2, area.Y + area.Height / 2);
        var lighter = partial(centre.X, centre.Y).G - before(centre.X, centre.Y).G;
        var apart = Largest(partial, whole, area, out var apartAt);
        Assert.True(lighter >= 40 && apart <= 2,
            $"the glass over the block grew {lighter} levels lighter (at least 40 expected) and is {apart} levels from a whole drawing (at {apartAt})");
    });

    // A white bar drawn over the material, 4 px to the right of a piece, is there before the piece appears. When the piece appears (only it changes)
    // its frost and refraction near that edge must not take the bar in: they read only what is drawn under the glass, as in a whole drawing.
    [Fact]
    [Trait("Req", "PR-WIN-180")]
    public void A_piece_that_appears_beside_content_drawn_over_it_does_not_frost_that_content() => headless.OnUiThread(() =>
    {
        var piece = Piece(visible: false);
        var bar = new Border { Width = 20, Height = 120, Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(212, 0, 0, 0) };
        using var scene = new Scene(new Pattern { Flat = true }, new Panel { Children = { piece, bar } });
        _ = scene.Pixels();
        piece.IsVisible = true;
        var partial = scene.Pixels();
        var whole = Whole(scene);
        var origin = piece.TranslatePoint(default, scene.Window)!.Value;
        var area = new PixelRect((int)origin.X, (int)origin.Y, (int)piece.Bounds.Width, (int)piece.Bounds.Height);
        var apart = Largest(partial, whole, area, out var apartAt);
        Assert.True(piece.Bounds.Width == 160 && apart <= 2, $"the glass that appeared is {apart} levels from a whole drawing (at {apartAt})");
    });
}
