using Marrow.Services.Assistant;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>PORT-ONLY (AS-10): a folder event source the test pokes by hand, so a debounce is proven on a manual clock with no real watcher in the way.</summary>
public sealed class FakeFolderEvents : IFolderEventSource
{
    private Action? poke;

    public string? Path { get; private set; }

    public bool Subfolders { get; private set; }

    public bool Started => poke is not null;

    public bool Disposed { get; private set; }

    public Exception? StartFails { get; set; }

    public void Start(string path, bool subfolders, Action poke)
    {
        if (StartFails is { } failure) throw failure;
        Path = path;
        Subfolders = subfolders;
        this.poke = poke;
    }

    /// <summary>Something happened in the folder.</summary>
    public void Poke() => poke?.Invoke();

    public void Dispose() => Disposed = true;
}
