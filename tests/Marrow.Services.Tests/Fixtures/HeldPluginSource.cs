using Marrow.Services.Port.Skills.Generations;

namespace Marrow.Services.Tests.Fixtures;

internal sealed class HeldPluginSource : IOwnPluginSource, IDisposable
{
    public ManualResetEventSlim Entered { get; } = new();
    public ManualResetEventSlim Release { get; } = new();
    public IReadOnlyList<OwnGenerationPlugin> SnapshotPlugins()
    {
        Entered.Set();
        if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("test did not release the plugin snapshot");
        return [];
    }
    public void Dispose() { Entered.Dispose(); Release.Dispose(); }
}
