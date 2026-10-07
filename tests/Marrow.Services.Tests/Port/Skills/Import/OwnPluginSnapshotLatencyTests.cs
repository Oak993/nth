using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Tests.Port.Skills.Generations;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.Skills.Import;

public sealed class OwnPluginSnapshotLatencyTests
{
    [Theory]
    [InlineData("prepare")]
    [InlineData("start")]
    [InlineData("listing")]
    public async Task A_slow_owned_plugin_snapshot_leaves_collection_available(string operation)
    {
        using var fixture = new GenerationFixture();
        var item = fixture.Add("A1", "pdf");
        fixture.Open();
        using var slow = new HeldPluginSource();
        using var service = new SkillGenerations(fixture.Paths, fixture.Store!, fixture.Protection, _ => [], ownPluginSource: slow);
        var work = Task.Run<IDisposable?>(() =>
        {
            var switches = fixture.Switches((item, true));
            return operation switch
            {
                "prepare" => Prepare(),
                "start" => service.Start("claude", "one", [item], switches, fixture.Budget),
                "listing" => service.Listing("claude", [item], switches, fixture.Budget),
                _ => throw new InvalidOperationException(operation),
            };
            IDisposable? Prepare() { service.Prepare("claude", [item], switches, fixture.Budget); return null; }
        });
        try
        {
            Assert.True(slow.Entered.Wait(TimeSpan.FromSeconds(5)));
            await Task.Run(service.Collect).WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            slow.Release.Set();
            using var result = await work.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
}
