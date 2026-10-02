using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/MessageEditor.swift (B3 52bc792), :27 restoresFiles, :56-62 the actions, :72-76 canSend,
// :156-171 send(), :173-175 cancel(). Every send runs against a throwaway git checkout the rig makes (MessageEditorRig).
[Collection("Headless")]
public sealed class MessageEditorSendTests(HeadlessSession headless)
{
    private const string Notice = "Sending replaces this message and everything after it."; // :48

    // :51-54 the resend error is the only Chrome.danger line; the box's own unavailable line uses secondaryText.
    private static string? ErrorLine(MessageEditorRig rig) =>
        rig.All<SwiftImage>().Where(image => image.SymbolName == "exclamationmark.triangle"
                && image.Foreground is Avalonia.Media.ISolidColorBrush brush && brush.Color == Chrome.Danger.Resolve(image.ActualThemeVariant))
            .Select(image => ((Panel)image.Parent!).Children.OfType<TextBlock>().Single().Text).SingleOrDefault();

    private static double Top(Control control, Visual relativeTo) => control.TranslatePoint(default, relativeTo)!.Value.Y;

    private static double Left(Control control, Visual relativeTo) => control.TranslatePoint(default, relativeTo)!.Value.X;

    // :27 @State private var restoresFiles = true; :159 restore = restoresFiles && !preview.files.isEmpty.
    [Fact]
    [Trait("Req", "PR-GIT-125")] // MessageEditor.swift:27, :159 the toggle starts on and the send passes it through when the preview found files
    [Trait("Req", "UI-GIT-056")] // MessageEditor.swift:59, :156-171 Send reverts to the message, restores the files as asked and sends the edit
    public void A_send_with_the_toggle_left_on_puts_the_files_back_and_sends_the_edited_text() => headless.OnUiThread(() =>
    {
        using var rig = new MessageEditorRig(EditorPreviewKind.Restorable);
        rig.WaitForPreview();
        Assert.Equal(Avalonia.Automation.Provider.ToggleState.On, rig.Switch().ToggleState); // :27 starts on
        Assert.Equal("after\n", rig.Read(rig.Files[0]));

        rig.Click(rig.ButtonNamed("Send"));
        rig.WaitForSendToEnd();

        Assert.Equal("before\n", rig.Read(rig.Files[0])); // the turn's file went back to the checkpoint
        Assert.Null(rig.Runtime.MessageEdit); // :165 the editor closes
        Assert.DoesNotContain(rig.Runtime.Turns, turn => turn.Id == rig.TurnId); // rewound to just before the message
        Assert.Equal("edit me", rig.FirstUserText()); // and the edited text went out from there
    });

    [Fact]
    [Trait("Req", "PR-GIT-125")] // MessageEditor.swift:102, :159 the switch's state is the value passed to the resend
    public void A_send_with_the_toggle_switched_off_keeps_the_files_but_still_rewinds_and_sends() => headless.OnUiThread(() =>
    {
        using var rig = new MessageEditorRig(EditorPreviewKind.Restorable);
        rig.WaitForPreview();
        rig.Switch().Toggle();
        MessageEditorRig.Drain();
        Assert.Equal(Avalonia.Automation.Provider.ToggleState.Off, rig.Switch().ToggleState);

        rig.Click(rig.ButtonNamed("Send"));
        rig.WaitForSendToEnd();

        Assert.Equal("after\n", rig.Read(rig.Files[0])); // restoration off: the working tree stays as it is
        Assert.Null(rig.Runtime.MessageEdit);
        Assert.DoesNotContain(rig.Runtime.Turns, turn => turn.Id == rig.TurnId);
        Assert.Equal("edit me", rig.FirstUserText());
    });

    // :27 @State: per editor, never persisted.
    [Fact]
    [Trait("Req", "PR-GIT-125")] // MessageEditor.swift:27 the toggle starts switched on for every editor and is not persisted
    public void A_new_editor_starts_with_the_toggle_on_even_after_another_one_was_switched_off() => headless.OnUiThread(() =>
    {
        using var rig = new MessageEditorRig(EditorPreviewKind.Restorable);
        rig.WaitForPreview();
        rig.Switch().Toggle();
        MessageEditorRig.Drain();
        Assert.Equal(Avalonia.Automation.Provider.ToggleState.Off, rig.Switch().ToggleState);

        using var second = new MessageEditorRig(EditorPreviewKind.Restorable);
        second.WaitForPreview();
        Assert.Equal(Avalonia.Automation.Provider.ToggleState.On, second.Switch().ToggleState);
    });

