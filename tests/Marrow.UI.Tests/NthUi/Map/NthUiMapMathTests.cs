using Avalonia;
using Marrow.UI.NthUi.Map;

namespace Marrow.UI.Tests.NthUi.Map;

// DESIGN.md 7.2 (web surfaces W16c; ED-NEW-web-w16c-1): the map's maths, held without a window: Web Mercator both ways, the camera
// (pan, zoom about a point, fit, clamp), great-circle routes across the antimeridian, the labels the zoom has reached and where
// each goes, and the model's reading of stops and routes.
public sealed class NthUiMapMathTests
{
    [Theory]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    [InlineData(0, 0, .5, .5)]
    [InlineData(0, -180, 0, .5)]
    [InlineData(0, 180, 1, .5)]
    [InlineData(85.0511287798, 0, .5, 0)]
    [InlineData(-85.0511287798, 0, .5, 1)]
    [InlineData(51.4779, -.0015, .49999, .3327)]
    [InlineData(48.8566, 2.3522, .50653, .3440)]
    [InlineData(-33.8688, 151.2093, .91998, .6001)]
    public void Mercator_puts_places_where_slippy_maps_do(double lat, double lon, double x, double y)
    {
        var unit = NthUiMapProjection.ToUnit(lat, lon);
        Assert.Equal(x, unit.X, 4);
        Assert.Equal(y, unit.Y, 3);
        Assert.Equal(x, unit.X, 3);
        Assert.Equal(lon, NthUiMapProjection.XToLon(unit.X), 6);
        Assert.Equal(lat, NthUiMapProjection.YToLat(unit.Y), 6);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Latitudes_beyond_the_projection_are_held_at_its_limit_and_the_world_doubles_each_zoom()
    {
        Assert.Equal(0, NthUiMapProjection.LatToY(90), 9);
        Assert.Equal(1, NthUiMapProjection.LatToY(-90), 9);
        Assert.Equal(256, NthUiMapProjection.WorldSize(0));
        Assert.Equal(1024, NthUiMapProjection.WorldSize(2));
        Assert.Equal(362.0386719, NthUiMapProjection.WorldSize(.5), 5);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Great_circle_distances_match_the_known_ones()
    {
        Assert.Equal(13.08, NthUiMapProjection.ArcDegrees(38.7223, -9.1393, 48.8566, 2.3522), .15); // Lisbon to Paris, about 1,454 km
        Assert.Equal(50.1, NthUiMapProjection.ArcDegrees(40.7128, -74.0060, 51.5074, -.1278), .3); // New York to London, about 5,570 km
        Assert.Equal(0, NthUiMapProjection.ArcDegrees(10, 20, 10, 20), 9);
        Assert.Equal(180, NthUiMapProjection.ArcDegrees(0, 0, 0, 180), 6);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_long_route_bows_poleward_and_a_short_hop_is_straight()
    {
        var long_ = NthUiMapProjection.Route([(40.7128, -74.0060), (51.5074, -.1278)]);
        var straightMidLat = (40.7128 + 51.5074) / 2;
        var mid = long_[long_.Count / 2];
        Assert.True(NthUiMapProjection.YToLat(mid.Y) > straightMidLat + 1, "the great circle rises north of the straight line");
        Assert.True(long_.Count >= 45);
        var short_ = NthUiMapProjection.Route([(38.72, -9.14), (38.72, -8.64)]);
        Assert.True(short_.Count <= 3);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_route_over_the_pacific_is_one_unbroken_line()
    {
        var path = NthUiMapProjection.Route([(35.68, 139.69), (34.05, -118.24)]); // Tokyo to Los Angeles
        for (var i = 1; i < path.Count; i++) Assert.True(Math.Abs(path[i].X - path[i - 1].X) < .05, $"jump at {i}: {path[i - 1].X} to {path[i].X}");
        Assert.True(path[^1].X > 1, "x runs on past the world's edge instead of wrapping back");
        Assert.Equal(NthUiMapProjection.LonToX(-118.24) + 1, path[^1].X, 6);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_route_through_one_place_or_none_is_a_point_or_nothing()
    {
        Assert.Empty(NthUiMapProjection.Route([]));
        Assert.Single(NthUiMapProjection.Route([(10, 10)]));
        Assert.Equal(2, NthUiMapProjection.Route([(10, 10), (10, 10)]).Count);
    }

    // ---- the camera --------------------------------------------------------------------------------------------------

    private static NthUiMapCamera Camera(double zoom = 4, double x = .5, double y = .4, double w = 480, double h = 300) =>
        new() { CenterX = x, CenterY = y, Zoom = zoom, Viewport = new Size(w, h) };

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void The_centre_is_the_middle_of_the_viewport_and_screen_and_unit_points_convert_back()
    {
        var camera = Camera();
        Assert.Equal(new Point(240, 150), camera.ToScreen(.5, .4));
        var unit = camera.ToUnit(new Point(100, 200));
        var back = camera.ToScreen(unit.X, unit.Y);
        Assert.Equal(100, back.X, 6);
        Assert.Equal(200, back.Y, 6);
        var visible = camera.VisibleUnits();
        Assert.Equal(480 / camera.World, visible.Width, 9);
        Assert.Equal(.5, visible.X + visible.Width / 2, 9);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Dragging_moves_the_map_with_the_pointer_one_to_one()
    {
        var camera = Camera();
        var place = new Point(.52, .41);
        var before = camera.ToScreen(place.X, place.Y);
        camera.Pan(new Vector(37, -12));
        var after = camera.ToScreen(place.X, place.Y);
        Assert.Equal(before.X + 37, after.X, 6);
        Assert.Equal(before.Y - 12, after.Y, 6);
    }

    [Theory]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    [InlineData(100, 80, 6)]
    [InlineData(400, 250, 2.5)]
    [InlineData(240, 150, 7)]
    public void Zooming_about_a_point_keeps_the_place_under_it_where_it_is(double sx, double sy, double zoom)
    {
        var camera = Camera();
        var point = new Point(sx, sy);
        var place = camera.ToUnit(point);
        camera.ZoomAbout(point, zoom);
        Assert.Equal(zoom, camera.Zoom, 9);
        var now = camera.ToScreen(place.X, place.Y);
        Assert.Equal(sx, now.X, 6);
        Assert.Equal(sy, now.Y, 6);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void The_zoom_is_held_between_the_world_filling_the_view_and_the_deepest_the_data_serves()
    {
        var camera = Camera(zoom: 0);
        camera.Clamp();
        Assert.Equal(Math.Log2(300 / 256.0), camera.MinZoom, 9);
        Assert.Equal(camera.MinZoom, camera.Zoom, 9);
        camera.Zoom = 14;
        camera.Clamp();
        Assert.Equal(NthUiMapCamera.MaxZoom, camera.Zoom);
        camera.Zoom = 14;
        camera.Clamp(maxZoom: 17);
        Assert.Equal(14, camera.Zoom);
        camera.ZoomAbout(new Point(0, 0), -3);
        Assert.Equal(camera.MinZoom, camera.Zoom, 9);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void The_poles_never_come_into_view_and_x_wraps_by_a_turn_of_the_world_only_when_asked()
    {
        var camera = Camera(zoom: 5, y: 0.001);
        camera.Clamp();
        var visible = camera.VisibleUnits();
        Assert.True(visible.Top >= -1e-9);
        camera.CenterY = .999;
        camera.Clamp();
        Assert.True(camera.VisibleUnits().Bottom <= 1 + 1e-9);
        camera.CenterX = 1.25;
        camera.Clamp();
        Assert.Equal(1.25, camera.CenterX);
        camera.WrapX();
        Assert.Equal(.25, camera.CenterX, 9);
        camera.CenterX = -.25;
        camera.WrapX();
        Assert.Equal(.75, camera.CenterX, 9);
        var whole = Camera(zoom: 0, h: 300);
        whole.Clamp();
        Assert.Equal(.5, whole.CenterY, 9);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_fit_shows_the_whole_box_inside_the_padding_and_never_goes_deeper_than_asked()
    {
        var box = new Rect(NthUiMapProjection.LonToX(-9.14), NthUiMapProjection.LatToY(48.86), NthUiMapProjection.LonToX(2.35) - NthUiMapProjection.LonToX(-9.14),
            NthUiMapProjection.LatToY(38.72) - NthUiMapProjection.LatToY(48.86));
        var camera = NthUiMapCamera.Fit(box, new Size(480, 300), 56, 10);
        foreach (var corner in new[] { box.TopLeft, box.TopRight, box.BottomLeft, box.BottomRight })
        {
            var at = camera.ToScreen(corner.X, corner.Y);
            Assert.InRange(at.X, 56 - .5, 480 - 56 + .5);
            Assert.InRange(at.Y, 56 - .5, 300 - 56 + .5);
        }
        // The box touches the padding on its tighter side: the zoom is the deepest that still fits.
        var zoomedIn = camera.Copy();
        zoomedIn.Zoom += .05;
        var topLeft = zoomedIn.ToScreen(box.Left, box.Top);
        var bottomRight = zoomedIn.ToScreen(box.Right, box.Bottom);
        Assert.True(topLeft.X < 56 || topLeft.Y < 56 || bottomRight.X > 424 || bottomRight.Y > 244);
        var one = NthUiMapCamera.Fit(new Rect(.5, .4, 0, 0), new Size(480, 300), 56, 6);
        Assert.Equal(6, one.Zoom);
        Assert.Equal(.5, one.CenterX, 9);
    }

    // ---- the model ---------------------------------------------------------------------------------------------------

    private static NthUiMapStop Stop(string label, double lat, double lon, string? detail = null) => new("k" + label, label, lat, lon, detail);

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_route_names_its_stops_by_label_and_one_it_cannot_find_is_skipped()
    {
        var model = new NthUiMapModel("Trip", [Stop("Lisbon", 38.72, -9.14), Stop("Madrid", 40.42, -3.70, "Overnight"), Stop("Paris", 48.86, 2.35)],
            [new NthUiMapRoute("r1", "By train", ["lisbon", " Madrid ", "Atlantis", "Paris"]), new NthUiMapRoute("r2", "Nowhere", ["Atlantis"]), new NthUiMapRoute("r3", "Hop", ["Paris", "Paris"])]);
        var path = Assert.Single(model.Paths);
        Assert.Equal(["Lisbon", "Madrid", "Paris"], path.Stops.Select(s => s.Label));
        Assert.True(path.Points.Count > 10);
        Assert.Equal(3, model.Stops.Count);
        Assert.Equal(3, model.Routes.Count);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void The_box_holds_every_stop_and_follows_a_route_across_the_antimeridian()
    {
        var empty = new NthUiMapModel("", [], []);
        Assert.Null(empty.Bounds());
        Assert.True(empty.IsEmpty);
        var lone = new NthUiMapModel("", [Stop("A", 10, 20)], []).Bounds()!.Value;
        Assert.Equal(0, lone.Width, 9);
        Assert.Equal(NthUiMapProjection.LonToX(20), lone.X, 9);
        var model = new NthUiMapModel("", [Stop("Tokyo", 35.68, 139.69), Stop("LA", 34.05, -118.24), Stop("Fiji", -17.7, 178)], [new NthUiMapRoute("r", "Flight", ["Tokyo", "LA"])]);
        var box = model.Bounds()!.Value;
        Assert.True(box.Width < .5, $"the box goes the short way round: {box.Width}");
        Assert.True(box.Right > 1 - .1);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void The_summary_names_the_stops_with_their_details_and_the_routes_through_them()
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        var model = new NthUiMapModel("Lisbon to Paris", [Stop("Lisbon", 38.72, -9.14), Stop("Madrid", 40.42, -3.70, "Overnight"), Stop("Paris", 48.86, 2.35)],
            [new NthUiMapRoute("r1", "By train", ["Lisbon", "Madrid", "Paris"])]);
        Assert.Equal("Lisbon to Paris: 3 stops, Lisbon, Madrid (Overnight), Paris. Route By train: Lisbon to Madrid to Paris.", model.Summary(culture));
        Assert.Equal("Map: 1 stop, A.", new NthUiMapModel("", [Stop("A", 1, 2)], []).Summary(culture));
        Assert.Equal("A map with no stops yet.", new NthUiMapModel("", [], []).Summary(culture));
        Assert.Equal("Quiet: no stops yet.", new NthUiMapModel("Quiet", [], []).Summary(culture));
        Assert.Equal("38.72°N, 9.14°W", model.Stops[0].Coordinates(culture));
        Assert.Equal("33.5°S, 151.2°E", Stop("Sydney", -33.5, 151.2).Coordinates(culture));
    }

    // ---- labels ------------------------------------------------------------------------------------------------------

    private static NthUiMapLabelCandidate Label(string key, NthUiMapLabelKind kind, double x, double y, double priority, double w = 60, double h = 14, double dot = 3) =>
        new(key, kind, key, new Point(x, y), new Size(w, h), priority, kind == NthUiMapLabelKind.Country ? 0 : dot);

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Cities_unlock_by_rank_as_the_zoom_grows_and_countries_by_their_own_range()
    {
        var zooms = Enumerable.Range(0, 300).Select(NthUiMapLabelPlanner.CityZoom).ToList();
        Assert.True(zooms.SequenceEqual(zooms.Order()), "a smaller city never appears before a bigger one");
        Assert.Equal(1.5, NthUiMapLabelPlanner.CityZoom(0));
        Assert.Equal(6.5, NthUiMapLabelPlanner.CityZoom(299));
        Assert.False(NthUiMapLabelPlanner.CountryZoom(0.5, 2.5, 6));
        Assert.True(NthUiMapLabelPlanner.CountryZoom(3, 2.5, 6));
        Assert.False(NthUiMapLabelPlanner.CountryZoom(7.9, 2.5, 9)); // deep in, the cities speak
        Assert.True(NthUiMapLabelPlanner.CountryZoom(3, double.NaN, double.NaN));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void No_two_placed_labels_overlap_and_all_of_them_are_inside_the_view()
    {
        var random = new Random(7);
        var view = new Rect(0, 0, 480, 300);
        var candidates = new List<NthUiMapLabelCandidate>();
        for (var i = 0; i < 160; i++)
        {
            var kind = (NthUiMapLabelKind)random.Next(3);
            candidates.Add(Label($"{kind}{i}", kind, random.Next(0, 480), random.Next(0, 300), random.Next(100), 30 + random.Next(60), 12 + random.Next(4)));
        }
        var placed = NthUiMapLabelPlanner.Place(candidates, view);
        Assert.NotEmpty(placed);
        for (var i = 0; i < placed.Count; i++)
        {
            for (var j = i + 1; j < placed.Count; j++)
            {
                if (placed[i].Kind == NthUiMapLabelKind.Stop && placed[j].Kind == NthUiMapLabelKind.Stop) continue; // stops are never dropped
                Assert.False(placed[i].Box.Intersects(placed[j].Box), $"{placed[i].Key} overlaps {placed[j].Key}");
            }
            if (placed[i].Kind == NthUiMapLabelKind.Stop) continue;
            Assert.True(view.Contains(placed[i].Box.TopLeft) && view.Contains(placed[i].Box.BottomRight));
            foreach (var other in candidates.Where(c => c.DotRadius > 0 && c.Key != placed[i].Key))
            {
                var dot = new Rect(other.Anchor.X - other.DotRadius, other.Anchor.Y - other.DotRadius, other.DotRadius * 2, other.DotRadius * 2);
                Assert.False(placed[i].Box.Intersects(dot), $"{placed[i].Key} covers the dot of {other.Key}");
            }
        }
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Stops_are_always_placed_the_bigger_place_wins_and_a_label_never_covers_a_dot()
    {
        var view = new Rect(0, 0, 480, 300);
        var crowd = Enumerable.Range(0, 8).Select(i => Label("stop" + i, NthUiMapLabelKind.Stop, 200 + i * 6, 150, 1000 - i, 70, 18, 6.5)).ToList();
        var placed = NthUiMapLabelPlanner.Place(crowd, view);
        Assert.Equal(8, placed.Count);
        Assert.All(placed, p => Assert.True(view.Contains(p.Box.TopLeft) && view.Contains(p.Box.BottomRight)));
        var big = Label("big", NthUiMapLabelKind.Country, 100, 100, 200);
        var small = Label("small", NthUiMapLabelKind.Country, 110, 102, 100);
        var result = NthUiMapLabelPlanner.Place([small, big], view);
        Assert.Contains(result, p => p.Key == "big");
        Assert.DoesNotContain(result, p => p.Key == "small");
        // A city whose label fits on neither side of its dot is left out; a stop with the same label is not.
        var wide = NthUiMapLabelPlanner.Place([Label("city", NthUiMapLabelKind.City, 240, 100, 5, 500), Label("stop", NthUiMapLabelKind.Stop, 240, 200, 5, 500, 14, 6.5)], view);
        Assert.DoesNotContain(wide, p => p.Key == "city");
        Assert.Contains(wide, p => p.Key == "stop");
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_stop_takes_the_first_free_of_its_four_places_and_a_stop_at_the_edge_turns_inward()
    {
        var view = new Rect(0, 0, 480, 300);
        var right = Assert.Single(NthUiMapLabelPlanner.Place([Label("a", NthUiMapLabelKind.Stop, 100, 100, 1, 50, 14, 6.5)], view));
        Assert.True(right.Box.X > 100);
        var edge = Assert.Single(NthUiMapLabelPlanner.Place([Label("b", NthUiMapLabelKind.Stop, 470, 100, 1, 50, 14, 6.5)], view));
        Assert.True(edge.Box.Right < 470, "at the right edge the label goes left of its dot");
        var top = Assert.Single(NthUiMapLabelPlanner.Place([Label("c", NthUiMapLabelKind.Stop, 470, 3, 1, 50, 14, 6.5)], view));
        Assert.True(view.Contains(top.Box.TopLeft) && view.Contains(top.Box.BottomRight));
    }
}
