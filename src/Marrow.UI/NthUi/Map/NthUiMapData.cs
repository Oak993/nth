// PORT-ONLY: web surfaces W16c (DESIGN.md 7.2 "a built-in offline world basemap (Natural Earth 1:50m, public domain ... simplified
// vector data, projected in C#, drawn natively) ... labels for countries and the 300 largest cities"; ED-NEW-web-w16c-1). No Swift
// counterpart. The basemap file (Assets/naturalearth/world.nemap, made by tools/naturalearth/build.mjs, which documents the
// format) read into the unit square: countries with their outlines and label points, lakes, and the 300 largest places. Each
// polygon keeps its rings as float pairs, simplifies them on demand to the level of detail a zoom needs (Douglas-Peucker at half a
// pixel), and builds its geometry in the pixels of that level, so strokes stay numerically sound at every zoom. The file is an
// embedded asset: reading it opens no connection and asks nothing of the machine.
using System.Text;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;

namespace Marrow.UI.NthUi.Map;

/// <summary>A land or lake polygon: outer ring and holes, in the unit square.</summary>
public sealed class NthUiMapPolygon
{
    /// <summary>The finest level of detail: the data as it is (zoom 8 and deeper).</summary>
    public const int FullLevel = 8;

    private readonly Geometry?[] geometries = new Geometry?[FullLevel + 1];

    public NthUiMapPolygon(IReadOnlyList<float[]> rings)
    {
        Rings = rings;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var ring in rings)
        {
            for (var i = 0; i + 1 < ring.Length; i += 2)
            {
                minX = Math.Min(minX, ring[i]);
                maxX = Math.Max(maxX, ring[i]);
                minY = Math.Min(minY, ring[i + 1]);
                maxY = Math.Max(maxY, ring[i + 1]);
            }
        }
        Bounds = minX > maxX ? default : new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    public IReadOnlyList<float[]> Rings { get; }

    public Rect Bounds { get; }

    public int PointCount => Rings.Sum(r => r.Length / 2);

    /// <summary>The rings simplified for <paramref name="level"/> (0 to <see cref="FullLevel"/>).</summary>
    public IReadOnlyList<float[]> Simplified(int level)
    {
        level = Math.Clamp(level, 0, FullLevel);
        if (level == FullLevel) return Rings;
        var tolerance = 0.5 / NthUiMapProjection.WorldSize(level);
        var result = new List<float[]>(Rings.Count);
        foreach (var ring in Rings)
        {
            var simple = NthUiMapGeometry.Simplify(ring, tolerance);
            if (simple.Length >= 6) result.Add(simple);
        }
        return result;
    }

    /// <summary>The polygon as a geometry in the pixels of <paramref name="level"/> (the world is 256 x 2^level wide), even-odd filled.</summary>
    public Geometry GeometryAt(int level)
    {
        level = Math.Clamp(level, 0, FullLevel);
        if (geometries[level] is { } built) return built;
        var scale = NthUiMapProjection.WorldSize(level);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.SetFillRule(FillRule.EvenOdd);
            foreach (var ring in Simplified(level))
            {
                context.BeginFigure(new Point(ring[0] * scale, ring[1] * scale), true);
                for (var i = 2; i + 1 < ring.Length; i += 2) context.LineTo(new Point(ring[i] * scale, ring[i + 1] * scale));
                context.EndFigure(true);
            }
        }
        return geometries[level] = geometry;
    }
}

public sealed record NthUiMapCountry(string Name, string Iso, double LabelX, double LabelY, int LabelRank, double MinLabel, double MaxLabel, long Population,
    IReadOnlyList<NthUiMapPolygon> Polygons, Rect Bounds);

public sealed record NthUiMapLake(double MinZoom, IReadOnlyList<NthUiMapPolygon> Polygons);

/// <summary>A populated place; <paramref name="Rank"/> counts from the largest (0).</summary>
public sealed record NthUiMapPlace(string Name, double Longitude, double Latitude, double X, double Y, long Population, bool Capital, int Rank);

/// <summary>The offline basemap.</summary>
public sealed class NthUiMapData
{
    public const string AssetAddress = "avares://Marrow.UI/Assets/naturalearth/world.nemap";

    private static readonly Lazy<NthUiMapData> Loaded = new(OpenAsset, LazyThreadSafetyMode.ExecutionAndPublication);

