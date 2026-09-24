using Marrow.Core.Port;

namespace Marrow.Services.Git;

// Source: DroppyCode/Services/Git/DiffParser.swift (B2 1fbf81e), :1-167.
public sealed record DiffFile
{
    // :4-9
    public enum Change { Added, Deleted, Modified, Renamed }

    // :11-17
    public string Path { get; init; }
    public string? OldPath { get; init; }
    public Change FileChange { get; init; } // Swift: change
    public bool IsBinary { get; init; }
    public ValueList<DiffHunk> Hunks { get; init; }
    public long? ReportedAdditions { get; init; }
    public long? ReportedDeletions { get; init; }

    public DiffFile(string path, Change change, bool isBinary, ValueList<DiffHunk> hunks,
        string? oldPath = null, long? reportedAdditions = null, long? reportedDeletions = null)
    {
        Path = path;
        FileChange = change;
        IsBinary = isBinary;
        Hunks = hunks;
        OldPath = oldPath;
        ReportedAdditions = reportedAdditions;
        ReportedDeletions = reportedDeletions;
    }

    // :19-25
    public string Id => Path;
    public long Additions => ReportedAdditions ?? Hunks.Sum(hunk => (long)hunk.Lines.Count(line => line.LineKind == DiffLine.Kind.Addition));
    public long Deletions => ReportedDeletions ?? Hunks.Sum(hunk => (long)hunk.Lines.Count(line => line.LineKind == DiffLine.Kind.Deletion));
    public string Name => SwiftPath.LastPathComponent(Path);
    public string Directory
    {
        get
        {
            var parent = SwiftPath.DeletingLastPathComponent(Path);
            return parent.Length == 0 ? "" : parent + "/";
        }
    }
}

// :29-33
public sealed record DiffHunk(long Id, string Header, ValueList<DiffLine> Lines);

// :35-48
public sealed record DiffLine(long Id, DiffLine.Kind LineKind, string Text, long? OldNumber = null, long? NewNumber = null)
{
    public enum Kind { Context, Addition, Deletion, Note }
}

