using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;
using Pattern = Marrow.UI.Tests.Fixtures.GlassBackdropPattern;
using Scene = Marrow.UI.Tests.Fixtures.GlassScene;

namespace Marrow.UI.Tests.Port;

// ED-NEW-stutterfix-4: a lone glass piece (one with no container) skips a layout pass of its window when nothing it is drawn with has
// changed (a keystroke in the composer laid the window out and synchronised each of the main window's ~44 pieces: 1,936 synchronisations
// for 43 keys), and synchronises as before when something has: its bounds, its opacity, and, at the next layout pass as before, its render
// transform or its fill's colour changed in place.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-stutterfix-4")]
public sealed class GlassMaterialSyncCoalesceTests(HeadlessSession headless)
{
    private static readonly Type Material = typeof(SwiftGlass).Assembly.GetType("Marrow.UI.Port.GlassMaterial", throwOnError: true)!;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

    private static object Attachment(Control control)
    {
        var table = Material.GetField("attachments", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var args = new object?[] { control, null };
        Assert.True((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, args)!);
        return args[1]!;
    }
    private static CompositionCustomVisual Visual(Control control) => (CompositionCustomVisual)Attachment(control).GetType().GetField("material", Instance)!.GetValue(Attachment(control))!;
    private static Matrix Drawn(Control control)
    {
        var state = Attachment(control).GetType().GetField("previous", Instance)!.GetValue(Attachment(control))!;
        var region = ((Array)state.GetType().GetProperty("Regions")!.GetValue(state)!).GetValue(0)!;
        return (Matrix)region.GetType().GetProperty("Transform")!.GetValue(region)!;
    }
    private static void Settle() { for (var i = 0; i < 4; i++) Scene.Drain(); }

    [Fact]
    public void A_lone_piece_synchronises_on_its_own_changes_and_not_on_a_layout_pass_elsewhere() => headless.OnUiThread(() =>
    {
        var piece = new Border { Background = Brushes.Transparent, Width = 60, Height = 40, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Margin = new Thickness(20, 20, 0, 0) };
        SwiftGlass.SetEffect(piece, GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous)));
        var text = new TextBlock { Text = "a", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom };
        var root = new Panel { Children = { piece, text } };
        using var scene = new Scene(new Pattern(), root);
        Settle();
        var synchronised = 0;
        GlassMaterial.SynchronizedForTests = control => { if (control == piece) synchronised++; };
        try
        {
            for (var i = 0; i < 5; i++) { text.Text += "b"; Settle(); } // the window is laid out again; nothing of the piece changed
            Assert.Equal(0, synchronised);

            piece.Margin = new Thickness(30, 24, 0, 0); Settle(); // it moves
            Assert.True(synchronised > 0);
            Assert.Equal(ElementComposition.GetElementVisual(piece)!.Offset, Visual(piece).Offset);

            var translate = new TranslateTransform();
            piece.RenderTransform = translate; Settle();
            synchronised = 0;
            translate.X = 5; text.Text += "c"; Settle(); // changed in place, then a layout pass
            Assert.True(synchronised > 0);
            Assert.Equal(5, Drawn(piece).M31, 6);

            var fill = new SolidColorBrush(Colors.Transparent);
            piece.Background = fill; Settle();
            synchronised = 0;
            fill.Color = Color.FromArgb(0x80, 0x20, 0x40, 0x60); text.Text += "d"; Settle(); // changed in place, then a layout pass
            Assert.True(synchronised > 0);

            synchronised = 0;
            piece.Opacity = 0.5; Settle();
            Assert.True(synchronised > 0);
            Assert.Equal(0.5, Visual(piece).Opacity, 6);
        }
        finally { GlassMaterial.SynchronizedForTests = null; }
    });
}
