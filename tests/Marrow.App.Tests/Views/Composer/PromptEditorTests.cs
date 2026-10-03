using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Views.Composer;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Marrow.App.Runtime;
using Marrow.App.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Theme;
using Avalonia.Input;
using Avalonia.Headless;
using Avalonia.Threading;
using System.Reflection;

namespace Marrow.App.Tests.Views.Composer;

[Collection("Headless")]
public sealed class PromptEditorTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-THEME-042")] // App/Views/Composer/PromptEditor.swift:209-210
    [Trait("Req", "PR-COMPOSER-181")] // App/Views/Composer/PromptEditor.swift:176-216
    [Trait("Req", "PR-COMPOSER-182")] // App/Views/Composer/PromptEditor.swift:218-279
    public void Review_popover_applies_chrome_resizes_and_dismisses_with_pass_through() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var previousTheme = ThemeManager.Current;
        ThemeManager.Current = AppTheme.Dracula;
        try
        {
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var anchor = new Border { Width = 40, Height = 30, Background = Brushes.Transparent, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Avalonia.Controls.Window { Width = 600, Height = 300, Content = anchor };
        window.Show(); Dispatcher.UIThread.RunJobs();
        var popover = new PromptEditorPopover<Border>();
        popover.SetAnchor(anchor);
        var host = new Border { Width = 400, Height = 100, UseLayoutRounding = false };
        var edit = runtime.OnMain(() => new PromptEdit(Guid.NewGuid(), "hello", ValueList<Attachment>.Empty));
        var dismissed = 0;
        runtime.OnMain(() => popover.Sync(edit, () => dismissed++, _ => host));
        var popup = Assert.Single(presenter.Calls).Popup;
        Assert.True(Popovers.GetIsCapsuleButtonBorderShape(host));
        Assert.Equal(ThemeManager.Spec.Accent?.Resolve(Avalonia.Styling.ThemeVariant.Light), Popovers.GetTint(host)?.Resolve(Avalonia.Styling.ThemeVariant.Light));
        Assert.False(popup.IsLightDismissEnabled);
        // OD-0095: the popup is the hosted view's size and the room of the tipped card it hangs on once presented.
        var room = Marrow.UI.Port.PopoverHost.Room(popup);
        Assert.True(room.Height > 0);
        Assert.Equal(400 + room.Width, popup.Width); Assert.Equal(100 + room.Height, popup.Height);
        var resize = typeof(PromptEditorPopover<Border>).GetMethod("ResizeToContent", BindingFlags.Instance | BindingFlags.NonPublic)!;
        host.Height = 100.4;
        resize.Invoke(popover, [popup, host, false]); Assert.Equal(100 + room.Height, popup.Height);
        host.Height = 102; host.Width = 410;
        resize.Invoke(popover, [popup, host, false]); Assert.Equal(102 + room.Height, popup.Height); Assert.Equal(410 + room.Width, popup.Width);
        window.MouseDown(new Point(10, 10), MouseButton.Left); window.MouseUp(new Point(10, 10), MouseButton.Left);
        Assert.Equal(0, dismissed);
        window.MouseDown(new Point(100, 100), MouseButton.Left); window.MouseUp(new Point(100, 100), MouseButton.Left);
        Assert.Equal(1, dismissed);
        var other = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.A };
        window.RaiseEvent(other); Assert.False(other.Handled);
        var escape = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
        window.RaiseEvent(escape); Assert.True(escape.Handled); Assert.Equal(2, dismissed);
        runtime.OnMain(popover.Close);
        Assert.False(presenter.Calls[^1].Presented);
        window.Close(); Dispatcher.UIThread.RunJobs();
        }
        finally { ThemeManager.Current = previousTheme; }
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-181")] // App/Views/Composer/PromptEditor.swift:177-190 generation invalidates deferred show
    public void Review_cancelled_deferred_show_never_presents() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var anchor = new Border(); var window = new Avalonia.Controls.Window { Content = anchor }; window.Show();
        var popover = new PromptEditorPopover<Border>(); popover.SetAnchor(anchor);
        runtime.OnMain(() =>
        {
            var edit = new PromptEdit(Guid.NewGuid(), "hello", ValueList<Attachment>.Empty);
            popover.Sync(edit, () => { }, _ => new Border { Width = 400, Height = 100 });
            popover.Sync(null, () => { }, _ => new Border());
        });
        Assert.Empty(presenter.Calls);
        window.Close(); Dispatcher.UIThread.RunJobs();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-179")] // App/Views/Composer/PromptEditor.swift:117-124 prompt is the approve-button label
    public void Review_file_dialog_receives_Attach_as_its_approve_caption() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var probe = new ComposerDialogProbe();
        using var seam = ComposerFileDialog.Current.Override(probe);
        var editor = MakeEditor("Edit message", "hello", []);
        runtime.OnMain(() => typeof(PromptEditor).GetMethod("ChooseFiles", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(editor, [Binding<ValueList<Attachment>>.Constant(ValueList<Attachment>.Empty)]));
        Assert.Equal((true, false, "Attach"), probe.Options);
        Dispatcher.UIThread.RunJobs();
    });

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

    [Fact]
    [Trait("Req", "PR-COMPOSER-179")] // App/Views/Composer/PromptEditor.swift:74-87 the body reads the attachments binding: the add button follows its count
    public void The_add_files_plus_follows_the_attachments_binding_after_construction() => headless.OnUiThread(() =>
    {
        var box = new ObservableAttachmentsBox { Value = new ValueList<Attachment>(Enumerable.Range(0, 8).Select(_ => NewAttachment())) };
        var editor = new PromptEditor(
            "Edit follow-up",
            new Binding<string>(() => "", _ => { }),
            box.AsBinding(),
            () => { },
            () => { },
            () => new Border(),
            () => new Border());
        var window = new Avalonia.Controls.Window { Width = 600, Height = 400, Content = editor };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var plus = FindAddFilesButton(editor);
        Assert.False(plus.IsEnabled);

        box.Value = new ValueList<Attachment>(Enumerable.Range(0, 7).Select(_ => NewAttachment())); // a removal elsewhere
        Dispatcher.UIThread.RunJobs();
        Assert.True(plus.IsEnabled);

        box.Value = ValueList<Attachment>.Empty;
        Dispatcher.UIThread.RunJobs();
        Assert.True(plus.IsEnabled);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-182")] // App/Views/Composer/PromptEditor.swift:247-252 the monitor matches only leftMouseDown and rightMouseDown
    public void A_middle_click_in_the_window_does_not_dismiss_the_editor_popover() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var anchor = new Border { Width = 40, Height = 30, Background = Brushes.Transparent, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Avalonia.Controls.Window { Width = 600, Height = 300, Content = anchor };
        window.Show(); Dispatcher.UIThread.RunJobs();
        var popover = new PromptEditorPopover<Border>();
        popover.SetAnchor(anchor);
        var edit = runtime.OnMain(() => new PromptEdit(Guid.NewGuid(), "hello", ValueList<Attachment>.Empty));
        var dismissed = 0;
        runtime.OnMain(() => popover.Sync(edit, () => dismissed++, _ => new Border { Width = 400, Height = 100 }));
        Assert.Single(presenter.Calls);

        window.MouseDown(new Point(100, 100), MouseButton.Middle); window.MouseUp(new Point(100, 100), MouseButton.Middle);
        Assert.Equal(0, dismissed);
        window.MouseDown(new Point(100, 100), MouseButton.Right); window.MouseUp(new Point(100, 100), MouseButton.Right);
        Assert.Equal(1, dismissed);
        runtime.OnMain(popover.Close);
        window.Close(); Dispatcher.UIThread.RunJobs();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-182")] // App/Views/Composer/PromptEditor.swift:240-246 the Escape monitor is application-wide, not one window's
    public void Escape_in_another_window_of_the_application_dismisses_the_editor_popover() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var anchor = new Border { Width = 40, Height = 30, Background = Brushes.Transparent, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Avalonia.Controls.Window { Width = 600, Height = 300, Content = anchor };
        window.Show(); Dispatcher.UIThread.RunJobs();
        var other = new Avalonia.Controls.Window { Width = 300, Height = 200, Content = new Border() };
        other.Show(); Dispatcher.UIThread.RunJobs();
        var popover = new PromptEditorPopover<Border>();
        popover.SetAnchor(anchor);
        var edit = runtime.OnMain(() => new PromptEdit(Guid.NewGuid(), "hello", ValueList<Attachment>.Empty));
        var dismissed = 0;
        runtime.OnMain(() => popover.Sync(edit, () => dismissed++, _ => new Border { Width = 400, Height = 100 }));
        Assert.Single(presenter.Calls);

        other.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        other.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, dismissed);
        runtime.OnMain(popover.Close);
        other.Close(); window.Close(); Dispatcher.UIThread.RunJobs();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-179")] // App/Views/Composer/PromptEditor.swift:88 the default SwiftUI .popover is transient: a click outside dismisses it
    public void The_add_files_popover_is_light_dismissed() => headless.OnUiThread(() =>
    {
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var editor = MakeEditor("Edit follow-up", "", []);
        var window = new Avalonia.Controls.Window { Width = 600, Height = 400, Content = editor };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var plus = FindAddFilesButton(editor);

        plus.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        var popup = Assert.Single(presenter.Calls).Popup;
        Assert.True(popup.IsLightDismissEnabled);

        window.Close();
        Dispatcher.UIThread.RunJobs();
    });

    // :218-230 the popover's content size is the hosted view's size, with its styles and templates in effect. The editor
    // measured detached left its buttons' templates and its attachment row out (161 pt for 177, and 175 for 241 with three
    // attachments), so the popup opened short until its first layout corrected it.
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [Trait("Req", "PR-COMPOSER-182")] // App/Views/Composer/PromptEditor.swift:218-230
    public void The_popup_opens_at_its_editor_measured_in_a_window(int attachmentCount) => headless.OnUiThread(() =>
    {
        var attachments = Enumerable.Range(0, attachmentCount)
            .Select(index => new Attachment { Id = Guid.NewGuid(), Name = "file" + index + ".png", Path = "C:/x/file" + index + ".png", MimeType = "image/png" })
            .ToArray();
        PromptEditor Editor() => MakeEditor("Edit message", "hello", attachments);

        var hosted = Editor();
        var hostWindow = new Avalonia.Controls.Window { Width = 700, Height = 600, Content = hosted };
        hostWindow.Show(); Dispatcher.UIThread.RunJobs();
        hosted.Measure(new Size(520, double.PositiveInfinity));
        var expected = hosted.DesiredSize;
        hostWindow.Close();

        using var runtime = new TestRuntime();
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var anchor = new Border { Width = 40, Height = 30, Background = Brushes.Transparent, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Avalonia.Controls.Window { Width = 600, Height = 300, Content = anchor };
        window.Show(); Dispatcher.UIThread.RunJobs();
        var popover = new PromptEditorPopover<PromptEditor>();
        popover.SetAnchor(anchor);
        var edit = runtime.OnMain(() => new PromptEdit(Guid.NewGuid(), "hello", ValueList<Attachment>.Empty));

        runtime.OnMain(() => popover.Sync(edit, () => { }, _ => Editor()));

        var popup = Assert.Single(presenter.Calls).Popup;
        var room = Marrow.UI.Port.PopoverHost.Room(popup); // OD-0095: the tipped card's room round the hosted view
        Assert.Equal(expected.Width + room.Width, popup.Width);
        Assert.Equal(expected.Height + room.Height, popup.Height);
        Assert.IsType<PromptEditor>(PopoverHost.Content(popup)); // measured out of the popup and put back, on the card the presenter hangs it on (OD-0095)
        runtime.OnMain(popover.Close);
        window.Close(); Dispatcher.UIThread.RunJobs();
    });
}
