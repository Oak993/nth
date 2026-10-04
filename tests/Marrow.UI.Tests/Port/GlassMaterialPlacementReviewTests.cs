// REVIEW, PRE-EXISTING (fails the same at 653d3ee68 and 2cc3021c0): a glass material inserted into the composition children of an owner's visual parent that is
// not the window's root panel is not in the composition tree once the scene has settled, so it draws nothing. Found in the scoped re-check of unit/blitz-frames at db0909d01, PR-CHAT-171 / UI-CHAT-073): the container's watch of the visuals between its glass pieces and itself
// (GlassMaterial.Attachment.watched) against detach and re-attach, a removed subtree, a replaced transform, a rebuild during a fade, a nested container, and the
// allocation a fade frame costs. Source: App/Views/Chat/ChatView.swift:1120-1123; UI/Chrome/Chrome.swift:933-960.
using System.Collections;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;
using Pattern = Marrow.UI.Tests.Fixtures.GlassBackdropPattern;
using Scene = Marrow.UI.Tests.Fixtures.GlassScene;

namespace Marrow.UI.Tests.Port;

[Collection("Headless")]
public sealed class GlassMaterialPlacementReviewTests(HeadlessSession headless)
{
    private static readonly Type Material = typeof(SwiftGlass).Assembly.GetType("Marrow.UI.Port.GlassMaterial", throwOnError: true)!;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

    private static object Attachment(Control container)
    {
        var table = Material.GetField("attachments", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var args = new object?[] { container, null };
        Assert.True((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, args)!, "the container has a material attachment");
        return args[1]!;
    }
    private static ICollection Watched(Control container) => ((IDictionary)Attachment(container).GetType().GetField("watched", Instance)!.GetValue(Attachment(container))!).Keys;
    private static bool Requested(Control container) => (bool)Attachment(container).GetType().GetField("requested", Instance)!.GetValue(Attachment(container))!;
    private static CompositionCustomVisual? MaterialVisual(Control control) => (CompositionCustomVisual?)Attachment(control).GetType().GetField("material", Instance)!.GetValue(Attachment(control));
    private static List<double> DrawnOpacities(Control container)
    {
        var state = Attachment(container).GetType().GetField("previous", Instance)!.GetValue(Attachment(container));
        if (state is null) return [];
        return [.. ((Array)state.GetType().GetProperty("Regions")!.GetValue(state)!).Cast<object>().Select(r => (double)r.GetType().GetProperty("Opacity")!.GetValue(r)!)];
    }
    private static void Note(string line) => File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "glass-watch-review.txt"), line + Environment.NewLine);
    private static void Settle() { for (var i = 0; i < 4; i++) Scene.Drain(); }
    private static Border Circle(double left)
    {
        var piece = new Border { Background = Brushes.Transparent, Width = 40, Height = 40, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Margin = new Thickness(left, 20, 0, 0) };
        SwiftGlass.SetEffect(piece, GlassEffect.Regular(GlassShape.Circle));
        return piece;
    }
    [Fact]
    [Trait("Req", "PR-CHAT-171")]
    public void REVIEW_A_container_nested_in_another_draws_its_material_beside_itself() => headless.OnUiThread(() =>
    {
        var nested = new Panel { Width = 120, Height = 100, Children = { new Border { Child = Circle(30) } } };
        SwiftGlass.SetContainerSpacing(nested, 2);
        var middle = new Border { Child = nested };
        var outer = new Panel { Width = 256, Height = 100, Children = { middle, Circle(150) } };
        SwiftGlass.SetContainerSpacing(outer, 2);
        using var scene = new Scene(new Pattern(), outer);
        Settle();
        var material = MaterialVisual(nested);
        Assert.NotNull(material);
        Assert.Contains(material!, ((CompositionContainerVisual)ElementComposition.GetElementVisual(middle)!).Children); // drawn beside the nested container, not orphaned
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-CHAT-171")]
    public void REVIEW_A_single_container_draws_its_material_beside_itself(bool underABorder) => headless.OnUiThread(() =>
    {
        var container = new Panel { Width = 120, Height = 100, Children = { new Border { Child = Circle(30) } } };
        SwiftGlass.SetContainerSpacing(container, 2);
        Control root = underABorder ? new Border { Child = container } : container;
        using var scene = new Scene(new Pattern(), root);
        Settle();
        var material = MaterialVisual(container);
        Assert.NotNull(material);
        Assert.Contains(material!, ((CompositionContainerVisual)ElementComposition.GetElementVisual((Visual)container.GetVisualParent()!)!).Children);
    });

    // On pixels: the fine stripes behind a container's piece lose their contrast when the container's material is drawn. Under a Border the material above is orphaned.
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [Trait("Req", "PR-CHAT-171")]
    public void REVIEW_A_containers_piece_frosts_the_stripes_behind_it_wherever_the_container_sits(bool underABorder, bool inAContainer) => headless.OnUiThread(() =>
    {
        var piece = new Border { Background = Brushes.Transparent, Width = 160, Height = 120, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        SwiftGlass.SetEffect(piece, GlassEffect.Regular(GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous)));
        var container = new Panel { Children = { piece } };
        if (inAContainer) SwiftGlass.SetContainerSpacing(container, 2);
        Control root = underABorder ? new Border { Child = container } : container;
        using var scene = new Scene(new Pattern(), root);
        var pixels = scene.Pixels();
        var contrast = Enumerable.Range(90, 60).Select(x => (int)pixels(x, 80).R).ToArray();
        Note($"under a border {underABorder}, in a container {inAContainer}: contrast {contrast.Max() - contrast.Min()}");
        Assert.True(contrast.Max() - contrast.Min() < 64, $"the stripes keep {contrast.Max() - contrast.Min()} of 255 behind the glass (under a border {underABorder}, in a container {inAContainer})");
    });
}
