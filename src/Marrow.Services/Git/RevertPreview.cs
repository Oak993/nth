using Marrow.Core.Port;

namespace Marrow.Services.Git;

// Source: DroppyCode/Services/Git/RevertPreview.swift (B2 1fbf81e), :1-42.

/// <summary>
/// :3-6 <c>struct RevertPreview: Sendable</c>: the files undoing a thread's changes would put back, with the counts of
/// what it did to them. A file the checkout has changed again since the thread last left it is somebody else's work
/// now: it is listed, marked kept, and left alone.
/// </summary>
/// <remarks>
/// A struct, so an immutable sealed record (R-VAL-1); Swift <c>Int</c> is <c>long</c> (R-NUM-4).
/// </remarks>
public sealed record RevertPreview
{
    /// <summary>
    /// :7-15 <c>struct File: Identifiable, Sendable</c>. The positional parameters are Swift's memberwise
    /// initializer, <c>isKept</c> defaulting to false (:12).
    /// </summary>
    public sealed record File(string Path, long Additions, long Deletions, bool IsBinary, bool IsKept = false)
    {
        // :14
        public string Id => Path;
    }

    // :17
    public ValueList<File> Files { get; init; }

    // :19-21
    public RevertPreview(ValueList<File> files)
    {
        Files = files;
    }

    /// <summary>:23-24 The files that will actually be put back.</summary>
    public ValueList<File> Restorable => new(Files.Where(file => !file.IsKept));

    // :25 reduce(0) { $0 + $1.additions }: Swift's + traps on overflow, so the sum is checked.
    public long Additions
    {
        get
        {
            long total = 0;
            foreach (var file in Restorable) total = checked(total + file.Additions);
            return total;
        }
    }

    // :26 reduce(0) { $0 + $1.deletions }, checked as :25.
    public long Deletions
    {
        get
        {
            long total = 0;
            foreach (var file in Restorable) total = checked(total + file.Deletions);
            return total;
        }
    }

    /// <summary>
    /// :28-41 From <c>git diff --numstat -z</c>: added, removed and path per file, <c>-</c> counts for binaries.
    /// Throws <see cref="CocoaError"/> <c>.fileReadCorruptFile</c> (ED-100) on any malformed record; the preview's
    /// files are assigned only after every record parses (:40).
    /// </summary>
    public RevertPreview(string numstat)
    {
        var files = new List<File>();
        // :31 split(separator: "\0"): Character separator, empty subsequences omitted.
        foreach (var entry in SwiftString.Split(numstat, "\0"))
        {
            // :32
            var fields = SwiftString.Split(entry, "\t", maxSplits: 2, omittingEmptySubsequences: false);
            // :33
            if (fields.Count != 3 || fields[2].Length == 0) throw new CocoaError(CocoaError.ErrorCode.FileReadCorruptFile);
            // :34
            var binary = SwiftString.Equal(fields[0], "-") && SwiftString.Equal(fields[1], "-");
            // :35-37 Int(String) semantics (R-NUM-1): Int.min parses and fails >= 0; below it is nil. No negation here,
            // so no count can overflow.
            if ((binary ? 0L : SwiftNumber.ParseInt(fields[0])) is not { } added
                || (binary ? 0L : SwiftNumber.ParseInt(fields[1])) is not { } removed
                || added < 0 || removed < 0)
            {
                throw new CocoaError(CocoaError.ErrorCode.FileReadCorruptFile);
            }
            // :38 String(fields[2]) keeps the path's scalars as they are.
            files.Add(new File(fields[2], added, removed, binary));
        }
        // :40
        Files = new ValueList<File>(files);
    }
}
