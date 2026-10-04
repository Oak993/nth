using Avalonia.Controls;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// ED-NEW-frames-1 (SOURCE FACT, E:\Droppy\work\reference-video 1-settings-tour.mov S 180.50-185.50, 1 px = 1 pt): a SwiftUI Text in the system font stands on a
// line box of 16 points at 11, 17 at 12 and 18 at 13 (the pitch of its wrapped lines, and every height it gives: the lead card 94, a Connected row 58, a catalog
// card 126); the face's own (Inter's 1.21 times the size) left every card 3 to 7 points short. The measured sizes only; everything else keeps the face's own height.
[Collection("Headless")]
public sealed class SwiftFontLineHeightTests(HeadlessSession headless)
{
    private static TextBlock Applied(SwiftFont font)
    {
        var block = new TextBlock { LineHeight = 99 };
        font.Apply(block);
        return block;
    }

    [Theory]
    [InlineData(SwiftFont.TextStyle.Subheadline, 16)]
    [InlineData(SwiftFont.TextStyle.Callout, 17)]
    [InlineData(SwiftFont.TextStyle.Body, 18)]
    [InlineData(SwiftFont.TextStyle.Headline, 18)]
    [Trait("Req", "PR-MCP-199")] // MCPSettingsPage.swift:35-63 against the stills (the page's card heights); partial: the font's half of that record
    [Trait("Rule", "R-TEST-4")]
    public void A_swiftui_text_style_at_a_measured_size_stands_on_the_macs_line_box(SwiftFont.TextStyle style, double expected) => headless.OnUiThread(() =>
    {
        Assert.Equal(expected, Applied(SwiftFont.System(style)).LineHeight);
        Assert.Equal(expected, Applied(SwiftFont.System(style, weight: SwiftFont.Weight.Semibold)).LineHeight); // the weight does not change the box
    });

    [Theory]
    [InlineData(11, 16)]
    [InlineData(12, 17)]
    [InlineData(13, 18)]
    [Trait("Req", "PR-MCP-199")] // the same sizes by their points (.font(.system(size:)))
    [Trait("Rule", "R-TEST-4")]
    public void A_fixed_size_swiftui_font_at_a_measured_size_takes_the_same_box(double size, double expected) => headless.OnUiThread(() =>
        Assert.Equal(expected, Applied(SwiftFont.System(size)).LineHeight));

    [Fact]
    [Trait("Req", "PR-MCP-199")] // partial: what the box does not apply to
    [Trait("Rule", "R-TEST-4")]
    public void Sizes_without_a_measurement_appkit_fonts_the_monospaced_design_and_a_board_font_keep_the_faces_own_height() => headless.OnUiThread(() =>
    {
        foreach (var unmeasured in new[] { SwiftFont.System(SwiftFont.TextStyle.Footnote), SwiftFont.System(SwiftFont.TextStyle.Title2), SwiftFont.System(SwiftFont.TextStyle.Title3), SwiftFont.System(14), SwiftFont.System(10.5) })
            Assert.True(double.IsNaN(Applied(unmeasured).LineHeight), unmeasured.PointSize + " has no measured box");
        Assert.True(double.IsNaN(Applied(SwiftFont.SystemFont(13)).LineHeight)); // NSFont.systemFont: the text system's own lines
        Assert.True(double.IsNaN(Applied(SwiftFont.BoldSystemFont(13)).LineHeight));
        Assert.True(double.IsNaN(Applied(SwiftFont.MonospacedSystemFont(12, SwiftFont.Weight.Regular)).LineHeight));
        Assert.True(double.IsNaN(Applied(SwiftFont.System(SwiftFont.TextStyle.Callout, SwiftFont.Design.Monospaced)).LineHeight)); // SF Mono: its own (10.5 reads 13 on the stills)
        Assert.True(double.IsNaN(Applied(SwiftFont.System(SwiftFont.TextStyle.Callout).WithNaturalLineHeight()).LineHeight)); // a surface drawn to the owner's board
    });
}
