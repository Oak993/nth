using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Store;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Theme;

namespace Marrow.App.Views.Composer;

using Chrome = Marrow.UI.Chrome.Chrome;

// Source: DroppyCode/App/Views/Composer/PromptEditor.swift (B3 52bc792), :1-280.

/// <summary>:8-129 <c>struct PromptEditor&lt;Extra: View, Actions: View&gt;: View</c>.</summary>
public sealed class PromptEditor : Decorator
{
    // :17-20 @State properties; read by the body and its actions.
    private double textHeight;
    private bool showingFiles;
    private Popup? filesPopup;
    private Button? addFilesButton;
    private Action? onSubmitAction;
    private Action? onCancelAction;
    private bool isAttached;
    private EditorAttachments? attachmentView;
    private StackPanel? attachmentSlot;
    private Binding<ValueList<Attachment>>? attachmentBinding;

    // :21-41 init(title:text:attachments:onSubmit:onCancel:extra:actions:); :58-101 body
    public PromptEditor(
        string title,
        Binding<string> text,
        Binding<ValueList<Attachment>> attachments,
        Action onSubmit,
        Action onCancel,
        Func<Control> extra,
        Func<Control> actions)
    {
        onSubmitAction = onSubmit;
        onCancelAction = onCancel;
        attachmentBinding = attachments;
        textHeight = MeasuredHeight(text.Value); // :37-40

        Button? addButton = null;
        var attachmentsPanel = new StackPanel { Orientation = Orientation.Vertical };
        attachmentSlot = attachmentsPanel;

        void UpdateAttachments()
        {
            var currentAttachments = attachments.Value;
            attachmentsPanel.IsVisible = currentAttachments.Count > 0;
            if (currentAttachments.Count == 0)
            {
                attachmentsPanel.Children.Clear();
                attachmentView = null;
            }
            else if (isAttached && attachmentView is null)
            {
                attachmentView = new EditorAttachments(attachments); // :74-76
                attachmentsPanel.Children.Clear();
                attachmentsPanel.Children.Add(attachmentView);
            }

            if (addButton is not null)
                addButton.IsEnabled = currentAttachments.Count < Storage.AttachmentLimit; // :87
        }

        void AttachAndRefresh(IReadOnlyList<AttachmentSource> sources)
        {
            var task = Attach(sources, attachments);
            if (task is null) return;
            MainActor.Spawn(async () =>
            {
                await task.Completion;
                UpdateAttachments();
            });
        }

        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 14, // :59
            Margin = new Thickness(20), // :99
            Width = EditorWidth - 40,
        };

        var heading = new TextBlock { Text = title }; // :60
        SwiftFont.Title2.FontWeight(SwiftFont.Weight.Semibold).Apply(heading); // :61
        stack.Children.Add(heading);

        ComposerNSTextView? editorTextView = null;
        Border? fieldPlate = null;
        ComposerTextView? composerTextView = null;

        void SetFieldHeight(double height)
        {
            textHeight = height;
            var fieldHeight = Math.Min(240, Math.Max(22, textHeight)); // :71
            if (editorTextView is not null) editorTextView.Height = fieldHeight;
            if (composerTextView is not null) composerTextView.Height = fieldHeight;
            if (fieldPlate is not null) fieldPlate.Height = fieldHeight + 20; // :72
        }

        var measuredFieldHeight = Math.Min(240, Math.Max(22, textHeight)); // :71
        var textView = new ComposerTextView(
            text,
            new Binding<double>(() => textHeight, SetFieldHeight),
            placeholder: "Message", // :65
            controller: new ComposerController(), // :66
            onKey: HandleKey, // :67
            onAttach: AttachAndRefresh, // :68
            onCursorChange: _ => { }); // :69
        composerTextView = textView;
        editorTextView = FindTextView(textView);
        textView.Height = measuredFieldHeight;
        if (editorTextView is not null) editorTextView.Height = measuredFieldHeight;

        fieldPlate = new Border
        {
            Child = textView,
            Padding = new Thickness(10), // :72
            Background = Chrome.Overlay(0.05).Brush(textView), // :73
            CornerRadius = new CornerRadius(12), // :73
            Height = measuredFieldHeight + 20,
        }.RoundedRectangle(12, RoundedCornerStyle.Continuous); // :73 `.rect(cornerRadius: 12, style: .continuous)`; one line tall the plate is about 37 pt, at the edge of ED-275 (2)'s band (OD-0102)
        stack.Children.Add(fieldPlate);

