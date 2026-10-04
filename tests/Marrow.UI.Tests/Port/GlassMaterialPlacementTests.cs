// Source: DroppyCode/UI/Chrome/Chrome.swift (B3 52bc792), :153-184 (the glass sits under its control). ED-NEW-glass-firstchild.
// PORT-ONLY: GlassMaterial inserts each material into the composition children of its owner's visual parent, directly before the
// owner's own visual. A control that is its parent's first child has index 0, and a material not yet in the parent also reads -1
// ("index - 1"), so the first-child material was never inserted and nothing was drawn. These tests put the glass owner (a piece with
// its own effect, a container, and a container nested in a container) first, in the middle and last among its parent's children,
// under the window's root panel and under a non-root parent, and check both that the material is in the composition tree beside
// the owner and that it frosts fine stripes behind it.
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.VisualTree;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;
using Pattern = Marrow.UI.Tests.Fixtures.GlassBackdropPattern;
using Scene = Marrow.UI.Tests.Fixtures.GlassScene;

namespace Marrow.UI.Tests.Port;

[Collection("Headless")]
public sealed class GlassMaterialPlacementTests(HeadlessSession headless)
{
    private static readonly Type Material = typeof(SwiftGlass).Assembly.GetType("Marrow.UI.Port.GlassMaterial", throwOnError: true)!;

    private static CompositionVisual? MaterialOf(Control owner)
    {
        var table = Material.GetField("attachments", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var args = new object?[] { owner, null };
        if (!(bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, args)!) return null;
        return (CompositionVisual?)args[1]!.GetType().GetField("material", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(args[1]);
    }

    private static Border Piece()
    {
        var piece = new Border { Background = Brushes.Transparent, Width = 160, Height = 120, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center };
        SwiftGlass.SetEffect(piece, GlassEffect.Regular(GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous)));
        return piece;
    }
    private static Border Filler() => new() { Width = 1, Height = 1, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private static Panel Container(Control child)
    {
        var container = new Panel { Width = 160, Height = 120, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { child } };
        SwiftGlass.SetContainerSpacing(container, 2);
        return container;
    }
    // The glass owner among two fillers: position 0 first, 1 in the middle, 2 last.
    private static Panel Among(int position, Control owner)
    {
        var parent = new Panel();
        var order = new List<Control> { Filler(), Filler() };
        order.Insert(position, owner);
        foreach (var child in order) parent.Children.Add(child);
        return parent;
    }
    // The owner whose material is checked, and the root put in the window. A nested case is a container whose first, middle or last
    // child is a container holding the piece.
    private static (Control owner, Control root) Build(string mode, int position, bool underABorder)
    {
        Control owner = mode switch
        {
            "piece" => Piece(),
            "container" => Container(Piece()),
            _ => Container(Among(position, Container(Piece()))),
        };
        var parent = Among(position, owner);
        return (owner, underABorder ? new Border { Child = parent } : parent);
    }
    // The inner container of a nested case, the one that holds the piece.
    private static Control Inner(Control outer) => outer.GetVisualDescendants().OfType<Panel>().First(p => SwiftGlass.GetContainerSpacing(p) is not null && p != outer);

    public static IEnumerable<object[]> Cases() =>
        from mode in new[] { "piece", "container", "nested" }
        from position in new[] { 0, 1, 2 }
        from underABorder in new[] { false, true }
        select new object[] { mode, position, underABorder };

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Req", "PR-CHAT-171")]
    public void The_material_is_in_the_composition_tree_directly_before_its_owner(string mode, int position, bool underABorder) => headless.OnUiThread(() =>
    {
        var (owner, root) = Build(mode, position, underABorder);
        using var scene = new Scene(new Pattern(), root);
        for (var i = 0; i < 4; i++) Scene.Drain();
        foreach (var drawing in mode == "nested" ? new[] { owner, Inner(owner) } : new[] { owner })
        {
            var material = MaterialOf(drawing);
            Assert.NotNull(material);
            var parent = (CompositionContainerVisual)ElementComposition.GetElementVisual((Visual)drawing.GetVisualParent()!)!;
            var own = ElementComposition.GetElementVisual(drawing)!;
            Assert.Contains(material!, parent.Children);
            Assert.Equal(parent.Children.IndexOf(own) - 1, parent.Children.IndexOf(material!));
        }
    });

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Req", "PR-CHAT-171")]
    public void The_material_frosts_the_fine_stripes_behind_its_piece(string mode, int position, bool underABorder) => headless.OnUiThread(() =>
    {
        var (_, root) = Build(mode, position, underABorder);
        using var scene = new Scene(new Pattern(), root);
        var pixels = scene.Pixels();
        var contrast = Enumerable.Range(90, 60).Select(x => (int)pixels(x, 80).R).ToArray();
        Assert.True(contrast.Max() - contrast.Min() < 64, $"the stripes keep {contrast.Max() - contrast.Min()} of 255 behind the glass ({mode}, position {position}, under a border {underABorder})");
    });

    // The same scene without glass: the stripes are drawn, so the test above measures the frost and not a blank backdrop.
    [Fact]
    [Trait("Req", "PR-CHAT-171")]
    public void Without_glass_the_same_scene_keeps_the_full_stripe_contrast() => headless.OnUiThread(() =>
    {
        var plain = Piece();
        SwiftGlass.SetEffect(plain, null);
        using var scene = new Scene(new Pattern(), Among(0, plain));
        var pixels = scene.Pixels();
        var contrast = Enumerable.Range(90, 60).Select(x => (int)pixels(x, 80).R).ToArray();
        Assert.True(contrast.Max() - contrast.Min() > 200, $"the stripes keep {contrast.Max() - contrast.Min()}");
    });

    // The first child's material stays before it when a sibling is put in front of it, and leaves with it when it is removed.
    [Fact]
    [Trait("Req", "PR-CHAT-171")]
    public void A_first_childs_material_stays_before_it_when_a_sibling_is_inserted_ahead_and_leaves_with_it() => headless.OnUiThread(() =>
    {
        var piece = Piece();
        var parent = Among(0, piece);
        using var scene = new Scene(new Pattern(), parent);
        for (var i = 0; i < 4; i++) Scene.Drain();
        var composition = (CompositionContainerVisual)ElementComposition.GetElementVisual(parent)!;
        Assert.Contains(MaterialOf(piece)!, composition.Children);
        parent.Children.Insert(0, Filler());
        for (var i = 0; i < 4; i++) Scene.Drain();
        Assert.Equal(composition.Children.IndexOf(ElementComposition.GetElementVisual(piece)!) - 1, composition.Children.IndexOf(MaterialOf(piece)!));
        parent.Children.Remove(piece);
        for (var i = 0; i < 4; i++) Scene.Drain();
        Assert.Null(MaterialOf(piece));
        Assert.DoesNotContain(composition.Children, v => v is CompositionCustomVisual);
    });
}
