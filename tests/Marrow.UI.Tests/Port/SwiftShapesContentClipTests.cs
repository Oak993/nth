using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// A plain `.continuous` Border WITH CONTENT (a Child), the case every card, notice and plate of the timeline is (ED-275 (3) (b)): its fill is the Border's own and
// Apple's corner is cut by Visual.Clip. ED-297 (9) left it on the slack clip, a NON-convex path that Skia anti-aliases with its coarser walker (64 of 255 off at 700 x 45,
// 102 at 380 x 46), while a childless Border is cut by the exact outline (convex: Skia's exact walker) and is within 1 of the path fill. The reference is a
// SwiftShapeBorder of the same size, which fills the path itself (ED-275 (3) (a)); the corner is read per corner, the whole run-in of each, not only the top-left.
[Collection("Headless")]
public sealed class SwiftShapesContentClipTests(HeadlessSession headless)
{
    private const double E = 1.528665;
    private const double Inset = 10;

    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static (Window Window, RenderedPixels Pixels) Render(Control shape, double width, double height)
    {
        var window = new Window { Width = width + 2 * Inset, Height = height + 2 * Inset, Background = Brushes.Black, Content = new Panel { Children = { shape } } };
        window.Show();
        Settle();
        return (window, RenderedPixels.Capture(window));
    }

