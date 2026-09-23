using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Marrow.Core.Port;
using Marrow.Services.Store;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Views.Composer;

// Source: DroppyCode/App/Views/Composer/ComposerTextView.swift (B2 1fbf81e), :1-379.

/// <summary>The key the composer text view reports to its owner from a doCommandBy selector. :5-15</summary>
public enum ComposerKey
{
    Up,
    Down,
    Tab,
    Escape,
    Submit,

    /// <summary>Command-Return: steer the running turn (queue as a follow-up). :11-12</summary>
    Steer,

    /// <summary>Delete with the caret at the very start and nothing selected: the chips before the text take it. :13-14</summary>
    DeleteAtStart,
}

/// <summary>Lets the owner reach into the text view for focus and in-place replacements. :17-114</summary>
public sealed class ComposerController
{
    // :20 fileprivate weak var textView: ComposerNSTextView?
    internal ComposerNSTextView? TextViewInstance;

    // :21 private var suggestionPopover: NSPopover?
    private Popup? suggestionPopover;

    // :27 var suggestionRefresh: Task<Void, Never>?
    public TaskHandle? SuggestionRefresh { get; set; }

    // :29-31 var cursorLocation: Int { textView?.selectedRange().location ?? 0 }
    public int CursorLocation => TextViewInstance?.SelectionStart ?? 0;

    // :33-36 var hasFocus: Bool. ENGINEERING DECISION: NSApp.isActive && window.isKeyWindow && firstResponder===
    // textView ports as the window being active and the text view holding input focus (IsFocused already implies
    // it is the currently focused element within its scope).
    public bool HasFocus
    {
        get
        {
            if (TextViewInstance is not { } textView) return false;
            return (Avalonia.Controls.TopLevel.GetTopLevel(textView) as Avalonia.Controls.Window)?.IsActive == true && textView.IsFocused;
        }
    }

    // :38-41 func focus()
    public void Focus() => TextViewInstance?.Focus();

    // :43-49 func replaceCharacters(in range: NSRange, with replacement: String)
    public void ReplaceCharacters(int location, int length, string replacement)
    {
        if (TextViewInstance is not { } textView) return;
        var text = textView.Text ?? "";
        if (location < 0 || location + length > text.Length) return;
        textView.Text = text[..location] + replacement + text[(location + length)..];
        var caret = location + replacement.Length;
        textView.SelectionStart = caret;
        textView.SelectionEnd = caret;
    }

    // :51-84 func showSuggestions(_ content: AnyView, itemCount: Int)
    public void ShowSuggestions(Func<Control> content, int itemCount)
    {
        if (TextViewInstance is not { } textView || Avalonia.Controls.TopLevel.GetTopLevel(textView) is null) return; // :64
        var size = new Size(340, Math.Min(340, 44 + Math.Max(itemCount, 1) * 26)); // :70
        var popup = suggestionPopover;
        if (popup is null)
        {
            popup = new Popup
            {
                PlacementTarget = textView,
                Placement = PlacementMode.AnchorAndGravity,
                PlacementAnchor = PopupAnchor.Top,
                PlacementGravity = PopupGravity.Top,
                IsLightDismissEnabled = false, // :60 .applicationDefined: the app, not the system, dismisses it
            };
            suggestionPopover = popup;
        }
        Popovers.SetFixedContent(popup, content(), size); // :70-71 frame(width:height:) + presentedChrome()
        popup.PlacementRect = CaretRect(textView); // :81 show(relativeTo: caretRect, of:, preferredEdge: .maxY)
        Presentation.Presenter.Current.SetPresented(popup, true);
        textView.Focus(); // :83 the popover must never steal typing focus
    }

    // :86-88 func hideSuggestions()
    public void HideSuggestions()
    {
        if (suggestionPopover is { } popup) Presentation.Presenter.Current.SetPresented(popup, false);
    }