    private NthUiMapData(IReadOnlyList<NthUiMapCountry> countries, IReadOnlyList<NthUiMapLake> lakes, IReadOnlyList<NthUiMapPlace> places)
    {
        Countries = countries;
        Lakes = lakes;
        Places = places;
    }

    /// <summary>The basemap the app ships, read once on first use.</summary>
    public static NthUiMapData Shared => Loaded.Value;

    /// <summary>Starts reading the basemap on the pool, so the first map in an answer finds it ready.</summary>
    public static Task PreloadAsync() => Task.Run(() => Loaded.Value);

    public IReadOnlyList<NthUiMapCountry> Countries { get; }

    public IReadOnlyList<NthUiMapLake> Lakes { get; }

    public IReadOnlyList<NthUiMapPlace> Places { get; }

    private static NthUiMapData OpenAsset()
    {
        using var stream = AssetLoader.Open(new Uri(AssetAddress));
        return Load(stream);
    }

    /// <summary>Reads a basemap file. A truncated or unknown file is a <see cref="FormatException"/>.</summary>
    public static NthUiMapData Load(Stream stream)
    {
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var reader = new Reader(memory.GetBuffer().AsSpan(0, (int)memory.Length).ToArray());
        if (reader.Remaining < 5 || reader.Byte() != 'N' || reader.Byte() != 'E' || reader.Byte() != 'M' || reader.Byte() != '1') throw new FormatException("Not a Natural Earth basemap file.");
        if (reader.Byte() != 1) throw new FormatException("A newer basemap file than this build reads.");

        var countries = new List<NthUiMapCountry>();
        var count = reader.Count(2000);
        for (var i = 0; i < count; i++)
        {
            var name = reader.Text();
            var iso = reader.Text();
            var labelX = NthUiMapProjection.LonToX(reader.Signed() / 1000.0);
            var labelY = NthUiMapProjection.LatToY(reader.Signed() / 1000.0);
            var rank = reader.Byte();
            var minLabel = Tenth(reader.Byte());
            var maxLabel = Tenth(reader.Byte());
            var population = reader.Unsigned() * 1000L;
            var polygons = Polygons(reader);
            countries.Add(new NthUiMapCountry(name, iso, labelX, labelY, rank, minLabel, maxLabel, population, polygons, Union(polygons)));
        }

        var lakes = new List<NthUiMapLake>();
        count = reader.Count(5000);
        for (var i = 0; i < count; i++)
        {
            var minZoom = Tenth(reader.Byte());
            lakes.Add(new NthUiMapLake(minZoom, Polygons(reader)));
        }

        var places = new List<NthUiMapPlace>();
        count = reader.Count(5000);
        for (var i = 0; i < count; i++)
        {
            var name = reader.Text();
            var lon = reader.Signed() / 1000.0;
            var lat = reader.Signed() / 1000.0;
            var population = reader.Unsigned() * 1000L;
            var capital = (reader.Byte() & 1) != 0;
            places.Add(new NthUiMapPlace(name, lon, lat, NthUiMapProjection.LonToX(lon), NthUiMapProjection.LatToY(lat), population, capital, i));
        }
        return new NthUiMapData(countries, lakes, places);
    }

    private static double Tenth(int value) => value == 255 ? double.NaN : value / 10.0;

    private static Rect Union(IReadOnlyList<NthUiMapPolygon> polygons)
    {
        Rect? union = null;
        foreach (var polygon in polygons) union = union is null ? polygon.Bounds : union.Value.Union(polygon.Bounds);
        return union ?? default;
    }

    private static List<NthUiMapPolygon> Polygons(Reader reader)
    {
        var polygons = new List<NthUiMapPolygon>();
        var count = reader.Count(100_000);
        for (var p = 0; p < count; p++)
        {
            var rings = new List<float[]>();
            var ringCount = reader.Count(10_000);
            for (var r = 0; r < ringCount; r++)
            {
                var points = reader.Count(2_000_000);
                var ring = new float[points * 2];
                long x = 0, y = 0;
                for (var i = 0; i < points; i++)
                {
                    x = i == 0 ? reader.Signed() : x + reader.Signed();
                    y = i == 0 ? reader.Signed() : y + reader.Signed();
                    ring[i * 2] = (float)NthUiMapProjection.LonToX(x / 1000.0);
                    ring[i * 2 + 1] = (float)NthUiMapProjection.LatToY(y / 1000.0);
                }
                if (points >= 3) rings.Add(ring);
            }
            if (rings.Count > 0) polygons.Add(new NthUiMapPolygon(rings));
        }
        return polygons;
    }

