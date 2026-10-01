using Marrow.Core.Port;
using Marrow.Services.Git;

namespace Marrow.App.Tests.Fixtures;

// Diff files, hunks and lines for the changes inspector's row grouping. Helper types live here, never inside a test
// class.
public static class DiffInspectorSamples
{
    private static long nextId;

    /// <summary>One line with a fresh id; the numbers default to none.</summary>
    public static DiffLine Line(DiffLine.Kind kind, string text, long? oldNumber = null, long? newNumber = null) =>
        new(Interlocked.Increment(ref nextId), kind, text, oldNumber, newNumber);

    /// <summary>Lines of one kind, with the text "prefix 1", "prefix 2" and so on.</summary>
    public static DiffLine[] Lines(DiffLine.Kind kind, int count, string prefix = "line") =>
        Enumerable.Range(1, count).Select(index => Line(kind, prefix + " " + index, index, index)).ToArray();

    public static DiffHunk Hunk(string header, params DiffLine[] lines) => new(1, header, new ValueList<DiffLine>(lines));

    public static DiffFile FileOf(params DiffHunk[] hunks) =>
        new("Sources/App/File.swift", DiffFile.Change.Modified, false, new ValueList<DiffHunk>(hunks));
}
