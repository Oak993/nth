using Avalonia;
using Avalonia.Controls;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/MessageEditor.swift (B3 52bc792), :4-6, :39-46 the PromptEditor the editor is made of.
[Collection("Headless")]
public sealed class MessageEditorShellTests(HeadlessSession headless)
{
    // :41 title: "Edit message"; :42 text: $edit.text
    [Fact]
    [Trait("Req", "UI-CHAT-015")] // MessageEditor.swift:40-46 the popover is a PromptEditor titled exactly "Edit message" holding the edit's text
    public void The_editor_is_a_prompt_editor_titled_Edit_message_holding_the_edits_text() => headless.OnUiThread(() =>
    {
        using var rig = new MessageEditorRig(EditorPreviewKind.NoFiles, text: "first words");
        rig.WaitForPreview();

        Assert.Contains("Edit message", rig.Texts());
        var field = Assert.Single(rig.All<TextBox>());
        Assert.Equal("first words", field.Text);
        Assert.True(rig.TakesInput);
    });

    // :5-6 "The edit lives on the thread (ThreadRuntime.messageEdit), so the editor closing with the thread and opening again
    // later shows it as it was left."
    [Fact]
    [Trait("Req", "UI-CHAT-015")] // MessageEditor.swift:5-6 the editor's state lives on the thread runtime, so another editor over it shows it as left
    public void An_editor_opened_again_over_the_same_edit_shows_it_as_it_was_left() => headless.OnUiThread(() =>
    {
        using var rig = new MessageEditorRig(EditorPreviewKind.NoFiles);
        rig.WaitForPreview();
        rig.Edit.Text = "typed so far";
        MessageEditorRig.Drain();

        var again = new MessageEditor(rig.Runtime.MessageEdit!, rig.Runtime);
        var window = new Avalonia.Controls.Window { Width = 480, Height = 700, Content = again };
        window.Show();
        try
        {
            MessageEditorRig.Drain();
            var field = Assert.Single(LogicalQuery.SelfAndDescendants<TextBox>(again));
            Assert.Equal("typed so far", field.Text);
            Assert.Same(rig.Edit, rig.Runtime.MessageEdit);
        }
        finally { window.Close(); }
    });

    // :46-55 the extra views (file changes, the notice, the error line) come from PromptEditor's @ViewBuilder `extra`, so they
    // are children of PromptEditor.swift:59 VStack(alignment: .leading, spacing: 14): 14 points apart, not a stack of their own.
    [Fact]
    [Trait("Req", "UI-CHAT-015")] // MessageEditor.swift:46-55 inside PromptEditor.swift:59,77 VStack(spacing: 14)
    public void The_file_changes_box_and_the_notice_sit_fourteen_points_apart_as_children_of_the_prompt_editor_stack() => headless.OnUiThread(() =>
    {
        using var rig = new MessageEditorRig(EditorPreviewKind.NoFiles);
        rig.WaitForPreview();
        rig.Relayout();

        var notice = Assert.Single(rig.All<TextBlock>(), text => text.Text == "Sending replaces this message and everything after it.");
        var stack = Assert.IsAssignableFrom<Panel>(notice.Parent);
        var box = (Control)stack.Children[stack.Children.IndexOf(notice) - 1];
        var noticeTop = notice.TranslatePoint(new Avalonia.Point(0, 0), rig.Editor)!.Value.Y;
        var boxBottom = box.TranslatePoint(new Avalonia.Point(0, box.Bounds.Height), rig.Editor)!.Value.Y;
        Assert.Equal(14, noticeTop - boxBottom, 3);
    });
}
