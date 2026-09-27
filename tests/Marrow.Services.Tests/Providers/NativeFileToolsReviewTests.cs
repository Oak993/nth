using System.Reflection;
using System.Text;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Providers;

public sealed class NativeFileToolsReviewTests
{
    [Fact]
    [Trait("Req", "PR-PROVAPI-176")] // Services/Providers/NativeFileTools.swift:22-28
    public void The_sandbox_does_not_fold_the_project_path_case()
    {
        var tools = new NativeFileTools(@"C:\project");
        Assert.Equal(@"C:\project\x", tools.ResolveURL(@"C:\project\x"));
        Assert.Equal("That path is outside the project. Stay inside C:\\project.",
            Assert.Throws<ProviderError>(() => tools.ResolveURL(@"C:\PROJECT\x")).ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-185")] // Services/Providers/NativeFileTools.swift:139-141
    public async Task Recursive_listing_skips_plain_files_with_every_skipped_name()
    {
        using var f = new ScratchFolder();
        foreach (var name in new[] { "node_modules", "build", "dist", "DerivedData", "Pods", "target", "venv" })
            await File.WriteAllTextAsync(f.Child(name), "x");
        await File.WriteAllTextAsync(f.Child("keep.txt"), "x");
        Assert.Equal("keep.txt", await new NativeFileTools(f.Root).ListFiles("", true));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-183")] // Services/Providers/NativeFileTools.swift:98
    public async Task Invalid_utf8_propagates_the_decoder_failure_and_preserves_the_file()
    {
        using var f = new ScratchFolder();
        await File.WriteAllBytesAsync(f.Child("bad.txt"), [0xff]);
        await Assert.ThrowsAsync<DecoderFallbackException>(() => new NativeFileTools(f.Root).EditFile("bad.txt", "x", "y"));
        Assert.Equal(new byte[] { 0xff }, await File.ReadAllBytesAsync(f.Child("bad.txt")));
    }

    [Theory]
    [InlineData("before e\u0301 after", "\u00e9", "before X after")]
    [InlineData("before \u00e9 after", "e\u0301", "before X after")]
    [InlineData("e\u0301 e\u0301", "e\u0301", null)]
    [InlineData("e\u0301 \u00e9", "\u00e9", null)]
    [Trait("Req", "PR-PROVAPI-183")] // Services/Providers/NativeFileTools.swift:98-118
    public async Task Canonically_equivalent_matches_count_and_replace_the_original_utf16_range(string text, string old, string? expected)
    {
        using var f = new ScratchFolder();
        var p = f.Child("f.txt");
        await File.WriteAllTextAsync(p, text, new UTF8Encoding(false));
        var tools = new NativeFileTools(f.Root);
        if (expected is null)
            Assert.Equal("That text appears 2 times in f.txt. Include more context so it matches once.",
                (await Assert.ThrowsAsync<ProviderError>(() => tools.EditFile("f.txt", old, "X"))).ErrorDescription);
        else
        {
            await tools.EditFile("f.txt", old, "X");
            Assert.Equal(expected, await File.ReadAllTextAsync(p));
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-182")] // Services/Providers/NativeFileTools.swift:7-13,81,94
    public async Task Writes_and_edits_in_different_instances_wait_for_the_one_shared_actor()
    {
        using var f = new ScratchFolder();
        await File.WriteAllTextAsync(f.Child("edit.txt"), "old");
        var actor = typeof(NativeFileTools).GetField("Serializer", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var held = (Task)actor.GetType().GetMethod("Run")!.Invoke(actor, [new Action(() =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Actor test barrier");
        })])!;
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        Task write;
        Task edit;
        try
        {
            write = new NativeFileTools(f.Root).WriteFile("write.txt", "written");
            edit = new NativeFileTools(f.Root).EditFile("edit.txt", "old", "new");
            Assert.False(write.IsCompleted);
            Assert.False(edit.IsCompleted);
            Assert.False(File.Exists(f.Child("write.txt")));
            Assert.Equal("old", await File.ReadAllTextAsync(f.Child("edit.txt")));
        }
        finally { release.Set(); }
        await held;
        await Task.WhenAll(write, edit);
        Assert.Equal("written", await File.ReadAllTextAsync(f.Child("write.txt")));
        Assert.Equal("new", await File.ReadAllTextAsync(f.Child("edit.txt")));
    }

    // STOP Services.Providers.NativeFileTools #1
    // Kind: pending owner decision. Swift :22-27, PR-PROVAPI-176; ED-137 excludes this sandbox gate.
    // Preserve the source's case-sensitive comparison; any Windows case-fold accommodation needs an owner ruling.
}
