// Source: DroppyCode/UI/Chrome/Chrome.swift (B3 52bc792), :171-182 (the glass frosts the app content under it). Review of ED-320 7b.
// REVIEW: GlassMaterial.Draw samples lease.SkSurface.Snapshot(), the whole retained surface. When Avalonia redraws only a dirty region (a
// control over the glass invalidates, nothing else), the pixels outside that region are the previous frame's finished picture: the glass
// itself and the content over it. The blur (up to 12 pt, read to 66 pt around the shape) inside the region then samples the glass's own
// output, so the veil is laid over itself and the content above leaks into the frost. A redraw that changes nothing then changes the
// picture, and repeating it walks the glass away from what a whole drawing gives. ED-320 7b measured one symptom of this
// class (a partial redraw differing from a whole one) and masks it with a whole-window repaint after the Settings turnover only; nothing
// masks it anywhere else (typing over the composer's glass, a hover over the chat row's).
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
public sealed class GlassMaterialPartialRedrawReviewTests(HeadlessSession headless)
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

    // A translucent label-sized control over the glass (a caret, a hover wash, a line of text) is redrawn with no change at all, a few
    // times; the glass under it must stay what it was, which is also what a whole drawing of the window gives.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-WIN-180")]
    public void REVIEW_A_redraw_of_a_control_over_the_glass_that_changes_nothing_leaves_the_glass_as_it_was(bool flat) => headless.OnUiThread(() =>
    {
        var wash = new Border { Width = 40, Height = 16, Background = new SolidColorBrush(Color.FromArgb(77, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var piece = new Border { Background = Brushes.Transparent, Width = 160, Height = 120, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Child = wash };
        SwiftGlass.SetEffect(piece, GlassEffect.Regular(GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous)));
        using var scene = new Scene(new Pattern { Flat = flat }, piece);
        var before = scene.Pixels();
        var origin = wash.TranslatePoint(default, scene.Window)!.Value;
        var area = new PixelRect((int)origin.X, (int)origin.Y, (int)wash.Bounds.Width, (int)wash.Bounds.Height);
        for (var i = 0; i < 4; i++)
        {
            wash.InvalidateVisual();
            _ = scene.Pixels();
        }
        var partial = scene.Pixels();
        var drift = Largest(before, partial, area, out var driftAt);
        foreach (var visual in scene.Window.GetVisualDescendants()) visual.InvalidateVisual();
        var whole = scene.Pixels();
        var apart = Largest(partial, whole, area, out var apartAt);
        Assert.True(drift <= 2 && apart <= 2,
            $"after four redraws that changed nothing the glass under the control moved {drift} levels (at {driftAt}) and is {apart} levels from a whole drawing (at {apartAt})");
    });
}
