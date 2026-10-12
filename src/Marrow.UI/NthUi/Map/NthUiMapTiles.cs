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
}
