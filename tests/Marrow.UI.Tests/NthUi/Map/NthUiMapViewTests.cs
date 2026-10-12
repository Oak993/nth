using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.Core.Tests.Fixtures;
using Marrow.UI.NthUi;
using Marrow.UI.NthUi.Map;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.NthUi.Map;

// DESIGN.md 7.2, 7.4 and 10 (web surfaces W16c; ED-NEW-web-w16c-1): a `map` of a live answer is a native map of the offline world
// with its stops, routes and labels in the thread's palette, pan and zoom by pointer, keys and buttons that glide on the nth
// spring and can be interrupted anywhere, a camera that keeps new stops in view until the reader moves the map, labels that fade in
// as the zoom reaches them, and a list of its stops as the text alternative. Nothing here goes near a network.
[Collection("Headless")]
public sealed class NthUiMapViewTests(HeadlessSession headless)
{
    private const string Iberia = """
        map "Iberia" zoom=5
          stop "Lisbon" lat=38.72 lon=-9.14
          stop "Madrid" lat=40.42 lon=-3.70 detail="Overnight"
          stop "Barcelona" lat=41.39 lon=2.17
          route "By train" via="Lisbon,Madrid,Barcelona"
        """;

    private static NthUiRig Show(string text, bool dark = false, bool reduceMotion = false, double width = 560)
    {
        var rig = new NthUiRig(NthUiView.Show(text, culture: NthUiRig.English), width: width, theme: dark ? Avalonia.Styling.ThemeVariant.Dark : null, reduceMotion: reduceMotion);
        rig.Settle();
        return rig;
    }

    private static Point InWindow(NthUiRig rig, NthUiMapCanvas canvas, Point local) => canvas.TranslatePoint(local, rig.Window) ?? throw new Xunit.Sdk.XunitException("not in the window");

    private static Color Pixel(RenderedPixels pixels, Point window)
    {
        var (r, g, b) = pixels.At((int)Math.Round(window.X), (int)Math.Round(window.Y));
        return Color.FromRgb((byte)r, (byte)g, (byte)b);
    }

    private static void Near(Color expected, Color actual, int tolerance, string what) =>
        Assert.True(Math.Abs(expected.R - actual.R) <= tolerance && Math.Abs(expected.G - actual.G) <= tolerance && Math.Abs(expected.B - actual.B) <= tolerance,
            $"{what}: expected {expected}, drew {actual}");

    private static void Glide(NthUiRig rig, double seconds = 1.5) => rig.Run(seconds);