        UpdateAttachments();
        stack.Children.Add(attachmentsPanel); // :74-76
        stack.Children.Add(extra()); // :77

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 8, // :78
        };

        var addButtonControl = new Button
        {
            Content = SwiftImage.SystemName("plus"), // :82
            Padding = new Thickness(8),
        };
        addButton = addButtonControl;
        addFilesButton = addButtonControl;
        ToolTip.SetTip(addButtonControl, "Add files"); // :85
        AutomationProperties.SetName(addButtonControl, "Add files"); // :86
        addButtonControl.IsEnabled = attachments.Value.Count < Storage.AttachmentLimit; // :87
        addButtonControl.Click += (_, _) => ToggleFilesPopover(attachments); // :79-80
        Grid.SetColumn(addButtonControl, 0);
        row.Children.Add(Chrome.ChromeGlassCircle(addButtonControl)); // :84

        var spacer = new Border();
        Grid.SetColumn(spacer, 1);
        row.Children.Add(spacer);

        var actionControl = actions(); // :95
        Grid.SetColumn(actionControl, 2);
        row.Children.Add(actionControl);

        stack.Children.Add(row);
        Width = EditorWidth; // :100
        Child = stack;

        // :74-87 the body reads the attachments binding, so its section and the add button follow every change to it,
        // whoever makes it (a removal in EditorAttachments, a caller).
        this.Track(() => attachments.Value, _ => UpdateAttachments());
        AttachedToVisualTree += (_, _) =>
        {
            isAttached = true;
            UpdateAttachments();
        };
        DetachedFromVisualTree += (_, _) => isAttached = false;
    }

    // :45-54 private static func measuredHeight(of:)
    private static double MeasuredHeight(string of)
    {
        var measuredText = of.Length == 0 ? " " : of;
        var font = SwiftFont.SystemFont(14).Resolve();
        using var layout = new Avalonia.Media.TextFormatting.TextLayout(
            measuredText,
            new Typeface(font.FontFamily, font.FontStyle, font.FontWeight),
            font.FontSize,
            null,
            textWrapping: TextWrapping.Wrap,
            maxWidth: EditorWidth - 2 * 20 - 2 * 10); // :47-52
        // DEVIATION OD-0009: Inter measurement is recomputed at the source width; its wrap points can differ from macOS.
        return Math.Ceiling(Math.Max(18, layout.Height) + 2 * 2); // :53
    }

    // :56 private static var editorWidth: CGFloat { 520 }
    private static double EditorWidth => 520;

    // :103-115 private func handleKey(_ key: ComposerKey) -> Bool
    private bool HandleKey(ComposerKey key) => key switch
    {
        ComposerKey.Submit or ComposerKey.Steer => Submit(),
        ComposerKey.Escape => Cancel(),
        ComposerKey.Up or ComposerKey.Down or ComposerKey.Tab or ComposerKey.DeleteAtStart => false,
        _ => false,
    };

    // :103-110 onSubmit and onCancel closures.
    private bool Submit()
    {
        onSubmitAction?.Invoke();
        return true;
    }

    // :108-110 onCancel closure.
    private bool Cancel()
    {
        onCancelAction?.Invoke();
        return true;
    }

    // :117-124 private func chooseFiles()
    private void ChooseFiles(Binding<ValueList<Attachment>> attachments)
    {
        MainActor.Spawn(async () =>
        {
            // :118-123 The shared native OpenPanel adapter maps prompt to IFileDialog.SetOkButtonLabel.
            var paths = await Marrow.App.Port.ComposerFileDialog.Current.Current.Choose(
                TopLevel.GetTopLevel(this) as Avalonia.Controls.Window, true, false, "Attach");
            if (paths is null) return;
            AttachAndRefresh(paths.Select(path => (AttachmentSource)new AttachmentSource.File(path)).ToList(), attachments);
        });
    }

    // :126-128 private func attach(_ sources: [AttachmentSource])
    private static TaskHandle? Attach(IReadOnlyList<AttachmentSource> sources, Binding<ValueList<Attachment>> attachments) =>
        Storage.Attach(sources, () => attachments.Value, value => attachments.Value = value);

    // :126-128 attachment completion refresh.
    private void AttachAndRefresh(IReadOnlyList<AttachmentSource> sources, Binding<ValueList<Attachment>> attachments)
    {
        var task = Attach(sources, attachments);
        if (task is null) return;
        MainActor.Spawn(async () =>
        {
            await task.Completion;
            if (attachmentSlot is { } slot && attachmentBinding is { } binding)
            {
                var currentAttachments = binding.Value;
                slot.IsVisible = currentAttachments.Count > 0;
                if (currentAttachments.Count == 0)
                {
                    slot.Children.Clear();
                    attachmentView = null;
                }
                else if (isAttached && attachmentView is null)
                {
                    attachmentView = new EditorAttachments(binding);
                    slot.Children.Clear();
                    slot.Children.Add(attachmentView);
                }
            }
            if (addFilesButton is { } button)
                button.IsEnabled = attachments.Value.Count < Storage.AttachmentLimit;
        });
    }

    // :174-191 private func sync(_:dismiss:content:) deferred show.
    private void ToggleFilesPopover(Binding<ValueList<Attachment>> attachments)
    {
        if (showingFiles)
        {
            if (filesPopup is { } openPopup) Presentation.Presenter.Current.SetPresented(openPopup, false);
            showingFiles = false;
            return;
        }

        if (addFilesButton is not { } anchor) return;
        if (filesPopup is null)
        {
            // :88 a plain SwiftUI .popover is transient: a click outside it dismisses it (the editor's own popover
            // is the application-defined one), and the state then reads as closed again.
            filesPopup = new Popup { IsLightDismissEnabled = true };
            filesPopup.Closed += (_, _) => showingFiles = false;
        }
        var popup = filesPopup;
        popup.PlacementTarget = anchor;
        popup.Placement = PlacementMode.Bottom;
        popup.Child = new DownloadsPopover(
            path => AttachAndRefresh([new AttachmentSource.File(path)], attachments),
            () =>
            {
                Presentation.Presenter.Current.SetPresented(popup, false);
                showingFiles = false;
                ChooseFiles(attachments);
            });
        showingFiles = true;
        Presentation.Presenter.Current.SetPresented(popup, true);
    }

    // :58-101 body tree search used to reach the text field through its scroll container.
    private static ComposerNSTextView? FindTextView(Control control)
    {
        if (control is ComposerNSTextView textView) return textView;
        if (control is Decorator decorator && decorator.Child is { } child && FindTextView(child) is { } decorated)
            return decorated;
        if (control is ContentControl contentControl && contentControl.Content is Control content && FindTextView(content) is { } contained)
            return contained;
        if (control is Panel panel)
        {
            foreach (var childControl in panel.Children)
                if (FindTextView(childControl) is { } found) return found;
        }
        return null;
    }
}

