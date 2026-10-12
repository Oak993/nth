// PORT-ONLY: web surfaces W16c (DESIGN.md 7.2 "Natural Earth ... projected in C#, drawn natively. Pan and zoom by pointer and
// keyboard"; ED-NEW-web-w16c-1). No Swift counterpart. The map's maths, with no drawing in it: Web Mercator between longitude and
// latitude and the unit square (x across the world 0 to 1, y down it 0 to 1, as slippy maps number them, so zoom 0 is a world
// 256 DIPs wide and each zoom doubles it), the camera that looks at it (centre, zoom, viewport; pan, zoom about a point, fit a
// box, keep the world on screen), and great-circle routes. Everything is plain doubles, so it is tested without a window.
using Avalonia;

namespace Marrow.UI.NthUi.Map;

public static class NthUiMapProjection
{
    /// <summary>Web Mercator's latitude limit.</summary>
    public const double MaxLatitude = 85.0511287798;

    /// <summary>The world's width in DIPs at zoom 0.</summary>
    public const double TileSize = 256;

    public static double LonToX(double longitude) => (longitude + 180) / 360;

    public static double LatToY(double latitude)
    {
        var s = Math.Sin(Math.Clamp(latitude, -MaxLatitude, MaxLatitude) * Math.PI / 180);
        return 0.5 - Math.Log((1 + s) / (1 - s)) / (4 * Math.PI);
    }

    public static double XToLon(double x) => x * 360 - 180;

    public static double YToLat(double y)
    {
        var n = Math.PI - 2 * Math.PI * y;
        return 180 / Math.PI * Math.Atan(0.5 * (Math.Exp(n) - Math.Exp(-n)));
    }

    /// <summary>The world's width in DIPs at <paramref name="zoom"/>.</summary>
    public static double WorldSize(double zoom) => TileSize * Math.Pow(2, zoom);

    /// <summary>A place as a point of the unit square.</summary>
    public static Point ToUnit(double latitude, double longitude) => new(LonToX(longitude), LatToY(latitude));

    /// <summary>The great-circle distance between two places, in degrees of arc.</summary>
    public static double ArcDegrees(double lat1, double lon1, double lat2, double lon2)
    {
        double r(double d) => d * Math.PI / 180;
        var h = Math.Pow(Math.Sin(r(lat2 - lat1) / 2), 2) + Math.Cos(r(lat1)) * Math.Cos(r(lat2)) * Math.Pow(Math.Sin(r(lon2 - lon1) / 2), 2);
        return 2 * Math.Asin(Math.Min(1, Math.Sqrt(h))) * 180 / Math.PI;
    }

    /// <summary>
    /// The path of a route through places, as points of the unit square: each leg follows the great circle between its ends
    /// (about one point a degree of arc, so a short hop is a straight line and a long flight curves poleward), and x is carried
    /// across the antimeridian without a jump, so a route over the Pacific is one unbroken line (draw it at -1, 0 and +1 worlds).
    /// </summary>
    public static IReadOnlyList<Point> Route(IReadOnlyList<(double Latitude, double Longitude)> places)
    {
        var path = new List<Point>();
        if (places.Count == 0) return path;
        var first = ToUnit(places[0].Latitude, places[0].Longitude);
        path.Add(first);
        var previousX = first.X;
        for (var i = 1; i < places.Count; i++)
        {
            var (lat1, lon1) = places[i - 1];
            var (lat2, lon2) = places[i];
            var arc = ArcDegrees(lat1, lon1, lat2, lon2);
            var steps = Math.Clamp((int)Math.Ceiling(arc), 1, 180);
            for (var s = 1; s <= steps; s++)
            {
                var (lat, lon) = Slerp(lat1, lon1, lat2, lon2, arc, (double)s / steps);
                var point = ToUnit(lat, lon);
                var x = point.X;
                while (x - previousX > .5) x -= 1;
                while (previousX - x > .5) x += 1;
                path.Add(new Point(x, point.Y));
                previousX = x;
            }
        }
        return path;
    }

    private static (double Latitude, double Longitude) Slerp(double lat1, double lon1, double lat2, double lon2, double arcDegrees, double t)
    {
        if (arcDegrees < 1e-9) return (lat2, lon2);
        double r(double d) => d * Math.PI / 180;
        var delta = r(arcDegrees);
        var a = Math.Sin((1 - t) * delta) / Math.Sin(delta);
        var b = Math.Sin(t * delta) / Math.Sin(delta);
        var x = a * Math.Cos(r(lat1)) * Math.Cos(r(lon1)) + b * Math.Cos(r(lat2)) * Math.Cos(r(lon2));
        var y = a * Math.Cos(r(lat1)) * Math.Sin(r(lon1)) + b * Math.Cos(r(lat2)) * Math.Sin(r(lon2));
        var z = a * Math.Sin(r(lat1)) + b * Math.Sin(r(lat2));
        return (Math.Atan2(z, Math.Sqrt(x * x + y * y)) * 180 / Math.PI, Math.Atan2(y, x) * 180 / Math.PI);
    }
}

