// REVIEW (scoped re-check of unit/blitz-frames at db0909d01, PR-CHAT-171 / UI-CHAT-073): the container's watch of the visuals between its glass pieces and itself
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
public sealed class GlassMaterialWatchReviewTests(HeadlessSession headless)
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
    public void REVIEW_A_detached_container_drops_every_watch_and_takes_them_up_again_when_reattached() => headless.OnUiThread(() =>
    {
        var piece = Circle(30);
        var translate = new TranslateTransform();
        var inner = new Border { Child = piece, RenderTransform = translate };
        var outer = new Border { Child = inner };
        var container = new Panel { Width = 200, Height = 100, Children = { outer } };
        SwiftGlass.SetContainerSpacing(container, 2);
        var holder = new Panel();
        using var scene = new Scene(new Pattern(), holder);
        holder.Children.Add(container);
        Settle();
        Assert.Equal(3, Watched(container).Count); // outer, inner, the piece
        holder.Children.Remove(container); // detached
        Settle();
        Assert.Empty(Watched(container));
        Assert.False(Requested(container));
        inner.Opacity = 0.5; translate.Y = 3; outer.RenderTransform = new TranslateTransform(1, 0); // nothing of the detached row reaches the container any more
        Assert.False(Requested(container));
        holder.Children.Add(container); // re-attached
        Settle();
        Assert.Equal(3, Watched(container).Count);
        Assert.Equal([0.5], DrawnOpacities(container));
        inner.Opacity = 0.8;
        Assert.True(Requested(container));
        Settle();
        Assert.Equal([0.8], DrawnOpacities(container));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-171")]
    public void REVIEW_A_removed_subtree_and_a_replaced_transform_are_no_longer_watched() => headless.OnUiThread(() =>
    {
        var old = new TranslateTransform();
        var leaving = new Border { Child = Circle(30), RenderTransform = old };
        var staying = new Border { Child = Circle(100) };
        var container = new Panel { Width = 200, Height = 100, Children = { leaving, staying } };
        SwiftGlass.SetContainerSpacing(container, 2);
        using var scene = new Scene(new Pattern(), container);
        Settle();
        Assert.Equal(4, Watched(container).Count);
        staying.RenderTransform = new TranslateTransform(); Settle();
        var replaced = (TranslateTransform)staying.RenderTransform!;
        container.Children.Remove(leaving); // the row rebuilt: the leaving control goes
        Settle();
        Assert.Equal(2, Watched(container).Count);
        Assert.DoesNotContain(leaving, Watched(container).Cast<Visual>());
        old.Y = 5; leaving.Opacity = 0.3; // changes of the removed control no longer ask for a synchronisation
        Assert.False(Requested(container));
        replaced.X = 4;
        Assert.True(Requested(container));
        Settle();
    });

    [Fact]
    [Trait("Req", "PR-CHAT-171")]
    public void REVIEW_A_row_rebuilt_while_a_piece_fades_draws_the_new_pieces_and_forgets_the_old() => headless.OnUiThread(() =>
    {
        var fading = new Border { Child = Circle(30) };
        var container = new Panel { Width = 200, Height = 100, Children = { fading } };
        SwiftGlass.SetContainerSpacing(container, 2);
        using var scene = new Scene(new Pattern(), container);
        Settle();
        for (var step = 1; step <= 3; step++) { fading.Opacity = 1 - 0.2 * step; Scene.Drain(); }
        Assert.Equal(0.4, DrawnOpacities(container).Single(), 6);
        var arriving = new Border { Opacity = 0, Child = Circle(100) };
        container.Children.Clear(); container.Children.Add(arriving); // rebuilt mid-fade
        fading.Opacity = 0.2; // the old row's fade goes on, detached
        Settle();
        Assert.Empty(DrawnOpacities(container));
        Assert.DoesNotContain(fading, Watched(container).Cast<Visual>());
        for (var step = 1; step <= 4; step++) { arriving.Opacity = 0.25 * step; Scene.Drain(); Assert.Equal(0.25 * step, DrawnOpacities(container).Single(), 6); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-171")]
    public void REVIEW_A_piece_under_a_nested_container_is_drawn_and_watched_by_the_nearest_container_only() => headless.OnUiThread(() =>
    {
        var nestedPiece = Circle(30);
        var nestedInner = new Border { Child = nestedPiece };
        var nested = new Panel { Width = 120, Height = 100, Children = { nestedInner } };
        SwiftGlass.SetContainerSpacing(nested, 2);
        var middle = new Border { Child = nested };
        var ownPiece = Circle(150);
        var outer = new Panel { Width = 256, Height = 100, Children = { middle, ownPiece } };
        SwiftGlass.SetContainerSpacing(outer, 2);
        using var scene = new Scene(new Pattern(), outer);
        Settle();
        Assert.Equal([1d], DrawnOpacities(outer)); // the outer container draws its own piece only
        Assert.Equal([1d], DrawnOpacities(nested));
        Assert.DoesNotContain(nestedInner, Watched(outer).Cast<Visual>());
        Assert.Equal(2, Watched(nested).Count);
        nestedInner.Opacity = 0.5;
        Assert.True(Requested(nested));
        Assert.False(Requested(outer));
        Scene.Drain();
        Assert.Equal([0.5], DrawnOpacities(nested));
        // A fade of the middle (above the nested container) leaves the nested regions as they are: it is not a visual between the nested pieces and their container.
        middle.Opacity = 0.3;
        Settle();
        Assert.Equal([0.5], DrawnOpacities(nested));
        // (Where the nested material sits in the composition tree is the separate, pre-existing placement question.)
        // The nested container itself fading: its material copies its opacity; record in how many frames.
        nested.Opacity = 0.6;
        var frames = 0;
        while (Math.Abs(MaterialVisual(nested)!.Opacity - 0.6) > 1e-6 && frames < 5) { Scene.Drain(); frames++; }
        Note($"the nested container's own fade reaches its material after {frames} frame(s)");
        Assert.True(frames <= 1, $"the nested container's own opacity reached its material after {frames} frames");
    });

    [Theory]
    [InlineData("container")]
    [InlineData("own glass")]
    [InlineData("no glass")]
    [Trait("Req", "PR-CHAT-171")]
    public void REVIEW_What_a_fade_frame_allocates(string kind) => headless.OnUiThread(() =>
    {
        var pieces = Enumerable.Range(0, 6).Select(i => Circle(4 + 42 * i)).ToArray();
        if (kind == "no glass") foreach (var p in pieces) SwiftGlass.SetEffect(p, null);
        var fading = new Border { Child = pieces[2] };
        var container = new Panel { Width = 256, Height = 100 };
        foreach (var p in pieces) container.Children.Add(p == pieces[2] ? fading : p);
        if (kind == "container") SwiftGlass.SetContainerSpacing(container, 2);
        using var scene = new Scene(new Pattern(), container);
        Settle();
        for (var i = 0; i < 20; i++) { fading.Opacity = 0.5 + 0.01 * i; Scene.Drain(); } // warm
        var idle0 = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 20; i++) Scene.Drain();
        var idle = (GC.GetAllocatedBytesForCurrentThread() - idle0) / 20.0;
        var fade0 = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 20; i++) { fading.Opacity = 0.05 + 0.045 * i; Scene.Drain(); }
        var fade = (GC.GetAllocatedBytesForCurrentThread() - fade0) / 20.0;
        Note($"{kind}: idle frame {idle:F0} B, fade frame {fade:F0} B");
        Assert.True(fade - idle < 64 * 1024, $"a fade frame allocates {fade - idle:F0} B more than an idle one");
    });
}