    // :90-96 func isClickInsideSuggestions() -> Bool. ENGINEERING DECISION: Avalonia has no synchronous
    // NSEvent.mouseLocation counterpart; IsPointerOverPopup is the platform's own continuously tracked
    // equivalent of "the pointer is currently over this popup's content".
    public bool IsClickInsideSuggestions() => suggestionPopover is { IsOpen: true } popup && popup.IsPointerOverPopup;

    // :98-113 private func caretRect(in textView: NSTextView) -> NSRect. UNVERIFIED: Avalonia's TextBox exposes
    // no public caret-glyph-rect query (no firstRect(forCharacterRange:) counterpart), so the primary branch
    // (:100-106) cannot be read back and this always takes the documented fallback branch (:108-112): the
    // leading edge of the visible text, offset 4, sized 1 by 18.
    private static Rect CaretRect(ComposerNSTextView textView)
    {
        var visible = new Rect(textView.Bounds.Size);
        return new Rect(visible.X + 4, visible.Y, 1, 18); // :109-111
    }
}

/// <summary>The Avalonia twin of ComposerNSTextView: NSTextView. :116-212</summary>
public sealed class ComposerNSTextView : TextBox
{
    // :117-119 var placeholder = "" { didSet { needsDisplay = true } }
    private string placeholder = "";

    public string Placeholder
    {
        get => placeholder;
        set
        {
            if (placeholder == value) return;
            placeholder = value;
            InvalidateVisual();
        }
    }

    public Action<IReadOnlyList<AttachmentSource>>? OnAttach { get; set; } // :120
    public Action? OnWidthChange { get; set; } // :121
    public Action? OnResign { get; set; } // :122
    public Func<ComposerKey, bool>? OnKeyCallback { get; set; } // the Coordinator's doCommandBy, :329-357

    // :124-128 override func setFrameSize(_ newSize: NSSize): width-change detection.
    private double lastWidth;

    // :191 private var placeholderShown = false: the last draw's emptiness, gating whole-view invalidation.
    private bool placeholderShown;

