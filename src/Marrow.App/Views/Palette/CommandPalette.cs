using System;
using Avalonia.Controls;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Views.Palette;

// Source: DroppyCode/App/Views/Palette/CommandPalette.swift (B2 1fbf81e), :1-211.
// Skeleton (c100 group 4): the view and its nested types are declared; body (:33-108), row (:110-147), results
// (:149-199), run (:201-206) and close (:208-210) are left to the implementing unit (MARROW_STUB).
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; `@Environment(AppModel.self)` is read
// from the environment, not passed in, so the view takes no inputs.

/// <summary>:3-211 <c>struct CommandPalette: View</c>: search threads, projects and actions.</summary>
public sealed class CommandPalette : Decorator
{
    // :33-108 body
    public CommandPalette() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Palette/CommandPalette.swift:33");

    /// <summary>:15-21 <c>private struct ResultsKey: Equatable</c>.</summary>
    private sealed record ResultsKey
    {
        public required string Query { get; init; } // :16
        public required ValueList<ChatThread> Threads { get; init; } // :17
        public required ValueList<Project> Projects { get; init; } // :18
        public Guid? SelectedThreadID { get; init; } // :19
        public required string FinishActionTitle { get; init; } // :20
    }

    /// <summary>:23-31 <c>private struct Item: Identifiable</c>.</summary>
    private sealed record Item
    {
        public required string Id { get; init; } // :24
        public required string Title { get; init; } // :25
        public string? Subtitle { get; init; } // :26
        public ProjectIcon? Icon { get; init; } // :27
        public required string Symbol { get; init; } // :28
        public string? Shortcut { get; init; } // :29
        public required Action Perform { get; init; } // :30
    }
}
