using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Providers;

// Audit fix (svc): NativeFileTools.swift:40,79,92,122 mark readFile, writeFile, editFile and listFiles @concurrent, so their file and
// directory work runs off the caller's executor; R-ISO-2 makes that Task.Run at the boundary. A call therefore returns before the
// work is done, where a plain async method with no await has finished all of it by the time it returns.
public sealed class NativeFileToolsConcurrentTests
{
    [Trait("Req", "PR-PROVAPI-178")] // NativeFileTools.swift:40-77
    [Fact]
    public async Task ReadFile_hands_its_file_work_to_the_pool_instead_of_running_it_in_the_call()
    {
        using var folder = new ScratchFolder();
        File.WriteAllText(folder.Child("big.txt"), new string('x', 900_000));
        var tools = new NativeFileTools(folder.Root);

        var pending = tools.ReadFile("big.txt", startLine: 1, lineCount: 1);

        Assert.False(pending.IsCompleted);
        Assert.StartsWith("(lines 1-1 of 1)", await pending, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-PROVAPI-184")] // NativeFileTools.swift:122-159
    [Fact]
    public async Task ListFiles_hands_its_directory_walk_to_the_pool_instead_of_running_it_in_the_call()
    {
        using var folder = new ScratchFolder();
        for (var i = 0; i < 250; i++) File.WriteAllText(folder.Child("f" + i + ".txt"), "x");
        var tools = new NativeFileTools(folder.Root);

        var pending = tools.ListFiles("", recursive: true);

        Assert.False(pending.IsCompleted);
        Assert.Contains("f0.txt", await pending, StringComparison.Ordinal);
    }
}
