using System;
using Avalonia.Controls;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Views.Chat;

// Source: DroppyCode/App/Views/Chat/HydraMergePopover.swift (B2 1fbf81e), :1-286.
// Skeleton (c100 group 2): HydraMergeNote's shape is complete; parse(_:link:) (:25-75), Head.id (:22) and the
// popover's body and private members (:83-285) are left to the implementing unit (MARROW_STUB).
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; a Swift struct is a sealed record
// (R-VAL-1) with its arrays as ValueList (R-VAL-2).

/// <summary>:4-76 <c>struct HydraMergeNote</c>: a merge note's body read into fields.</summary>
public sealed record HydraMergeNote
{
    public required MergeRequestLink Link { get; init; } // :8
    public required long Files { get; init; } // :9
    public required long Additions { get; init; } // :10
    public required long Deletions { get; init; } // :11
    public required string Target { get; init; } // :12
    public required string Branch { get; init; } // :13
    public required ValueList<string> Notes { get; init; } // :14
    public required ValueList<Head> Heads { get; init; } // :15

    /// <summary>:17-23 <c>struct Head: Hashable, Identifiable</c>.</summary>
    public sealed record Head
    {
        public required string Name { get; init; } // :18
        public required long Index { get; init; } // :19
        public required string Task { get; init; } // :20
        public required ValueList<string> Files { get; init; } // :21

        // :22 var id: String
        public string Id =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraMergePopover.swift:22");
    }

    // :25-75 static func parse(_:link:) -> HydraMergeNote?
    public static HydraMergeNote? Parse(string body, MergeRequestLink link) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraMergePopover.swift:25");
}

/// <summary>:78-286 <c>struct HydraMergePopover: View</c>: a merge note as a small card.</summary>
public sealed class HydraMergePopover : Decorator
{
    // :81 let note: HydraMergeNote; :98-227 body
    public HydraMergePopover(HydraMergeNote note) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraMergePopover.swift:98");
}
