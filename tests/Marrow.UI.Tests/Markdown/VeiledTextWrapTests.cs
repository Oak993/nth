using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.UI.Markdown;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Markdown;

// Source: DroppyCode/UI/Markdown/StreamVeil.swift:200-339 VeiledText renders a SwiftUI Text, which wraps at the width it
// is offered; MarkdownView.swift:1129-1133 (InlineText's link-free branch, used by the sent-message bubble with
// hugsContent) puts it in a frame that is as wide as the row allows. An Avalonia TextBlock does not wrap unless asked: the
// native view gallery showed a long sent message as one line cut off at the bubble's edge.
[Collection("Headless")]
public sealed class VeiledTextWrapTests(HeadlessSession headless)
{
    private static (double Width, double Height) Measured(Control control, double available)
    {
        var window = new Avalonia.Controls.Window { Width = available, Height = 400, Content = new StackPanel { Children = { control } } };
        window.Show();
        window.UpdateLayout();
        try { return (control.Bounds.Width, control.Bounds.Height); }
        finally { window.Close(); }
    }

    [Fact]
    [Trait("Req", "PR-THEME-160")] // StreamVeil.swift:200-339 a Text wraps; MarkdownView.swift:1129-1133
    public void A_long_veiled_text_wraps_inside_the_width_it_is_offered() => headless.OnUiThread(() =>
    {
        const string sentence = "Add a dark-mode toggle to the settings page, keep the choice in localStorage and cover it with a test. Follow the existing Switch component.";
        var one = Measured(new VeiledText("short") { HorizontalAlignment = HorizontalAlignment.Left }, 300);
        var (width, height) = Measured(new VeiledText(sentence) { HorizontalAlignment = HorizontalAlignment.Left }, 300);
        Assert.True(width <= 300 + 0.5, $"the text is {width} wide in a 300 point window");
        Assert.True(height >= 2 * one.Height - 1, $"the text is {height} high against {one.Height} for one line: it did not wrap");
    });
}
