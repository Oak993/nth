// PORT-ONLY: web surfaces W16c (DESIGN.md 7.2 "Street tiles only if the user adds a tile source in Settings; off by default because it
// sends locations to a third party"; 16 settings defaults "Map street tiles source (none)"; ED-NEW-web-w16c-1). No Swift counterpart.
// The seam a tile source plugs into. Nothing in this unit fetches a tile: the Settings unit (W10) binds a source the reader added,
// whose fetching, caching and consent are its own; until then <see cref="NthUiMapTiles.Source"/> is null and the map is the offline
// basemap alone. A source answers from memory (it returns the tile it has, or null and fetches in the background), and says when a
// tile arrived so the map draws again.
using Avalonia.Media;
using Marrow.Core.Port;

namespace Marrow.UI.NthUi.Map;

/// <summary>A source of 256 DIP map tiles in the usual z/x/y scheme.</summary>
public interface INthUiMapTiles
{
    /// <summary>The deepest zoom the source has.</summary>
    int MaxZoom { get; }

    /// <summary>The credit the source's terms ask the map to show.</summary>
    string Attribution { get; }

    /// <summary>The tile now, or null if it is not here yet (the source fetches it and raises <see cref="TilesChanged"/>).</summary>
    IImage? Tile(int zoom, int x, int y);

    /// <summary>A tile arrived.</summary>
    event Action? TilesChanged;
}

public static class NthUiMapTiles
{
    private sealed class NoTiles : INthUiMapTiles
    {
        public static NoTiles Instance { get; } = new();

        public int MaxZoom => 0;

        public string Attribution => "";

        public IImage? Tile(int zoom, int x, int y) => null;

        public event Action? TilesChanged
        {
            add { }
            remove { }
        }
    }

    /// <summary>The reader's tile source, or none: the offline basemap alone.</summary>
    public static Ambient<INthUiMapTiles> Source { get; } = new(NoTiles.Instance);

    /// <summary>Whether a source other than none is bound.</summary>
    public static bool IsBound => Source.Current is not NoTiles;

    private static INthUiMapTiles? forwarding;

    /// <summary>
    /// Raised when a tile arrived or the source was replaced (<see cref="Use"/>); the maps draw again. It may be raised on any thread, and a
    /// listener posts to the UI thread. (W10, ED-NEW-web-w10-5.)
    /// </summary>
    public static event Action? Changed;

    /// <summary>
    /// Binds the reader's tile source for the process, or none (null): the maps draw the offline basemap alone. The source it replaces is
    /// let go, so its pending fetches stop (a source that is disposable is disposed). Settings calls this when the address changes.
    /// </summary>
    public static void Use(INthUiMapTiles? tiles)
    {
        var previous = forwarding;
        if (previous is not null) previous.TilesChanged -= Forward;
        forwarding = tiles;
        Source.SetProcessDefault(tiles ?? NoTiles.Instance);
        if (tiles is not null) tiles.TilesChanged += Forward;
        (previous as IDisposable)?.Dispose();
        Changed?.Invoke();
    }

    private static void Forward() => Changed?.Invoke();
}
