using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Services.Git;
using Marrow.Testing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Marrow.App.Views.Chat;
using Marrow.Core.Port;
using Marrow.Core.Models;
using Marrow.UI.Port;
using System.Reflection;

namespace Marrow.App.Tests.Views.Chat;

// RESOLVED App.Views.Chat.MessageEditor #1: the merged ThreadRuntimePart12Harness now provides
// an offline AppModel/runtime; review tests below construct and exercise the actual editor.
// Historical limitation:
// Kind: missing dependency
// Swift: DroppyCode/App/Views/Chat/MessageEditor.swift:31-69 (B2 1fbf81e)
// Requirements: PR-CHAT-083, PR-CHAT-084, PR-CHAT-085, PR-CHAT-086, PR-GIT-124, PR-GIT-125, UI-CHAT-015, UI-CHAT-016, UI-CHAT-017, UI-CHAT-018, UI-CHAT-065, UI-CHAT-066, UI-CHAT-067, UI-CHAT-068, UI-GIT-052, UI-GIT-053, UI-GIT-054, UI-GIT-055, UI-GIT-056
// What is unclear or missing: The editor requires a ThreadRuntime and its preview state, but the provided declarations do not provide a safe way to construct a runtime for these tests: its constructor requires AppModel, whose construction API and test fixture are not supplied. The provided headless fixture only supplies a UI session and presenter seam. Constructing a runtime with null or bypassing its constructor would not test a valid Swift-shaped runtime. Therefore these tests cover only the independently constructible edit and preview values; they do not claim to verify the editor UI or resend behavior.
// Needed to proceed: A safe test construction path for ThreadRuntime and AppModel.
// Done so far: Tests for the edit's empty predicate and preview restorable-file statistics; no MessageEditor UI tests written.

[Collection("Headless")]
public sealed class MessageEditorTests(HeadlessSession headless)
{
    private static readonly BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private static object State() => Activator.CreateInstance(typeof(MessageEditor).GetNestedType("ViewState", BindingFlags.NonPublic)!, nonPublic: true)!;
    private static void SetState(object state, string name, object value) => state.GetType().GetProperty(name)!.SetValue(state, value);
    private static object Changes(string name, params object[] arguments) => Activator.CreateInstance(
        typeof(MessageEditor).GetNestedType("Changes", BindingFlags.NonPublic)!.GetNestedType(name)!, arguments)!;
    private static Control FileChanges(object state) => (Control)typeof(MessageEditor).GetMethod("BuildFileChanges", PrivateStatic)!.Invoke(null, [state])!;
    // The restore toggle's switch (:102-113): the one control whose automation peer toggles.
    private static Control SwitchControl(Control view) => Assert.Single(LogicalQuery.SelfAndDescendants<Control>(view),
        control => Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(control) is Avalonia.Automation.Provider.IToggleProvider);
    private static Avalonia.Automation.Provider.IToggleProvider RestoreSwitch(Control view) =>
        (Avalonia.Automation.Provider.IToggleProvider)Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(SwitchControl(view));
    private static string[] Texts(Control view) => LogicalQuery.SelfAndDescendants<TextBlock>(view).Select(t => t.Text ?? "").ToArray();
    private static Button NamedButton(Control view, string title) => Assert.Single(LogicalQuery.SelfAndDescendants<Button>(view),
        b => Equals(b.Content, title) || Texts(b).Contains(title));