    // ---- the view -------------------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_map_line_is_a_native_map_with_its_title_buttons_summary_and_every_stop_in_view() => headless.OnUiThread(() =>
    {
        using var rig = Show(NthUiSamples.Map);
        var view = rig.ViewOf<NthUiMapView>();
        var canvas = view.Canvas;
        Assert.Equal(3, view.Model.Stops.Count);
        Assert.Equal(528, canvas.Bounds.Width, 1);
        Assert.InRange(canvas.Bounds.Height, 200, 380);
        Assert.Contains("Lisbon to Paris", rig.Texts());
        Assert.Equal("Lisbon to Paris", AutomationProperties.GetName(canvas));
        Assert.Equal(view.Summary, AutomationProperties.GetHelpText(canvas));
        Assert.Equal("Lisbon to Paris: 3 stops, Lisbon, Madrid (Overnight), Paris. Route By train: Lisbon to Madrid to Paris.", view.Summary);
        foreach (var (button, name) in new[] { (view.ZoomInButton, "Zoom in"), (view.ZoomOutButton, "Zoom out"), (view.FitButton, "Show all stops") })
            Assert.Equal(name, AutomationProperties.GetName(button));
        Assert.Equal("Show as list", AutomationProperties.GetName(view.ListButton));
        for (var i = 0; i < 3; i++)
        {
            var at = canvas.StopPosition(i);
            Assert.True(new Rect(canvas.Bounds.Size).Deflate(20).Contains(at), $"stop {i} at {at}");
        }
        Assert.False(canvas.UserMoved);
        Assert.InRange(canvas.Camera.Zoom, 3, 4.05); // the line asked for zoom 4 and the three stops need no more
        Assert.True(canvas.PlacedLabels.Count(l => l.Kind == NthUiMapLabelKind.Stop) == 3);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void The_list_is_the_text_alternative_and_swaps_with_the_map_and_back() => headless.OnUiThread(() =>
    {
        using var rig = Show(NthUiSamples.Map);
        var view = rig.ViewOf<NthUiMapView>();
        Assert.False(view.List.IsVisible);
        rig.Click(view.ListButton);
        rig.Settle();
        Assert.True(view.IsListShown);
        Assert.True(view.List.IsVisible);
        Assert.False(view.Canvas.IsEffectivelyVisible);
        Assert.Equal("Show as map", AutomationProperties.GetName(view.ListButton));
        Assert.Equal(["Lisbon to Paris", "Lisbon", "38.72°N, 9.14°W", "Madrid", "Overnight · 40.42°N, 3.7°W", "Paris", "48.86°N, 2.35°E", "By train", "Lisbon → Madrid → Paris", "Show as map"],
            rig.TextsOf("map"));
        rig.Click(view.ListButton);
        rig.Settle();
        Assert.False(view.IsListShown);
        Assert.True(view.Canvas.IsEffectivelyVisible);
        Assert.Equal("Show as list", AutomationProperties.GetName(view.ListButton));
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_map_with_no_stops_shows_the_world_and_says_so() => headless.OnUiThread(() =>
    {
        using var rig = Show("map \"Nowhere yet\"");
        var view = rig.ViewOf<NthUiMapView>();
        Assert.True(view.Model.IsEmpty);
        Assert.Equal("Nowhere yet: no stops yet.", view.Summary);
        Assert.DoesNotContain(view.Canvas.PlacedLabels, l => l.Kind == NthUiMapLabelKind.Stop);
        Assert.True(view.Canvas.Camera.Zoom <= 1.5);
        rig.Capture(); // drawing the bare world must work
    });

    // ---- pixels ------------------------------------------------------------------------------------------------------

    [Theory]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    [InlineData(false)]
    [InlineData(true)]
    public void The_map_draws_ocean_land_pins_and_route_in_the_threads_palette(bool dark) => headless.OnUiThread(() =>
    {
        using var rig = Show(Iberia, dark);
        var view = rig.ViewOf<NthUiMapView>();
        var canvas = view.Canvas;
        var (ocean, land, _, _, _, _) = canvas.Colours();
        var pixels = rig.Capture();
        var camera = canvas.Camera;
        Color AtPlace(double lat, double lon)
        {
            var unit = NthUiMapProjection.ToUnit(lat, lon);
            return Pixel(pixels, InWindow(rig, canvas, camera.ToScreen(unit.X, unit.Y)));
        }
        Near(land, AtPlace(39.5, -5.5), 3, "the Spanish plateau");
        Near(ocean, AtPlace(38.0, -13.5), 3, "the Atlantic west of Lisbon");
        var madrid = canvas.StopPosition(1);
        var pin = Pixel(pixels, InWindow(rig, canvas, madrid));
        Assert.True(NthUiTokens.Contrast(pin, land) > 4, $"the pin stands out from the land ({pin})");
        // The route follows the great circle between the stops: along it, away from the pins and their label pills, the ink is neither
        // land nor ocean.
        var path = view.Model.Paths.Single();
        var pins = Enumerable.Range(0, view.Model.Stops.Count).Select(canvas.StopPosition).ToList();
        var inked = 0;
        var samples = 0;
        for (var i = 1; i < path.Points.Count; i++)
        {
            var a = camera.ToScreen(path.Points[i - 1].X, path.Points[i - 1].Y);
            var b = camera.ToScreen(path.Points[i].X, path.Points[i].Y);
            for (var t = 0.0; t < 1; t += .25)
            {
                var at = new Point(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
                if (!new Rect(canvas.Bounds.Size).Deflate(8).Contains(at) || pins.Any(p => Math.Sqrt(Math.Pow(p.X - at.X, 2) + Math.Pow(p.Y - at.Y, 2)) < 16)
                    || canvas.PlacedLabels.Any(l => l.Kind == NthUiMapLabelKind.Stop && l.Box.Inflate(5).Contains(at))) continue;
                samples++;
                var drawn = Pixel(pixels, InWindow(rig, canvas, at));
                if (NthUiTokens.Contrast(drawn, land) > 1.6 && NthUiTokens.Contrast(drawn, ocean) > 1.6) inked++;
            }
        }
        Assert.True(samples >= 6 && inked >= samples * .9, $"the route is drawn along its path ({inked} of {samples} samples)");
        // The corner is rounded and the rim is on the edge: the pixel at the very corner is the thread's, not the ocean's.
        var corner = Pixel(pixels, InWindow(rig, canvas, new Point(.5, .5)));
        Near(dark ? NthUiRig.DarkThread : NthUiRig.LightThread, corner, 6, "the rounded corner");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void The_palette_differs_between_light_and_dark_and_every_label_colour_reads_on_the_ocean_the_land_and_the_stop_pill() => headless.OnUiThread(() =>
    {
        using var light = Show(Iberia);
        using var dark = Show(Iberia, dark: true);
        var l = light.ViewOf<NthUiMapView>().Canvas.Colours();
        var d = dark.ViewOf<NthUiMapView>().Canvas.Colours();
        Assert.True(NthUiTokens.Luminance(d.Ocean) < NthUiTokens.Luminance(l.Ocean));
        Assert.True(NthUiTokens.Luminance(d.Land) < NthUiTokens.Luminance(l.Land));
        foreach (var (name, colours) in new[] { ("light", l), ("dark", d) })
        {
            foreach (var (what, ink) in new[] { ("city", colours.City), ("country", colours.Country) })
            {
                foreach (var (ground, back) in new[] { ("ocean", colours.Ocean), ("land", colours.Land) })
                    Assert.True(NthUiTokens.Contrast(NthUiTokens.Over(ink, back), back) >= 4.5, $"{name}: {what} label on {ground}: {NthUiTokens.Contrast(NthUiTokens.Over(ink, back), back):0.00}");
            }
            Assert.True(NthUiTokens.Contrast(colours.StopLabel, colours.Pill) >= 4.5, $"{name}: stop label on its pill");
            Assert.True(NthUiTokens.Contrast(colours.Land, colours.Ocean) >= 1.04, $"{name}: land is told from ocean");
        }
    });

    // ---- the camera by pointer, keys and buttons ------------------------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Ctrl_and_the_wheel_zoom_about_the_pointer_and_a_plain_wheel_is_left_for_the_thread() => headless.OnUiThread(() =>
    {
        using var rig = Show(Iberia);
        var canvas = rig.ViewOf<NthUiMapView>().Canvas;
        var zoom = canvas.Camera.Zoom;
        var at = new Point(150, 120);
        var under = canvas.Camera.ToUnit(at);
        var window = InWindow(rig, canvas, at);
        rig.Window.MouseWheel(window, new Vector(0, 1), RawInputModifiers.None);
        rig.Pump();
        Glide(rig);
        Assert.Equal(zoom, canvas.Camera.Zoom, 6);
        Assert.False(canvas.UserMoved);
        rig.Window.MouseWheel(window, new Vector(0, 1), RawInputModifiers.Control);
        rig.Pump();
        Assert.True(canvas.UserMoved);
        Assert.Equal(zoom + NthUiMapCanvas.WheelStep, canvas.TargetZoom, 6);
        Glide(rig);
        Assert.Equal(zoom + NthUiMapCanvas.WheelStep, canvas.Camera.Zoom, 3);
        var still = canvas.Camera.ToScreen(under.X, under.Y);
        Assert.Equal(at.X, still.X, 1);
        Assert.Equal(at.Y, still.Y, 1);
        // Notches add up from the target, not from where the glide has got to.
        rig.Window.MouseWheel(window, new Vector(0, 1), RawInputModifiers.Control);
        rig.Pump();
        rig.Tick();
        rig.Window.MouseWheel(window, new Vector(0, 1), RawInputModifiers.Control);
        rig.Pump();
        Assert.Equal(zoom + 3 * NthUiMapCanvas.WheelStep, canvas.TargetZoom, 6);
        Glide(rig);
        Assert.Equal(zoom + 3 * NthUiMapCanvas.WheelStep, canvas.Camera.Zoom, 3);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Dragging_moves_the_map_with_the_pointer_one_to_one_and_stops_it_following_new_stops() => headless.OnUiThread(() =>
    {
        using var rig = Show(Iberia);
        var canvas = rig.ViewOf<NthUiMapView>().Canvas;
        var before = canvas.StopPosition(1);
        var start = InWindow(rig, canvas, new Point(200, 100));
        rig.Window.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        for (var i = 1; i <= 5; i++)
        {
            rig.Window.MouseMove(new Point(start.X + 8 * i, start.Y + 2 * i), RawInputModifiers.LeftMouseButton);
            rig.Pump();
            var stop = canvas.StopPosition(1);
            Assert.Equal(before.X + 8 * i, stop.X, 0); // 1:1 with the pointer at every step, no lag
            Assert.Equal(before.Y + 2 * i, stop.Y, 0);
        }
        rig.Window.MouseUp(new Point(start.X + 40, start.Y + 10), MouseButton.Left, RawInputModifiers.None);
        rig.Pump();
        Assert.True(canvas.UserMoved);
        var after = canvas.StopPosition(1);
        Glide(rig);
        Assert.Equal(after, canvas.StopPosition(1)); // nothing keeps gliding after the drag
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_drag_takes_the_map_from_a_glide_where_it_is_without_a_jump() => headless.OnUiThread(() =>
    {
        using var rig = Show(Iberia);
        var canvas = rig.ViewOf<NthUiMapView>().Canvas;
        canvas.ZoomBy(1.2, null);
        rig.Run(.12);
        var midway = canvas.Camera;
        Assert.True(canvas.IsGliding);
        var stop = canvas.StopPosition(0);
        var start = InWindow(rig, canvas, new Point(200, 100));
        rig.Window.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        rig.Window.MouseMove(new Point(start.X + 20, start.Y), RawInputModifiers.LeftMouseButton);
        rig.Pump();
        var grabbed = canvas.Camera;
        Assert.False(canvas.IsGliding);
        Assert.Equal(midway.Zoom, grabbed.Zoom, 2); // the zoom stays where the glide had got to
        Assert.Equal(stop.X + 20, canvas.StopPosition(0).X, 0);
        rig.Window.MouseUp(new Point(start.X + 20, start.Y), MouseButton.Left, RawInputModifiers.None);
        Glide(rig);
        Assert.Equal(grabbed.Zoom, canvas.Camera.Zoom, 3);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void The_keys_pan_zoom_and_fit_and_the_buttons_do_the_same() => headless.OnUiThread(() =>
    {
        using var rig = Show(Iberia);
        var view = rig.ViewOf<NthUiMapView>();
        var canvas = view.Canvas;
        canvas.Focus();
        rig.Pump();
        var zoom = canvas.Camera.Zoom;
        var first = canvas.StopPosition(0);
        rig.Window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
        rig.Pump();
        Glide(rig);
        Assert.Equal(first.X + canvas.Bounds.Width * .2, canvas.StopPosition(0).X, 1); // Left looks left, so the map moves right
        Assert.Equal(first.Y, canvas.StopPosition(0).Y, 1);
        rig.Window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        rig.Pump();
        Glide(rig);
        Assert.Equal(first.X, canvas.StopPosition(0).X, 1);
        rig.Window.KeyPress(Key.OemPlus, RawInputModifiers.None, PhysicalKey.Equal, "=");
        rig.Pump();
        Glide(rig);
        Assert.Equal(zoom + 1, canvas.Camera.Zoom, 3);
        rig.Window.KeyPress(Key.OemMinus, RawInputModifiers.None, PhysicalKey.Minus, "-");
        rig.Pump();
        Glide(rig);
        Assert.Equal(zoom, canvas.Camera.Zoom, 3);
        rig.Click(view.ZoomInButton);
        Glide(rig);
        Assert.Equal(zoom + 1, canvas.Camera.Zoom, 3);
        rig.Click(view.ZoomOutButton);
        Glide(rig);
        Assert.Equal(zoom, canvas.Camera.Zoom, 3);
        canvas.PanBy(new Vector(90, 40));
        Assert.True(canvas.UserMoved);
        rig.Window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
        rig.Pump();
        Glide(rig);
        Assert.False(canvas.UserMoved);
        Assert.Equal(zoom, canvas.Camera.Zoom, 2);
        Assert.Equal(first.X, canvas.StopPosition(0).X, 1);
        canvas.PanBy(new Vector(-60, 0));
        rig.Click(view.FitButton);
        Glide(rig);
        Assert.Equal(first.X, canvas.StopPosition(0).X, 1);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void The_zoom_stays_between_the_worlds_edge_and_the_deepest_the_basemap_has() => headless.OnUiThread(() =>
    {
        using var rig = Show(Iberia);
        var canvas = rig.ViewOf<NthUiMapView>().Canvas;
        for (var i = 0; i < 20; i++) canvas.ZoomIn();
        Assert.Equal(NthUiMapCamera.MaxZoom, canvas.TargetZoom, 6);
        Glide(rig, 3);
        Assert.Equal(NthUiMapCamera.MaxZoom, canvas.Camera.Zoom, 3);
        for (var i = 0; i < 30; i++) canvas.ZoomOut();
        Glide(rig, 3);
        Assert.Equal(canvas.Camera.MinZoom, canvas.Camera.Zoom, 3);
        var visible = canvas.Camera.VisibleUnits();
        Assert.True(visible.Top >= -1e-6 && visible.Bottom <= 1 + 1e-6, "the poles stay out of view");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_new_request_in_the_middle_of_a_glide_carries_on_from_where_it_is_with_no_snap() => headless.OnUiThread(() =>
    {
        using var rig = Show(Iberia);
        var canvas = rig.ViewOf<NthUiMapView>().Canvas;
        var start = canvas.Camera.Zoom;
        canvas.ZoomBy(1.5, null);
        rig.Run(.1);
        var before = canvas.Camera;
        canvas.ZoomBy(-1.5, null);
        var after = canvas.Camera;
        Assert.Equal(before.Zoom, after.Zoom, 6);
        Assert.Equal(before.CenterX, after.CenterX, 9);
        // Reversing: the zoom turns around smoothly (the speed carries over, so it overshoots toward the old way a little first).
        var zooms = new List<double>();
        for (var i = 0; i < 60; i++)
        {
            rig.Tick();
            zooms.Add(canvas.Camera.Zoom);
        }
        for (var i = 1; i < zooms.Count; i++) Assert.True(Math.Abs(zooms[i] - zooms[i - 1]) < .25, $"a snap at frame {i}: {zooms[i - 1]} to {zooms[i]}");
        Assert.Equal(start, zooms[^1], 2); // the second request undid the first, from wherever the glide had got to
    });

    [Theory]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    [InlineData(false, 1.2)]
    [InlineData(true, .2)]
    public void A_glide_is_a_spring_and_with_Reduce_Motion_a_short_tween(bool calm, double within) => headless.OnUiThread(() =>
    {
        using var rig = Show(Iberia, reduceMotion: calm);
        var canvas = rig.ViewOf<NthUiMapView>().Canvas;
        var zoom = canvas.Camera.Zoom;
        canvas.ZoomIn();
        rig.Tick();
        Assert.True(canvas.IsGliding);
        var settledAt = -1.0;
        for (var i = 1; i <= 120; i++)
        {
            rig.Tick();
            if (!canvas.IsGliding)
            {
                settledAt = i * NthUiRig.Frame;
                break;
            }
        }
        Assert.InRange(settledAt, .05, within);
        Assert.Equal(zoom + 1, canvas.Camera.Zoom, 3);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Hovering_a_stop_names_it_with_the_hand_and_leaving_lets_go() => headless.OnUiThread(() =>
    {
        using var rig = Show(Iberia);
        var canvas = rig.ViewOf<NthUiMapView>().Canvas;
        var madrid = InWindow(rig, canvas, canvas.StopPosition(1));
        rig.Window.MouseMove(madrid, RawInputModifiers.None);
        rig.Pump();
        Assert.Equal(1, canvas.HoveredStop);
        Assert.NotNull(canvas.Cursor);
        rig.Window.MouseMove(InWindow(rig, canvas, new Point(10, 10)), RawInputModifiers.None);
        rig.Pump();
        Assert.Equal(-1, canvas.HoveredStop);
    });

    // ---- streaming ---------------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Stops_arrive_with_a_fade_the_camera_keeps_them_in_view_and_nothing_before_the_map_moves() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig();
        Rect? first = null;
        void Watch()
        {
            var text = rig.View.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == "Before the map.");
            if (text is null) return;
            var rect = rig.Layout(text);
            first ??= rect;
            Assert.Equal(first, rect);
        }
        const string text = """
            text "Before the map."
            map "Iberia"
              stop "Lisbon" lat=38.72 lon=-9.14
              stop "Madrid" lat=40.42 lon=-3.70
              stop "Barcelona" lat=41.39 lon=2.17
            text "After the map."
            """;
        var zooms = new List<double>();
        rig.Stream(text, 6, () =>
        {
            Watch();
            if (rig.View.Document.Nodes.Any(n => n.Kind == "map" && !n.Degraded) && rig.View.ViewFor(rig.Node("map").Key) is NthUiMapView view) zooms.Add(view.Canvas.TargetZoom);
        });
        rig.View.Complete();
        rig.Settle(5, Watch);
        Glide(rig, 2);
        var map = rig.ViewOf<NthUiMapView>();
        Assert.Equal(3, map.Model.Stops.Count);
        Assert.False(map.Canvas.UserMoved);
        for (var i = 0; i < 3; i++) Assert.True(new Rect(map.Canvas.Bounds.Size).Deflate(30).Contains(map.Canvas.StopPosition(i)), $"stop {i}");
        // The camera went from the world to the first stop to the three: the target zoom changed as stops arrived.
        Assert.True(zooms.Distinct().Count() >= 2, "the camera followed the stops");
        Assert.True(zooms.Max() > zooms.Min());
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Once_the_reader_moves_the_map_it_stops_following_new_stops() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig();
        rig.View.Append("map \"Trip\"\n  stop \"Lisbon\" lat=38.72 lon=-9.14\n  stop \"Madrid\" lat=40.42 lon=-3.70\n");
        rig.Pump();
        Glide(rig, 2);
        var canvas = rig.ViewOf<NthUiMapView>().Canvas;
        canvas.ZoomIn();
        Glide(rig);
        var target = canvas.TargetZoom;
        var center = canvas.TargetCamera.CenterX;
        rig.View.Append("  stop \"Paris\" lat=48.86 lon=2.35\n");
        rig.View.Complete();
        Glide(rig, 2);
        Assert.Equal(target, canvas.TargetZoom, 6);
        Assert.Equal(center, canvas.TargetCamera.CenterX, 6);
        Assert.Equal(3, rig.ViewOf<NthUiMapView>().Model.Stops.Count);
        // "Show all stops" brings the new one into view and the map follows again.
        canvas.FitAll();
        Glide(rig, 2);
        Assert.True(new Rect(canvas.Bounds.Size).Deflate(20).Contains(canvas.StopPosition(2)));
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_map_nobody_has_moved_follows_its_column_when_it_is_resized() => headless.OnUiThread(() =>
    {
        using var rig = Show(NthUiSamples.Map);
        var canvas = rig.ViewOf<NthUiMapView>().Canvas;
        var narrow = canvas.Bounds.Width;
        rig.Window.Width = 400;
        rig.Pump();
        rig.Settle();
        Assert.True(canvas.Bounds.Width < narrow);
        for (var i = 0; i < 3; i++) Assert.True(new Rect(canvas.Bounds.Size).Deflate(20).Contains(canvas.StopPosition(i)), $"stop {i} after the resize");
    });

    // ---- labels ----------------------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Labels_name_the_stops_always_and_countries_and_cities_as_the_zoom_reaches_them_without_overlap() => headless.OnUiThread(() =>
    {
        using var rig = Show(Iberia);
        var canvas = rig.ViewOf<NthUiMapView>().Canvas;
        rig.Capture();
        Glide(rig, 1);
        rig.Capture();
        var labels = canvas.PlacedLabels;
        foreach (var name in new[] { "Lisbon", "Madrid", "Barcelona" }) Assert.Contains(labels, l => l.Kind == NthUiMapLabelKind.Stop && l.Text == name);
        Assert.Contains(labels, l => l.Kind == NthUiMapLabelKind.Country && l.Text is "Spain" or "Portugal" or "France");
        var view = new Rect(canvas.Bounds.Size);
        var others = labels.Where(l => l.Kind != NthUiMapLabelKind.Stop).ToList();
        for (var i = 0; i < others.Count; i++)
        {
            Assert.True(view.Contains(others[i].Box.TopLeft) && view.Contains(others[i].Box.BottomRight), others[i].Text);
            for (var j = i + 1; j < others.Count; j++) Assert.False(others[i].Box.Intersects(others[j].Box), $"{others[i].Text} overlaps {others[j].Text}");
            foreach (var stop in labels.Where(l => l.Kind == NthUiMapLabelKind.Stop)) Assert.False(others[i].Box.Intersects(stop.Box), $"{others[i].Text} overlaps the stop {stop.Text}");
        }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Cities_are_named_by_rank_as_the_zoom_grows_and_the_far_view_names_none() => headless.OnUiThread(() =>
    {
        using var rig = Show("map \"Europe\" zoom=3\n  stop \"Istanbul\" lat=41.01 lon=28.98");
        var canvas = rig.ViewOf<NthUiMapView>().Canvas;
        rig.Capture();
        Glide(rig, 1);
        rig.Capture();
        Assert.Equal(3, canvas.Camera.Zoom, 2);
        var places = NthUiMapData.Shared.Places;
        var cities = canvas.PlacedLabels.Where(l => l.Kind == NthUiMapLabelKind.City).ToList();
        Assert.True(cities.Count >= 3, "cities are named at zoom 3: " + string.Join(", ", canvas.PlacedLabels.Select(l => l.Kind + ":" + l.Text)));
        Assert.All(cities, city => Assert.True(NthUiMapLabelPlanner.CityZoom(places.First(p => "city:" + p.Rank == city.Key).Rank) <= 3));
        // A step out, only the biggest twelve are named.
        canvas.ZoomBy(-1, null);
        Glide(rig, 2);
        rig.Capture();
        var fewer = canvas.PlacedLabels.Where(l => l.Kind == NthUiMapLabelKind.City).ToList();
        Assert.True(fewer.Count <= cities.Count);
        Assert.All(fewer, city => Assert.True(places.First(p => "city:" + p.Rank == city.Key).Rank < 12, city.Text));
        canvas.ZoomBy(-6, null);
        Glide(rig, 3);
        rig.Capture();
        Assert.DoesNotContain(canvas.PlacedLabels, l => l.Kind == NthUiMapLabelKind.City);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void A_label_that_the_zoom_reaches_fades_in_over_a_few_frames_and_one_that_it_leaves_fades_out() => headless.OnUiThread(() =>
    {
        using var rig = Show(Iberia);
        var canvas = rig.ViewOf<NthUiMapView>().Canvas;
        rig.Capture();
        Glide(rig, 1);
        rig.Capture();
        var before = canvas.PlacedLabels.Where(l => l.Kind == NthUiMapLabelKind.City).Select(l => l.Key).ToHashSet();
        canvas.ZoomBy(1.5, null);
        var arrived = new Dictionary<string, List<double>>();
        for (var i = 0; i < 80; i++)
        {
            rig.Tick();
            rig.Capture();
            foreach (var label in canvas.PlacedLabels.Where(l => !before.Contains(l.Key)))
            {
                if (!arrived.TryGetValue(label.Key, out var list)) arrived[label.Key] = list = [];
                list.Add(canvas.LabelAlpha(label.Key));
            }
        }
        Assert.NotEmpty(arrived);
        var gradual = arrived.Values.Any(list => list.Count > 4 && list.Take(list.Count - 1).Any(a => a is > .05 and < .95));
        Assert.True(gradual, "some label came in over several frames, not at once");
        Assert.All(arrived.Values.Where(l => l.Count > 15), list => Assert.True(list[^1] > .95));
    });

    // ---- the tile seam ---------------------------------------------------------------------------------------------------

    private sealed class SolidTiles(Color color, int maxZoom) : INthUiMapTiles
    {
        private readonly Avalonia.Media.Imaging.RenderTargetBitmap tile = Make(color);

        public int Asked { get; private set; }

        public int MaxZoom { get; } = maxZoom;

        public string Attribution => "Test tiles";

        public IImage? Tile(int zoom, int x, int y)
        {
            Asked++;
            return tile;
        }

        public event Action? TilesChanged
        {
            add { }
            remove { }
        }

        private static Avalonia.Media.Imaging.RenderTargetBitmap Make(Color color)
        {
            var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(256, 256));
            using (var context = bitmap.CreateDrawingContext(true)) context.FillRectangle(new SolidColorBrush(color), new Rect(0, 0, 256, 256));
            return bitmap;
        }
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void With_no_tile_source_the_map_is_the_basemap_alone_and_a_bound_source_draws_over_it_and_allows_deeper_zoom() => headless.OnUiThread(() =>
    {
        Assert.False(NthUiMapTiles.IsBound);
        using var rig = Show(Iberia);
        var canvas = rig.ViewOf<NthUiMapView>().Canvas;
        Assert.Equal(NthUiMapCamera.MaxZoom, canvas.MaxZoom);
        var tiles = new SolidTiles(Color.FromRgb(200, 30, 120), 16);
        using (NthUiMapTiles.Source.Override(tiles))
        {
            Assert.True(NthUiMapTiles.IsBound);
            Assert.Equal(16, canvas.MaxZoom);
            canvas.InvalidateVisual();
            var pixels = rig.Capture();
            Assert.True(tiles.Asked > 0, "the source was asked for tiles");
            Near(Color.FromRgb(200, 30, 120), Pixel(pixels, InWindow(rig, canvas, new Point(canvas.Bounds.Width / 2, 40))), 4, "a tile over the basemap");
            for (var i = 0; i < 20; i++) canvas.ZoomIn();
            Assert.Equal(16, canvas.TargetZoom, 6);
        }
        Assert.False(NthUiMapTiles.IsBound);
        Assert.Equal(NthUiMapCamera.MaxZoom, canvas.MaxZoom);
    });

    // ---- no network ----------------------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Nothing_in_the_map_or_the_diagram_drawing_code_can_open_a_connection()
    {
        string? root = null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Marrow.sln"))) { root = dir.FullName; break; }
        }
        Assert.NotNull(root);
        var forbidden = new[] { "HttpClient", "WebRequest", "WebClient", "System.Net.Http", "System.Net.Sockets", "TcpClient", "Socket(", "WebSocket", "DownloadString", "DownloadFile", "Process.Start" };
        foreach (var sub in new[] { "Map", "Diagram" })
        {
            foreach (var file in Directory.GetFiles(Path.Combine(root!, "src", "Marrow.UI", "NthUi", sub), "*.cs"))
            {
                var code = string.Join('\n', File.ReadAllLines(file).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
                foreach (var word in forbidden) Assert.False(code.Contains(word, StringComparison.Ordinal), $"{Path.GetFileName(file)} mentions {word}");
            }
        }
    }

    // ---- cost ------------------------------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void The_whole_world_and_a_close_view_each_draw_inside_a_frame_budget_after_the_first_build() => headless.OnUiThread(() =>
    {
        using var rig = Show(Iberia);
        var canvas = rig.ViewOf<NthUiMapView>().Canvas;
        rig.Capture(); // builds the geometry of this level
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 5; i++) rig.Capture();
        var close = watch.Elapsed.TotalMilliseconds / 5;
        canvas.ZoomBy(-5, null);
        Glide(rig, 3);
        rig.Capture();
        watch.Restart();
        for (var i = 0; i < 5; i++) rig.Capture();
        var world = watch.Elapsed.TotalMilliseconds / 5;
        Assert.True(close < 150, $"a close view took {close:0} ms a render (software drawing, debug build)");
        Assert.True(world < 250, $"the whole world took {world:0} ms a render (software drawing, debug build)");
    });
}
