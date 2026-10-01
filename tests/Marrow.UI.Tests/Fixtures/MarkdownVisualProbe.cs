using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Fixtures;

/// <summary>
/// Reads what a viewer sees from a rendered markdown control: the text of a text block, the colour of a brush,
/// where a control landed in its panel. Shared by the block-level markdown tests (code block, table, list,
/// quote, rule, heading).
/// </summary>
public static class MarkdownVisualProbe
{
    /// <summary>
    /// Shows the control in a window of the given size and lays it out. The control sits in a vertical stack, so it
    /// takes its natural height the way a block in the reply's column does, and the width of the window.
    /// </summary>
    public static Window Show(Control content, double width = 400, double height = 600, ThemeVariant? theme = null)
    {
        var column = new StackPanel { Orientation = Orientation.Vertical };
        column.Children.Add(content);
        var window = new Window { Width = width, Height = height, Content = column };
        if (theme is not null) window.RequestedThemeVariant = theme;
        window.Show();
        Pump();
        return window;
    }

    /// <summary>Runs queued jobs and one render tick, so Track-driven updates and layout have happened.</summary>
    public static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The characters a text block shows, whether set as Text or as inlines.</summary>
    public static string TextOf(TextBlock block) =>
        block.Inlines is { Count: > 0 } inlines ? string.Concat(inlines.OfType<Run>().Select(run => run.Text)) : block.Text ?? "";

    /// <summary>The colour of a solid brush, with its alpha.</summary>
    public static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    /// <summary>What a SwiftUI colour is drawn as in the control's own appearance.</summary>
    public static Color Drawn(SwiftColor colour, Control on) => colour.Resolve(on.ActualThemeVariant);

    /// <summary>Where the top-left corner of a control sits in another visual's coordinates.</summary>
    public static Point Origin(Visual of, Visual relativeTo) => of.TranslatePoint(default, relativeTo)!.Value;

    /// <summary>All text blocks under a root whose text equals the given string.</summary>
    public static IEnumerable<TextBlock> TextBlocksSaying(Visual root, string text) =>
        Fixtures.VisualQuery.Descendants<TextBlock>(root).Where(block => TextOf(block) == text);
}