    // :159 case .found only: a failed preview leaves restore false, so a send goes ahead without asking for any file.
    [Fact]
    [Trait("Req", "PR-GIT-125")] // MessageEditor.swift:158-159 an unavailable preview always sends with restoration off
    public void A_send_after_a_failed_preview_asks_for_no_files_back() => headless.OnUiThread(() =>
    {
        using var rig = new MessageEditorRig(EditorPreviewKind.Unavailable);
        rig.WaitForPreview();

        rig.Click(rig.ButtonNamed("Send"));
        rig.WaitForSendToEnd();

        // With restoration on, :1852-1854 would have refused a turn that has no checkpoint and left the editor open.
        Assert.Null(rig.Runtime.MessageEdit);
        Assert.DoesNotContain(rig.Runtime.Turns, turn => turn.Id == rig.TurnId);
        Assert.Equal("edit me", rig.FirstUserText());
        Assert.Equal("after\n", rig.Read(rig.Files[0]));
    });

    // :159 !preview.files.isEmpty: an empty file list sends with restoration off, however the toggle would stand.
    [Fact]
    [Trait("Req", "PR-GIT-125")] // MessageEditor.swift:159 a preview with no files sends with restoration off
    public void A_send_when_the_turn_changed_no_files_asks_for_no_files_back() => headless.OnUiThread(() =>
    {
        using var rig = new MessageEditorRig(EditorPreviewKind.NoFiles);
        rig.WaitForPreview();
        rig.DeleteStartCheckpoint(); // a send that asked for the files back would now fail at :1855

        rig.Click(rig.ButtonNamed("Send"));
        rig.WaitForSendToEnd();

        Assert.Null(rig.Runtime.MessageEdit);
        Assert.Equal("edit me", rig.FirstUserText());
        Assert.Null(ErrorLine(rig));
    });

    // :159 files.isEmpty, not restorable.isEmpty: a preview whose files were all kept still sends with the toggle's value (on).
    [Fact]
    [Trait("Req", "PR-GIT-125")] // MessageEditor.swift:159 restore is the toggle's value when the preview found any file, kept ones included
    public void A_send_when_every_file_was_kept_still_passes_the_toggle_on() => headless.OnUiThread(() =>
    {
        using var rig = new MessageEditorRig(EditorPreviewKind.AllKept);
        rig.WaitForPreview();
        // the toggle is only rendered where restorable files exist (:100-113)
        Assert.DoesNotContain(rig.All<Control>(), control => Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(control) is Avalonia.Automation.Provider.IToggleProvider);
        rig.DeleteStartCheckpoint(); // a send that asks for the files back now fails at :1855

        rig.Click(rig.ButtonNamed("Send"));
        rig.WaitForSendToEnd();

        Assert.NotNull(rig.Runtime.MessageEdit); // :166-168 the failure leaves the editor open
        Assert.Contains(rig.Runtime.Turns, turn => turn.Id == rig.TurnId); // nothing was rewound
        Assert.NotNull(ErrorLine(rig));
    });

    // :51-54 and :161, :166-169: a failed send shows the error in Chrome.danger under the notice and the editor takes input again.
    [Fact]
    [Trait("Req", "UI-CHAT-015")] // MessageEditor.swift:51-54 the error line is Chrome.danger beside an exclamationmark.triangle, in callout
    public void A_failed_send_shows_the_error_in_the_danger_colour_and_leaves_the_editor_open() => headless.OnUiThread(() =>
    {
        using var rig = new MessageEditorRig(EditorPreviewKind.Restorable);
        rig.WaitForPreview();
        Assert.Null(ErrorLine(rig)); // nothing before a send
        rig.DeleteStartCheckpoint();

        rig.Click(rig.ButtonNamed("Send"));
        rig.WaitForSendToEnd();
        MessageEditorRig.Drain();

        var error = ErrorLine(rig);
        Assert.False(string.IsNullOrWhiteSpace(error));
        var text = Assert.Single(rig.All<TextBlock>(), block => block.Text == error);
        Assert.Equal(12, text.FontSize); // .font(.callout)
        Assert.Equal(Chrome.Danger.Resolve(text.ActualThemeVariant), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(text.Foreground).Color);
        var notice = Assert.Single(rig.All<TextBlock>(), block => block.Text == Notice);
        Assert.True(Top(text, rig.Window) > Top(notice, rig.Window)); // :48-55 after the notice
        Assert.True(rig.TakesInput); // :169 isSending is false again, so the editor takes input
        Assert.True(rig.ButtonNamed("Send").IsEnabled);
    });

