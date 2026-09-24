using Avalonia.Controls;
using Marrow.App.Runtime;

// Source: DroppyCode/App/Views/Composer/FollowUpEditor.swift (B2 1fbf81e), :1-36.
// SKELETON (cycle c100, group 3). Every member with behaviour throws MARROW_STUB until its unit ports it.
// Form (ED-095, R-VIEW-1): a SwiftUI View is a Decorator whose constructor builds `body`; `@Bindable var edit` is
// the observable object itself (R-VIEW-2 builds its Binding<T> pairs in the body). Not declared yet, private in Swift
// and read by no other file: save() (:27-31) and cancel() (:33-35), which the implementing unit adds with the body.

namespace Marrow.App.Views.Composer;

/// <summary>:6-36 <c>struct FollowUpEditor: View</c>.</summary>
public sealed class FollowUpEditor : Decorator
{
    /// <param name="edit">:7 <c>@Bindable var edit: PromptEdit</c>.</param>
    /// <param name="runtime">:8 <c>let runtime: ThreadRuntime</c>.</param>
    // :10-25 body
    public FollowUpEditor(PromptEdit edit, ThreadRuntime runtime) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/FollowUpEditor.swift:10");
}
