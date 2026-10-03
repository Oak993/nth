using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// REVIEW (final review of unit/effort-title-fidelity, round 4, 2026-10-03): ED-238 round 2 (5) has the card's one shadow
// "faded out on the tip's side" and PopoverCard's note "so it never ends on a hard line under the tip" (it fades out over the
// last 5 pt of the room on the tip's side, ShadowFade). PopoverCardTests.The_card_and_its_tip_cast_one_soft_shadow_inside_the_popup
// reads only the popup's last row: with the fade squeezed into a hundredth of a point (mutant K2, ShadowFade 0.01), the shadow
// beside the tip stops on a hard line one point above the popup's edge, and every UI Port, Popover and Traceability test and
// every App Views.Composer test passes (work/claude-verify/resume-2026-10-03/effort-rev-r4.mutants*.txt). Beside the tip,
// down to the popup's edge, the shadow must fall off a row at a time: no step larger than the falloff and the 5 pt fade allow
// together (the Gaussian's steepest, 0.22 x 255 x e^-1/2 / 3.5 = 9.7 levels a point, and the fade's, 0.22 x 255 / 5 = 11.2:
// at most 21, so 22 with a level for rounding).
[Collection("Headless")]
public sealed class PopoverCardShadowReviewTests(HeadlessSession headless)
{
    [Theory]
    [Trait("Decision", "ED-238")] // OD-0095's one shadow (ED-238 round 2 (5))
    [InlineData("Light")]
    [InlineData("Dark")]
    public void REVIEW_Beside_the_tip_the_shadow_fades_out_to_the_popups_edge_with_no_hard_line(string name) => headless.OnUiThread(() =>
    {
        var appearance = name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var card = new PopoverCard
        {
            Child = new Border { Width = 200, Height = 100 },
            TipEdge = PopoverTipEdge.Bottom,
            Background = new SolidColorBrush(PopoverHost.Fill.Resolve(appearance)),
            BorderBrush = new SolidColorBrush(PopoverHost.Border.Resolve(appearance)),
            ShadowRoom = true,
        };
        var host = new Border { Child = card }; // the popup's top-level, clear
        host.Measure(Size.Infinity);
        host.Arrange(new Rect(host.DesiredSize));
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)host.Bounds.Width, (int)host.Bounds.Height));
        bitmap.Render(host);
        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        stream.Position = 0;
        using var image = SkiaSharp.SKBitmap.Decode(stream);
        var margin = PopoverCard.ShadowReach + PopoverCard.ShadowDrop;
        var tip = card.TipPoint; // in the card's own space; the card stands margin in from the host's left and top
        var body = 102.0; // the card's body: the 200 x 100 content and its 1 pt edge
        var lastRow = (int)host.Bounds.Height - 1;
        var seen = new List<string>();
        foreach (var beside in new[] { 4, 6, 8, -6 })
        {
            var x = (int)(margin + tip.X + beside);
            // From 2 pt below where the tip's 45 degree side crosses this column, down to the popup's last row.
            var from = (int)(margin + body + (PopoverCard.TipHeight - Math.Abs(beside)) + 2);
            var column = Enumerable.Range(from, lastRow - from + 1).Select(y => (int)image.GetPixel(x, y).Alpha).ToList();
            seen.Add($"{beside:+0;-0} pt: {string.Join(",", column)}");
            for (var i = 1; i < column.Count; i++)
                Assert.True(column[i - 1] - column[i] <= 22, $"the shadow beside the tip ends on a hard line ({name}): {string.Join(" | ", seen)}");
            Assert.True(column[^1] <= 3, $"the shadow is cut by the popup's edge ({name}): {string.Join(" | ", seen)}");
        }
    });

    // ED-238 round 2 (5) decides the shadow's strength: "0.22 black at the outline, sigma 3.5, 10 pt reach". The existing test
    // bounds the alpha by the outline with PopoverCard.ShadowPeak itself (`left[0] <= PopoverCard.ShadowPeak * 255 + 2`), so a
    // shadow twice as dark (mutant K8, ShadowPeak 0.44) passes every UI Port, Popover and Traceability test and every App
    // Views.Composer test. Read against the decided number: half a point out the falloff is 0.22 e^(-0.25 / 24.5) = 0.218, 55.5
    // of 255; 3.5 pt out (one sigma), 0.133, 34.
    [Theory]
    [Trait("Decision", "ED-238")] // OD-0095's one shadow, ED-238 round 2 (5)'s numbers
    [InlineData("Light")]
    [InlineData("Dark")]
    public void REVIEW_The_shadow_by_the_outline_is_the_decided_0_22_black_falling_off_over_sigma_3_5(string name) => headless.OnUiThread(() =>
    {
        var appearance = name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var card = new PopoverCard
        {
            Child = new Border { Width = 200, Height = 100 },
            TipEdge = PopoverTipEdge.Bottom,
            Background = new SolidColorBrush(PopoverHost.Fill.Resolve(appearance)),
            BorderBrush = new SolidColorBrush(PopoverHost.Border.Resolve(appearance)),
            ShadowRoom = true,
        };
        var host = new Border { Child = card };
        host.Measure(Size.Infinity);
        host.Arrange(new Rect(host.DesiredSize));
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)host.Bounds.Width, (int)host.Bounds.Height));
        bitmap.Render(host);
        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        stream.Position = 0;
        using var image = SkiaSharp.SKBitmap.Decode(stream);
        var margin = PopoverCard.ShadowReach + PopoverCard.ShadowDrop;
        int Alpha(double x, double y) => image.GetPixel((int)(margin + x), (int)(margin + y)).Alpha;
        var near = Alpha(-1, 50); // the pixel just left of the card's outline, halfway down: 0.5 pt out
        var sigma = Alpha(-4, 50); // 3.5 pt out
        Assert.InRange(near, 45, 62); // 55.5 decided
        Assert.InRange(sigma, 26, 42); // 34 decided
    });
}