    [Fact]
    [Trait("Req", "UI-CHAT-017")] // App/Views/Chat/MessageEditor.swift:57-63,72-76
    [Trait("Req", "UI-CHAT-018")] // App/Views/Chat/MessageEditor.swift:57-61
    public void Review_real_editor_has_glass_actions_blocks_checking_and_cancel_closes_the_edit() => headless.OnUiThread(() =>
    {
        System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        using var h = new ThreadRuntimePart12Harness();
        h.Main.OnMain(() =>
        {
            var edit = new PromptEdit(Guid.NewGuid(), "hello", ValueList<Attachment>.Empty);
            h.Runtime.MessageEdit = edit;
            var editor = new MessageEditor(edit, h.Runtime);
            var window = new Avalonia.Controls.Window { Content = editor };
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var send = NamedButton(editor, "Send");
            var cancel = NamedButton(editor, "Cancel");
            Assert.False(send.IsEnabled);
            Assert.Contains("Checking for file changes\u2026", Texts(editor));
            foreach (var button in new[] { send, cancel })
                Assert.Contains(LogicalQuery.SelfAndDescendants<Control>(button), c => SwiftGlass.GetEffect(c) is { IsInteractive: true, Shape.Kind: GlassShapeKind.Capsule });
            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Null(h.Runtime.MessageEdit);
            window.Close();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-084")] // App/Views/Chat/MessageEditor.swift:78-114
    [Trait("Req", "PR-CHAT-086")] // App/Views/Chat/MessageEditor.swift:72-76
    public void Review_actual_state_branches_toggle_and_send_conditions() => headless.OnUiThread(() =>
    {
        System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        using var h = new ThreadRuntimePart12Harness();
        h.Main.OnMain(() =>
        {
            var state = State();
            var edit = new PromptEdit(Guid.NewGuid(), "hello", ValueList<Attachment>.Empty);
            var canSend = typeof(MessageEditor).GetMethod("CanSend", PrivateStatic)!;
            bool Allowed() => (bool)canSend.Invoke(null, [edit, h.Runtime, state])!;
            Assert.False(Allowed());
            SetState(state, "Changes", Changes("Unavailable", "Preview unavailable"));
            Assert.Contains("Preview unavailable", Texts(FileChanges(state)));
            Assert.True(Allowed());
            edit.Text = " "; Assert.False(Allowed()); edit.Text = "hello";
            SetState(state, "IsSending", true); Assert.False(Allowed()); SetState(state, "IsSending", false);
            var preview = new RevertPreview(new ValueList<RevertPreview.File>([new("one.txt", 3, 2, false), new("kept.txt", 7, 5, false, true)]));
            SetState(state, "Changes", Changes("Found", preview));
            var changes = FileChanges(state);
            Assert.Contains("1 file", Texts(changes));
            Assert.Contains("Undo the file changes this thread made", Texts(changes));
            var toggle = RestoreSwitch(changes);
            Assert.Equal(Avalonia.Automation.Provider.ToggleState.On, toggle.ToggleState);
            toggle.Toggle();
            Assert.False((bool)state.GetType().GetProperty("RestoresFiles")!.GetValue(state)!);
            SetState(state, "Changes", Changes("Found", new RevertPreview(ValueList<RevertPreview.File>.Empty)));
            Assert.Contains("This thread changed no files since this message", Texts(FileChanges(state)));
            SetState(state, "Changes", Changes("Found", new RevertPreview(new ValueList<RevertPreview.File>([new("kept.txt", 0, 0, false, true)]))));
            Assert.Contains("The file it changed has changed again since, so it stays as it is", Texts(FileChanges(state)));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        });
    });

    [Fact]
    [Trait("Req", "UI-GIT-055")] // App/Views/Chat/MessageEditor.swift:128-145 spacing 8 + spacer 4 + spacing 8
    public void Review_long_file_path_keeps_twenty_points_before_the_trailing_value() => headless.OnUiThread(() =>
    {
        var files = new ValueList<RevertPreview.File>([new(new string('a', 150) + "/file.bin", 0, 0, true)]);
        var rows = (Control)typeof(MessageEditor).GetMethod("BuildFileList", PrivateStatic)!.Invoke(null, [files, true])!;
        rows.Measure(new Size(300, double.PositiveInfinity));
        rows.Arrange(new Rect(0, 0, 300, rows.DesiredSize.Height));
        var row = Assert.Single(LogicalQuery.SelfAndDescendants<Grid>(rows));
        var path = row.Children[1];
        var trailing = row.Children[2];
        Assert.Equal(20, trailing.Bounds.Left - path.Bounds.Right, 1);
        Assert.Equal(1, row.Opacity);
    });

    [Fact]
    [Trait("Req", "UI-CHAT-017")] // App/Views/Chat/MessageEditor.swift:72-76
    [Trait("Req", "PR-CHAT-086")] // App/Views/Chat/MessageEditor.swift:72-76
    [Trait("Req", "PR-GIT-124")] // App/Views/Chat/MessageEditor.swift:72-76
    public void An_edit_without_attachments_or_non_whitespace_text_is_empty()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();

            var empty = new PromptEdit(Guid.NewGuid(), " \t\r\n", Marrow.Core.Port.ValueList<Marrow.Core.Models.Attachment>.Empty);
            var nonEmpty = new PromptEdit(Guid.NewGuid(), "x", Marrow.Core.Port.ValueList<Marrow.Core.Models.Attachment>.Empty);

            Assert.True(empty.IsEmpty);
            Assert.False(nonEmpty.IsEmpty);
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-084")] // App/Views/Chat/MessageEditor.swift:100-114
    [Trait("Req", "UI-CHAT-016")] // App/Views/Chat/MessageEditor.swift:100-114
    [Trait("Req", "UI-GIT-054")] // App/Views/Chat/MessageEditor.swift:100-114
    public void Preview_statistics_and_restorable_count_exclude_kept_files()
    {
        var preview = new RevertPreview(new Marrow.Core.Port.ValueList<RevertPreview.File>(
        [
            new RevertPreview.File("one.txt", 3, 2, IsBinary: false),
            new RevertPreview.File("kept.txt", 7, 5, IsBinary: false, IsKept: true),
            new RevertPreview.File("binary.dat", 0, 0, IsBinary: true),
        ]));

        Assert.Equal(2, preview.Restorable.Count);
        Assert.Equal(3, preview.Additions);
        Assert.Equal(2, preview.Deletions);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-085")] // App/Views/Chat/MessageEditor.swift:125-154
    [Trait("Req", "UI-CHAT-065")] // App/Views/Chat/MessageEditor.swift:125-146
    [Trait("Req", "UI-GIT-055")] // App/Views/Chat/MessageEditor.swift:125-154
    public void Preview_keeps_each_file_and_its_kept_state_in_the_list()
    {
        var preview = new RevertPreview(new Marrow.Core.Port.ValueList<RevertPreview.File>(
        [
            new RevertPreview.File("src/changed.cs", 4, 1, IsBinary: false),
            new RevertPreview.File("src/kept.cs", 2, 3, IsBinary: false, IsKept: true),
        ]));

        Assert.Equal(2, preview.Files.Count);
        Assert.Equal("src/changed.cs", preview.Files[0].Path);
        Assert.False(preview.Files[0].IsKept);
        Assert.Equal("src/kept.cs", preview.Files[1].Path);
        Assert.True(preview.Files[1].IsKept);
    }

    [Fact]
    [Trait("Req", "UI-GIT-052")] // App/Views/Chat/MessageEditor.swift:46-53,85-89 the notice and the unavailable Label have no line limit: they wrap
    public void Long_unavailable_and_notice_text_wraps_inside_the_box_instead_of_overflowing() => headless.OnUiThread(() =>
    {
        var state = State();
        var reason = string.Join(" ", Enumerable.Repeat("The preview could not be produced", 8));
        SetState(state, "Changes", Changes("Unavailable", reason));
        var box = FileChanges(state);
        box.Measure(new Size(240, double.PositiveInfinity));
        box.Arrange(new Rect(0, 0, 240, box.DesiredSize.Height));
        var text = LogicalQuery.SelfAndDescendants<TextBlock>(box).Single(block => block.Text == reason);
        Assert.Equal(Avalonia.Media.TextWrapping.Wrap, text.TextWrapping);
        Assert.True(text.Bounds.Right <= 240, "the text overflows the box: " + text.Bounds);
    });

    [Fact]
    [Trait("Req", "UI-GIT-054")] // App/Views/Chat/MessageEditor.swift:102-113 Toggle(isOn: $restoresFiles) { ... }.toggleStyle(.switch).controlSize(.small)
    public void The_restore_toggle_can_be_shown_and_switched_in_a_window() => headless.OnUiThread(() =>
    {
        // The app carries no theme package: Avalonia's ToggleSwitch looks for its Fluent template's PART_MovingKnobs
        // and throws as soon as it is laid out, so the editor's file section could not appear at all.
        var state = State();
        SetState(state, "Changes", Changes("Found", new RevertPreview(new ValueList<RevertPreview.File>([new("one.txt", 3, 2, false)]))));
        var changes = FileChanges(state);
        var window = new Avalonia.Controls.Window { Content = changes, Width = 420, Height = 300 };
        window.Show();
        try
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var toggle = changes.GetVisualDescendants().OfType<Control>().Single(control => Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(control) is Avalonia.Automation.Provider.IToggleProvider);
            var provider = (Avalonia.Automation.Provider.IToggleProvider)Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(toggle);
            Assert.True(toggle.Bounds.Width > 0 && toggle.Bounds.Height > 0, "the switch is not drawn: " + toggle.Bounds);
            Assert.Equal(Avalonia.Automation.Provider.ToggleState.On, provider.ToggleState); // :27 restoresFiles starts true
            provider.Toggle();
            Assert.False((bool)state.GetType().GetProperty("RestoresFiles")!.GetValue(state)!);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "UI-GIT-054")] // App/Views/Chat/MessageEditor.swift:100-109 HStack(spacing: 8) { title; Spacer(minLength: 8); count; stat }: 24 points minimum before the count
    public void The_restore_toggle_keeps_twenty_four_points_between_its_title_and_the_count() => headless.OnUiThread(() =>
    {
        var state = State();
        var preview = new RevertPreview(new ValueList<RevertPreview.File>([new("one.txt", 3, 2, false)]));
        SetState(state, "Changes", Changes("Found", preview));
        var changes = FileChanges(state);
        RestoreSwitch(changes);
        var label = Assert.IsType<Grid>(((Grid)SwitchControl(changes).Parent!).Children[0]);
        label.Measure(new Size(260, double.PositiveInfinity));
        label.Arrange(new Rect(0, 0, 260, label.DesiredSize.Height));
        var title = label.Children[0];
        var count = label.Children[1];
        Assert.Equal(24, count.Bounds.Left - title.Bounds.Right, 1);
    });
}
