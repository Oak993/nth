using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Views.Composer;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

[Collection("Headless")]
public sealed class PromptEditorTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-COMPOSER-176")] // App/Views/Composer/PromptEditor.swift:56-61,99-100
    [Trait("Req", "UI-COMPOSER-057")] // App/Views/Composer/PromptEditor.swift:56-61,99-100
    [Trait("Decision", "ED-059")]
    public void The_follow_up_editor_uses_the_source_width_and_title_style()
    {
        headless.OnUiThread(() =>
        {
            var editor = MakeEditor("Edit follow-up", "", []);
            Assert.Equal(520, editor.Width);

            var title = Assert.Single(
                LogicalQuery.SelfAndDescendants<TextBlock>(editor),
                block => block.Text == "Edit follow-up");
            Assert.Equal(17, title.FontSize);
            Assert.Equal(FontWeight.SemiBold, title.FontWeight);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-177")] // App/Views/Composer/PromptEditor.swift:37-54,62-73
    [Trait("Req", "UI-COMPOSER-058")] // App/Views/Composer/PromptEditor.swift:37-73
    public void The_message_field_starts_with_its_text_and_the_exact_placeholder()
    {
        headless.OnUiThread(() =>
        {
            var editor = MakeEditor("Edit follow-up", "A message already being edited", []);
            var textView = Assert.Single(LogicalQuery.SelfAndDescendants<ComposerNSTextView>(editor));

            Assert.Equal("A message already being edited", textView.Text);
            Assert.Equal("Message", textView.Placeholder);
            Assert.Single(LogicalQuery.SelfAndDescendants<ScrollViewer>(editor));
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-178")] // App/Views/Composer/PromptEditor.swift:103-115
    [Trait("Req", "UI-COMPOSER-058")] // App/Views/Composer/PromptEditor.swift:103-115
    public void Return_and_command_return_submit_escape_cancels_and_other_keys_remain_with_the_field()
    {
        headless.OnUiThread(() =>
        {
            var submitted = 0;
            var cancelled = 0;
            var editor = new PromptEditor(
                "Edit follow-up",
                new Binding<string>(() => "", _ => { }),
                new Binding<ValueList<Attachment>>(() => ValueList<Attachment>.Empty, _ => { }),
                () => submitted++,
                () => cancelled++,
                () => new Border(),
                () => new Border());
            var textView = Assert.Single(LogicalQuery.SelfAndDescendants<ComposerNSTextView>(editor));
            var onKey = Assert.IsType<Func<ComposerKey, bool>>(textView.OnKeyCallback);

            Assert.True(onKey(ComposerKey.Submit));
            Assert.True(onKey(ComposerKey.Steer));
            Assert.Equal(2, submitted);

            Assert.True(onKey(ComposerKey.Escape));
            Assert.Equal(1, cancelled);

            Assert.False(onKey(ComposerKey.Up));
            Assert.False(onKey(ComposerKey.Down));
            Assert.False(onKey(ComposerKey.Tab));
            Assert.False(onKey(ComposerKey.DeleteAtStart));
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-179")] // App/Views/Composer/PromptEditor.swift:74-93
    [Trait("Req", "UI-COMPOSER-059")] // App/Views/Composer/PromptEditor.swift:79-93
    public void The_add_files_plus_is_labelled_and_disabled_at_eight_attachments()
    {
        headless.OnUiThread(() =>
        {
            var seven = Enumerable.Range(0, 7).Select(_ => NewAttachment()).ToArray();
            var eight = Enumerable.Range(0, 8).Select(_ => NewAttachment()).ToArray();

            var enabled = FindAddFilesButton(MakeEditor("Edit follow-up", "", seven));
            var disabled = FindAddFilesButton(MakeEditor("Edit follow-up", "", eight));

            Assert.Equal("Add files", ToolTip.GetTip(enabled));
            Assert.True(enabled.IsEnabled);
            Assert.Equal("Add files", ToolTip.GetTip(disabled));
            Assert.False(disabled.IsEnabled);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-177")] // App/Views/Composer/PromptEditor.swift:37-54,62-73
    public void The_empty_message_field_reserves_at_least_twenty_two_points()
    {
        headless.OnUiThread(() =>
        {
            var editor = MakeEditor("Edit follow-up", "", []);
            var textView = Assert.Single(LogicalQuery.SelfAndDescendants<ComposerNSTextView>(editor));

            Assert.Equal(22, textView.Height);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-177")] // App/Views/Composer/PromptEditor.swift:37-54,62-73
    public void The_message_field_caps_its_height_and_keeps_its_scroll_view()
    {
        headless.OnUiThread(() =>
        {
            var editor = MakeEditor("Edit follow-up", string.Concat(Enumerable.Repeat("word ", 10000)), []);
            var textView = Assert.Single(LogicalQuery.SelfAndDescendants<ComposerNSTextView>(editor));

            Assert.Equal(240, textView.Height);
            Assert.Single(LogicalQuery.SelfAndDescendants<ScrollViewer>(editor));
        });
    }

    private static PromptEditor MakeEditor(string title, string initialText, Attachment[] initialAttachments)
    {
        var text = initialText;
        var attachments = new ValueList<Attachment>(initialAttachments);
        return new PromptEditor(
            title,
            new Binding<string>(() => text, value => text = value),
            new Binding<ValueList<Attachment>>(() => attachments, value => attachments = value),
            () => { },
            () => { },
            () => new Border(),
            () => new Border());
    }

    private static Button FindAddFilesButton(PromptEditor editor) =>
        Assert.Single(
            LogicalQuery.SelfAndDescendants<Button>(editor),
            button => LogicalQuery.SelfAndDescendants<SwiftImage>(button)
                .Any(image => image.SymbolName == "plus"));

    private static Attachment NewAttachment() => new()
    {
        Id = Guid.NewGuid(),
        Name = "",
        Path = "",
        MimeType = "",
    };
}
