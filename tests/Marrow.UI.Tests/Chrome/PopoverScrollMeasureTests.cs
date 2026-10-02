using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Marrow.UI.Chrome;
using Marrow.UI.Markdown;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Chrome;

// Source under test: DroppyCode/UI/Chrome/Popovers.swift (B3 52bc792), PopoverScroll :43-92: with a width the content is
// measured once, off screen (:72, :77-80 measuredHeight(of:width:) = NSHostingView(rootView: content.frame(width:))
// .fittingSize.height rounded up), and the scroll's height is that measure clamped to minHeight...maxHeight (:90).
// A fitting size is taken from a hosted view, with its styles and templates in effect; a detached Avalonia control has
// none (a code block's copy button and a table's scroll viewer measured short, 53 and 64 pt, in the report popovers).
[Collection("Headless")]
public sealed class PopoverScrollMeasureTests(HeadlessSession headless)
{
    private const double Width = 460;

    // A report body as HydraHeadPopovers.swift:84-99 builds it: the markdown blocks, 16/14 padding, the popover's width.
    private static Control Body(string markdown)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10 };
        MarkdownView.SetMarkdownPointSize(stack, 12);
        foreach (var block in MarkdownView.Blocks(markdown))
        {
            var view = new MarkdownBlockView(block);
            MarkdownView.SetMarkdownPointSize(view, 12);
            stack.Children.Add(view);
        }
        return new Border { Child = stack, Padding = new Thickness(16, 14), HorizontalAlignment = HorizontalAlignment.Stretch };
    }

    private static string Lines(params string[] lines) => string.Join(((char)10).ToString(), lines);

    // What the body measures at the popover's width once it is in a window.
    private static double HostedHeight(string markdown)
    {
        var body = Body(markdown);
        var window = new Window { Width = 800, Height = 600, Content = body };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        body.Measure(new Size(Width, double.PositiveInfinity));
        var height = Math.Ceiling(body.DesiredSize.Height);
        window.Close();
        return height;
    }

    [Theory]
    [InlineData("code")]
    [InlineData("table")]
    [InlineData("list")]
    [Trait("Req", "PR-HYDRA-213")] // UI/Chrome/Popovers.swift:72,77-80,90 the scroll is as tall as its content measured at the width (HydraHeadPopovers.swift:96-100)
    public void A_measured_scroll_is_exactly_as_tall_as_its_content_measured_in_a_window(string kind) => headless.OnUiThread(() =>
    {
        var markdown = kind switch
        {
            "code" => Lines("Changes:", "", "```csharp", "var x = 1;", "Console.WriteLine(x);", "```", "", "Then more text."),
            "table" => Lines("| a | b |", "|---|---|", "| 1 | 2 |", "| 3 | 4 |", "", "After."),
            _ => Lines("Done:", "", "- first item here", "- second item with `code`", "- third item", "", "1. one", "2. two"),
        };
        var expected = HostedHeight(markdown);
        Assert.True(expected > 80, $"the fixture's content should be taller than the minimum height, was {expected}");

        var scroll = new PopoverScroll(() => Body(markdown), maxHeight: 2000, width: Width);

        var viewer = Assert.IsType<ScrollViewer>(scroll.Child);
        Assert.Equal(expected, viewer.Height);
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-213")] // UI/Chrome/Popovers.swift:77-80 measuredHeight(of:width:) = the fitting height rounded up
    public void MeasuredHeight_is_the_height_the_content_takes_in_a_window_rounded_up() => headless.OnUiThread(() =>
    {
        var markdown = Lines("```csharp", "var x = 1;", "```", "", "| a | b |", "|---|---|", "| 1 | 2 |");
        Assert.Equal(HostedHeight(markdown), PopoverScroll.MeasuredHeight(Body(markdown), Width));
    });

    // :85-87 without a width the scroll follows the content's height as it changes (onGeometryChange of the content), in
    // both directions: content that shrinks takes the scroll down with it, down to minHeight.
    [Fact]
    [Trait("Req", "PR-HYDRA-213")] // UI/Chrome/Popovers.swift:85-90 onGeometryChange(for: height) then the clamp
    public void An_unmeasured_scroll_follows_its_content_down_as_well_as_up() => headless.OnUiThread(() =>
    {
        var first = new Border { Height = 120 };
        var second = new Border { Height = 80 };
        var body = new StackPanel { Children = { first, second } }; // no height of its own: it takes its children's
        var popover = new PopoverScroll(() => body);
        var window = new Window { Width = 800, Height = 600, Content = popover };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var scroll = (ScrollViewer)popover.Child!;
        Assert.Equal(200, scroll.Height, 0.5);

        body.Children.Remove(second);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(120, scroll.Height, 0.5); // was stuck at 200: the viewport had stretched the content it reads

        first.Height = 50;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(80, scroll.Height, 0.5); // the 80-point floor
        window.Close();
    });
}
