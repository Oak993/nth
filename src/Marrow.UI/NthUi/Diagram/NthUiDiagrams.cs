// PORT-ONLY: web surfaces W16c (DESIGN.md 7.2; 10; ED-NEW-web-w16c-1). No Swift counterpart. What the diagram and line-art views share:
// the words they show, the numbers that pace a streaming diagram, and a small cache of drawn diagrams so a saved answer that is
// opened again (or a theme that was drawn before) appears at once without asking the renderer again.
using Marrow.Core.Web;

namespace Marrow.UI.NthUi.Diagram;

/// <summary>Words of the diagram, line-art and map views (W22 moves them into the resource files).</summary>
public static class NthUiDiagramStrings
{
    public const string Drawing = "Drawing the diagram";
    public const string DiagramFailed = "This diagram could not be drawn";
    public const string LineArtFailed = "This picture could not be drawn";
    public const string ShowSource = "Show source";
    public const string ShowDiagram = "Show diagram";
    public const string CopySource = "Copy source";
    public const string CopySvg = "Copy as SVG";

    public static string FailedAt(string what, int? line) =>
        line is { } n ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{what} (line {n})") : what;
}

/// <summary>The renderer the diagram views use, and their pacing.</summary>
public static class NthUiDiagrams
{
    /// <summary>The renderer in use now (the process's, or a test's override).</summary>
    public static IDiagramEngine Engine => DiagramEngines.Shared.Current;

    /// <summary>A streaming diagram is redrawn at most this often (seconds): a line that arrives within it waits for the next.</summary>
    public const double RedrawSeconds = .2;

    /// <summary>A wide diagram is scaled down to this fraction of its own size, and scrolls sideways beyond that.</summary>
    public const double MinScale = .62;

    /// <summary>The diagrams drawn lately, by source and theme.</summary>
    internal static DiagramCache Cache { get; } = new(32);

    /// <summary>Forgets every drawn diagram (a changed renderer, a test).</summary>
    public static void ClearCache() => Cache.Clear();
}

/// <summary>A small cache of sanitised diagrams.</summary>
internal sealed class DiagramCache(int capacity)
{
    private readonly object gate = new();
    private readonly LinkedList<string> order = new();
    private readonly Dictionary<string, (NthSvgDocument Document, LinkedListNode<string> Node)> items = new(StringComparer.Ordinal);

    public static string Key(string source, DiagramTheme theme) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source + "\n" + theme.Key)));

    public NthSvgDocument? Get(string key)
    {
        lock (gate)
        {
            if (!items.TryGetValue(key, out var entry)) return null;
            order.Remove(entry.Node);
            order.AddFirst(entry.Node);
            return entry.Document;
        }
    }

    public void Add(string key, NthSvgDocument document)
    {
        lock (gate)
        {
            if (items.TryGetValue(key, out var existing))
            {
                order.Remove(existing.Node);
                items.Remove(key);
            }
            var node = order.AddFirst(key);
            items[key] = (document, node);
            while (items.Count > capacity)
            {
                var last = order.Last!;
                order.RemoveLast();
                items.Remove(last.Value);
            }
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            order.Clear();
            items.Clear();
        }
    }
}
