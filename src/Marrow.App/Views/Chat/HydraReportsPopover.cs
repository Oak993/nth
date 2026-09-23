using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Views.Chat;

// Source: DroppyCode/App/Views/Chat/HydraReportsPopover.swift (B2 1fbf81e), :1-655.
// Skeleton (c100 group 2): HydraReportDigest's shape is complete; parse(_:) (:44-76), its private helpers (:78-196),
// the computed ids, and the popover's body and private members (:212-654) are left to the implementing unit
// (MARROW_STUB); cleanedTitle(_:names:) (:393-405), which is not private, is declared.
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; a Swift struct is a sealed record
// (R-VAL-1); an enum with associated values is a record union (R-ENUM-2), an unlabelled value named `Value`.

/// <summary>:3-197 <c>struct HydraReportDigest</c>: a heads' report batch read as a digest.</summary>
public sealed record HydraReportDigest
{
    public string? Intro { get; init; } // :7
    public required ValueList<Head> Heads { get; init; } // :8

    /// <summary>:10-18 <c>struct Head: Identifiable</c>.</summary>
    public sealed record Head
    {
        public required string Id { get; init; } // :11
        public required string Name { get; init; } // :12
        public required string Task { get; init; } // :13
        public required Outcome Outcome { get; init; } // :14
        public string? Effort { get; init; } // :15
        public required Landing Landing { get; init; } // :16
        public required string Body { get; init; } // :17
    }

    /// <summary>:20-22 <c>enum Outcome</c>.</summary>
    public enum Outcome
    {
        Done, // :21
        Failed, // :21
        Stopped, // :21
    }

    /// <summary>:24-30 <c>enum Landing</c>, a record union (R-ENUM-2).</summary>
    public abstract record Landing
    {
        private Landing() { }

        public sealed record Landed(ValueList<LandedFile> Value) : Landing; // :25
        public sealed record NotLanded(ValueList<LandedFile> Value, string? Error) : Landing; // :26
        public sealed record Nothing : Landing; // :27
        public sealed record Unfinished(string? CopyPath) : Landing; // :28
        public sealed record None : Landing; // :29
    }

    /// <summary>:32-38 <c>struct LandedFile: Identifiable</c>.</summary>
    public sealed record LandedFile
    {
        public required string Path { get; init; } // :33
        public required long Additions { get; init; } // :34
        public required long Deletions { get; init; } // :35
        public required bool HasConflicts { get; init; } // :36

        // :37 var id: String { path }
        public string Id =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraReportsPopover.swift:37");
    }

    // :44-76 static func parse(_:) -> HydraReportDigest
    public static HydraReportDigest Parse(string text) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraReportsPopover.swift:44");
}

/// <summary>:199-655 <c>struct HydraReportsPopover: View</c>: the digest in the popover its pill opens.</summary>
public sealed class HydraReportsPopover : Decorator
{
    private const double HydraReportsPopoverWidth = 460; // :218 Swift: width (Layoutable.Width is inherited)

    // :220-235 @MainActor init(title:text:personas:); :237-266 body
    public HydraReportsPopover(string title, string text, IReadOnlyList<HydraPersona> personas) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraReportsPopover.swift:220");

    // :393-405 static func cleanedTitle(_:names:) -> String
    public static string CleanedTitle(string title, IReadOnlyList<string> names) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraReportsPopover.swift:393");
}
