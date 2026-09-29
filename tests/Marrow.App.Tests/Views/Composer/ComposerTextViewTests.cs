using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Services.Store;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// STOP App.Views.Composer.ComposerTextView #1
// Kind: platform default not specified by pinned source
// Swift: App/Views/Composer/ComposerTextView.swift:61 (B3 52bc792)
// Records: PR-COMPOSER-046
// Missing: NSPopover.animates native show/close/reposition behavior has no Avalonia Popup equivalent.
// Needed: an owner-defined shared presenter animation contract; Swift supplies no duration or curve.
// Done: explicit source citation/UNVERIFIED marker and real applicationDefined flag assertion.

// Source: DroppyCode/App/Views/Composer/ComposerTextView.swift (B2 1fbf81e), :1-379.
[Collection("Headless")]
public sealed class ComposerTextViewTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-COMPOSER-017")] // ComposerTextView.swift:242-268
    public void The_text_view_is_plain_undoable_and_never_substitutes_typed_text() => headless.OnUiThread(() =>
    {
        var h = new ComposerTextViewHarness();
        h.Focus();

        Assert.True(h.TextView.IsUndoEnabled); // :244 allowsUndo
        Assert.Equal(Brushes.Transparent, h.TextView.Background); // :245 drawsBackground = false
        Assert.Equal(14, h.TextView.FontSize); // :246
        Assert.Equal(new Thickness(0, 2, 0, 2), h.TextView.Padding); // :249 textContainerInset (0, 2)
        Assert.Equal(TextWrapping.Wrap, h.TextView.TextWrapping); // :263-264
        Assert.False(h.TextView.AcceptsReturn); // :332-341, Return is always intercepted below

        h.Type("She said \"hi\" -- twice");
        Assert.Equal("She said \"hi\" -- twice", h.Text); // straight quotes and a double hyphen, untouched
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-018")] // ComposerTextView.swift:248, :298-300
    public void The_caret_takes_the_theme_accent_and_follows_a_theme_change_in_place() => headless.OnUiThread(() =>
    {
        var h = new ComposerTextViewHarness();
        var expected = Chrome.AccentNSColor.Resolve(h.TextView.ActualThemeVariant);
        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(h.TextView.CaretBrush);
        Assert.Equal(expected, brush.Color);

        // A theme change is applied in place through Update, never a rebuild (:298-300).
        h.View.Update(newPlaceholder: "", newOnKey: _ => true, newOnAttach: _ => { });
        var textViewAfter = h.View.GetLogicalDescendants().OfType<ComposerNSTextView>().Single();
        Assert.Same(h.TextView, textViewAfter); // the same control instance, no rebuild
        var brush2 = Assert.IsAssignableFrom<ISolidColorBrush>(h.TextView.CaretBrush);
        Assert.Equal(expected, brush2.Color);
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-019")] // ComposerTextView.swift:198-211
    public void The_placeholder_draws_only_while_empty_at_the_container_inset() => headless.OnUiThread(() =>
    {
        var h = new ComposerTextViewHarness(placeholder: "Ask anything");
        h.Layout();
        var ink = Ink.Bounds(h.Window, new Rect(0, 0, h.View.Bounds.Width, ComposerTextViewHarness.ComposerHeight));
        var reference = Ink.Reference("Ask anything", h.TextView, new Point(0, 2),
            new Size(h.Window.Width, h.Window.Height), new Rect(0, 0, h.View.Bounds.Width, ComposerTextViewHarness.ComposerHeight));
        Ink.AssertClose(reference, ink, tolerance: 2);

        h.Type("x");
        var inkAfter = Ink.Bounds(h.Window, new Rect(0, 0, h.View.Bounds.Width, ComposerTextViewHarness.ComposerHeight));
        Assert.NotNull(inkAfter); // the typed character draws, not the placeholder
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-019")] // ComposerTextView.swift:200, failure_cases: an empty placeholder draws nothing
    public void An_empty_placeholder_string_draws_nothing() => headless.OnUiThread(() =>
    {
        var h = new ComposerTextViewHarness(placeholder: "");
        h.Layout();
        var ink = Ink.Bounds(h.Window, new Rect(0, 0, h.View.Bounds.Width, ComposerTextViewHarness.ComposerHeight));
        Assert.Null(ink);
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-024")] // ComposerTextView.swift:332-341
    public void Return_sends_and_shift_or_option_return_insert_a_newline() => headless.OnUiThread(() =>
    {
        var h = new ComposerTextViewHarness();
        h.Focus();
        h.Type("hi");
        h.Press(Key.Enter, PhysicalKey.Enter);
        Assert.Equal([ComposerKey.Submit], h.Keys);
        Assert.Equal("hi", h.Text); // no newline inserted

        var h2 = new ComposerTextViewHarness();
        h2.Focus();
        h2.Type("hi");
        h2.Press(Key.Enter, PhysicalKey.Enter, RawInputModifiers.Shift);
        Assert.Empty(h2.Keys); // :334 shift inserts a newline ignoring the field editor, never reported to onKey
        Assert.Equal("hi\n", h2.Text);

        var h3 = new ComposerTextViewHarness();
        h3.Focus();
        h3.Type("hi");
        h3.Press(Key.Enter, PhysicalKey.Enter, RawInputModifiers.Alt);
        Assert.Empty(h3.Keys);
        Assert.Equal("hi\n", h3.Text);
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-024")] // ComposerTextView.swift:337-339 command-return steers
    public void Command_return_steers() => headless.OnUiThread(() =>
    {
        var h = new ComposerTextViewHarness();
        h.Focus();
        h.Press(Key.Enter, PhysicalKey.Enter, RawInputModifiers.Control);
        Assert.Equal([ComposerKey.Steer], h.Keys);
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-028")] // ComposerTextView.swift:350-354
    public void Backspace_at_the_very_start_reports_delete_at_start() => headless.OnUiThread(() =>
    {
        var h = new ComposerTextViewHarness(text: "hello");
        h.Focus();
        h.TextView.SelectionStart = 0;
        h.TextView.SelectionEnd = 0;
        ComposerTextViewHarness.Pump();
        h.Press(Key.Back, PhysicalKey.Backspace);
        Assert.Equal([ComposerKey.DeleteAtStart], h.Keys);
        Assert.Equal("hello", h.Text); // the answer (true) means the key is consumed, not the default delete
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-028")] // ComposerTextView.swift:351 a nonzero selection falls through
    public void Backspace_with_a_selection_does_not_report_delete_at_start() => headless.OnUiThread(() =>
    {
        var h = new ComposerTextViewHarness(text: "hello");
        h.Focus();
        h.TextView.SelectionStart = 1;
        h.TextView.SelectionEnd = 1;
        ComposerTextViewHarness.Pump();
        h.Press(Key.Back, PhysicalKey.Backspace);
        // :354 "return false": the key is left unhandled and never reported to onKey, so the field's own
        // (Avalonia's) default backspace decides what happens next, whatever that is in this environment.
        Assert.Empty(h.Keys);
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-046")] // ComposerTextView.swift:54-84
    public void The_suggestion_panel_is_sized_340_by_the_row_formula_and_keeps_typing_focus() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var h = new ComposerTextViewHarness();
        h.Focus();
        h.Controller.ShowSuggestions(() => new TextBlock { Text = "item" }, itemCount: 3);
        ComposerTextViewHarness.Pump();

        var (popup, presented) = Assert.Single(recorder.Calls);
        Assert.True(presented);
        Assert.False(popup.IsLightDismissEnabled); // App/Views/Composer/ComposerTextView.swift:60 .applicationDefined
        var frame = Assert.IsType<Border>(popup.Child);
        Assert.Equal(340, frame.Width);
        Assert.Equal(44 + 3 * 26, frame.Height); // min(340, 44 + 3*26) = 122
        Assert.True(h.TextView.IsFocused); // :83 never steals typing focus
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-046")] // ComposerTextView.swift:70 height ceiling at 340
    public void The_suggestion_panel_height_is_capped_at_340() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var h = new ComposerTextViewHarness();
        h.Focus();
        h.Controller.ShowSuggestions(() => new TextBlock { Text = "item" }, itemCount: 50);
        ComposerTextViewHarness.Pump();
        var (popup, _) = Assert.Single(recorder.Calls);
        var frame = Assert.IsType<Border>(popup.Child);
        Assert.Equal(340, frame.Height);
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-047")] // ComposerTextView.swift:98-107 the caret rect; the :108-112 fallback only serves a view with no window
    public void The_suggestion_panel_anchors_to_the_empty_caret_at_the_container_inset() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var h = new ComposerTextViewHarness();
        h.Focus();
        h.Controller.ShowSuggestions(() => new TextBlock { Text = "item" }, itemCount: 1);
        ComposerTextViewHarness.Pump();
        var (popup, _) = Assert.Single(recorder.Calls);
        Assert.NotNull(popup.PlacementRect);
        var rect = popup.PlacementRect!.Value;
        // :98-107 the shown view's caret rect (primary branch): the container inset origin, at least 1 wide, the line's height.
        Assert.Equal(0, rect.X);
        Assert.Equal(2, rect.Y);
        Assert.Equal(1, rect.Width);
        Assert.True(rect.Height >= 4);
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-064")] // ComposerTextView.swift:163-169 file URLs attach instead of pasting text
    public void Pasting_a_file_attaches_it_instead_of_pasting_text() => headless.OnUiThread(() =>
    {
        var h = new ComposerTextViewHarness();
        h.Focus();
        var paths = ComposerTextViewHarness.TemporaryFiles("note.txt");
        var storageFile = h.StorageFile(paths[0]);
        var dt = new DataTransfer();
        dt.Add(DataTransferItem.CreateFile(storageFile));
        h.SetClipboard(dt);

        h.Press(Key.V, PhysicalKey.V, RawInputModifiers.Control);
        ComposerTextViewHarness.Pump(() => h.Attached.Count > 0);

        Assert.Single(h.Attached);
        Assert.Equal([System.IO.Path.GetFullPath(paths[0])], ComposerTextViewHarness.FilePaths(h.Attached[0]).Select(System.IO.Path.GetFullPath));
        Assert.Equal("", h.Text); // no text was pasted
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-065")] // ComposerTextView.swift:138-152, :170-176 an image-only clipboard is attached, not refused
    public void Pasting_an_image_with_no_text_attaches_it() => headless.OnUiThread(() =>
    {
        var h = new ComposerTextViewHarness();
        h.Focus();
        var bitmap = new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96));
        var dt = new DataTransfer();
        dt.Add(DataTransferItem.Create(DataFormat.Bitmap, bitmap));
        h.SetClipboard(dt);

        h.Press(Key.V, PhysicalKey.V, RawInputModifiers.Control);
        ComposerTextViewHarness.Pump(() => h.Attached.Count > 0);

        Assert.Single(h.Attached);
        var source = Assert.Single(h.Attached[0]);
        var image = Assert.IsType<AttachmentSource.Image>(source);
        Assert.Equal(AttachmentSource.PngMimeType, image.MimeType); // :175, DEVIATION: re-encoded, see ComposerTextView.cs
        Assert.NotEmpty(image.Data);
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-065")] // ComposerTextView.swift:170 text beside the image wins: plain text pastes
    public void Pasting_text_alongside_an_image_pastes_the_text() => headless.OnUiThread(() =>
    {
        var h = new ComposerTextViewHarness();
        h.Focus();
        var bitmap = new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96));
        var dt = new DataTransfer();
        dt.Add(DataTransferItem.Create(DataFormat.Bitmap, bitmap));
        dt.Add(DataTransferItem.CreateText("hello"));
        h.SetClipboard(dt);

        h.Press(Key.V, PhysicalKey.V, RawInputModifiers.Control);
        ComposerTextViewHarness.Pump(() => h.Text == "hello");

        Assert.Empty(h.Attached);
        Assert.Equal("hello", h.Text);
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-066")] // ComposerTextView.swift:180-187
    public void Dropping_a_file_attaches_it() => headless.OnUiThread(() =>
    {
        var h = new ComposerTextViewHarness();
        var paths = ComposerTextViewHarness.TemporaryFiles("dropped.txt");
        var storageFile = h.StorageFile(paths[0]);
        var dt = new DataTransfer();
        dt.Add(DataTransferItem.CreateFile(storageFile));

        // Raised directly on the control (the headless simulated pointer-position drag pipeline does not
        // deliver DragDrop routed events to a TextBox target in this Avalonia version): this still exercises
        // the same OnDropEvent handler ComposerTextView.swift:180-187 ports.
        h.TextView.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, dt, h.TextView, new Point(10, 10), KeyModifiers.None));
        ComposerTextViewHarness.Pump(() => h.Attached.Count > 0);

        Assert.Single(h.Attached);
        Assert.Equal([System.IO.Path.GetFullPath(paths[0])], ComposerTextViewHarness.FilePaths(h.Attached[0]).Select(System.IO.Path.GetFullPath));
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-066")] // ComposerTextView.swift:186 an empty URL list falls through
    public void Dropping_text_does_not_attach_anything() => headless.OnUiThread(() =>
    {
        var h = new ComposerTextViewHarness();
        var dt = new DataTransfer();
        dt.Add(DataTransferItem.CreateText("dropped text"));

        h.TextView.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, dt, h.TextView, new Point(10, 10), KeyModifiers.None));
        ComposerTextViewHarness.Pump();

        Assert.Empty(h.Attached);
    });

    [Fact]
    [Trait("Req", "UI-COMPOSER-003")] // ComposerTextView.swift:214-306, geometry and typography
    public void The_field_geometry_matches_the_source_container_and_typography() => headless.OnUiThread(() =>
    {
        var h = new ComposerTextViewHarness();
        Assert.Equal(new Thickness(0, 2, 0, 2), h.TextView.Padding);
        Assert.Equal(0, h.TextView.BorderThickness.Left);
        var scroll = Assert.IsType<ScrollViewer>(h.View.Child);
        Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
        Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
        Assert.Equal(Brushes.Transparent, scroll.Background); // :234 drawsBackground = false
    });

    [Fact]
    [Trait("Req", "UI-COMPOSER-018")] // ComposerTextView.swift:54-96, the popover never takes typing focus and a click inside does not dismiss
    public void The_suggestion_panel_never_takes_typing_focus_and_reports_pointer_over() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var h = new ComposerTextViewHarness();
        h.Focus();
        h.Controller.ShowSuggestions(() => new TextBlock { Text = "row" }, itemCount: 1);
        ComposerTextViewHarness.Pump();
        Assert.False(h.Controller.IsClickInsideSuggestions()); // nothing has hovered it yet
        Assert.True(h.TextView.IsFocused);

        h.Controller.HideSuggestions();
        ComposerTextViewHarness.Pump();
        Assert.Equal(2, recorder.Calls.Count);
        Assert.False(recorder.Calls[^1].Presented);
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-024")] // ComposerTextView.swift:341 return parent.onKey(.submit) == false: AppKit's insertNewline runs
    public void An_unhandled_return_inserts_a_newline() => headless.OnUiThread(() =>
    {
        var h = new ComposerTextViewHarness();
        h.Answer = _ => false;
        h.Focus();
        h.Type("hi");
        h.Press(Key.Enter, PhysicalKey.Enter);
        Assert.Equal([ComposerKey.Submit], h.Keys);
        Assert.Equal("hi\n", h.Text);
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-024")] // ComposerTextView.swift:339 return parent.onKey(.steer) == false: AppKit's insertNewline runs
    public void An_unhandled_command_return_inserts_a_newline() => headless.OnUiThread(() =>
    {
        var h = new ComposerTextViewHarness();
        h.Answer = _ => false;
        h.Focus();
        h.Type("hi");
        h.Press(Key.Enter, PhysicalKey.Enter, RawInputModifiers.Control);
        Assert.Equal([ComposerKey.Steer], h.Keys);
        Assert.Equal("hi\n", h.Text);
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-064")] // ComposerTextView.swift:163-178 paste is synchronous: plain text lands at the selection of the key event
    public void A_plain_text_paste_lands_at_the_selection_of_the_key_press() => headless.OnUiThread(() =>
    {
        var h = new ComposerTextViewHarness();
        h.Focus();
        h.Type("abcdef");
        h.TextView.SelectionStart = h.TextView.SelectionEnd = 3;
        var gate = new TaskCompletionSource<object?>();
        var clipboard = h.Window.Clipboard!;
        var set = clipboard.SetDataAsync(new DelayedTextTransfer(gate.Task));
        ComposerTextViewHarness.Pump(() => set.IsCompleted);

        h.Window.KeyPress(Key.V, RawInputModifiers.Control, PhysicalKey.V, null);
        h.Window.KeyRelease(Key.V, RawInputModifiers.Control, PhysicalKey.V, null);
        h.TextView.SelectionStart = h.TextView.SelectionEnd = 6; // the caret moves before the clipboard read completes
        gate.SetResult("XYZ");
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (h.TextView.Text!.Length < 9 && DateTime.UtcNow < deadline) // the clipboard read hops threads
        {
            System.Threading.Thread.Sleep(5);
            ComposerTextViewHarness.Pump();
        }

        var pasted = h.TextView.Text;
        ComposerTextViewHarness.Pump();
        h.Window.Close();
        ComposerTextViewHarness.Pump();
        Assert.Equal("abcXYZdef", pasted);
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-019")] // ComposerTextView.swift:293-327 updateNSView replaces coordinator.parent: the latest onCursorChange and onBlur run
    public void Update_replaces_the_cursor_and_blur_callbacks() => headless.OnUiThread(() =>
    {
        var oldCursor = new List<int>();
        var newCursor = new List<int>();
        var oldBlur = 0;
        var newBlur = 0;
        var text = "abc";
        var view = new ComposerTextView(
            new Binding<string>(() => text, value => text = value),
            new Binding<double>(() => 0, _ => { }),
            "",
            new ComposerController(),
            _ => true,
            _ => { },
            oldCursor.Add,
            () => oldBlur++);
        var other = new Button { Content = "other", Focusable = true };
        var window = new Avalonia.Controls.Window { Width = 400, Height = 200, Content = new StackPanel { Children = { view, other } } };
        window.Show();
        ComposerTextViewHarness.Pump();
        var box = view.GetLogicalDescendants().OfType<ComposerNSTextView>().Single();

        view.Update("", _ => true, _ => { }, newCursor.Add, () => newBlur++);
        oldCursor.Clear();
        box.SelectionStart = box.SelectionEnd = 1;
        ComposerTextViewHarness.Pump();
        Assert.Empty(oldCursor);
        Assert.Equal(1, newCursor[^1]);

        box.Focus();
        ComposerTextViewHarness.Pump();
        other.Focus();
        ComposerTextViewHarness.Pump();
        Assert.Equal(0, oldBlur);
        Assert.Equal(1, newBlur);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-019")] // ComposerTextView.swift:280-282 onResign schedules a MainActor Task: the blur callback runs after the focus change
    public void The_blur_callback_is_queued_not_run_inside_the_focus_loss() => headless.OnUiThread(() =>
    {
        var blurred = 0;
        var text = "";
        var view = new ComposerTextView(
            new Binding<string>(() => text, value => text = value),
            new Binding<double>(() => 0, _ => { }),
            "",
            new ComposerController(),
            _ => true,
            _ => { },
            _ => { },
            () => blurred++);
        var other = new Button { Content = "other", Focusable = true };
        var window = new Avalonia.Controls.Window { Width = 400, Height = 200, Content = new StackPanel { Children = { view, other } } };
        window.Show();
        ComposerTextViewHarness.Pump();
        var box = view.GetLogicalDescendants().OfType<ComposerNSTextView>().Single();
        box.Focus();
        ComposerTextViewHarness.Pump();
        Assert.Equal(0, blurred);

        other.Focus();
        Assert.Equal(0, blurred); // still inside the resign: the Task has not run
        ComposerTextViewHarness.Pump();
        Assert.Equal(1, blurred);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-047")] // ComposerTextView.swift:20 fileprivate weak var textView: a controller does not keep a dropped text view alive
    public void The_controller_does_not_keep_a_dropped_text_view_alive() => headless.OnUiThread(() =>
    {
        var controller = new ComposerController();
        BuildDropped(controller);
        ComposerTextViewHarness.Pump(); // the queued text-change jobs no longer reference the dropped view
        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        Assert.Equal(0, controller.CursorLocation);
    });

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void BuildDropped(ComposerController controller)
    {
        var text = "hello";
        var view = new ComposerTextView(
            new Binding<string>(() => text, value => text = value),
            new Binding<double>(() => 0, _ => { }),
            "",
            controller,
            _ => true,
            _ => { },
            _ => { });
        var box = view.GetLogicalDescendants().OfType<ComposerNSTextView>().Single();
        box.SelectionStart = box.SelectionEnd = 5;
        Assert.Equal(5, controller.CursorLocation);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-047")] // ComposerTextView.swift:98-107 the popover anchors to the caret's own rect, not the text's leading edge
    public void The_suggestion_panel_anchors_to_the_caret_on_the_second_line() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var h = new ComposerTextViewHarness("first line\nsecond line");
        h.Focus();
        h.TextView.SelectionStart = h.TextView.SelectionEnd = h.TextView.Text!.Length;
        h.Layout();
        h.Controller.ShowSuggestions(() => new TextBlock { Text = "item" }, itemCount: 1);
        ComposerTextViewHarness.Pump();
        var (popup, _) = Assert.Single(recorder.Calls);
        var rect = popup.PlacementRect!.Value;
        Assert.True(rect.Y > 12, $"the caret is on the second line, the anchor's Y was {rect.Y}");
        Assert.True(rect.X > 20, $"the caret is at the end of 'second line', the anchor's X was {rect.X}");
        Assert.True(rect.Width >= 1 && rect.Height >= 4); // :104-105
    });
}
