using Marrow.Core.Port.Updates;

namespace Marrow.App.Tests.Fixtures;

public sealed class DeferredDevToolSource
{
    public TaskCompletionSource<(IReadOnlyList<IUpdateItem> Items, Action Restart)> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<(IReadOnlyList<IUpdateItem> Items, Action Restart)> Read() => Completion.Task;
}
