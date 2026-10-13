using Marrow.App.Assistant.Views;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Dextra AS-19: the Teach model's file work, run on the caller's thread so a test sees its outcome at once.</summary>
internal sealed class InlineTeachWork : ITeachWork
{
    public Task<T> Run<T>(Func<T> work) => Task.FromResult(work());
}
