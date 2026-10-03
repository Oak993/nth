namespace Marrow.App.Tests.Fixtures;

/// <summary>An IProgress that reports on the caller's thread at once (Progress&lt;T&gt; posts to a context).</summary>
public sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