    private sealed class Reader(byte[] data)
    {
        private int index;

        public int Remaining => data.Length - index;

        public int Byte()
        {
            if (index >= data.Length) throw new FormatException("The basemap file ends early.");
            return data[index++];
        }

        public long Unsigned()
        {
            long value = 0;
            var shift = 0;
            while (true)
            {
                var b = Byte();
                value |= (long)(b & 0x7f) << shift;
                if ((b & 0x80) == 0) return value;
                shift += 7;
                if (shift > 56) throw new FormatException("A number in the basemap file is too long.");
            }
        }

        public long Signed()
        {
            var v = Unsigned();
            return (v & 1) == 0 ? v / 2 : -(v + 1) / 2;
        }

        public int Count(int limit)
        {
            var v = Unsigned();
            if (v > limit) throw new FormatException("A count in the basemap file is out of range.");
            return (int)v;
        }

        public string Text()
        {
            var length = Count(10_000);
            if (Remaining < length) throw new FormatException("The basemap file ends early.");
            var text = Encoding.UTF8.GetString(data, index, length);
            index += length;
            return text;
        }
    }
}

/// <summary>Geometry helpers of the basemap.</summary>
public static class NthUiMapGeometry
{
    /// <summary>
    /// Douglas-Peucker: the points of <paramref name="ring"/> (x, y pairs) that matter at <paramref name="tolerance"/> (in the ring's
    /// own units). The first and last points stay; a ring left with fewer than three points is returned empty, since it would not
    /// show.
    /// </summary>
    public static float[] Simplify(float[] ring, double tolerance)
    {
        var n = ring.Length / 2;
        if (n <= 4 || tolerance <= 0) return ring;
        var keep = new bool[n];
        keep[0] = keep[n - 1] = true;
        var stack = new Stack<(int First, int Last)>();
        stack.Push((0, n - 1));
        var limit = tolerance * tolerance;
        while (stack.Count > 0)
        {
            var (first, last) = stack.Pop();
            if (last - first < 2) continue;
            double ax = ring[first * 2], ay = ring[first * 2 + 1], bx = ring[last * 2], by = ring[last * 2 + 1];
            double dx = bx - ax, dy = by - ay;
            var length2 = dx * dx + dy * dy;
            var farthest = -1;
            var far = limit;
            for (var i = first + 1; i < last; i++)
            {
                double px = ring[i * 2], py = ring[i * 2 + 1];
                double d2;
                if (length2 < 1e-30) d2 = (px - ax) * (px - ax) + (py - ay) * (py - ay);
                else
                {
                    var t = Math.Clamp(((px - ax) * dx + (py - ay) * dy) / length2, 0, 1);
                    var qx = ax + t * dx - px;
                    var qy = ay + t * dy - py;
                    d2 = qx * qx + qy * qy;
                }
                if (d2 > far)
                {
                    far = d2;
                    farthest = i;
                }
            }
            if (farthest < 0) continue;
            keep[farthest] = true;
            stack.Push((first, farthest));
            stack.Push((farthest, last));
        }
        var kept = keep.Count(k => k);
        if (kept < 3) return [];
        var result = new float[kept * 2];
        var o = 0;
        for (var i = 0; i < n; i++)
        {
            if (!keep[i]) continue;
            result[o++] = ring[i * 2];
            result[o++] = ring[i * 2 + 1];
        }
        return result;
    }

    /// <summary>Whether the point is inside the polygon (outer ring and holes, even-odd).</summary>
    public static bool Contains(NthUiMapPolygon polygon, double x, double y)
    {
        if (!polygon.Bounds.Contains(new Point(x, y))) return false;
        var inside = false;
        foreach (var ring in polygon.Rings)
        {
            var n = ring.Length / 2;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = ring[i * 2], yi = ring[i * 2 + 1], xj = ring[j * 2], yj = ring[j * 2 + 1];
                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
            }
        }
        return inside;
    }
}
