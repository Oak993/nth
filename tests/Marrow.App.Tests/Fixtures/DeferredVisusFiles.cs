using Avalonia.Controls;
using Marrow.App.Views.Artifacts;

namespace Marrow.App.Tests.Fixtures;

public sealed class DeferredVisusFiles : ITabulaFiles
{
    public TaskCompletionSource<string?> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Saves { get; private set; }
    public Task<string?> SaveAsync(Control owner, TabulaSaveRequest request) { Saves++; return Answer.Task; }
    public Task<IReadOnlyList<string>> OpenAsync(Control owner, string title, bool multiple) => Task.FromResult<IReadOnlyList<string>>([]);
}
