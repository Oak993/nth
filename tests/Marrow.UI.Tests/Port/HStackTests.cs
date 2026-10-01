using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// HStack: SwiftUI's HStack centres its children on one line by default (HStack(alignment: .center)).
[Collection("Headless")]
public sealed class HStackTests(HeadlessSession headless)
{
    private static double CentreY(Control child, Visual root) => child.TranslatePoint(new Point(0, child.Bounds.Height / 2), root)!.Value.Y;

    [Fact]
    [Trait("Decision", "ED-107")] // the port-owned SwiftUI layout primitives
    public void A_text_beside_a_taller_item_sits_on_the_items_centre_line() => headless.OnUiThread(() =>
    {
        var tall = new Border { Width = 18, Height = 28 };
        var text = new TextBlock { Text = "Edited 13 files" };
        var stack = new HStack { Spacing = 8, Children = { tall, text } };
        var window = new Window { Width = 300, Height = 100, Content = new Border { Child = stack, VerticalAlignment = VerticalAlignment.Top } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.Equal(CentreY(tall, stack), CentreY(text, stack), 0.5);
            Assert.Equal(text.DesiredSize.Height, text.Bounds.Height, 0.5); // its own height, not the row's
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-107")]
    public void A_childs_own_alignment_is_kept_and_a_top_stack_aligns_to_the_top()
    {
        var own = new Border { VerticalAlignment = VerticalAlignment.Stretch };
        var plain = new Border();
        var centred = new HStack { Children = { own, plain } };
        Assert.Equal(VerticalAlignment.Stretch, own.VerticalAlignment);
        Assert.Equal(VerticalAlignment.Center, plain.VerticalAlignment);
        var top = new HStack(VerticalAlignment.Top) { Children = { new Border() } };
        Assert.Equal(VerticalAlignment.Top, top.Children[0].VerticalAlignment);
        Assert.Equal(Orientation.Horizontal, centred.Orientation);
    }
}