/// <summary>What the map is looking at: the unit-square point at the middle of the viewport and a zoom, over a viewport in DIPs.</summary>
public sealed class NthUiMapCamera
{
    /// <summary>The deepest zoom of the offline basemap (the data is 1:50m; deeper only blurs it).</summary>
    public const double MaxZoom = 10;

    public double CenterX { get; set; } = .5;

    public double CenterY { get; set; } = .5;

    public double Zoom { get; set; }

    public Size Viewport { get; set; } = new(480, 300);

    /// <summary>The world's size in DIPs at the current zoom.</summary>
    public double World => NthUiMapProjection.WorldSize(Zoom);

    /// <summary>The smallest zoom at which the world still fills the viewport's height.</summary>
    public double MinZoom => Viewport.Height <= 0 ? 0 : Math.Max(0, Math.Log2(Viewport.Height / NthUiMapProjection.TileSize));

    /// <summary>Where a point of the unit square falls in the viewport (x is not wrapped: copies of the world are a world apart).</summary>
    public Point ToScreen(double x, double y)
    {
        var world = World;
        return new Point(Viewport.Width / 2 + (x - CenterX) * world, Viewport.Height / 2 + (y - CenterY) * world);
    }

    /// <summary>The point of the unit square under a point of the viewport.</summary>
    public Point ToUnit(Point screen)
    {
        var world = World;
        return new Point(CenterX + (screen.X - Viewport.Width / 2) / world, CenterY + (screen.Y - Viewport.Height / 2) / world);
    }

    /// <summary>The unit-square box the viewport shows.</summary>
    public Rect VisibleUnits()
    {
        var world = World;
        var width = Viewport.Width / world;
        var height = Viewport.Height / world;
        return new Rect(CenterX - width / 2, CenterY - height / 2, width, height);
    }

    /// <summary>Keeps the zoom in range and the world on screen vertically (no poles beyond the edge).</summary>
    public void Clamp(double maxZoom = MaxZoom)
    {
        Zoom = Math.Clamp(Zoom, MinZoom, Math.Max(MinZoom, maxZoom));
        var half = Viewport.Height / 2 / World;
        CenterY = half >= .5 ? .5 : Math.Clamp(CenterY, half, 1 - half);
    }

    /// <summary>Brings x into one turn of the world (a view can keep going round; animations leave it unwrapped).</summary>
    public void WrapX() => CenterX -= Math.Floor(CenterX);

    /// <summary>Moves the view by a drag of <paramref name="pixels"/> (the map follows the pointer).</summary>
    public void Pan(Vector pixels)
    {
        var world = World;
        CenterX -= pixels.X / world;
        CenterY -= pixels.Y / world;
    }

    /// <summary>Zooms to <paramref name="zoom"/> keeping the place under <paramref name="screen"/> where it is.</summary>
    public void ZoomAbout(Point screen, double zoom, double maxZoom = MaxZoom)
    {
        var anchor = ToUnit(screen);
        Zoom = Math.Clamp(zoom, MinZoom, Math.Max(MinZoom, maxZoom));
        var world = World;
        CenterX = anchor.X - (screen.X - Viewport.Width / 2) / world;
        CenterY = anchor.Y - (screen.Y - Viewport.Height / 2) / world;
    }

    /// <summary>The camera that shows <paramref name="units"/> (a box of the unit square) with <paramref name="padding"/> DIPs
    /// around it, no deeper than <paramref name="maxZoom"/>.</summary>
    public static NthUiMapCamera Fit(Rect units, Size viewport, double padding, double maxZoom)
    {
        var camera = new NthUiMapCamera { Viewport = viewport, CenterX = units.X + units.Width / 2, CenterY = units.Y + units.Height / 2 };
        var width = Math.Max(1e-9, units.Width);
        var height = Math.Max(1e-9, units.Height);
        var availableW = Math.Max(1, viewport.Width - 2 * padding);
        var availableH = Math.Max(1, viewport.Height - 2 * padding);
        var zoomX = Math.Log2(availableW / (width * NthUiMapProjection.TileSize));
        var zoomY = Math.Log2(availableH / (height * NthUiMapProjection.TileSize));
        camera.Zoom = Math.Min(zoomX, zoomY);
        camera.Clamp(maxZoom);
        camera.WrapX();
        return camera;
    }

    public NthUiMapCamera Copy() => new() { CenterX = CenterX, CenterY = CenterY, Zoom = Zoom, Viewport = Viewport };
}