    // :72-76 `if case .checking = changes { return false }`: until the file check is in, a send would silently keep the files.
    [Fact]
    [Trait("Req", "UI-GIT-056")] // MessageEditor.swift:74 Send is disabled while the file check is still running
    public void Send_is_disabled_and_does_nothing_while_the_file_check_is_running() => headless.OnUiThread(() =>
    {
        using var rig = new MessageEditorRig(EditorPreviewKind.Restorable, pumpOnShow: false);

        var send = rig.ButtonNamed("Send");
        Assert.False(send.IsEnabled);
        rig.Click(send); // a submit from the text field takes the same guard, :157
        Assert.NotNull(rig.Runtime.MessageEdit);
        Assert.Contains(rig.Runtime.Turns, turn => turn.Id == rig.TurnId);
        Assert.Equal("after\n", rig.Read(rig.Files[0]));

        rig.WaitForPreview();
        Assert.True(rig.ButtonNamed("Send").IsEnabled); // the answer is in
    });

    // :75 !edit.isEmpty
    [Fact]
    [Trait("Req", "UI-GIT-056")] // MessageEditor.swift:75 Send is disabled while the edit is empty
    public void Send_is_disabled_while_the_edit_is_empty() => headless.OnUiThread(() =>
    {
        using var rig = new MessageEditorRig(EditorPreviewKind.Restorable);
        rig.WaitForPreview();
        Assert.True(rig.ButtonNamed("Send").IsEnabled);

        rig.Edit.Text = " \n\t";
        MessageEditorRig.Drain();
        var send = rig.ButtonNamed("Send");
        Assert.False(send.IsEnabled);
        rig.Click(send);
        Assert.NotNull(rig.Runtime.MessageEdit);
        Assert.Equal("after\n", rig.Read(rig.Files[0]));

        rig.Edit.Text = "back again";
        MessageEditorRig.Drain();
        Assert.True(rig.ButtonNamed("Send").IsEnabled);
    });

    // :75 !runtime.isRunning
    [Fact]
    [Trait("Req", "UI-GIT-056")] // MessageEditor.swift:75 Send is disabled while the thread is running
    public void Send_is_disabled_while_the_thread_is_running() => headless.OnUiThread(() =>
    {
        using var rig = new MessageEditorRig(EditorPreviewKind.Restorable);
        rig.WaitForPreview();
        Assert.True(rig.ButtonNamed("Send").IsEnabled);

        rig.Runtime.RehearseTurn(null); // a turn is running
        MessageEditorRig.Drain();
        Assert.True(rig.Runtime.IsRunning);
        var send = rig.ButtonNamed("Send");
        Assert.False(send.IsEnabled);
        rig.Click(send);
        Assert.NotNull(rig.Runtime.MessageEdit);
        Assert.Equal("after\n", rig.Read(rig.Files[0]));
    });