// :50-167
public static class DiffParser
{
    // :51-129
    public static ValueList<DiffFile> Parse(string patch)
    {
        var files = new List<DiffFile>();
        DiffFile? current = null;
        DiffHunk? hunk = null;
        long oldLine = 0;
        long newLine = 0;
        long lineID = 0;

        // :60-69
        void CloseHunk()
        {
            if (hunk is not null) current = current! with { Hunks = current.Hunks.Appending(hunk) };
            hunk = null;
        }

        void CloseFile()
        {
            CloseHunk();
            if (current is not null) files.Add(current);
            current = null;
        }

        var rawLines = SwiftString.Split(patch, "\n", omittingEmptySubsequences: false);
        for (var index = 0; index < rawLines.Count; index++)
        {
            var line = rawLines[index];
            // :73-78
            if (SwiftString.HasPrefix(line, "diff --git "))
            {
                CloseFile();
                current = new DiffFile(HeaderPath(line), DiffFile.Change.Modified, false, ValueList<DiffHunk>.Empty);
                continue;
            }
            if (current is null) continue;

            // :80-108
            if (hunk is null || SwiftString.HasPrefix(line, "@@"))
            {
                if (SwiftString.HasPrefix(line, "new file mode")) current = current with { FileChange = DiffFile.Change.Added };
                else if (SwiftString.HasPrefix(line, "deleted file mode")) current = current with { FileChange = DiffFile.Change.Deleted };
                else if (SwiftString.HasPrefix(line, "rename from ")) current = current with
                {
                    FileChange = DiffFile.Change.Renamed,
                    OldPath = SwiftString.DropFirst(line, SwiftString.Count("rename from "))
                };
                else if (SwiftString.HasPrefix(line, "rename to ")) current = current with
                {
                    Path = SwiftString.DropFirst(line, SwiftString.Count("rename to "))
                };
                else if (SwiftString.HasPrefix(line, "Binary files") || SwiftString.HasPrefix(line, "GIT binary patch"))
                    current = current with { IsBinary = true };
                else if (SwiftString.HasPrefix(line, "--- "))
                {
                    if (SwiftString.Equal(line, "--- /dev/null")) current = current with { FileChange = DiffFile.Change.Added };
                }
                else if (SwiftString.HasPrefix(line, "+++ "))
                {
                    current = SwiftString.Equal(line, "+++ /dev/null")
                        ? current with { FileChange = DiffFile.Change.Deleted }
                        : current with { Path = StripPrefix(SwiftString.DropFirst(line, 4)) };
                }
                else if (SwiftString.HasPrefix(line, "@@"))
                {
                    CloseHunk();
                    (oldLine, newLine) = HunkStarts(line);
                    lineID = checked(lineID + 1);
                    hunk = new DiffHunk(lineID, line, ValueList<DiffLine>.Empty);
                }
                continue;
            }

            // :111-125
            lineID = checked(lineID + 1);
            if (SwiftString.HasPrefix(line, "+"))
            {
                hunk = hunk with { Lines = hunk.Lines.Appending(new DiffLine(lineID, DiffLine.Kind.Addition, SwiftString.DropFirst(line), NewNumber: newLine)) };
                newLine = checked(newLine + 1);
            }
            else if (SwiftString.HasPrefix(line, "-"))
            {
                hunk = hunk with { Lines = hunk.Lines.Appending(new DiffLine(lineID, DiffLine.Kind.Deletion, SwiftString.DropFirst(line), OldNumber: oldLine)) };
                oldLine = checked(oldLine + 1);
            }
            else if (SwiftString.HasPrefix(line, "\\"))
                hunk = hunk with { Lines = hunk.Lines.Appending(new DiffLine(lineID, DiffLine.Kind.Note, SwiftString.DropFirst(line, 2))) };
            else if (SwiftString.HasPrefix(line, " ") || line.Length == 0)
            {
                if (line.Length == 0 && index == rawLines.Count - 1) continue;
                hunk = hunk with { Lines = hunk.Lines.Appending(new DiffLine(lineID, DiffLine.Kind.Context, SwiftString.DropFirst(line), oldLine, newLine)) };
                oldLine = checked(oldLine + 1);
                newLine = checked(newLine + 1);
            }
        }
        CloseFile();
        return new ValueList<DiffFile>(files);
    }

    // :131-137
    public static DiffFile ParseHunks(string text, string path)
    {
        var body = SwiftString.HasPrefix(text, "diff --git") ? text : $"diff --git a/{path} b/{path}\n{text}";
        var parsed = Parse(body);
        var file = parsed.Count > 0 ? parsed[0] : new DiffFile(path, DiffFile.Change.Modified, false, ValueList<DiffHunk>.Empty);
        return file with { Path = path };
    }

    // :139-154
    private static string HeaderPath(string line)
    {
        var rest = SwiftString.DropFirst(line, SwiftString.Count("diff --git "));
        var marker = rest.LastIndexOf(" b/", StringComparison.Ordinal);
        return marker >= 0 ? rest[(marker + 3)..] : StripPrefix(rest);
    }

    private static string StripPrefix(string path)
    {
        var value = path;
        if (SwiftString.HasPrefix(value, "\"") && SwiftString.HasSuffix(value, "\"") && SwiftString.Count(value) >= 2)
            value = SwiftString.DropLast(SwiftString.DropFirst(value));
        if (SwiftString.HasPrefix(value, "a/") || SwiftString.HasPrefix(value, "b/")) value = SwiftString.DropFirst(value, 2);
        return value;
    }

    // :156-166
    private static (long Old, long New) HunkStarts(string header)
    {
        var parts = SwiftString.Split(header, " ");
        static long Start(string? token)
        {
            if (token is null) return 1;
            var fields = SwiftString.Split(SwiftString.DropFirst(token), ",");
            var digits = fields.Count > 0 ? fields[0] : "1";
            return SwiftNumber.ParseInt(digits) ?? 1;
        }
        return (Start(parts.FirstOrDefault(part => SwiftString.HasPrefix(part, "-"))),
            Start(parts.FirstOrDefault(part => SwiftString.HasPrefix(part, "+"))));
    }
}
