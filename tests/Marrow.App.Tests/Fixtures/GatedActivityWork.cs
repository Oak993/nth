using Marrow.App.Assistant.Views;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// PORT-ONLY (AS-21): the Activity model's slow work held until a test lets it go, in any order, so a test can make a slow
/// answer arrive after a newer one. While <see cref="Gated"/> is false the work runs at once, as <see cref="InlineActivityWork"/> does.
/// </summary>
public sealed class GatedActivityWork : IActivityWork
{
    private readonly List<Action> held = [];

    public bool Gated { get; set; }

    /// <summary>How many pieces of work have been held (released or not).</summary>
    public int Held => held.Count;

    public Task<T> RunAsync<T>(Func<CancellationToken, T> work, CancellationToken cancellation)
    {
        if (!Gated) return new InlineActivityWork().RunAsync(work, cancellation);
        var source = new TaskCompletionSource<T>();
        held.Add(() =>
        {
            try
            {
                cancellation.ThrowIfCancellationRequested();
                source.SetResult(work(cancellation));
            }
            catch (OperationCanceledException)
            {
                source.TrySetCanceled();
            }
            catch (Exception error)
            {
                source.TrySetException(error);
            }
        });
        return source.Task;
    }

    /// <summary>Lets the piece of work held at <paramref name="index"/> (in the order they were asked for) run now.</summary>
    public void Release(int index)
    {
        var run = held[index];
        held[index] = () => { };
        run();
    }
}
