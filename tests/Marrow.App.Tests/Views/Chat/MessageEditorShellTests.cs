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
}
