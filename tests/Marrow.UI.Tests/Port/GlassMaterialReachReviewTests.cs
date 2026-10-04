// Source: DroppyCode/UI/Chrome/Chrome.swift (B3 52bc792), :171-182 (the glass frosts the app content under it). Review of ED-320 15-16.
// REVIEW (round 3): the same-frame reach (GlassMaterial.Reach, set as the composition target's internal debug events), the smaller reach deep inside a
// big lone piece (Spread), the kept surfaces (Backdrop: the window surface's size is read again only when its handle changes) and the small shader
// for the inside of a lone rounded rectangle (Inside, Inner, Unlit). These pin:
// - the reach is really installed, on a window and on a popup's own top level (in whatever build configuration the tests run: Debug and Release);
// - a change under a big piece, swept from its edge to its middle and at the threshold where the reach shrinks to the blur's alone, at 1 and 1.5,
//   on a corner radius of 26 and of 0 and on a piece scaled to 0.5 by its render transform: the whole piece is a whole drawing's;
// - two windows with glass, a change under each: each is a whole drawing's;
// - after the window grows or shrinks past a piece whose sampled margin crosses the window's edge, and after a scale change, the glass is what a
//   window made at that size and scale draws (a whole drawing of the same window is no reference: it shares the kept state);
// - the inside shader: a lone rounded rectangle drawn as it is (Inside, Inner or Unlit) is byte-identical to the same piece forced onto the full shader
//   everywhere (a render transform whose M12 is 1e-300: Inside and Spread see a turned shape, while the shader's float transform is the identity),
//   over several sizes, radii, styles, positions at fractions of a pixel, hover and press, at 1, 1.25, 1.5 and 2. Measured: identical at 1, 1.5 and 2;
//   at 1.25 three of the ten pieces differ by 1 level in a few rows of the inside (55, 1 and 105 pixels; against aed1c1c31's whole drawings also the
//   window-sized overlay at 1.25, 664 and 342 pixels, and a piece scaled to 0.8 at 1.25 and 1.5): the same sum, sampled through Skia's other
//   coordinate path. The bar here is that: 0 levels, 1 at 1.25.
// - Backdrop measures the window surface again when a new surface has the old one's address (driven directly, with software surfaces): FAILS at bbafefde5;
// - Backdrop makes the blur's surface again after a GPU context change (driven directly): FAILS at bbafefde5.
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;
using Scene = Marrow.UI.Tests.Fixtures.GlassScene;

namespace Marrow.UI.Tests.Port;

