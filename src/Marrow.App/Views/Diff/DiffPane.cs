using Marrow.App.Runtime;
using Marrow.App.Views.Panes;
using Marrow.Core.Localisation;
using Marrow.Core.Models;

// PORT-ONLY: web surfaces W4, the Changes pane (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.1 "Changes | narrow | grid |
// the diff, now a pane (Alt+click on the toolbar button still opens it as a popover ...)", 3.7 "Changes | Ctrl+D (existing
// ToggleChanges, now the pane)", 3.12 "Header (36 DIP): kind glyph, title ..."; the canvas's Changes cell: "Changes · 3 files",
// Expand, Close; 14 W4 "DiffPane.cs (from DiffInspector)"; ED-NEW-web-w4-1). No Swift counterpart. The pane body is the
// thread's DiffInspector in pane mode: the same file cards, selection and load as the popover, in the size its cell gives it.
// The workspace draws the header (the kind glyph, the title, the file count, Expand and Close).

namespace Marrow.App.Views.Diff;

/// <summary>The Changes pane's content.</summary>
public static class DiffPane
{
    /// <summary>Makes Changes drawable in the workspace (the app's start-up calls it; tests call it or replace the factory).</summary>
    public static void Register() => PaneContents.Register(PaneKind.Changes, (_, runtime) => Create(runtime));

    /// <summary>The content of a thread's Changes pane: the diff, and a header whose detail counts its files.</summary>
    public static PaneContent Create(ThreadRuntime runtime)
    {
        var inspector = new DiffInspector(runtime, DiffInspectorMode.Pane);
        var content = new PaneContent(inspector, PaneKinds.Title(PaneKind.Changes), Subtitle(inspector));
        inspector.Changed += () => content.Update(PaneKinds.Title(PaneKind.Changes), Subtitle(inspector));
        return content;
    }

    /// <summary>"3 files", "1 file", or nothing while there are none (or the first load has not come back).</summary>
    internal static string? Subtitle(DiffInspector inspector) => inspector.FileCount switch
    {
        0 => null,
        var count => UiText.PluralWith(System.Globalization.CultureInfo.InvariantCulture, "panes.changes.files", count),
    };
}
