using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Marrow.Core.Models;
using Marrow.UI.Panes;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Panes;

// PORT-ONLY: web surfaces W14, the two header pieces Tabula adds (DESIGN.md 3.12 "Header (36 DIP): kind glyph, title,
// optional title menu (version, tabs), then trailing actions", 6.3 Tabula's "title with a menu" and the canvas .abtn "Ask
// to change" with its spark). The title becomes a button with a chevron that opens the menu, in the title's place and
// width; an action with a glyph and words draws both, glyph first; neither breaks the header's fit.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w14-1")]
public sealed class PaneHeaderTitleMenuTests(HeadlessSession headless)
{
    [Fact]
    public void A_title_menu_makes_the_title_a_button_that_opens_it_and_taking_it_away_restores_the_plain_title() => headless.OnUiThread(() =>
    {
        using var rig = new PaneHeaderRig(PaneKind.Artifacts, 600);
        Assert.Null(rig.Header.TitleButton);
        var opened = 0;
        rig.Header.TitleMenu = () => opened++;
        rig.Layout();
        var button = Assert.IsType<Button>(rig.Header.TitleButton);
        Assert.True(button.IsVisible);
        Assert.Same(button, rig.Header.TitleBlock.GetLogicalAncestors().OfType<Button>().FirstOrDefault());
        Assert.Equal(PaneHeaderRig.LongTitle + ", Markdown", AutomationProperties.GetName(button));
        Assert.Contains(button.GetLogicalDescendants().OfType<PaneGlyph>(), glyph => glyph.Kind == PaneGlyphKind.Chevron);
        PaneHeaderRig.Click(button);
        Assert.Equal(1, opened);

        rig.Header.SetTitle("Pricing calculator", "HTML app");
        rig.Layout();
        Assert.Equal("Pricing calculator, HTML app", AutomationProperties.GetName(button));

        rig.Header.TitleMenu = null;
        rig.Layout();
        Assert.Null(rig.Header.TitleButton);
        Assert.True(rig.Header.TitleBlock.IsVisible);
        Assert.Empty(rig.Header.TitleBlock.GetLogicalAncestors().OfType<Button>());
    });

    [Fact]
    public void With_a_title_menu_the_title_keeps_its_minimum_and_nothing_overlaps_it_as_the_header_narrows() => headless.OnUiThread(() =>
    {
        using var rig = new PaneHeaderRig(PaneKind.Artifacts, 900);
        rig.Header.TitleMenu = () => { };
        for (var width = 900; width >= 318; width -= 29)
        {
            rig.SetWidth(width);
            var title = rig.BoxOf(rig.Header.TitleButton!);
            Assert.True(title.Width >= PaneHeaderParts.TitleMinimum - 0.5, $"{width}: the title menu got {title.Width}");
            foreach (var item in rig.TrailingShown())
                Assert.True(rig.BoxOf(item).X >= title.Right - 0.5, $"{width}: {AutomationProperties.GetName(item)} overlaps the title menu");
            Assert.True(rig.Header.CloseButton.IsVisible);
        }
    });

    [Fact]
    public void An_action_with_a_glyph_and_words_draws_the_glyph_first_and_a_glyph_alone_stays_a_glyph_button() => headless.OnUiThread(() =>
    {
        using var rig = new PaneHeaderRig(PaneKind.Artifacts, 900, withActions: false);
        var asked = 0;
        rig.Header.SetActions(
        [
            new PaneHeaderAction("Ask to change", () => asked++, Glyph: PaneGlyphKind.Spark, Text: "Ask to change"),
            new PaneHeaderAction("Copy", () => { }, Glyph: PaneGlyphKind.Copy),
        ]);
        rig.Layout();
        var ask = rig.Header.ActionViews[0];
        var parts = ask.GetLogicalDescendants().Where(c => c is PaneGlyph or TextBlock).ToList();
        Assert.Equal(PaneGlyphKind.Spark, Assert.IsType<PaneGlyph>(parts[0]).Kind);
        Assert.Contains(parts, part => part is TextBlock { Text: "Ask to change" });
        Assert.True(ask.Bounds.Width > rig.Header.ActionViews[1].Bounds.Width);
        Assert.DoesNotContain(rig.Header.ActionViews[1].GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "Copy");
        PaneHeaderRig.Click(ask);
        Assert.Equal(1, asked);
    });
}