    public ComposerNSTextView()
    {
        // :242-243 isRichText = false, importsGraphics = false: TextBox is plain text only, nothing to set.
        IsUndoEnabled = true; // :244 allowsUndo = true
        Background = Brushes.Transparent; // :245 drawsBackground = false
        SwiftFont.SystemFont(14).Apply(this); // :246 font = .systemFont(ofSize: 14)
        Foreground = SwiftColor.LabelColor.Brush(this); // :247 textColor = .labelColor
        Padding = new Thickness(0, 2, 0, 2); // :249 textContainerInset (0, 2); :268 lineFragmentPadding = 0
        // :250-254, :260-261 smart quote/dash/replacement/spelling-correction/completion substitution, inline
        // prediction and writing tools: Avalonia's TextBox has no such platform feature to begin with, so there
        // is nothing this control needs to disable (naturally scoped: no other field gains one either).
        TextWrapping = TextWrapping.Wrap; // :263-264 isHorizontallyResizable = false, autoresizingMask = [.width]
        AcceptsReturn = false; // :332-341 Return is always handled explicitly below, never a default newline
        AcceptsTab = true; // :346-347 an unhandled Tab still inserts a literal tab character
        BorderThickness = new Thickness(0);
        CornerRadius = new CornerRadius(0);
        MinHeight = 18;
        lastWidth = Bounds.Width;
        TextChanged += (_, _) =>
        {
            // :193-196 override func didChangeText(): redraw whole only when emptiness changed.
            if (string.IsNullOrEmpty(Text) != placeholderShown) InvalidateVisual();
        };
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOverEvent);
        AddHandler(DragDrop.DropEvent, OnDropEvent);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var result = base.ArrangeOverride(finalSize);
        // :124-128 override func setFrameSize(_ newSize: NSSize)
        if (Math.Abs(finalSize.Width - lastWidth) > 0.5)
        {
            lastWidth = finalSize.Width;
            OnWidthChange?.Invoke();
        }
        return result;
    }

    protected override void OnLostFocus(Avalonia.Input.FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        OnResign?.Invoke(); // :130-134 override func resignFirstResponder() -> Bool
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // :154-161 override func performKeyEquivalent(with event: NSEvent) -> Bool. This accelerator is the
        // port's whole reachable path to paste(_:) (no OS Edit menu exists to gate through validateUserInterfaceItem,
        // OD-0008 C3); Ctrl is the Windows chord mapping for Command.
        if (e.Key == Key.V && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            MainActor.Spawn(PasteAsync);
            return;
        }
        HandleComposerCommand(e); // :329-357 textView(_:doCommandBy:)
        if (!e.Handled) base.OnKeyDown(e);
    }

    // :330 if textView.hasMarkedText() { return false }. UNVERIFIED: Avalonia's TextBox exposes no public IME
    // composition state; this relies on the platform not delivering these key-down events while composing.
    private void HandleComposerCommand(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter: // :332-341 #selector(NSResponder.insertNewline(_:))
                if ((e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Alt)) != 0) // :334 shift or option
                {
                    InsertNewlineIgnoringFieldEditor(); // :335
                    e.Handled = true;
                    return;
                }
                if ((e.KeyModifiers & KeyModifiers.Control) != 0) // :338 command -> Ctrl chord mapping
                {
                    e.Handled = OnKeyCallback?.Invoke(ComposerKey.Steer) ?? false; // :339
                    return;
                }
                e.Handled = OnKeyCallback?.Invoke(ComposerKey.Submit) ?? false; // :341
                return;
            case Key.Up: // :342-343 #selector(NSResponder.moveUp(_:))
                e.Handled = OnKeyCallback?.Invoke(ComposerKey.Up) ?? false;
                return;
            case Key.Down: // :344-345 #selector(NSResponder.moveDown(_:))
                e.Handled = OnKeyCallback?.Invoke(ComposerKey.Down) ?? false;
                return;
            case Key.Tab: // :346-347 #selector(NSResponder.insertTab(_:))
                e.Handled = OnKeyCallback?.Invoke(ComposerKey.Tab) ?? false;
                return;
            case Key.Escape: // :348-349 #selector(NSResponder.cancelOperation(_:))
                e.Handled = OnKeyCallback?.Invoke(ComposerKey.Escape) ?? false;
                return;
            case Key.Back: // :350-354 #selector(NSResponder.deleteBackward(_:))
                if (SelectionStart == 0 && SelectionEnd == 0) // :351 selectedRange == NSRange(location: 0, length: 0)
                {
                    e.Handled = OnKeyCallback?.Invoke(ComposerKey.DeleteAtStart) ?? false; // :352
                }
                return; // :354 otherwise return false: the default deleteBackward runs (a no-op at position 0)
        }
    }

    // :335 textView.insertNewlineIgnoringFieldEditor(nil)
    private void InsertNewlineIgnoringFieldEditor()
    {
        var start = Math.Min(SelectionStart, SelectionEnd);
        var end = Math.Max(SelectionStart, SelectionEnd);
        var current = Text ?? "";
        Text = current[..start] + "\n" + current[end..];
        SelectionStart = SelectionEnd = start + 1;
    }

    // :163-178 override func paste(_ sender: Any?)
    private async Task PasteAsync()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        var data = clipboard is null ? null : await clipboard.TryGetDataAsync();
        if (data is not null)
        {
            var files = new List<AttachmentSource>();
            foreach (var item in data.Items)
            {
                if (await item.TryGetFileAsync() is { } file && file.TryGetLocalPath() is { } path)
                    files.Add(new AttachmentSource.File(path));
            }
            if (files.Count > 0) // :165-168
            {
                OnAttach?.Invoke(files);
                return;
            }

            string? text = null;
            foreach (var item in data.Items)
            {
                text = await item.TryGetTextAsync();
                if (text is not null) break;
            }
            if (text is null) // :170 pasteboard.string(forType: .string) == nil
            {
                foreach (var item in data.Items)
                {
                    if (await item.TryGetBitmapAsync() is { } bitmap) // :171-172
                    {
                        using var stream = new MemoryStream();
                        // DEVIATION: Avalonia's clipboard abstraction exposes only a decoded Bitmap, never the
                        // clipboard's raw per-format bytes, so the source's PNG/TIFF/JPEG/HEIC priority order
                        // (:138-139, :171-172) cannot be read back; every image paste is re-encoded as PNG.
                        bitmap.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                        OnAttach?.Invoke([new AttachmentSource.Image(stream.ToArray(), AttachmentSource.PngMimeType)]); // :173-175
                        return;
                    }
                }
            }
            if (text is not null)
            {
                InsertPlainText(text); // :177 pasteAsPlainText(sender)
                return;
            }
        }
        // Nothing usable on the clipboard: matches pasteAsPlainText(sender) pasting nothing.
    }

    private void InsertPlainText(string text)
    {
        var start = Math.Min(SelectionStart, SelectionEnd);
        var end = Math.Max(SelectionStart, SelectionEnd);
        var current = Text ?? "";
        Text = current[..start] + text + current[end..];
        SelectionStart = SelectionEnd = start + text.Length;
    }

    // PORT-ONLY: Avalonia's drag-and-drop pipeline only fires Drop when a DragOver handler has already
    // accepted the drag (there is no NSDraggingInfo counterpart needing this on macOS).
    private void OnDragOverEvent(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    // :180-187 override func performDragOperation(_ sender: NSDraggingInfo) -> Bool
    private void OnDropEvent(object? sender, DragEventArgs e)
    {
        var files = new List<AttachmentSource>();
        foreach (var item in e.DataTransfer.Items)
        {
            if (item.TryGetFile() is { } file && file.TryGetLocalPath() is { } path)
                files.Add(new AttachmentSource.File(path));
        }
        if (files.Count == 0) return; // :186 super.performDragOperation(sender): the text view handles the drop itself
        OnAttach?.Invoke(files);
        e.Handled = true;
    }

    // :198-211 override func draw(_ dirtyRect: NSRect)
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        placeholderShown = string.IsNullOrEmpty(Text) && !string.IsNullOrEmpty(placeholder); // :200
        if (!placeholderShown) return;
        var typeface = new Typeface(FontFamily, FontStyle, FontWeight);
        var formatted = new FormattedText(
            placeholder,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            FontSize,
            SwiftColor.PlaceholderTextColor.Brush(this)); // :203-204 font, .placeholderTextColor
        var origin = new Point(Padding.Left, Padding.Top); // :206-209 inset.width + lineFragmentPadding, inset.height
        context.DrawText(formatted, origin); // :210
    }
}

