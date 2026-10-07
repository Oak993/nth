namespace Marrow.App.Tests.Fixtures;

/// <summary>A subscription handle that runs a callback when it is disposed, for the tests that script a change source.</summary>
internal sealed class CallbackDisposable(Action dispose) : IDisposable
{
    public void Dispose() => dispose();
}
