using System.Text;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Runtime;

public sealed class ThreadRuntimeFileEditsTests
{
    [Fact]
    [Trait("Req", "PR-RUNTIME-192")] // part8.md:84; part7.md:1257-1267
    public async Task File_edits_returns_one_absolute_file_edit_per_patch_section()
    {
        var repositoryRoot = SwiftURL.Create("file:///workspace/")!;
        var firstSection =
            "diff --git a/src/first.cs b/src/first.cs\n" +
            "index 1234567..89abcde 100644\n" +
            "--- a/src/first.cs\n" +
            "+++ b/src/first.cs\n" +
            "@@ -1 +1,2 @@\n" +
            "-before\n" +
            "+after\n" +
            "+added";
        var secondSection =
            "diff --git a/README.md b/README.md\n" +
            "index 1234567..89abcde 100644\n" +
            "--- a/README.md\n" +
            "+++ b/README.md\n" +
            "@@ -1,2 +1 @@\n" +
            "-old one\n" +
            "-old two\n" +
            "+new\n";

        var edits = await ThreadRuntime.FileEdits(firstSection + "\n" + secondSection, repositoryRoot);

        Assert.Equal(2, edits.Count);
        Assert.Equal(repositoryRoot.Appending("src/first.cs").Path, edits[0].Path);
        Assert.Equal(firstSection, edits[0].Diff);
        Assert.Equal((2L, 1L), (edits[0].Additions, edits[0].Deletions));
        Assert.Equal(repositoryRoot.Appending("README.md").Path, edits[1].Path);
        Assert.Equal(secondSection, edits[1].Diff);
        Assert.Equal((1L, 2L), (edits[1].Additions, edits[1].Deletions));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-154")] // part7.md:1243-1253, 1257-1262
    [Trait("Req", "PR-RUNTIME-192")] // part8.md:84
    public async Task File_edits_caps_each_section_at_200000_utf8_bytes_and_keeps_its_stats()
    {
        const int limit = 200_000;
        var repositoryRoot = SwiftURL.Create("file:///workspace/")!;
        var exactPrefix = SingleAddedLinePrefix("exact.txt");
        var exactPayloadBytes = limit - Encoding.UTF8.GetByteCount(exactPrefix + "\n");
        var exactSection = exactPrefix + new string('x', exactPayloadBytes) + "\n";
        var overSection = SingleAddedLinePrefix("over.txt") + new string('é', 100_000) + "\n";
        Assert.Equal(limit, Encoding.UTF8.GetByteCount(exactSection));
        Assert.True(Encoding.UTF8.GetByteCount(overSection) > limit);
        Assert.True(overSection.Length < limit);

        var edits = await ThreadRuntime.FileEdits(overSection + exactSection, repositoryRoot);

        Assert.Equal(2, edits.Count);
        Assert.Null(edits[0].Diff);
        Assert.Equal((1L, 0L), (edits[0].Additions, edits[0].Deletions));
        Assert.Equal(exactSection, edits[1].Diff);
        Assert.Equal((1L, 0L), (edits[1].Additions, edits[1].Deletions));
    }

    private static string SingleAddedLinePrefix(string path)
    {
        return
            $"diff --git a/{path} b/{path}\n" +
            $"--- a/{path}\n" +
            $"+++ b/{path}\n" +
            "@@ -0,0 +1,1 @@\n+";
    }
}