    // :75 !isSending, :63 .disabled(isSending): from the press until the send ends, Send is off and the whole editor takes no input.
    [Fact]
    [Trait("Req", "UI-GIT-056")] // MessageEditor.swift:75 Send is disabled while a send is in flight
    [Trait("Req", "UI-CHAT-015")] // MessageEditor.swift:63 sending: the whole editor is disabled
    public void While_a_send_is_in_flight_Send_is_off_and_the_whole_editor_takes_no_input() => headless.OnUiThread(() =>
    {
        using var rig = new MessageEditorRig(EditorPreviewKind.Restorable);
        rig.WaitForPreview();
        Assert.True(rig.TakesInput);

        rig.Click(rig.ButtonNamed("Send"));
        Assert.False(rig.TakesInput); // the git work behind the send is still out: the editor is disabled
        Assert.False(rig.ButtonNamed("Send").IsEnabled);
        Assert.False(rig.ButtonNamed("Cancel").IsEffectivelyEnabled);
        Assert.NotEmpty(rig.All<TextBox>());
        Assert.All(rig.All<TextBox>(), box => Assert.False(box.IsEffectivelyEnabled));
        rig.Click(rig.ButtonNamed("Send")); // a second press is refused by the guard at :157 and sends nothing twice
        rig.WaitForSendToEnd();

        Assert.Null(rig.Runtime.MessageEdit);
        Assert.Equal(1, rig.Runtime.Entries.Count(entry => entry.Item.TimelineItemContent is TimelineItem.Content.User));
        Assert.Null(ErrorLine(rig));
    });

    // :56-62 Cancel first, then Send; Cancel .glass with the cancel role, Send .glassProminent.
    [Fact]
    [Trait("Req", "UI-GIT-056")] // MessageEditor.swift:56-62 the action row is Cancel then Send, Cancel glass, Send the prominent glass
    public void The_action_row_is_Cancel_then_Send_with_Send_in_the_prominent_glass() => headless.OnUiThread(() =>
    {
        using var rig = new MessageEditorRig(EditorPreviewKind.Restorable);
        rig.WaitForPreview();

        var cancel = rig.ButtonNamed("Cancel");
        var send = rig.ButtonNamed("Send");
        Assert.Same(cancel.Parent, send.Parent);
        Assert.True(Left(cancel, rig.Window) < Left(send, rig.Window));
        Assert.Equal(Top(cancel, rig.Window), Top(send, rig.Window), 0.5);
        Assert.True(cancel.IsCancel); // role: .cancel
        Assert.False(send.IsCancel);
        var cancelGlass = Assert.Single(LogicalQuery.SelfAndDescendants<Control>(cancel), control => SwiftGlass.GetEffect(control) is not null);
        var sendGlass = Assert.Single(LogicalQuery.SelfAndDescendants<Control>(send), control => SwiftGlass.GetEffect(control) is not null);
        Assert.Null(SwiftGlass.GetEffect(cancelGlass)!.TintColor); // .glass
        Assert.NotNull(SwiftGlass.GetEffect(sendGlass)!.TintColor); // .glassProminent
    });

    // :173-175 cancel(): runtime.messageEdit = nil and nothing else.
    [Fact]
    [Trait("Req", "UI-GIT-056")] // MessageEditor.swift:57, :173-175 Cancel closes the editor and discards the edit, touching nothing
    [Trait("Req", "UI-CHAT-015")] // MessageEditor.swift:173-175 Cancel closes without touching the thread
    public void Cancel_closes_the_editor_and_leaves_the_thread_and_files_as_they_were() => headless.OnUiThread(() =>
    {
        using var rig = new MessageEditorRig(EditorPreviewKind.Restorable);
        rig.WaitForPreview();
        rig.Edit.Text = "changed my mind";

        rig.Click(rig.ButtonNamed("Cancel"));

        Assert.Null(rig.Runtime.MessageEdit);
        Assert.Equal("after\n", rig.Read(rig.Files[0]));
        Assert.Equal(rig.TurnId, Assert.Single(rig.Runtime.Turns).Id);
        Assert.Equal("original message", rig.FirstUserText());
        Assert.True(rig.Runtime.Draft.IsEmpty);
    });

    // :57 Button("Cancel", role: .cancel): the cancel role is what Escape presses.
    [Fact]
    [Trait("Req", "UI-GIT-056")] // MessageEditor.swift:57 Cancel carries the cancel role, so Escape closes the editor
    public void Escape_presses_Cancel() => headless.OnUiThread(() =>
    {
        using var rig = new MessageEditorRig(EditorPreviewKind.Restorable);
        rig.WaitForPreview();

        rig.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        MessageEditorRig.Drain();

        Assert.Null(rig.Runtime.MessageEdit);
        Assert.Equal(rig.TurnId, Assert.Single(rig.Runtime.Turns).Id);
        Assert.Equal("after\n", rig.Read(rig.Files[0]));
    });
}
