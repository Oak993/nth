using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.Core.NthUi;
using Marrow.Core.Tests.Fixtures;
using Marrow.UI.NthUi;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.NthUi;

// DESIGN.md 7.2 "Unknown or invalid lines degrade to their text" and 14 W16b/W16c (web surfaces W16a; ED-NEW-web-w16a-1):
// a line the parser cannot use shows as its own words in the secondary colour; kinds later units draw (chart, map, svg,
// diagram) show a readable stand-in until their renderer arrives (the charts' came with W16b, ED-NEW-web-w16b-1), and a
// registered renderer takes the kind and its parts, never a degraded line.
[Collection("Headless")]
public sealed class NthUiDegradeTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void A_line_that_cannot_be_used_shows_as_its_text_in_the_secondary_colour() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig();
        rig.Stream("card \"Plan\"\n  stepper label=\"No state\"\n  wobble \"Not a component\"\n  text \"Fine\"\n", 11);
        rig.View.Complete();
        rig.Settle();
        var degraded = rig.View.Document.Nodes.Where(n => n.Degraded).ToList();
        Assert.Equal(2, degraded.Count);
        foreach (var node in degraded)
        {
            var block = Assert.IsType<TextBlock>(rig.View.ControlFor(node.Key));
            Assert.Equal(node.Text ?? node.Source, block.Text);
            Assert.Equal(NthUiTokens.Text2.Resolve(rig.Window.ActualThemeVariant), ((ISolidColorBrush)block.Foreground!).Color);
            Assert.True(rig.View.SlotFor(node.Key)!.IsShown);
        }
        Assert.Contains("Fine", rig.Texts());
        Assert.Equal(NthUiTokens.Text1.Resolve(rig.Window.ActualThemeVariant), ((ISolidColorBrush)((TextBlock)rig.Control("text")).Foreground!).Color);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void Maps_line_art_diagrams_and_images_show_readable_stand_ins() => headless.OnUiThread(() =>
    {
        using var map = new NthUiRig(NthUiView.Show(NthUiSamples.Map, culture: NthUiRig.English));
        // W16c draws the map natively; its list of stops and routes is the text alternative the stand-in used to be.
        map.Click(map.ViewOf<Marrow.UI.NthUi.Map.NthUiMapView>().ListButton);
        map.Settle();
        Assert.Equal(["Lisbon to Paris", "Lisbon", "38.72°N, 9.14°W", "Madrid", "Overnight · 40.42°N, 3.7°W", "Paris", "48.86°N, 2.35°E", "By train", "Lisbon → Madrid → Paris", "Show as map"], map.TextsOf("map"));
        using var svg = new NthUiRig(NthUiView.Show(NthUiSamples.Svg, culture: NthUiRig.English));
        var picture = Assert.IsType<Marrow.UI.NthUi.Diagram.NthUiSvgView>(svg.ViewOf("svg")); // W16c draws line art natively
        Assert.NotNull(picture.Document);
        Assert.Equal("A rising line", Avalonia.Automation.AutomationProperties.GetName(picture.Canvas));
        using var diagram = new NthUiRig(NthUiView.Show(NthUiSamples.Diagram, culture: NthUiRig.English));
        var source = diagram.TextsOf("diagram");
        Assert.Contains("Diagram · Request flow", source);
        Assert.Contains(source, t => t.Contains("A[Client] --> B[Server]", StringComparison.Ordinal));
        using var image = new NthUiRig(NthUiView.Show(NthUiSamples.Image, culture: NthUiRig.English));
        Assert.Contains("The nth logo", image.TextsOf("image"));
        var box = image.Control("image");
        Assert.Equal(64, box.Bounds.Width, 1);
        Assert.Equal(64, box.Bounds.Height, 1);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void A_registered_renderer_takes_its_kind_and_parts_but_never_a_degraded_line() => headless.OnUiThread(() =>
    {
        NthUiRenderers.Register("chart", (context, node) => new NthUiStubView(context, node));
        NthUiRenderers.Register("text", (context, node) => new NthUiStubView(context, node));
        try
        {
            Assert.True(NthUiRenderers.IsRegistered("chart"));
            using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.Scatter + "\nwobble \"plain words\"", culture: NthUiRig.English));
            var stub = Assert.IsType<NthUiStubView>(rig.ViewOf("chart"));
            Assert.Equal(["series", "series"], stub.Parts);
            Assert.Same(stub, rig.View.ViewFor(rig.Node("series", 1).Key));
            Assert.Contains("stub chart", rig.Texts());
            Assert.Contains("stub text", rig.Texts());
            Assert.Contains("wobble \"plain words\"", rig.Texts());
            Assert.IsType<NthUiTextView>(rig.View.ViewFor(rig.View.Document.Nodes.Single(n => n.Degraded).Key));
        }
        finally
        {
            NthUiRenderers.Unregister("chart");
            NthUiRenderers.Unregister("text");
        }
        Assert.False(NthUiRenderers.IsRegistered("chart"));
        using var after = new NthUiRig(NthUiView.Show(NthUiSamples.Scatter, culture: NthUiRig.English));
        Assert.IsType<Marrow.UI.NthUi.Charts.NthUiChartView>(after.ViewOf("chart"));
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void The_lite_profile_degrades_what_it_leaves_out_and_still_draws_the_rest() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.Lite + "\ntimeline \"Not in lite\"\n  event \"Never\"", NthUiProfile.Lite, NthUiRig.English));
        Assert.Contains("Room temperature", rig.Texts());
        Assert.Contains("Living room", rig.Texts());
        Assert.Contains(rig.View.Document.Nodes, n => n.Degraded);
        Assert.DoesNotContain(rig.View.Document.Nodes, n => n.Kind == "timeline" && !n.Degraded);
        Assert.Contains(rig.Texts(), t => t.Contains("Not in lite", StringComparison.Ordinal));
        Assert.Empty(rig.View.GetVisualDescendants().OfType<NthUiRail>());
    });
}