[Collection("Headless")]
public sealed class GlassMaterialReachReviewTests(HeadlessSession headless)
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static int Largest(Func<int, int, Color> a, Func<int, int, Color> b, PixelRect area, out PixelPoint at)
    {
        var most = 0;
        at = default;
        for (var y = area.Y; y < area.Bottom; y++)
            for (var x = area.X; x < area.Right; x++)
            {
                Color p = a(x, y), q = b(x, y);
                var d = Math.Max(Math.Abs(p.R - q.R), Math.Max(Math.Abs(p.G - q.G), Math.Abs(p.B - q.B)));
                if (d > most) { most = d; at = new PixelPoint(x, y); }
            }
        return most;
    }

    private static Func<int, int, Color> Whole(Scene scene)
    {
        foreach (var visual in scene.Window.GetVisualDescendants()) visual.InvalidateVisual();
        return scene.Pixels();
    }

    private static void Scale(TopLevel window, double scale)
    {
        var type = typeof(AvaloniaHeadlessPlatformOptions).Assembly.GetType("Avalonia.Headless.IHeadlessWindow", throwOnError: true)!;
        type.GetMethod("SetRenderScaling")!.Invoke(window.PlatformImpl, [scale]);
    }

    private static PixelRect Area(Control control, Visual window, double scale)
    {
        var origin = control.TranslatePoint(default, window)!.Value;
        return new PixelRect((int)Math.Ceiling(origin.X * scale), (int)Math.Ceiling(origin.Y * scale),
            (int)Math.Floor(control.Bounds.Width * scale) - 1, (int)Math.Floor(control.Bounds.Height * scale) - 1);
    }

    // The window's ServerCompositionTarget (TopLevel.Renderer, CompositingRenderer.CompositionTarget, CompositionTarget.Server; Avalonia 12.1.2) and its debug events.
    private static object? DebugEvents(TopLevel top)
    {
        var renderer = typeof(TopLevel).GetProperty("Renderer", Members)!.GetValue(top)!;
        var target = renderer.GetType().GetProperty("CompositionTarget", Members)!.GetValue(renderer)!;
        var server = target.GetType().GetProperties(Members).First(p => p.Name == "Server" && p.DeclaringType == target.GetType()).GetValue(target)!;
        return server.GetType().GetProperty("DebugEvents", Members)!.GetValue(server);
    }

    // Vertical white stripes every 8 px and horizontal colour bands every 24 px over grey, as wide and tall as asked: something the blur and the bend both move.
    private static Panel Backdrop(double width, double height)
    {
        var panel = new Panel { Background = Brushes.Gray };
        for (var x = 0; x < width; x += 8)
            panel.Children.Add(new Border { Width = 4, Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(x, 0, 0, 0) });
        IBrush[] bands = [Brushes.DarkRed, Brushes.DarkGreen, Brushes.Navy, Brushes.Goldenrod];
        for (var y = 0; y < height; y += 24)
            panel.Children.Add(new Border { Height = 6, Background = bands[y / 24 % bands.Length], VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, y, 0, 0) });
        return panel;
    }

    private static Border Glass(double width, double height, GlassShape shape, bool interactive = false)
    {
        var piece = new Border { Background = Brushes.Transparent, Width = width, Height = height };
        var effect = GlassEffect.Regular(shape);
        SwiftGlass.SetEffect(piece, interactive ? effect.Interactive() : effect);
        return piece;
    }

    [Fact]
    [Trait("Req", "PR-WIN-180")]
    public void REVIEW_A_window_with_glass_has_the_material_reach_as_its_targets_debug_events() => headless.OnUiThread(() =>
    {
        var piece = Glass(160, 120, GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous));
        piece.HorizontalAlignment = HorizontalAlignment.Center;
        piece.VerticalAlignment = VerticalAlignment.Center;
        using var scene = new Scene(Backdrop(256, 160), piece);
        _ = scene.Pixels();
        Assert.IsAssignableFrom<GlassMaterial.Reach>(DebugEvents(scene.Window));
    });

    // A popup in the window's overlay layer (the headless platform has no popup top level; on Win32 a popup is its own top level, whose target the
    // material's first draw there gives its own reach, as a window's): a block under a glass piece in the popup changes, and the popup's glass is
    // a whole drawing's.
    [Fact]
    [Trait("Req", "PR-WIN-180")]
    public void REVIEW_A_change_under_glass_in_a_popup_is_drawn_as_a_whole_drawing() => headless.OnUiThread(() =>
    {
        var piece = Glass(120, 80, GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous));
        piece.HorizontalAlignment = HorizontalAlignment.Center;
        piece.VerticalAlignment = VerticalAlignment.Center;
        var block = new Border { Width = 30, Height = 20, Background = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var anchor = new Border { Width = 20, Height = 20, Background = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var window = HeadlessPopupHost.OverlayWindow(new Panel { Children = { Backdrop(400, 300), anchor } });
        window.RequestedThemeVariant = ThemeVariant.Dark;
        window.Show();
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, ShouldUseOverlayLayer = true,
            Child = new Panel { Width = 200, Height = 140, Children = { Backdrop(200, 140), block, piece } } };
        ((Panel)window.Content!).Children.Add(popup);
        try
        {
            popup.IsOpen = true;
            Func<int, int, Color> Pixels()
            {
                for (var i = 0; i < 4; i++) Scene.Drain();
                using var bitmap = window.CaptureRenderedFrame()!;
                var size = bitmap.PixelSize;
                var bytes = new byte[size.Width * size.Height * 4];
                var handle = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
                try { bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), bytes.Length, size.Width * 4); }
                finally { handle.Free(); }
                var stride = size.Width;
                return (x, y) => { var p = (y * stride + x) * 4; return Color.FromArgb(bytes[p + 3], bytes[p], bytes[p + 1], bytes[p + 2]); };
            }
            _ = Pixels();
            Assert.Same(window, TopLevel.GetTopLevel(piece));
            Assert.IsAssignableFrom<GlassMaterial.Reach>(DebugEvents(window));
            block.Background = Brushes.White;
            var partial = Pixels();
            foreach (var visual in window.GetVisualDescendants()) visual.InvalidateVisual();
            var whole = Pixels();
            var apart = Largest(partial, whole, Area(piece, window, 1), out var apartAt);
            Assert.True(apart <= 2, $"the glass in the popup is {apart} levels from a whole drawing (at {apartAt})");
        }
        finally { popup.IsOpen = false; window.Close(); }
    });

    public static TheoryData<double, int, int, int> Sweep()
    {
        var data = new TheoryData<double, int, int, int>();
        foreach (var scale in new[] { 1.0, 1.5 })
            foreach (var variant in new[] { 0, 1, 2 })
            {
                foreach (var x in new[] { 30, 60, 100, 140, 180, 196, 204, 212, 230, 280 }) data.Add(scale, variant, x, -1);
                foreach (var y in new[] { 30, 60, 100, 120, 140 }) data.Add(scale, variant, -1, y);
            }
        return data;
    }

    // A 520 x 320 piece in a 600 x 400 window over stripes and bands (variant 0: corners of 26, continuous; 1: square corners; 2: corners of 26 and a
    // render transform scaling the piece to 0.5 about its centre). A 40 x 30 block under it turns from black to white at x (from the window's left,
    // centred vertically) or at y (from its top, centred horizontally); only the block is redrawn. The whole piece is what a whole drawing gives.
    [Theory]
    [MemberData(nameof(Sweep))]
    [Trait("Req", "PR-WIN-180")]
    public void REVIEW_A_change_swept_from_the_edge_to_the_middle_of_a_big_piece_is_drawn_as_a_whole_drawing(double scale, int variant, int x, int y) => headless.OnUiThread(() =>
    {
        var block = new Border { Width = 40, Height = 30, Background = Brushes.Black,
            HorizontalAlignment = x >= 0 ? HorizontalAlignment.Left : HorizontalAlignment.Center, VerticalAlignment = y >= 0 ? VerticalAlignment.Top : VerticalAlignment.Center,
            Margin = new Thickness(Math.Max(x, 0), Math.Max(y, 0), 0, 0) };
        var piece = Glass(520, 320, GlassShape.RoundedRectangle(variant == 1 ? 0 : 26, RoundedCornerStyle.Continuous));
        piece.HorizontalAlignment = HorizontalAlignment.Center;
        piece.VerticalAlignment = VerticalAlignment.Center;
        if (variant == 2) piece.RenderTransform = new ScaleTransform(0.5, 0.5);
        using var scene = new Scene(Backdrop(600, 400), new Panel { Children = { block, piece } }, 600, 400);
        _ = scene.Pixels();
        if (scale != 1.0) { Scale(scene.Window, scale); _ = scene.Pixels(); }
        block.Background = Brushes.White;
        var partial = scene.Pixels();
        var whole = Whole(scene);
        var apart = Largest(partial, whole, new PixelRect(0, 0, (int)(600 * scale), (int)(400 * scale)), out var apartAt);
        Assert.True(apart <= 2, $"at {scale}, variant {variant}, block at x {x} y {y}: the window is {apart} levels from a whole drawing (at {apartAt})");
    });

    // Two windows, each with a piece and a block under it; the blocks change in the same frame (A's) and then one after the other (B's alone).
    [Fact]
    [Trait("Req", "PR-WIN-180")]
    public void REVIEW_In_two_windows_a_change_under_each_glass_is_drawn_as_a_whole_drawing() => headless.OnUiThread(() =>
    {
        Border Block() => new() { Width = 40, Height = 30, Background = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Border Piece()
        {
            var piece = Glass(160, 120, GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous));
            piece.HorizontalAlignment = HorizontalAlignment.Center;
            piece.VerticalAlignment = VerticalAlignment.Center;
            return piece;
        }
        Border blockA = Block(), blockB = Block();
        using var a = new Scene(Backdrop(256, 160), new Panel { Children = { blockA, Piece() } });
        using var b = new Scene(Backdrop(256, 160), new Panel { Children = { blockB, Piece() } });
        _ = a.Pixels();
        _ = b.Pixels();
        blockA.Background = Brushes.White;
        blockB.Background = Brushes.White;
        var partialA = a.Pixels();
        var partialB = b.Pixels();
        var area = new PixelRect(0, 0, 256, 160);
        var apartA = Largest(partialA, Whole(a), area, out var atA);
        var apartB = Largest(partialB, Whole(b), area, out var atB);
        blockB.Background = Brushes.Black;
        var againB = b.Pixels();
        var apartB2 = Largest(againB, Whole(b), area, out var atB2);
        Assert.True(apartA <= 2 && apartB <= 2 && apartB2 <= 2,
            $"window A is {apartA} levels from a whole drawing (at {atA}); window B {apartB} (at {atB}) and, after B alone changed back, {apartB2} (at {atB2})");
    });

    // The window grows past a piece it cut off (its sampled margin, and part of it, lay outside the old window), or shrinks to cut one off; then a block
    // under it changes. The piece is what a window made at the final size draws.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Req", "PR-WIN-180")]
    public void REVIEW_After_the_window_grows_or_shrinks_the_glass_is_what_a_window_of_that_size_draws(bool grows) => headless.OnUiThread(() =>
    {
        (double W, double H) first = grows ? (256, 160) : (420, 300), last = grows ? (420, 300) : (256, 160);
        var at = grows ? new Thickness(150, 80, 0, 0) : new Thickness(120, 60, 0, 0);
        (Panel Content, Border Piece, Border Block) Build()
        {
            var piece = Glass(160, 120, GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous));
            piece.HorizontalAlignment = HorizontalAlignment.Left;
            piece.VerticalAlignment = VerticalAlignment.Top;
            piece.Margin = at;
            var block = new Border { Width = 30, Height = 20, Background = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(at.Left + 20, at.Top + 20, 0, 0) };
            return (new Panel { Children = { block, piece } }, piece, block);
        }
        var (content, piece, block) = Build();
        using var scene = new Scene(Backdrop(420, 300), content, first.W, first.H);
        _ = scene.Pixels();
        scene.Window.Width = last.W;
        scene.Window.Height = last.H;
        _ = scene.Pixels();
        block.Background = Brushes.White;
        var resized = scene.Pixels();
        var (freshContent, _, freshBlock) = Build();
        freshBlock.Background = Brushes.White;
        using var fresh = new Scene(Backdrop(420, 300), freshContent, last.W, last.H);
        var made = fresh.Pixels();
        var origin = piece.TranslatePoint(default, scene.Window)!.Value;
        var area = new PixelRect((int)origin.X, (int)origin.Y, (int)Math.Min(piece.Bounds.Width, last.W - origin.X), (int)Math.Min(piece.Bounds.Height, last.H - origin.Y));
        var apart = Largest(resized, made, area, out var apartAt);
        Assert.True(scene.Window.Bounds.Width == last.W && apart <= 2,
            $"after the window {(grows ? "grew" : "shrank")} the glass is {apart} levels from a window made at {last.W} x {last.H} (at {apartAt}, area {area})");
    });

    // The scale changes from 1 to 1.5 (as a window moved to another monitor) and a block under the glass changes; the glass is what a window made at 1.5 draws.
    [Fact]
    [Trait("Req", "PR-WIN-180")]
    public void REVIEW_After_a_scale_change_the_glass_is_what_a_window_made_at_that_scale_draws() => headless.OnUiThread(() =>
    {
        (Panel Content, Border Piece, Border Block) Build()
        {
            var piece = Glass(160, 120, GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous));
            piece.HorizontalAlignment = HorizontalAlignment.Right;
            piece.VerticalAlignment = VerticalAlignment.Bottom;
            var block = new Border { Width = 30, Height = 20, Background = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 60, 50) };
            return (new Panel { Children = { block, piece } }, piece, block);
        }
        var (content, piece, block) = Build();
        using var scene = new Scene(Backdrop(256, 160), content);
        _ = scene.Pixels();
        Scale(scene.Window, 1.5);
        _ = scene.Pixels();
        block.Background = Brushes.White;
        var changed = scene.Pixels();
        var (freshContent, _, freshBlock) = Build();
        freshBlock.Background = Brushes.White;
        var window = new Window { Width = 256, Height = 160, RequestedThemeVariant = ThemeVariant.Dark, Content = new Panel { Children = { Backdrop(256, 160), freshContent } } };
        Scale(window, 1.5);
        window.Show();
        try
        {
            for (var i = 0; i < 4; i++) Scene.Drain();
            using var bitmap = window.CaptureRenderedFrame()!;
            var size = bitmap.PixelSize;
            var bytes = new byte[size.Width * size.Height * 4];
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
            try { bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), bytes.Length, size.Width * 4); }
            finally { handle.Free(); }
            var rgba = bitmap.Format == Avalonia.Platform.PixelFormat.Rgba8888;
            Color Made(int x, int y)
            {
                var p = (y * size.Width + x) * 4;
                return Color.FromArgb(bytes[p + 3], bytes[p + (rgba ? 0 : 2)], bytes[p + 1], bytes[p + (rgba ? 2 : 0)]);
            }
            var area = Area(piece, scene.Window, 1.5);
            var apart = Largest(changed, Made, area, out var apartAt);
            Assert.True(window.RenderScaling == 1.5 && apart <= 2, $"after the scale change the glass is {apart} levels from a window made at 1.5 (at {apartAt})");
        }
        finally { window.Close(); }
    });

    public static TheoryData<double, double, double, double, bool, int, double> Inside()
    {
        var data = new TheoryData<double, double, double, double, bool, int, double>();
        foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 })
        {
            data.Add(scale, 160, 120, 16, true, 0, 0);
            data.Add(scale, 160, 120, 16, true, 1, 0.3);
            data.Add(scale, 160, 120, 16, false, 2, 0.7);
            data.Add(scale, 90, 90, 0, true, 0, 0.5);
            data.Add(scale, 300, 200, 8, true, 1, 0.25);
            data.Add(scale, 300, 200, 40, false, 0, 0);
            data.Add(scale, 520, 320, 26, true, 0, 0.4);
            data.Add(scale, 520, 320, 26, true, 1, 0);
            data.Add(scale, 200, 140, 28, true, 0, 0.1);
            data.Add(scale, 124, 124, 19, true, 2, 0.6);
        }
        return data;
    }

    // A lone rounded rectangle (width x height, corner radius, continuous or circular; pointer 0 none, 1 hover, 2 press, at its middle; its left and top at
    // 20 plus the fraction) drawn as it is, and the same piece with a render transform whose M12 is 1e-300 (Inside and Spread take it as turned, so the
    // full shader draws all of it; the shader's float transform is the identity). The two windows are byte-identical.
    [Theory]
    [MemberData(nameof(Inside))]
    [Trait("Req", "PR-WIN-180")]
    public void REVIEW_The_inside_of_a_lone_rounded_rectangle_is_byte_identical_to_the_full_shader(double scale, double width, double height, double radius,
        bool continuous, int pointer, double fraction) => headless.OnUiThread(() =>
    {
        Func<int, int, Color> Draw(bool full)
        {
            var piece = Glass(width, height, GlassShape.RoundedRectangle(radius, continuous ? RoundedCornerStyle.Continuous : RoundedCornerStyle.Circular), pointer > 0);
            piece.HorizontalAlignment = HorizontalAlignment.Left;
            piece.VerticalAlignment = VerticalAlignment.Top;
            piece.Margin = new Thickness(20 + fraction, 20 + fraction, 0, 0);
            if (full) piece.RenderTransform = new MatrixTransform(new Matrix(1, 1e-300, 0, 1, 0, 0));
            var window = new Window { Width = width + 60, Height = height + 60, RequestedThemeVariant = ThemeVariant.Dark,
                Content = new Panel { Children = { Backdrop(width + 60, height + 60), piece } } };
            Scale(window, scale);
            window.Show();
            try
            {
                var middle = new Point(20 + fraction + width / 2, 20 + fraction + height / 2);
                if (pointer > 0) window.MouseMove(middle);
                if (pointer > 1) window.MouseDown(middle, Avalonia.Input.MouseButton.Left);
                for (var i = 0; i < 4; i++) Scene.Drain();
                using var bitmap = window.CaptureRenderedFrame()!;
                var size = bitmap.PixelSize;
                var bytes = new byte[size.Width * size.Height * 4];
                var handle = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
                try { bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), bytes.Length, size.Width * 4); }
                finally { handle.Free(); }
                if (pointer > 1) window.MouseUp(middle, Avalonia.Input.MouseButton.Left);
                var stride = size.Width;
                return (x, y) => { var p = (y * stride + x) * 4; return Color.FromArgb(bytes[p + 3], bytes[p], bytes[p + 1], bytes[p + 2]); };
            }
            finally { window.Close(); }
        }
        var drawn = Draw(full: false);
        var reference = Draw(full: true);
        var whole = new PixelRect(0, 0, (int)Math.Ceiling((width + 60) * scale) - 1, (int)Math.Ceiling((height + 60) * scale) - 1);
        var apart = Largest(drawn, reference, whole, out var at);
        int count = 0, left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        for (var py = 0; py < whole.Height; py++)
            for (var px = 0; px < whole.Width; px++)
                if (drawn(px, py) != reference(px, py)) { count++; left = Math.Min(left, px); top = Math.Min(top, py); right = Math.Max(right, px); bottom = Math.Max(bottom, py); }
        Assert.True(apart <= (scale == 1.25 ? 1 : 0), $"at {scale}, {width} x {height}, radius {radius} ({(continuous ? "continuous" : "circular")}), pointer {pointer}, at +{fraction}: " +
            $"the inside drawing is {apart} levels from the full shader's (at {at}; {count} pixels differ, within {left},{top} to {right},{bottom})");
    });

    // Backdrop reads the window surface's size again only when the surface's handle (the native SkSurface's address) changes. A window that is resized
    // or moved to another scale gets a new surface (Avalonia makes its layer again at the new pixel size), and a native object made right after one is
    // freed can be given the freed one's address. Driven directly: the surface of 256 x 160 is freed, surfaces of 420 x 300 are made until one has its
    // address, and a piece whose crop is 100,50 to 420,300 takes its pixels with the whole new surface dirty: the area it keeps must be the new surface's.
    [Fact]
    [Trait("Req", "PR-WIN-180")]
    public void REVIEW_A_new_window_surface_at_the_old_ones_address_is_measured_again() => headless.OnUiThread(() =>
    {
        var type = typeof(GlassMaterial).GetNestedType("Backdrop", BindingFlags.NonPublic)!;
        var take = type.GetMethod("Take", Members)!;
        var backdrop = Activator.CreateInstance(type, nonPublic: true)!;
        var crop = new SkiaSharp.SKRectI(100, 50, 420, 300);
        SkiaSharp.SKRectI Taken(SkiaSharp.SKSurface surface, SkiaSharp.SKRectI dirty)
        {
            var result = take.Invoke(backdrop, [new GlassFakeLease(surface), crop, (SkiaSharp.SKRectI?)dirty]);
            Assert.NotNull(result);
            return (SkiaSharp.SKRectI)result!.GetType().GetField("Item2")!.GetValue(result)!;
        }
        var info = new SkiaSharp.SKImageInfo(256, 160, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul);
        var old = SkiaSharp.SKSurface.Create(info);
        Assert.Equal(new SkiaSharp.SKRectI(100, 50, 256, 160), Taken(old, new SkiaSharp.SKRectI(0, 0, 256, 160)));
        var address = old.Handle;
        old.Dispose();
        var held = new List<SkiaSharp.SKSurface>();
        SkiaSharp.SKSurface? reused = null;
        try
        {
            for (var i = 0; i < 4000 && reused is null; i++)
            {
                var next = SkiaSharp.SKSurface.Create(info.WithSize(420, 300));
                if (next.Handle == address) reused = next; else held.Add(next);
            }
            Assert.True(reused is not null, "no surface was given the freed one's address in 4000 tries (the case was not reached)");
            var area = Taken(reused!, new SkiaSharp.SKRectI(0, 0, 420, 300));
            Assert.True(area == crop, $"after {held.Count + 1} tries a 420 x 300 surface had the old one's address; the piece kept {area}, not {crop}: the old size was used");
        }
        finally
        {
            reused?.Dispose();
            foreach (var surface in held) surface.Dispose();
        }
    });

    // Backdrop keeps the blur's surface while its size and the GPU context it was made on stay the same (ED-320 16 (b)). Draw takes the
    // kept pixels (Take) before it asks for the blur's surface (Blurred), and Take records the lease's context as the one it last saw; Blurred compares the
    // lease's context with that record, not with the context its own surface was made on. After a context change (a lost device made again, or
    // Avalonia falling back to software) it hands back the surface of the old context. Driven directly: the backdrop's last frame is put on a context
    // that is not this lease's (each context handle it recorded set to another, its blur surface standing for that context's), then one frame draws on this lease.
    [Fact]
    [Trait("Req", "PR-WIN-180")]
    public void REVIEW_After_a_GPU_context_change_the_blur_surface_is_made_again() => headless.OnUiThread(() =>
    {
        var type = typeof(GlassMaterial).GetNestedType("Backdrop", BindingFlags.NonPublic)!;
        var backdrop = Activator.CreateInstance(type, nonPublic: true)!;
        var crop = new SkiaSharp.SKRectI(10, 10, 200, 140);
        using var window = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(256, 160, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul));
        var lease = new GlassFakeLease(window);
        Assert.NotNull(type.GetMethod("Take", Members)!.Invoke(backdrop, [lease, crop, (SkiaSharp.SKRectI?)new SkiaSharp.SKRectI(0, 0, 256, 160)]));
        var blurred = type.GetMethod("Blurred", Members)!;
        var old = (SkiaSharp.SKSurface)blurred.Invoke(backdrop, [lease, crop])!;
        // The last frame's context was another one: every context handle the backdrop recorded (each IntPtr field that holds this lease's, none) is set to it.
        foreach (var field in type.GetFields(Members).Where(f => f.FieldType == typeof(IntPtr) && (IntPtr)f.GetValue(backdrop)! == IntPtr.Zero))
            field.SetValue(backdrop, new IntPtr(0x5150));
        Assert.NotNull(type.GetMethod("Take", Members)!.Invoke(backdrop, [lease, crop, (SkiaSharp.SKRectI?)new SkiaSharp.SKRectI(0, 0, 256, 160)]));
        var next = (SkiaSharp.SKSurface)blurred.Invoke(backdrop, [lease, crop])!;
        Assert.False(ReferenceEquals(old, next), "the blur's surface made on the old context was handed back on the new one");
    });
}
