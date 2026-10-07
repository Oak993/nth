using System.Text;
using Marrow.Services.Port.Skills.Custom;

namespace Marrow.Services.Tests.Port.Skills.Custom;

[Trait("Feature", "SK-2")]
[Trait("Rule", "R-TEST-4")]
public sealed class ParallelCustomInstallTests
{
    [Fact]
    public async Task Selected_items_copy_in_parallel_and_commit_one_registry_without_losing_either_item()
    {
        var fake = new CustomSourceFake { Files = new() { ["one/SKILL.md"] = Encoding.UTF8.GetBytes("---\nname: one\ndescription: O.\n---\nOne"), ["two/SKILL.md"] = Encoding.UTF8.GetBytes("---\nname: two\ndescription: T.\n---\nTwo") } };
        var files = new ParallelCustomFiles(fake);
        var service = new CustomSources("/fake/own", files, fake, TimeProvider.System);
        var candidates = await service.Check(new(CustomAddKind.Skills, CustomAddSource.Folder, "fixture", "", ""), CancellationToken.None);
        var first = service.Add(candidates[0], new CustomSourceProgress(), CancellationToken.None);
        await files.First.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var second = service.Add(candidates[1], new CustomSourceProgress(), CancellationToken.None);
        try { await files.Both.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
        finally
        {
            files.Release.TrySetResult();
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(20));
        }
        Assert.Equal(2, fake.Open().List(CustomAddKind.Skills).Count);
        Assert.Equal(["one", "two"], fake.Open().Snapshot().Select(s => s.Name).Order());
    }
}
