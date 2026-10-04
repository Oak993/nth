using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port;

// ED-290 (1), ED-316 (3): a text input is not measured, so it keeps the face's own line box under any text style; that
// holds for the prompt of ChatDialogControls.TextEditor (the sheets' TextEditor and rounded-border fields, OD-0042, OD-0045) as it
// does for the plain field's (PlainControls.TextField). ChatView.swift:1558-1563 puts such an editor under `.font(.system(.body))`.
[Collection("Headless")]
public sealed class ChatDialogControlsLineBoxTests(HeadlessSession headless)
{
    [Theory]
    [Trait("Req", "PR-MCP-199")]
    [Trait("Decision", "ED-290")]
    [InlineData("TextEditor")]
    [InlineData("RoundedBorder")]
    public void The_text_editors_prompt_keeps_the_faces_own_line_box_under_a_text_style(string style) => headless.OnUiThread(() =>
    {
        var field = new TextBox { PlaceholderText = "Commit message" };
        ChatDialogControls.TextEditor(field, style: style);
        var window = new Avalonia.Controls.Window { Width = 400, Height = 200, Content = new StackPanel { Children = { field } }.Font(SwiftFont.Body) };
        window.Show();
        try
        {
            window.UpdateLayout();
            var prompt = field.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "Commit message");
            Assert.True(double.IsNaN(prompt.LineHeight), $"the prompt took a text style's line box: {prompt.LineHeight}");
        }
        finally { window.Close(); }
    });
}
