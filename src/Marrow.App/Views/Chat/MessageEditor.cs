using System;
using Avalonia.Controls;
using Marrow.App.Runtime;
using Marrow.Services.Git;

namespace Marrow.App.Views.Chat;

// Source: DroppyCode/App/Views/Chat/MessageEditor.swift (B2 1fbf81e), :1-176.
// Skeleton (c100 group 2): the view, its inputs and its private Changes enum. The body (:39-70), canSend (:72-76),
// fileChanges (:78-121), fileList (:125-154), send (:156-171) and cancel (:173-175) are left to the implementing unit
// (MARROW_STUB). Changes.init(_:) (:17-23) takes the `Result<RevertPreview, Error>?` that
// ThreadRuntime.cachedRevertPreview(for:) returns (ThreadRuntime.swift:1785); its C# shape is ThreadRuntime's to
// choose, so the init is not declared here.
// Form (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; `@Bindable var edit` is the PromptEdit
// itself, read and written through its properties (R-VIEW-2).

/// <summary>:7-176 <c>struct MessageEditor: View</c>: edits a message already sent.</summary>
public sealed class MessageEditor : Decorator
{
    /// <summary>:12-24 <c>private enum Changes</c>, a record union (R-ENUM-2).</summary>
    private abstract record Changes
    {
        private Changes() { }

        public sealed record Checking : Changes; // :13
        public sealed record Found(RevertPreview Value) : Changes; // :14
        public sealed record Unavailable(string Value) : Changes; // :15
    }

    // :31-37 init(edit:runtime:); :39-70 body
    public MessageEditor(PromptEdit edit, ThreadRuntime runtime) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/MessageEditor.swift:31");
}