/// <summary>The Avalonia twin of the NSViewRepresentable ComposerTextView. :214-379</summary>
public sealed class ComposerTextView : Decorator
{
    private readonly Binding<string> text; // :215
    private readonly Binding<double> height; // :216
    private string placeholder; // :217
    private readonly ComposerController controller; // :218
    private Func<ComposerKey, bool> onKey; // :219
    private Action<IReadOnlyList<AttachmentSource>> onAttach; // :220
    private readonly Action<int> onCursorChange; // :221
    private readonly Action onBlur; // :222
    private readonly ComposerNSTextView textView;
    private readonly ScrollViewer scroll;

    public ComposerTextView(
        Binding<string> text,
        Binding<double> height,
        string placeholder,
        ComposerController controller,
        Func<ComposerKey, bool> onKey,
        Action<IReadOnlyList<AttachmentSource>> onAttach,
        Action<int> onCursorChange,
        Action? onBlur = null,
        bool takesFocusOnAppear = true) // :223-226
    {
        this.text = text;
        this.height = height;
        this.placeholder = placeholder;
        this.controller = controller;
        this.onKey = onKey;
        this.onAttach = onAttach;
        this.onCursorChange = onCursorChange;
        this.onBlur = onBlur ?? (() => { });

        // :232-291 func makeNSView(context:) -> NSScrollView
        textView = new ComposerNSTextView
        {
            Placeholder = placeholder, // :269
            Text = text.Value, // :270
        };
        textView.CaretBrush = Chrome.AccentNSColor.Brush(textView); // :248 insertionPointColor
        textView.OnAttach = sources => this.onAttach(sources); // :271
        textView.OnKeyCallback = key => this.onKey(key);
        textView.OnWidthChange = () => MainActor.Spawn(RunUpdateHeight); // :277-279
        textView.OnResign = () => this.onBlur(); // :280-282
        textView.TextChanged += (_, _) => OnTextDidChange(); // :317-322 Coordinator.textDidChange
        textView.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.SelectionStartProperty || e.Property == TextBox.SelectionEndProperty)
                this.onCursorChange(textView.SelectionStart); // :324-327 textViewDidChangeSelection
        };

        scroll = new ScrollViewer
        {
            Background = Brushes.Transparent, // :234 drawsBackground = false
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, // :236-238 hasVerticalScroller, overlay style
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = textView,
        };
        Child = scroll; // :273 scrollView.documentView = textView

        controller.TextViewInstance = textView; // :274

        AttachedToVisualTree += (_, _) => MainActor.Spawn(() => RunAppear(takesFocusOnAppear)); // :283-289
    }

    private Task RunAppear(bool takesFocusOnAppear)
    {
        // :284-287 never steal focus from another box the user is typing in, whichever one this is.
        if (takesFocusOnAppear && Avalonia.Controls.TopLevel.GetTopLevel(textView) is Avalonia.Controls.Window window && window.FocusManager?.GetFocusedElement() is not ComposerNSTextView)
            textView.Focus();
        UpdateHeight(); // :288
        return Task.CompletedTask;
    }

    private Task RunUpdateHeight()
    {
        UpdateHeight();
        return Task.CompletedTask;
    }

    // :317-322 func textDidChange(_ notification: Notification)
    private void OnTextDidChange()
    {
        text.Value = textView.Text ?? ""; // :319
        UpdateHeight(); // :320
        onCursorChange(textView.SelectionStart); // :321
    }

    // :293-306 func updateNSView(_ scrollView: NSScrollView, context: Context)
    public void Update(string newPlaceholder, Func<ComposerKey, bool> newOnKey, Action<IReadOnlyList<AttachmentSource>> newOnAttach)
    {
        onKey = newOnKey;
        onAttach = newOnAttach; // :296
        if (placeholder != newPlaceholder) // :297
        {
            placeholder = newPlaceholder;
            textView.Placeholder = newPlaceholder;
        }
        textView.CaretBrush = Chrome.AccentNSColor.Brush(textView); // :298-300 the caret follows the theme's accent
        if (textView.Text != text.Value) // :301, hasMarkedText check UNVERIFIED, see ComposerNSTextView remarks
        {
            textView.Text = text.Value; // :302
            var caret = text.Value.Length;
            textView.SelectionStart = textView.SelectionEnd = caret; // :303
            UpdateHeight(); // :304
        }
    }

    // :360-376 func updateHeight()
    private void UpdateHeight()
    {
        if (textView.Bounds.Width <= 24) return; // :361
        var typeface = new Typeface(textView.FontFamily, textView.FontStyle, textView.FontWeight);
        using var layout = new TextLayout(
            textView.Text ?? "", typeface, textView.FontSize, null,
            textWrapping: TextWrapping.Wrap, maxWidth: textView.Bounds.Width); // :363-372
        var contentHeight = layout.Height;
        var total = Math.Ceiling(Math.Max(18, contentHeight) + textView.Padding.Top + textView.Padding.Bottom); // :374
        if (Math.Abs(height.Value - total) > 0.5) height.Value = total; // :375
    }
}
