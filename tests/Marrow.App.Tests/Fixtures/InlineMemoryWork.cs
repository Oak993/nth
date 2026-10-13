using Marrow.App.Assistant.Views;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Dextra AS-12: the Memory model's file work run on the calling thread, so a command has finished when it returns.</summary>
public sealed class InlineMemoryWork : IMemoryWork
{
    public Task<T> Run<T>(Func<T> work) => Task.FromResult(work());

    public void Post(Action ui) => ui();
}
