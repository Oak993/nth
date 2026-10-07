// RR-48: regressions for manual placement, using disposable files and the downloader's real writer turn.
using System.Reflection;
using Marrow.Services.Port.Downloads;
using Marrow.Services.Port.Skills.Fetch;

namespace Marrow.Services.Tests.Port.Skills.Fetch;

[Trait("Decision", "ED-NEW-designgaps-1")]
public sealed class DesignGapsManualTests
{
    [Fact]
    public async Task Placement_waits_for_the_downloaders_turn_for_both_entrypoints()
    {
        await using var rig = new LoopbackRig();
        var folder = ManualFiles.ManualFolder(rig.Root);
        Directory.CreateDirectory(folder);
        var source = Path.Combine(folder, "manual.bin");
        File.WriteAllBytes(source, rig.Big.Bytes);
        foreach (var loose in new[] { false, true })
        {
            var part = rig.PartOf(rig.Big);
            var turn = await PartFileTurns.Take(part, CancellationToken.None);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var placement = Task.Run(() =>
            {
                entered.SetResult();
                if (loose) Assert.Equal("tool.zip", ManualFiles.Place(rig.Root, rig.Tool, source));
                else Assert.Equal(1, ManualFiles.PlaceFolder(rig.Root, rig.Tool));
            });
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.False(await Task.WhenAny(placement, Task.Delay(200)) == placement);
            }
            finally { PartFileTurns.Give(part, turn); }
            await placement.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(rig.Big.Bytes, File.ReadAllBytes(part));
        }
    }

    [Fact]
    public async Task The_bytes_published_are_the_bytes_hashed_from_one_opening()
    {
        await using var rig = new LoopbackRig();
        var opens = 0;
        Stream Open() => new MemoryStream(++opens == 1 ? rig.Big.Bytes : new byte[rig.Big.Bytes.Length]);
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.True(Keep(rig.Root, [(rig.Big.Length, rig.Big.Sha256Hex)], found, Open));
        Assert.Equal(1, opens);
        Assert.Equal(rig.Big.Bytes, File.ReadAllBytes(rig.PartOf(rig.Big)));
        Assert.Equal([rig.Big.Sha256Hex], found);
        Assert.Empty(Directory.EnumerateFiles(ManualFiles.PartFolder(rig.Root), "*.tmp"));
    }

    [Fact]
    public async Task Failed_publication_is_not_counted_and_removes_its_temporary_file()
    {
        await using var rig = new LoopbackRig();
        Directory.CreateDirectory(rig.PartOf(rig.Big)); // deterministic failed rename on either OS
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.Throws<IOException>(() => Keep(rig.Root, [(rig.Big.Length, rig.Big.Sha256Hex)], found, () => new MemoryStream(rig.Big.Bytes)));
        Assert.Empty(found);
        Assert.Empty(Directory.EnumerateFiles(ManualFiles.PartFolder(rig.Root), "*.tmp"));
    }

    private static bool Keep(string root, List<(long Size, string Sha256)> inputs, HashSet<string> found, Func<Stream> open)
    {
        var method = typeof(ManualFiles).GetMethod("Keep", BindingFlags.Static | BindingFlags.NonPublic)!;
        try { return (bool)method.Invoke(null, [root, inputs, found, open])!; }
        catch (TargetInvocationException e) when (e.InnerException is { } inner)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(inner).Throw();
            throw;
        }
    }
}
