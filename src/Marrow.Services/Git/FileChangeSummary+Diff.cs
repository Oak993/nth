using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.Services.Git;

// Source: DroppyCode/Services/Git/FileChangeSummary+Diff.swift (B2 1fbf81e), :3-18.
// Swift adds this initializer to a Core model from Services. C# cannot add a constructor across assemblies, so this
// Services factory returns the same Core value without introducing a project reference.
public static class FileChangeSummaryDiff
{
    // :5-17
    public static FileChangeSummary Create(ValueList<DiffFile> files)
    {
        var entries = new List<FileChangeSummary.File>(files.Count);
        long additions = 0;
        long deletions = 0;
        foreach (var file in files)
        {
            var added = file.Additions;
            var deleted = file.Deletions;
            entries.Add(new FileChangeSummary.File(file.Path, added, deleted, file.IsBinary));
            additions = checked(additions + added);
            deletions = checked(deletions + deleted);
        }

        return new FileChangeSummary(new ValueList<FileChangeSummary.File>(entries), additions, deletions);
    }
}
