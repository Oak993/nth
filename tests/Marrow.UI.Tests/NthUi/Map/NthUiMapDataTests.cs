using Avalonia;
using Avalonia.Platform;
using Marrow.UI.NthUi.Map;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.NthUi.Map;

// DESIGN.md 7.2 (web surfaces W16c; ED-NEW-web-w16c-1): the offline basemap is Natural Earth's 1:50m countries, lakes and the 300
// largest places, read from an asset of the app. These tests hold what the file must contain (the world as it is, not a sample),
// that a damaged file is refused cleanly, that simplifying a ring for a coarse zoom keeps it within half a pixel of the original,
// and that reading it opens nothing but the asset.
[Collection("Headless")]
public sealed class NthUiMapDataTests(HeadlessSession headless)
{
    private static NthUiMapCountry Country(NthUiMapData data, string name) => data.Countries.Single(c => c.Name == name);

    private static bool Inside(NthUiMapData data, string country, double lat, double lon)
    {
        var p = NthUiMapProjection.ToUnit(lat, lon);
        return Country(data, country).Polygons.Any(polygon => NthUiMapGeometry.Contains(polygon, p.X, p.Y));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void The_asset_is_small_and_holds_the_worlds_countries_lakes_and_three_hundred_places() => headless.OnUiThread(() =>
    {
        using (var stream = AssetLoader.Open(new Uri(NthUiMapData.AssetAddress)))
            Assert.InRange(stream.Length, 100_000, 900_000);
        var data = NthUiMapData.Shared;
        Assert.InRange(data.Countries.Count, 230, 260);
        Assert.InRange(data.Lakes.Count, 300, 500);
        Assert.Equal(300, data.Places.Count);
        Assert.InRange(data.Countries.Sum(c => c.Polygons.Sum(p => p.PointCount)), 90_000, 110_000);
        Assert.Same(data, NthUiMapData.Shared);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void The_places_are_the_largest_first_with_the_ones_the_design_names() => headless.OnUiThread(() =>
    {
        var places = NthUiMapData.Shared.Places;
        Assert.Equal("Tokyo", places[0].Name);
        Assert.True(places.Select(p => p.Population).SequenceEqual(places.Select(p => p.Population).OrderDescending()));
        Assert.Equal(Enumerable.Range(0, 300), places.Select(p => p.Rank));
        foreach (var name in new[] { "Paris", "Madrid", "Lisbon", "London", "New York", "São Paulo", "Sydney", "Cairo", "Mumbai", "Moscow" })
            Assert.Contains(places, p => p.Name == name);
        var lisbon = places.Single(p => p.Name == "Lisbon");
        Assert.Equal(38.72, lisbon.Latitude, 1);
        Assert.InRange(lisbon.Longitude, -9.16, -9.14);
        Assert.Equal(NthUiMapProjection.LonToX(lisbon.Longitude), lisbon.X, 9);
        Assert.Contains(places, p => p.Capital);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Cities_lie_inside_their_countries_and_the_open_sea_is_in_none() => headless.OnUiThread(() =>
    {
        var data = NthUiMapData.Shared;
        Assert.True(Inside(data, "Portugal", 38.75, -9.2), "Lisbon's inland edge in Portugal (the 1:50m coast cuts the Tagus estuary)");
        Assert.True(Inside(data, "Spain", 40.4168, -3.7038), "Madrid in Spain");
        Assert.True(Inside(data, "France", 48.8566, 2.3522), "Paris in France");
        Assert.True(Inside(data, "Egypt", 30.0444, 31.2357), "Cairo in Egypt");
        Assert.True(Inside(data, "Australia", -33.8688, 151.2093), "Sydney in Australia");
        Assert.True(Inside(data, "United States of America", 39.7392, -104.9903), "Denver in the USA");
        Assert.False(Inside(data, "France", 38.75, -9.2), "Lisbon is not in France");
        var sea = NthUiMapProjection.ToUnit(0, -30); // the mid-Atlantic
        Assert.DoesNotContain(data.Countries, c => c.Polygons.Any(p => NthUiMapGeometry.Contains(p, sea.X, sea.Y)));
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_countrys_label_point_is_inside_its_box_and_the_big_ones_are_ranked_to_be_named_first() => headless.OnUiThread(() =>
    {
        var data = NthUiMapData.Shared;
        foreach (var country in data.Countries.Where(c => c.Population > 20_000_000))
        {
            Assert.True(country.Bounds.Inflate(.01).Contains(new Point(country.LabelX, country.LabelY)), $"{country.Name}'s label point");
            Assert.False(string.IsNullOrWhiteSpace(country.Iso));
        }
        var russia = Country(data, "Russia");
        var malta = Country(data, "Malta");
        Assert.True(russia.LabelRank <= malta.LabelRank);
        Assert.True(russia.MinLabel < malta.MinLabel || double.IsNaN(malta.MinLabel));
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Holes_stay_holes_and_polygons_keep_their_bounds() => headless.OnUiThread(() =>
    {
        var data = NthUiMapData.Shared;
        var lesotho = Country(data, "Lesotho"); // a country inside another one: South Africa has a hole there
        var south = Country(data, "South Africa");
        var p = NthUiMapProjection.ToUnit(-29.5, 28.2);
        Assert.True(lesotho.Polygons.Any(poly => NthUiMapGeometry.Contains(poly, p.X, p.Y)), "inside Lesotho");
        Assert.False(south.Polygons.Any(poly => NthUiMapGeometry.Contains(poly, p.X, p.Y)), "not inside South Africa's hole");
        foreach (var polygon in data.Countries.SelectMany(c => c.Polygons).Take(300))
        {
            foreach (var ring in polygon.Rings)
            {
                for (var i = 0; i < ring.Length; i += 2) Assert.True(polygon.Bounds.Inflate(1e-6).Contains(new Point(ring[i], ring[i + 1])));
            }
        }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Coarse_levels_keep_far_fewer_points_within_half_a_pixel_and_the_finest_level_is_the_data_itself() => headless.OnUiThread(() =>
    {
        var data = NthUiMapData.Shared;
        var big = data.Countries.SelectMany(c => c.Polygons).OrderByDescending(p => p.PointCount).Take(12).ToList();
        var full = big.Sum(p => p.PointCount);
        foreach (var level in new[] { 0, 2, 4 })
        {
            var tolerance = .5 / NthUiMapProjection.WorldSize(level);
            var kept = 0;
            foreach (var polygon in big)
            {
                kept += polygon.Simplified(level).Sum(r => r.Length / 2);
                foreach (var original in polygon.Rings)
                {
                    var simple = NthUiMapGeometry.Simplify(original, tolerance);
                    if (simple.Length < 6) continue;
                    // Every original point is within the tolerance (a little over, for float rounding) of the simplified outline.
                    for (var i = 0; i < original.Length; i += 2)
                        Assert.True(DistanceToPolyline(simple, original[i], original[i + 1]) <= tolerance * 1.05 + 1e-7, $"level {level}");
                }
            }
            Assert.True(kept < full * (level == 0 ? .08 : level == 2 ? .25 : .6), $"level {level} kept {kept} of {full}");
        }
        Assert.Same(big[0].Rings, big[0].Simplified(NthUiMapPolygon.FullLevel));
    });

    private static double DistanceToPolyline(float[] ring, double x, double y)
    {
        var best = double.MaxValue;
        var n = ring.Length / 2;
        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;
            double ax = ring[i * 2], ay = ring[i * 2 + 1], bx = ring[j * 2], by = ring[j * 2 + 1];
            double dx = bx - ax, dy = by - ay;
            var length2 = dx * dx + dy * dy;
            var t = length2 < 1e-30 ? 0 : Math.Clamp(((x - ax) * dx + (y - ay) * dy) / length2, 0, 1);
            best = Math.Min(best, Math.Sqrt(Math.Pow(ax + t * dx - x, 2) + Math.Pow(ay + t * dy - y, 2)));
        }
        return best;
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Simplifying_keeps_small_rings_as_they_are_and_drops_ones_that_would_vanish()
    {
        float[] square = [0, 0, 1, 0, 1, 1, 0, 1];
        Assert.Same(square, NthUiMapGeometry.Simplify(square, .01));
        Assert.Same(square, NthUiMapGeometry.Simplify(square, 0));
        float[] line = [0, 0, 1, 0, 2, 0, 3, 0, 4, 0, 5, .0001f];
        Assert.Empty(NthUiMapGeometry.Simplify(line, .01)); // a sliver collapses to nothing
        float[] bump = [0, 0, 1, 0, 2, 0, 3, 0, 4, 0, 4, 5, 0, 5];
        var simplified = NthUiMapGeometry.Simplify(bump, .01);
        Assert.Equal(4, simplified.Length / 2); // the collinear points go, the corners stay
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_polygon_makes_even_odd_geometry_in_the_pixels_of_its_level() => headless.OnUiThread(() =>
    {
        var polygon = NthUiMapData.Shared.Countries.Single(c => c.Name == "Portugal").Polygons.OrderByDescending(p => p.PointCount).First();
        var level = 4;
        var geometry = polygon.GeometryAt(level);
        var scale = NthUiMapProjection.WorldSize(level);
        Assert.Equal(polygon.Bounds.Width * scale, geometry.Bounds.Width, 0);
        Assert.Equal(polygon.Bounds.X * scale, geometry.Bounds.X, 0);
        Assert.Same(geometry, polygon.GeometryAt(level));
        Assert.NotSame(geometry, polygon.GeometryAt(5));
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_damaged_file_is_refused_with_a_format_error_and_never_anything_else() => headless.OnUiThread(() =>
    {
        Assert.Throws<FormatException>(() => NthUiMapData.Load(new MemoryStream([])));
        Assert.Throws<FormatException>(() => NthUiMapData.Load(new MemoryStream("NOPE1"u8.ToArray())));
        Assert.Throws<FormatException>(() => NthUiMapData.Load(new MemoryStream("NEM1\u0002"u8.ToArray()))); // a newer version
        Assert.Throws<FormatException>(() => NthUiMapData.Load(new MemoryStream("NEM1\u0001"u8.ToArray()))); // nothing after the header
        // Every strict prefix of a real file is a format error, never a crash or an index error.
        using var stream = AssetLoader.Open(new Uri(NthUiMapData.AssetAddress));
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var bytes = memory.ToArray();
        var step = Math.Max(1, bytes.Length / 250);
        for (var length = 0; length < bytes.Length; length += step)
            Assert.Throws<FormatException>(() => NthUiMapData.Load(new MemoryStream(bytes[..length])));
        Assert.Throws<FormatException>(() => NthUiMapData.Load(new MemoryStream(bytes[..^1])));
        foreach (var small in new[] { Sample() })
        {
            for (var length = 0; length < small.Length; length++)
                Assert.Throws<FormatException>(() => NthUiMapData.Load(new MemoryStream(small[..length])));
        }
        // A count out of range.
        Assert.Throws<FormatException>(() => NthUiMapData.Load(new MemoryStream([.. "NEM1"u8.ToArray(), 1, 0xff, 0xff, 0xff, 0x7f])));
    });

    private static byte[] Sample()
    {
        // One country with one triangle, no lakes, no places.
        var bytes = new List<byte>("NEM1"u8.ToArray()) { 1, 1 };
        void Text(string value) { bytes.Add((byte)value.Length); bytes.AddRange(System.Text.Encoding.UTF8.GetBytes(value)); }
        Text("Testland");
        Text("TST");
        bytes.AddRange([0, 0, 1, 255, 255, 1]); // label point, rank 1, no label zoom range, population 1
        bytes.AddRange([1, 1, 3, 0, 0, 40, 0, 0, 40]); // one polygon, one ring, three points: (0,0), (+20,0), (0,+20) in zig-zag steps of a thousandth degree
        bytes.AddRange([0, 0]); // no lakes, no places
        return [.. bytes];
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_small_hand_made_file_reads_back_as_written()
    {
        var data = NthUiMapData.Load(new MemoryStream(Sample()));
        var country = Assert.Single(data.Countries);
        Assert.Equal("Testland", country.Name);
        Assert.Equal("TST", country.Iso);
        Assert.Equal(1000, country.Population);
        Assert.True(double.IsNaN(country.MinLabel));
        Assert.Single(country.Polygons);
        Assert.Equal(3, country.Polygons[0].PointCount);
        Assert.Empty(data.Lakes);
        Assert.Empty(data.Places);
    }
}
