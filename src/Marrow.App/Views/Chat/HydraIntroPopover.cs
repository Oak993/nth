using System;
using Avalonia.Controls;

namespace Marrow.App.Views.Chat;

// Source: DroppyCode/App/Views/Chat/HydraIntroPopover.swift (B2 1fbf81e), :1-158.
// Skeleton (c100 group 2): both views, their inputs, HydraInfoPopover.Topic and the file-scope function
// hydraIntroStep. Every body is left to the implementing unit (MARROW_STUB).
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; AnyView is Control. The free function
// hydraIntroStep (:78-89) goes on a static member of the class named after the Swift file (R-NAME-4): the file's own
// view HydraIntroPopover carries that name, so the function is its static member.

/// <summary>:3-76 <c>struct HydraIntroPopover: View</c>: the first chat's Hydra intro.</summary>
public sealed class HydraIntroPopover : Decorator
{
    // :8 let dismiss: () -> Void; :10-75 body
    public HydraIntroPopover(Action dismiss) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraIntroPopover.swift:10");

    // :78-89 private func hydraIntroStep(glyph:title:line:) -> some View
    internal static Control HydraIntroStep(Control glyph, string title, string line) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraIntroPopover.swift:78");
}

/// <summary>:91-158 <c>struct HydraInfoPopover: View</c>.</summary>
public sealed class HydraInfoPopover : Decorator
{
    /// <summary>:92 <c>enum Topic { case hydra, heads }</c>.</summary>
    public enum Topic
    {
        Hydra, // :92
        Heads, // :92
    }

    // :93 let topic: Topic; :95-157 body
    public HydraInfoPopover(Topic topic) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraIntroPopover.swift:95");
}
