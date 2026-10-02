namespace Marrow.App.Tests.Fixtures;

/// <summary>The revert preview a <see cref="MessageEditorRig"/> thread gives its editor.</summary>
public enum EditorPreviewKind
{
    /// <summary>The turn changed files and every one is still as the turn left it: the undo toggle is offered.</summary>
    Restorable,

    /// <summary>The turn changed files, and something changed the first one again afterwards: it is kept.</summary>
    FirstKept,

    /// <summary>Every file the turn changed has changed again since: nothing is restorable.</summary>
    AllKept,

    /// <summary>The turn touched no files: the preview resolves with an empty file list.</summary>
    NoFiles,

    /// <summary>The turn has no file checkpoint: the preview fails.</summary>
    Unavailable,
}
