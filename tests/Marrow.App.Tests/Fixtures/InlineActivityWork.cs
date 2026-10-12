using Marrow.App.Assistant.Views;

namespace Marrow.App.Tests.Fixtures;

/// <summary>PORT-ONLY (AS-21): the Activity model's slow work run on the calling thread, so a test sees every step done when a call returns.</summary>
public sealed class InlineActivityWork : IActivityWork
{
    public Task<T> RunAsync<T>(Func<CancellationToken, T> work, CancellationToken cancellation)
    {
        try
        {
            cancellation.ThrowIfCancellationRequested();
            return Task.FromResult(work(cancellation));
        }
        catch (Exception error)
        {
            return Task.FromException<T>(error);
        }
    }
}
