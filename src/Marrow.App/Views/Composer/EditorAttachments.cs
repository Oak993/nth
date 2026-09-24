using Avalonia.Controls;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;

// Source: DroppyCode/App/Views/Composer/EditorAttachments.swift (B2 1fbf81e), :1-53.
// SKELETON (cycle c100, group 3). Every member with behaviour throws MARROW_STUB until its unit ports it.
// Form (ED-095, R-VIEW-1): a SwiftUI View is a Decorator whose constructor builds `body`; `@Binding` is a
// Binding<T> constructor parameter (R-VIEW-2). Not declared yet, private in Swift and read by no other file: the
// @State storage `preview` (:8) and `drawn` (:9), which the implementing unit adds with the body.

namespace Marrow.App.Views.Composer;

/// <summary>:3-53 <c>struct EditorAttachments: View</c>.</summary>
public sealed class EditorAttachments : Decorator
{
    /// <param name="attachments">:4 <c>@Binding var attachments: [Attachment]</c>.</param>
    // :11-52 body
    public EditorAttachments(Binding<ValueList<Attachment>> attachments) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EditorAttachments.swift:11");
}