/// <summary>:138-280 <c>@MainActor final class PromptEditorPopover&lt;Content: View&gt;: NSObject</c>: a prompt
/// editor as an anchored, application-defined popover.</summary>
public sealed class PromptEditorPopover<TContent> where TContent : Control
{
    private const double EditorWidth = 520; // :56

    private Popup? shownPopup;
    private WeakView? anchor;
    private Rect? anchorRect;
    private Action dismiss = static () => { };
    private Avalonia.Controls.Window? monitorWindow;
    private IDisposable? applicationEscape;
    private int generation;
    private bool popupSized;
    private bool presented; // :176-216 shown state across the R-VIEW-9 presenter

    // :165-167 func setAnchor(_ view: NSView)
    public void SetAnchor(Control view)
    {
        anchor = new WeakView(view);
    }

    // :170-172 func setAnchor(windowRect: CGRect)
    public void SetAnchor(Rect windowRect)
    {
        anchorRect = windowRect;
    }

    // :176-191 func sync(_ edit: PromptEdit?, dismiss:content:)
    public void Sync(PromptEdit? edit, Action dismiss, Func<PromptEdit, TContent> content)
    {
        generation++;
        if (edit is null)
        {
            Close();
            return;
        }

        var hasAnchor = anchor?.Value is { } view && TopLevel.GetTopLevel(view) is not null || anchorRect is not null;
        if (presented || shownPopup?.IsOpen == true || !hasAnchor) return;

        var currentGeneration = generation;
        MainActor.Spawn(() =>
        {
            if (currentGeneration == generation)
                Show(edit, dismiss, content);
            return Task.CompletedTask;
        });
    }

    // :232-236 func close()
    public void Close()
    {
        StopMonitors();
        if (shownPopup is { } popup && (presented || popup.IsOpen))
        {
            presented = false;
            Presentation.Presenter.Current.SetPresented(popup, false);
        }
    }

