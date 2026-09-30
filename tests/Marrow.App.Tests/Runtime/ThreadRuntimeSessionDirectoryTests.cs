using System.Reflection;
using Marrow.App.Runtime;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Runtime;

// Windows boundary only: no session construction/start, process, environment or global override.
public sealed class ThreadRuntimeSessionDirectoryTests
{
    [Theory]
    [Trait("Req", "PR-RUNTIME-033")] // ThreadRuntime.swift:1537; ProviderRegistry.FileUrl Windows adaptation
    [InlineData(@"C:\work", @"C:\work")]
    [InlineData(@"C:\", @"C:\")]
    [InlineData("C:/work/sub", @"C:\work\sub")]
    [InlineData(@"\\server\share\work", @"\\server\share\work")]
    [InlineData(@"\\server\share\", @"\\server\share\")]
    [InlineData(@"\\?\C:\work", @"\\?\C:\work")]
    [InlineData(@"C:\work\a #?%", @"C:\work\a #?%")]
    [InlineData("C:/caf\u00E9", "C:\\caf\u00E9")]
    public void The_session_directory_reaches_Windows_as_a_native_path(string directory, string expected)
    {
        var method = typeof(ThreadRuntime).GetMethod("SessionDirectoryUrl", BindingFlags.Static | BindingFlags.NonPublic)!;
        var url = Assert.IsType<SwiftURL>(method.Invoke(null, [directory]));
        Assert.Equal(expected, url.Path);
        Assert.Null(url.Query);
        Assert.Null(url.Fragment);
    }

    [Theory]
    [Trait("Req", "PR-RUNTIME-033")] // ThreadRuntime.swift:1537; native working-directory boundary
    [Trait("Req", "PR-RUNTIME-192")] // ThreadRuntime.swift:3501-3521; FileEdits appends the patch path to its repository root
    [InlineData(false)]
    [InlineData(true)]
    public async Task Appending_and_file_edits_preserve_a_readable_native_path_with_optional_trailing_separator(bool trailingSeparator)
    {
        using var h = new Marrow.App.Tests.Fixtures.AppModelHarness();
        var directory = h.NewFolder("repository # % caf\u00E9");
        var nested = System.IO.Path.Combine(directory, "nested");
        System.IO.Directory.CreateDirectory(nested);
        var expected = System.IO.Path.Combine(nested, "sample.txt");
        System.IO.File.WriteAllText(expected, "reference");
        var input = directory + (trailingSeparator ? System.IO.Path.DirectorySeparatorChar.ToString() : "");
        var method = typeof(ThreadRuntime).GetMethod("SessionDirectoryUrl", BindingFlags.Static | BindingFlags.NonPublic)!;
        var url = Assert.IsType<SwiftURL>(method.Invoke(null, [input]));
        var appended = url.Appending("nested/sample.txt");
        Assert.Null(appended.Query);
        Assert.Null(appended.Fragment);
        Assert.Equal(System.IO.Path.GetFullPath(expected), System.IO.Path.GetFullPath(appended.Path));
        Assert.Equal("reference", System.IO.File.ReadAllText(appended.Path));

        const string patch = "diff --git a/nested/sample.txt b/nested/sample.txt\n"
            + "--- a/nested/sample.txt\n+++ b/nested/sample.txt\n@@ -1 +1 @@\n-before\n+after\n";
        var edit = Assert.Single(await ThreadRuntime.FileEdits(patch, url));
        Assert.Equal(System.IO.Path.GetFullPath(expected), System.IO.Path.GetFullPath(edit.Path));
        Assert.Equal("reference", System.IO.File.ReadAllText(edit.Path));
        Assert.Equal(patch, edit.Diff);
        Assert.Equal((1L, 1L), (edit.Additions, edit.Deletions));
    }
}
