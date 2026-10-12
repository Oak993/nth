// Source: DroppyCode/UI/Chrome/Chrome.swift (B3 52bc792), :171-182 (the glass frosts the app content under it, and only that).
// ED-320 11: a partial redraw of the window draws the glass as a whole drawing does. The material keeps the pixels under it and takes
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

    private static Border Piece(bool visible = true, GlassLook? look = null)
    {
        var piece = new Border { Background = Brushes.Transparent, Width = 160, Height = 120, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, IsVisible = visible };
        var effect = GlassEffect.Regular(GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous));
        SwiftGlass.SetEffect(piece, look is null ? effect : effect.WithLook(look));
        return piece;
    }

    // A block under the middle of the glass turns from black to white; only it is redrawn. The glass over it shows the white (the frost is lighter
    // there than before), and the whole piece, the ring the frost spreads the change to around the block included, is what a whole drawing gives.
    [Fact]
    [Trait("Req", "PR-WIN-180")]
    public void A_change_under_the_glass_that_only_it_redraws_shows_through_the_glass_as_in_a_whole_drawing() => headless.OnUiThread(() =>
    {
        var block = new Border { Width = 40, Height = 30, Background = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center };
        var piece = Piece(look: GlassLook.Release); // OD-0147: the release material (which a piece that names it still draws); the canvas's below

        using var scene = new Scene(new Pattern { Flat = true }, new Panel { Children = { block, piece } });
        var before = scene.Pixels();
        block.Background = Brushes.White;
        var partial = scene.Pixels();
        var whole = Whole(scene);
        var origin = block.TranslatePoint(default, scene.Window)!.Value;
        var centre = new PixelPoint((int)(origin.X + block.Bounds.Width / 2), (int)(origin.Y + block.Bounds.Height / 2));
        var lighter = partial(centre.X, centre.Y).G - before(centre.X, centre.Y).G;
        var at = piece.TranslatePoint(default, scene.Window)!.Value;
        var area = new PixelRect((int)at.X, (int)at.Y, (int)piece.Bounds.Width, (int)piece.Bounds.Height);
        var apart = Largest(partial, whole, area, out var apartAt);
        Assert.True(lighter >= 40 && apart <= 2,
            $"the glass over the block grew {lighter} levels lighter (at least 40 expected); the piece is {apart} levels from a whole drawing (at {apartAt})");
    });

    // OD-0147: the same for the shipped canvas material. Its popover frost is wider (1.2 x 32 pt), so the block is twice as large to change the
    // glass over it by as much; the piece must again be what a whole drawing gives, to 2 levels.
    [Fact]
    [Trait("Req", "PR-WIN-180")]
    [Trait("Decision", "OD-0147")]
    public void A_change_under_the_canvas_glass_that_only_it_redraws_shows_through_it_as_in_a_whole_drawing() => headless.OnUiThread(() =>
    {
        var block = new Border { Width = 80, Height = 60, Background = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center };
        var piece = Piece();
        Assert.NotNull(GlassLooks.Of(SwiftGlass.GetEffect(piece)!).Canvas);
        using var scene = new Scene(new Pattern { Flat = true }, new Panel { Children = { block, piece } });
        var before = scene.Pixels();
        block.Background = Brushes.White;
        var partial = scene.Pixels();
        var whole = Whole(scene);
        var origin = block.TranslatePoint(default, scene.Window)!.Value;
        var centre = new PixelPoint((int)(origin.X + block.Bounds.Width / 2), (int)(origin.Y + block.Bounds.Height / 2));
        var lighter = partial(centre.X, centre.Y).G - before(centre.X, centre.Y).G;
        var at = piece.TranslatePoint(default, scene.Window)!.Value;
        var area = new PixelRect((int)at.X, (int)at.Y, (int)piece.Bounds.Width, (int)piece.Bounds.Height);
        var apart = Largest(partial, whole, area, out var apartAt);
        Assert.True(lighter >= 40 && apart <= 2,
            $"the glass over the block grew {lighter} levels lighter (at least 40 expected); the piece is {apart} levels from a whole drawing (at {apartAt})");
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

    // No feedback: once a change under the glass has been drawn, a scene that does not change draws the glass no more (the reach the material adds to
    // a frame's dirty region is not itself a change that asks for another frame).
    [Fact]
    [Trait("Req", "PR-WIN-180")]
    public void After_a_change_under_the_glass_a_scene_that_does_not_change_draws_the_glass_no_more() => headless.OnUiThread(() =>
    {
        var block = new Border { Width = 40, Height = 30, Background = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center };
        var piece = Piece();
        using var scene = new Scene(new Pattern { Flat = true }, new Panel { Children = { block, piece } });
        _ = scene.Pixels();
        block.Background = Brushes.White;
        _ = scene.Pixels();
        var drawn = GlassMaterial.DrawsForTests;
        for (var i = 0; i < 10; i++) Scene.Drain();
        Assert.Equal(0, GlassMaterial.DrawsForTests - drawn);
    });

    // A big piece (520 x 320 in a 600 x 400 window over stripes): a block under it changes, deep inside (where only the blur spreads the change, so the
    // frame's region grows by the blur's reach) or across its left edge, half outside (the refracting band reads it from up to SampledMargin away, so
    // the region grows by all of it). The whole piece is what a whole drawing gives.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-WIN-180")]
    public void A_change_deep_inside_or_near_the_edge_of_a_big_piece_is_drawn_as_a_whole_drawing(bool nearEdge) => headless.OnUiThread(() =>
    {
        var block = new Border { Width = 40, Height = 30, Background = Brushes.Black, VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = nearEdge ? HorizontalAlignment.Left : HorizontalAlignment.Center, Margin = nearEdge ? new Thickness(20, 0, 0, 0) : default };
        var piece = new Border { Background = Brushes.Transparent, Width = 520, Height = 320, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center };
        SwiftGlass.SetEffect(piece, GlassEffect.Regular(GlassShape.RoundedRectangle(26, RoundedCornerStyle.Continuous)));
        var stripes = new Panel { Background = Brushes.Gray };
        for (var x = 0; x < 600; x += 8) stripes.Children.Add(new Border { Width = 4, Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(x, 0, 0, 0) });
        using var scene = new Scene(stripes, new Panel { Children = { block, piece } }, 600, 400);
        _ = scene.Pixels();
        block.Background = Brushes.White;
        var partial = scene.Pixels();
        var whole = Whole(scene);
        var at = piece.TranslatePoint(default, scene.Window)!.Value;
        var apart = Largest(partial, whole, new PixelRect((int)at.X, (int)at.Y, (int)piece.Bounds.Width, (int)piece.Bounds.Height), out var apartAt);
        Assert.True(apart <= 2, $"the big piece is {apart} levels from a whole drawing (at {apartAt})");
    });
}
