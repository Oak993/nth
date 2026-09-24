using System;
using Avalonia;
using Avalonia.Controls;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Views.Composer;

// Source: DroppyCode/App/Views/Composer/PromptEditor.swift (B2 1fbf81e), :1-280.
// Skeleton (c100 group 4): the shapes are declared; the editor's body (:58-101), its helpers (:45-56, :103-128) and
// every behaviour of PromptEditorPopover (:143-279) are left to the implementing unit (MARROW_STUB).
// Forms (ED-095, R-VIEW-2): a SwiftUI View is a Decorator whose constructor builds `body`; the @ViewBuilder
// parameters `extra` and `actions` are Func<Control>, so the Swift generic parameters Extra and Actions drop out.

/// <summary>:8-129 <c>struct PromptEditor&lt;Extra: View, Actions: View&gt;: View</c>.</summary>
public sealed class PromptEditor : Decorator
{
    // :21-41 init(title:text:attachments:onSubmit:onCancel:extra:actions:); :58-101 body
    public PromptEditor(
        string title,
        Binding<string> text,
        Binding<ValueList<Attachment>> attachments,
        Action onSubmit,
        Action onCancel,
        Func<Control> extra,
        Func<Control> actions) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/PromptEditor.swift:21");

    // :45-54 private static func measuredHeight(of:)
    private static double MeasuredHeight(string of) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/PromptEditor.swift:45");

    // :56 private static var editorWidth: CGFloat { 520 }
    private static double EditorWidth =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/PromptEditor.swift:56");
}

/// <summary>:138-280 <c>@MainActor final class PromptEditorPopover&lt;Content: View&gt;: NSObject</c>: a prompt
/// editor as an anchored, application-defined popover.</summary>
public sealed class PromptEditorPopover<TContent> where TContent : Control
{
    // :165-167 func setAnchor(_ view: NSView)
    public void SetAnchor(Control view) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/PromptEditor.swift:165");

    // :170-172 func setAnchor(windowRect: CGRect)
    public void SetAnchor(Rect windowRect) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/PromptEditor.swift:170");

    // :176-191 func sync(_ edit: PromptEdit?, dismiss:content:)
    public void Sync(PromptEdit? edit, Action dismiss, Func<PromptEdit, TContent> content) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/PromptEditor.swift:176");

    // :233-236 func close()
    public void Close() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/PromptEditor.swift:233");
}
