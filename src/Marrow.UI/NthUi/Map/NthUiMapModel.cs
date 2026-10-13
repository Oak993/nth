// PORT-ONLY: web surfaces W16c (DESIGN.md 7.2 "Map: stops and routes on a built-in offline world basemap"; 10 text alternatives;
// ED-NEW-web-w16c-1). No Swift counterpart. What a `map` line holds, as the canvas and the list draw it: the stops (a label, a place
// and an optional detail), the routes (a label and the stops it goes through, by the stops' labels), each route's path along the
// great circles between its stops, the box that holds everything, and the sentence a screen reader gets. Pure data and maths.
using System.Globalization;
using Avalonia;

namespace Marrow.UI.NthUi.Map;

/// <summary>One stop of a map.</summary>
public sealed record NthUiMapStop(string Key, string Label, double Latitude, double Longitude, string? Detail)
{
    public Point Unit => NthUiMapProjection.ToUnit(Latitude, Longitude);

    /// <summary>"38.72°N, 9.14°W".</summary>
    public string Coordinates(CultureInfo culture) => NthUiMapStrings.Coordinates(culture, Latitude, Longitude);
}

/// <summary>One route: a label and the labels of the stops it goes through, in order.</summary>
public sealed record NthUiMapRoute(string Key, string Label, IReadOnlyList<string> Via);

/// <summary>A route resolved to its stops and the path between them.</summary>
public sealed record NthUiMapPath(NthUiMapRoute Route, IReadOnlyList<NthUiMapStop> Stops, IReadOnlyList<Point> Points);

public sealed class NthUiMapModel
{
    public NthUiMapModel(string title, IReadOnlyList<NthUiMapStop> stops, IReadOnlyList<NthUiMapRoute> routes)
    {
        Title = title;
        Stops = stops;
        Routes = routes;
        var paths = new List<NthUiMapPath>();
        foreach (var route in routes)
        {
            var through = new List<NthUiMapStop>();
            foreach (var label in route.Via)
            {
                if (Find(stops, label) is { } stop && (through.Count == 0 || !ReferenceEquals(through[^1], stop))) through.Add(stop);
            }
            if (through.Count >= 2) paths.Add(new NthUiMapPath(route, through, NthUiMapProjection.Route([.. through.Select(s => (s.Latitude, s.Longitude))])));
        }
        Paths = paths;
    }

    public string Title { get; }

    public IReadOnlyList<NthUiMapStop> Stops { get; }

    public IReadOnlyList<NthUiMapRoute> Routes { get; }

    /// <summary>The routes whose stops are all known and at least two, with their paths.</summary>
    public IReadOnlyList<NthUiMapPath> Paths { get; }

    /// <summary>Whether there is anything to draw.</summary>
    public bool IsEmpty => Stops.Count == 0;

    /// <summary>The stop a route names (labels compared without case or surrounding spaces).</summary>
    public static NthUiMapStop? Find(IReadOnlyList<NthUiMapStop> stops, string label)
    {
        var wanted = label.Trim();
        return stops.FirstOrDefault(s => string.Equals(s.Label.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The unit-square box of every stop and every route's path (x unwrapped across the antimeridian), or null when empty.</summary>
    public Rect? Bounds()
    {
        if (Stops.Count == 0) return null;
        var points = new List<Point>();
        foreach (var path in Paths) points.AddRange(path.Points);
        var routed = new HashSet<NthUiMapStop>(Paths.SelectMany(p => p.Stops));
        // A stop not on a route is a point of its own; one on a route is already inside its path (unwrapped).
        foreach (var stop in Stops)
        {
            if (!routed.Contains(stop)) points.Add(stop.Unit);
        }
        if (points.Count == 0) return null;
        // Stops without a route may sit across the antimeridian from the others: bring each to the side nearest the first point.
        var anchor = points[0].X;
        var xs = points.Select(p => Nearest(p.X, anchor)).ToList();
        var ys = points.Select(p => p.Y).ToList();
        return new Rect(xs.Min(), ys.Min(), xs.Max() - xs.Min(), ys.Max() - ys.Min());
    }

    private static double Nearest(double x, double anchor)
    {
        while (x - anchor > .5) x -= 1;
        while (anchor - x > .5) x += 1;
        return x;
    }

    /// <summary>The words that describe the map, as a screen reader gets them.</summary>
    public string Summary(CultureInfo culture)
    {
        if (Stops.Count == 0) return NthUiMapStrings.NoStops(culture, Title);
        var stops = string.Join(", ", Stops.Select(s => s.Detail is { Length: > 0 } d ? NthUiMapStrings.StopWithDetail(s.Label, d) : s.Label));
        var head = NthUiMapStrings.StopCount(culture, Title.Length > 0 ? Title : NthUiStrings.Map, Stops.Count, stops);
        if (Routes.Count == 0) return head;
        var routes = Routes.Select(r =>
        {
            var through = string.Join(NthUiMapStrings.RouteJoin, r.Via.Select(v => Find(Stops, v)?.Label ?? v.Trim()));
            return NthUiMapStrings.RouteSentence(r.Label, through);
        });
        return head + " " + string.Join(" ", routes);
    }
}