    // :193-216 private func show(_:dismiss:content:)
    private void Show(PromptEdit edit, Action dismiss, Func<PromptEdit, TContent> content)
    {
        if (presented || shownPopup?.IsOpen == true) return;

        Control target;
        Rect targetRect;
        if (anchor?.Value is { } view && TopLevel.GetTopLevel(view) is not null)
        {
            target = view;
            targetRect = new Rect(view.Bounds.Size);
        }
        else if (anchorRect is { } rect && WindowRectAnchor.Target(rect) is { } resolved)
        {
            target = resolved.View;
            targetRect = resolved.Rect;
        }
        else
        {
            return;
        }

        this.dismiss = dismiss;
        var host = content(edit);
        Marrow.UI.Chrome.Popovers.PresentedChrome(host); // :209-210 capsule button shape and raw optional accent on the whole hosted tree
        var popup = shownPopup;
        if (popup is null)
        {
            popup = new Popup { IsLightDismissEnabled = false };
            popup.Closed += (_, _) => { presented = false; StopMonitors(); };
            popup.LayoutUpdated += (_, _) =>
            {
                if (PopoverHost.Content(popup) is { } currentContent) // the hosted view, not the card it hangs on (OD-0095)
                    ResizeToContent(popup, currentContent, initial: false);
            };
            shownPopup = popup;
        }
        popupSized = false;
        popup.PlacementTarget = target;
        popup.PlacementRect = targetRect;
        popup.Placement = PlacementMode.Bottom;
        popup.Child = host;
        ResizeToContent(popup, host, initial: true);
        presented = true;
        Presentation.SetPresented(popup, true, target, () => { dismiss(); Close(); });
        StartMonitors(target);
    }

    // :218-230 private func resize(to size: CGSize)
    private void ResizeToContent(Popup popup, Control content, bool initial)
    {
        if (popup.Child is null) return;
        var available = new Size(EditorWidth, double.PositiveInfinity);
        Size size;
        if (initial && TopLevel.GetTopLevel(content) is null)
        {
            // :218-230 the size of the hosted view, with its styles and templates in effect. The popup is not open yet, so the
            // content is in no window: it is measured in one, out of the popup (a control has one parent) and put back.
            popup.Child = null;
            try { size = StyledMeasure.Of(content, available); }
            finally { popup.Child = content; }
        }
        else
        {
            content.Measure(available);
            size = content.DesiredSize;
        }
        if (size.Width <= 0 || size.Height <= 0) return;
        // OD-0095: the popover hangs on its tipped card when it opens; the popup takes the card's room as well as the
        // content's size, every time it is measured.
        var room = PopoverHost.Room(popup);
        size = new Size(size.Width + room.Width, size.Height + room.Height);

        if (initial || !popupSized)
        {
            popup.Width = size.Width;
            popup.Height = size.Height;
            popupSized = true;
            return;
        }

        if (Math.Abs(popup.Height - size.Height) <= 0.5) return;
        popup.Width = size.Width; // :226 contentSize follows both dimensions after a qualifying height change
        popup.Height = size.Height;
    }

    // :238-252 private func startMonitors()
    private void StartMonitors(Control target)
    {
        if (monitorWindow is not null) return;
        var window = TopLevel.GetTopLevel(target) as Avalonia.Controls.Window;
        if (window is null) return;

        monitorWindow = window;
        window.AddHandler(InputElement.KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        // :240-244 NSEvent.addLocalMonitorForEvents sees every key-down of the application, whichever of its windows
        // (a nested popup, the attachment preview) has the focus; the anchor window's own handler above covers that window.
        applicationEscape = InputElement.KeyDownEvent.AddClassHandler<TopLevel>(
            OnOtherTopLevelKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void OnOtherTopLevelKeyDown(TopLevel topLevel, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Escape || ReferenceEquals(topLevel, monitorWindow)) return; // the window handler owns that one
        dismiss();
        e.Handled = true; // :243 return nil: the key is consumed
    }

    // :258-274 private func handleMouseDown(_ event: NSEvent)
    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // :247 matching: [.leftMouseDown, .rightMouseDown]: any other button passes untouched.
        if (e.GetCurrentPoint(null).Properties.PointerUpdateKind is not (PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.RightButtonPressed)) return;
        if (anchor?.Value is { } view && ReferenceEquals(TopLevel.GetTopLevel(view), monitorWindow))
        {
            if (new Rect(view.Bounds.Size).Contains(e.GetPosition(view))) return;
            dismiss();
            return;
        }

        if (anchorRect is { } rect && WindowRectAnchor.Target(rect) is { } target &&
            ReferenceEquals(TopLevel.GetTopLevel(target.View), monitorWindow) &&
            !target.Rect.Contains(e.GetPosition(target.View)))
            dismiss();
    }

    // :240-246 private key-down monitor
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Escape) return;
        dismiss();
        e.Handled = true;
    }

    // :276-279 private func stopMonitors()
    private void StopMonitors()
    {
        applicationEscape?.Dispose();
        applicationEscape = null;
        if (monitorWindow is not { } window) return;
        window.RemoveHandler(InputElement.KeyDownEvent, OnWindowKeyDown);
        window.RemoveHandler(InputElement.PointerPressedEvent, OnWindowPointerPressed);
        monitorWindow = null;
    }
}
