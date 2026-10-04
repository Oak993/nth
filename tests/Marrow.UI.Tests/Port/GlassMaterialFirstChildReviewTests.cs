// Source: DroppyCode/UI/Chrome/Chrome.swift (B3 52bc792), :153-184 (the glass sits under its control). Review of ED-320.
// REVIEW: Avalonia 12.1.2's Visual.SynchronizeCompositionChildVisuals (run by CompositingRenderer.UpdateCore for every dirty visual)
// compares the composition children's count with the visual children's and, when they differ, clears the composition children and
// re-adds only the element visuals. A parent that holds a glass material always has one composition child more than it has visual
// children, so any render-only invalidation of that parent (a Background change, InvalidateVisual) drops every material in it. Nothing
// re-inserts the material until the owner's next layout pass, property change or appearance change.
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;
using Pattern = Marrow.UI.Tests.Fixtures.GlassBackdropPattern;
using Scene = Marrow.UI.Tests.Fixtures.GlassScene;

namespace Marrow.UI.Tests.Port;

[Collection("Headless")]
public sealed class GlassMaterialFirstChildReviewTests(HeadlessSession headless)
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

    private static void Settle() { for (var i = 0; i < 4; i++) Scene.Drain(); }

    private static void AssertDirectlyBefore(Control owner, string when)
    {
        var material = MaterialOf(owner);
        Assert.True(material is not null, $"{when}: the owner has no material");
        var parent = (CompositionContainerVisual)ElementComposition.GetElementVisual((Visual)owner.GetVisualParent()!)!;
        var own = ElementComposition.GetElementVisual(owner)!;
        Assert.True(parent.Children.Contains(material!), $"{when}: the material is not in its parent's composition children");
        Assert.Equal(parent.Children.IndexOf(own) - 1, parent.Children.IndexOf(material!));
        Assert.Equal(1, parent.Children.Count(v => v == material));
    }

    private static int Contrast(Scene scene)
    {
        var pixels = scene.Pixels();
        var row = Enumerable.Range(90, 60).Select(x => (int)pixels(x, 80).R).ToArray();
        return row.Max() - row.Min();
    }

    // A render-only change of the parent (its Background: AffectsRender, no layout pass) must not take the glass away.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [Trait("Req", "PR-CHAT-171")]
    public void A_render_only_change_of_the_parent_keeps_the_material_in_the_tree(int position) => headless.OnUiThread(() =>
    {
        var piece = Piece();
        var parent = new Panel { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
        parent.Children.Add(Filler());
        parent.Children.Insert(position, piece);
        if (position == 0) parent.Children.Add(Filler());
        using var scene = new Scene(new Pattern(), parent);
        Settle();
        AssertDirectlyBefore(piece, "settled");
        parent.Background = new SolidColorBrush(Color.FromArgb(2, 0, 0, 0));
        Settle();
        AssertDirectlyBefore(piece, "after the parent's Background changed");
        var contrast = Contrast(scene);
        Assert.True(contrast < 64, $"the stripes keep {contrast} of 255 behind the glass after the parent's Background changed");
    });

    // Two glass pieces that share a parent, the first and the second child, each keep one material directly before itself, also after
    // the two are swapped, after one is removed and put back, and after an appearance change and back.
    [Fact]
    [Trait("Req", "PR-CHAT-171")]
    public void Two_glass_siblings_keep_one_material_each_through_reorder_removal_and_appearance_changes() => headless.OnUiThread(() =>
    {
        var a = Piece();
        var b = Piece();
        var parent = new Panel { Children = { a, b, Filler() } };
        using var scene = new Scene(new Pattern(), parent);
        Settle();
        AssertDirectlyBefore(a, "settled a");
        AssertDirectlyBefore(b, "settled b");
        parent.Children.Move(0, 1);
        Settle();
        AssertDirectlyBefore(a, "swapped a");
        AssertDirectlyBefore(b, "swapped b");
        parent.Children.Remove(b);
        Settle();
        Assert.Null(MaterialOf(b));
        parent.Children.Insert(0, b);
        Settle();
        AssertDirectlyBefore(a, "re-added a");
        AssertDirectlyBefore(b, "re-added b");
        scene.Window.RequestedThemeVariant = ThemeVariant.Light;
        Settle();
        scene.Window.RequestedThemeVariant = ThemeVariant.Dark;
        Settle();
        AssertDirectlyBefore(a, "after the appearance change a");
        AssertDirectlyBefore(b, "after the appearance change b");
        var composition = (CompositionContainerVisual)ElementComposition.GetElementVisual(parent)!;
        Assert.Equal(2, composition.Children.Count(v => v is CompositionCustomVisual));
    });

    // The appearance change's whole-window repaint invalidates every parent, which (above) clears every material out of its parent; the
    // re-send must put each one back in the same composition update, so no frame is drawn without the glass. Checked after one tick.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [Trait("Req", "PR-CHAT-171")]
    public void The_appearance_change_does_not_leave_a_frame_without_the_material(int position) => headless.OnUiThread(() =>
    {
        var piece = Piece();
        var parent = new Panel();
        parent.Children.Add(Filler());
        parent.Children.Insert(position, piece);
        using var scene = new Scene(new Pattern(), new Border { Child = parent });
        Settle();
        AssertDirectlyBefore(piece, "settled");
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            scene.Window.RequestedThemeVariant = variant;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            AssertDirectlyBefore(piece, $"the first tick after the change to {variant}");
            Settle();
        }
    });

    // Reparenting a first child from one parent to another moves its material with it: none is left in the old parent.
    [Fact]
    [Trait("Req", "PR-CHAT-171")]
    public void A_reparented_first_child_takes_its_material_with_it() => headless.OnUiThread(() =>
    {
        var piece = Piece();
        var first = new Panel { Children = { piece } };
        var second = new Panel { Children = { Filler() } };
        using var scene = new Scene(new Pattern(), new Panel { Children = { first, second } });
        Settle();
        AssertDirectlyBefore(piece, "settled");
        first.Children.Remove(piece);
        second.Children.Insert(0, piece);
        Settle();
        AssertDirectlyBefore(piece, "reparented");
        var old = (CompositionContainerVisual)ElementComposition.GetElementVisual(first)!;
        Assert.DoesNotContain(old.Children, v => v is CompositionCustomVisual);
    });
}