    // Content that draws nowhere near a corner: a small blue block in the middle of the padding box, as a card's text is.
    private static Control Content() => new Border { Width = 20, Height = 10, Background = Brushes.Blue, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

    private static Border Card(double width, double height, double radius, bool content) =>
        new Border
        {
            Width = width, Height = height, Background = Brushes.Red, Margin = new Thickness(Inset), Padding = new Thickness(14),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = content ? Content() : null,
        }.RoundedRectangle(radius, RoundedCornerStyle.Continuous);

    private static SwiftShapeBorder PathFill(double width, double height, double radius) =>
        new SwiftShapeBorder { Width = width, Height = height, Background = Brushes.Red, Margin = new Thickness(Inset), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top }
            .RoundedRectangle(radius, RoundedCornerStyle.Continuous);

    /// <summary>The worst red difference in each corner's square (top-left, top-right, bottom-right, bottom-left): the run-in of the drawn corner and two pixels more.</summary>
    private static int[] PerCorner(RenderedPixels a, RenderedPixels b, double width, double height, double radius)
    {
        var drawn = SwiftShapes.Outline(new Rect(0, 0, width, height), new CornerRadius(radius), RoundedCornerStyle.Continuous).DrawnRadius(0);
        var side = (int)Math.Ceiling(E * drawn) + 2;
        int x0 = (int)Inset, y0 = (int)Inset, x1 = (int)(Inset + width), y1 = (int)(Inset + height);
        var squares = new[] { (x0, y0), (x1 - side, y0), (x1 - side, y1 - side), (x0, y1 - side) };
        var worst = new int[4];
        for (var c = 0; c < 4; c++)
        {
            var (sx, sy) = squares[c];
            for (var y = sy; y < sy + side; y++)
                for (var x = sx; x < sx + side; x++) worst[c] = Math.Max(worst[c], Math.Abs(a.At(x, y).Red - b.At(x, y).Red));
        }
        return worst;
    }

    [Theory]
    [Trait("Decision", "ED-297")]
    [InlineData(700, 45, 18)] // the one-step steps card at 700 pt (UI-CHAT-036): 64 of 255 off on the slack clip
    [InlineData(380, 45, 18)]
    [InlineData(380, 50, 18)] // a two-line notice (UI-CHAT-034)
    [InlineData(380, 40, 18)] // the Edited-files card's height (UI-CHAT-037)
    [InlineData(700, 54, 18)] // the delegation card of one head (UI-CHAT-054)
    [InlineData(380, 120, 18)] // a tall card: Apple's corner at the stated radius
    [InlineData(700, 200, 18)]
    [InlineData(300, 90, 12)]
    public void A_Border_with_content_draws_every_corner_as_exactly_as_the_childless_Border_and_the_path_fill(double width, double height, double radius) => headless.OnUiThread(() =>
    {
        var (w0, card) = Render(Card(width, height, radius, content: true), width, height);
        var (w1, plain) = Render(Card(width, height, radius, content: false), width, height);
        var (w2, path) = Render(PathFill(width, height, radius), width, height);
        w0.Close(); w1.Close(); w2.Close();
        var againstPath = PerCorner(card, path, width, height, radius);
        var againstPlain = PerCorner(card, plain, width, height, radius);
        var seen = $"{width} x {height}, r {radius}: against the path fill {string.Join(", ", againstPath)}; against the childless Border {string.Join(", ", againstPlain)} (top-left, top-right, bottom-right, bottom-left)";
        // The Border with content is cut exactly as the childless Border is (the exact outline), so its corners are the childless Border's pixel for pixel.
        Assert.True(againstPlain.All(level => level <= 1), seen);
        // In OD-0102's band the childless Border's own corner is square under that clip (ED-297 (8)), so the clip alone draws the corner and it is the path fill's (within 1
        // at the band sizes of ED-297 (9)). A tall card is Apple's corner at the STATED radius, where the Border keeps CornerRadius r (what the sites and their tests read,
        // ED-275 (3)) and so fills the circle of r under the clip: that corner is the plain Border's own and is not this test's (ED-317 (4)).
        var inBand = SwiftShapes.Outline(new Rect(0, 0, width, height), new CornerRadius(radius), RoundedCornerStyle.Continuous).DrawnRadius(0) < radius - 1e-9;
        if (inBand) Assert.True(againstPath.All(level => level <= 8), seen);
    });

    [Fact]
    [Trait("Decision", "ED-297")]
    public void A_Border_with_content_is_cut_by_the_exact_outline_and_its_hit_shape_is_that_outline() => headless.OnUiThread(() =>
    {
        var content = new Border { Width = 40, Height = 10, Background = Brushes.Blue };
        var card = new Border
        {
            Width = 700, Height = 45, Background = Brushes.Red, Padding = new Thickness(14), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(Inset), Child = content,
        }.RoundedRectangle(18, RoundedCornerStyle.Continuous);
        var window = new Window { Width = 720, Height = 65, Content = new Panel { Children = { card } } };
        window.Show();
        Settle();
        var exact = SwiftShapes.ClipGeometry(new Rect(0, 0, 700, 45), new CornerRadius(18), exact: true);
        Assert.Same(exact, card.Clip);
        Assert.Equal(new CornerRadius(18), SwiftShapes.StatedCornerRadius(card));
        Assert.Equal(default, card.CornerRadius); // OD-0102's band: the clip alone makes the corner (ED-297 (8))
        // The hit shape is the clip: inside the circle of the drawn radius but outside Apple's curve misses, just inside it hits, beyond a side misses.
        var l = 45 / (2 * E);
        Assert.NotSame(card, window.InputHitTest(new Point(Inset + 0.869 * l, Inset + 0.0155 * l)));
        Assert.Same(card, window.InputHitTest(new Point(Inset + 0.869 * l, Inset + 0.05 * l)));
        Assert.Same(card, window.InputHitTest(new Point(Inset + 350, Inset + 0.5)));
        Assert.NotSame(card, window.InputHitTest(new Point(Inset + 350, Inset - 1.5)));
        Assert.Same(content, window.InputHitTest(new Point(Inset + 350, Inset + 22.5))); // the content, centred in the card

        // The clip is the cached geometry: a colour, a move and the child coming and going build nothing; a resize builds one.
        var builds = SwiftShapes.GeometryBuildCount;
        card.Background = Brushes.Green;
        card.Margin = new Thickness(Inset + 3, Inset, 0, 0);
        Settle();
        card.Child = null;
        Settle();
        Assert.Same(exact, card.Clip);
        card.Child = content;
        Settle();
        Assert.Same(exact, card.Clip);
        Assert.Equal(builds, SwiftShapes.GeometryBuildCount);
        card.Width = 690;
        Settle();
        Assert.Same(SwiftShapes.ClipGeometry(new Rect(0, 0, 690, 45), new CornerRadius(18), exact: true), card.Clip);
        Assert.Equal(1, SwiftShapes.GeometryBuildCount - builds);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-297")]
    public void Only_a_Border_whose_content_is_drawn_beyond_its_sides_keeps_the_slack_clip() => headless.OnUiThread(() =>
    {
        // SwiftUI's `.background(shape)` does not clip its content; where the source draws content past a side (the effort title's leaving letters, EffortSlider.swift:589
        // `.buttonStyle(.plain)`), the site marks the shape and the clip runs past its sides (ED-275 (3)) between the corners' run-ins (a tall card: a band shape's short
        // sides are all curve, h / 2 from each end). Everywhere else the exact outline cuts it.
        var overflow = new Border { Width = 260, Height = 20, Background = Brushes.Blue, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var card = new Border
        {
            Width = 200, Height = 120, Background = Brushes.Red, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(20), Child = new Panel { Children = { overflow } },
        }.RoundedRectangle(18, RoundedCornerStyle.Continuous);
        var window = new Window { Width = 300, Height = 160, Background = Brushes.Black, Content = new Panel { Children = { card } } };
        window.Show();
        Settle();
        Assert.False(SwiftShapes.GetContentBeyondSides(card));
        Assert.Same(SwiftShapes.ClipGeometry(new Rect(0, 0, 200, 120), new CornerRadius(18), exact: true), card.Clip);
        var cut = RenderedPixels.Capture(window).At(20 + 200 + 30, 20 + 60);
        Assert.True(cut == (0, 0, 0), $"past the side of an unmarked Border: {cut}, expected cut to the black window"); // cut at the side

        card.ContentBeyondSides();
        Settle();
        Assert.Same(SwiftShapes.ClipGeometry(new Rect(0, 0, 200, 120), new CornerRadius(18)), card.Clip);
        var drawn = RenderedPixels.Capture(window).At(20 + 200 + 30, 20 + 60);
        Assert.True(drawn == (0, 0, 255), $"past the side of a marked Border: {drawn}, expected the blue child"); // drawn past the side
        Assert.Same(overflow, window.InputHitTest(new Point(20 + 200 + 30, 20 + 60)));

        // A marked Border that cannot draw beyond a side (no child, or clipped to its bounds) is cut by the exact outline.
        card.ClipToBounds = true;
        Settle();
        Assert.Same(SwiftShapes.ClipGeometry(new Rect(0, 0, 200, 120), new CornerRadius(18), exact: true), card.Clip);
        card.ClipToBounds = false;
        card.Child = null;
        Settle();
        Assert.Same(SwiftShapes.ClipGeometry(new Rect(0, 0, 200, 120), new CornerRadius(18), exact: true), card.Clip);
        card.ContentBeyondSides(false);
        card.Child = new Panel { Children = { new Border { Width = 10, Height = 10 } } };
        Settle();
        Assert.Same(SwiftShapes.ClipGeometry(new Rect(0, 0, 200, 120), new CornerRadius(18), exact: true), card.Clip);
        window.Close();
    });
}
