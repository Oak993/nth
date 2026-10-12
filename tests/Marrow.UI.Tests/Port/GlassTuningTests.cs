// OD-0147 (the owner approved the Glass UI canvas, 2026-10-11: its material and its defaults ship, every one of its settings the user's), with
// ED-NEW-glasslook-1 (the per-surface look set in one place) and ED-NEW-glasslook-2 (the settings in Customization): the look table, the canvas's
// numbers per kind, the user's settings over them, Windows' switches until the user sets them, and what each reaches. Every knob is a uniform or a CPU-side size, so a tuning change
// redraws every glass piece of every window live, with no new GPU program, and lands on exactly what a fresh window draws under the same tuning.
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.Core.Port;
using Marrow.Core.Port.Settings;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Port;

[Collection("Headless")]
public sealed class GlassTuningTests(HeadlessSession headless)
{
    private const int Left = 60, Top = 30, PillWidth = 400, PillHeight = 80, SceneWidth = 520, SceneHeight = 140;

    // A lit, colourful scene: warm and cool halves, white and dark lines across the pill's ends and edges (what the bend, the fringe and the
    // frost show on), at levels over Optics' LitFrom.
    private static Panel Scene()
    {
        var panel = new Panel { Background = new SolidColorBrush(Color.FromRgb(150, 140, 130)) };
        panel.Children.Add(new Border { Width = SceneWidth / 2, HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Color.FromRgb(230, 120, 70)) });
        panel.Children.Add(new Border { Width = SceneWidth / 2, HorizontalAlignment = HorizontalAlignment.Right, Background = new SolidColorBrush(Color.FromRgb(70, 120, 230)) });
        for (var x = 20; x < SceneWidth; x += 26)
            panel.Children.Add(new Border { Width = 2, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(x, 0, 0, 0), Background = Brushes.White });
        foreach (var y in new[] { 22, 50, 92, 116 })
            panel.Children.Add(new Border { Height = 2, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, y, 0, 0), Background = new SolidColorBrush(Color.FromRgb(20, 20, 30)) });
        return panel;
    }

    private static Border Pill(GlassEffect? effect = null)
    {
        var pill = new Border
        {
            Background = Brushes.Transparent, Width = PillWidth, Height = PillHeight, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(Left, Top, 0, 0),
        };
        SwiftGlass.SetEffect(pill, effect ?? GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous)));
        return pill;
    }

    private static Window Show(Control glass, bool light = false)
    {
        var window = new Window
        {
            Width = SceneWidth, Height = SceneHeight, RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark, Background = Brushes.Black,
            Content = new Panel { Children = { Scene(), glass } },
        };
        window.Show();
        return window;
    }

    private static byte[] Capture(Window window)
    {
        for (var i = 0; i < 4; i++) GlassScene.Drain();
        using var bitmap = window.CaptureRenderedFrame()!;
        var size = bitmap.PixelSize;
        var bytes = new byte[size.Width * size.Height * 4];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), bytes.Length, size.Width * 4); }
        finally { handle.Free(); }
        return bytes;
    }

    private static byte[] Fresh(GlassEffect? effect = null, bool light = false)
    {
        var window = Show(Pill(effect), light);
        try { return Capture(window); }
        finally { window.Close(); }
    }

    // How far two captures part over the pill and 8 pt around it: the largest channel difference, and how many pixels part by more than 2.
    private static (int Most, int Parted) Parting(byte[] a, byte[] b)
    {
        var most = 0;
        var parted = 0;
        for (var y = Top - 8; y < Top + PillHeight + 8; y++)
            for (var x = Left - 8; x < Left + PillWidth + 8; x++)
            {
                var p = (y * SceneWidth + x) * 4;
                var d = Math.Max(Math.Abs(a[p] - b[p]), Math.Max(Math.Abs(a[p + 1] - b[p + 1]), Math.Abs(a[p + 2] - b[p + 2])));
                most = Math.Max(most, d);
                if (d > 2) parted++;
            }
        return (most, parted);
    }

    // How far two captures are apart over the pill and 8 pt around it: the sum of every channel's difference.
    private static long Distance(byte[] a, byte[] b)
    {
        long sum = 0;
        for (var y = Top - 8; y < Top + PillHeight + 8; y++)
            for (var x = Left - 8; x < Left + PillWidth + 8; x++)
            {
                var p = (y * SceneWidth + x) * 4;
                sum += Math.Abs(a[p] - b[p]) + Math.Abs(a[p + 1] - b[p + 1]) + Math.Abs(a[p + 2] - b[p + 2]);
            }
        return sum;
    }

    // Windows' switches, as a test sets them (GlassLooks.System): by default none is on, so a test's glass does not follow the machine it runs on.
    private static void Tuned(GlassSettings tuning, Action body, IGlassSystem? system = null)
    {
        GlassLooks.System = system ?? new FixedGlassSystem();
        GlassLooks.Tuning = tuning;
        try { body(); }
        finally
        {
            GlassLooks.Tuning = GlassSettings.Default;
            GlassLooks.System = new FixedGlassSystem();
        }
    }

    private static GlassCanvas CanvasOf(GlassEffect effect, bool nested = false) => GlassLooks.Of(effect, nested).Canvas ?? throw new InvalidOperationException("no canvas material");

    [Fact]
    [Trait("Decision", "OD-0147")] // THE ONE PLACE: every surface is the approved canvas's material for its kind, under the user's settings; each kind of piece is told apart; a piece's own look wins
    public void The_look_table_gives_every_surface_the_canvas_material_for_its_kind() => headless.OnUiThread(() => Tuned(GlassSettings.Default, () =>
    {
        var kinds = new Dictionary<GlassSurface, GlassKind>
        {
            [GlassSurface.Composer] = GlassKind.Composer, [GlassSurface.Capsule] = GlassKind.Capsule, [GlassSurface.Button] = GlassKind.Capsule,
            [GlassSurface.Sidebar] = GlassKind.Sidebar, [GlassSurface.Panel] = GlassKind.Popover, [GlassSurface.Window] = GlassKind.Settings,
            [GlassSurface.Inner] = GlassKind.Flat,
        };
        Assert.Equal(Enum.GetValues<GlassSurface>().Order(), kinds.Keys.Order());
        foreach (var (surface, kind) in kinds)
        {
            Assert.Equal(kind, GlassLooks.KindOf(surface));
            Assert.Equal(new GlassCanvas(kind, GlassLooks.Shown), GlassLooks.For(surface).Canvas);
        }
        // the shipped inputs are the canvas's Regular, every switch off (none set, Windows' off)
        Assert.Equal(new GlassTuning(1.30, 12, 32, 0.4, 0.50, 0.12, 1.22, 1, 0, 0, 0, 1, 0, 0, 0, 0), GlassLooks.Shown);
        var regular = GlassEffect.Regular(GlassShape.RoundedRectangle(24, RoundedCornerStyle.Continuous));
        Assert.Equal(GlassSurface.Panel, GlassLooks.SurfaceOf(regular));
        Assert.Equal(GlassSurface.Window, GlassLooks.SurfaceOf(GlassEffect.Regular(GlassShape.RoundedRectangle(0, RoundedCornerStyle.Continuous))));
        Assert.Equal(GlassSurface.Composer, GlassLooks.SurfaceOf(GlassEffect.Regular(GlassShape.RoundedRectangle(22, RoundedCornerStyle.Continuous, circularInBand: true))));
        Assert.Equal(GlassSurface.Capsule, GlassLooks.SurfaceOf(GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous))));
        Assert.Equal(GlassSurface.Button, GlassLooks.SurfaceOf(GlassEffect.Regular(GlassShape.Circle).Interactive()));
        Assert.Equal(GlassSurface.Button, GlassLooks.SurfaceOf(GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous)).Tint(SwiftColor.Red, 0.5)));
        Assert.Equal(GlassSurface.Sidebar, GlassLooks.SurfaceOf(regular.OnSurface(GlassSurface.Sidebar)));
        Assert.Equal(GlassKind.Flat, CanvasOf(regular, nested: true).Kind);
        Assert.Equal(GlassKind.Sidebar, CanvasOf(regular.OnSurface(GlassSurface.Sidebar), nested: true).Kind); // a piece that names its surface keeps it
        // a piece's own look wins over its surface's, and draws with its knobs (the release's, kept for what names it)
        Assert.Equal(GlassLook.Liquid, GlassLooks.Of(regular.WithLook(GlassLook.Liquid)));
        Assert.Null(GlassLook.Release.Canvas);
    }));

    [Fact]
    [Trait("Decision", "OD-0147")] // the canvas's surface table (its glassSpec and matL), recomputed by hand from its formulas at the canvas's own sizes in the dark appearance
    public void The_material_numbers_are_the_canvas_surface_table()
    {
        var p = GlassTuning.Of(GlassSettings.Default, new FixedGlassSystem());
        GlassCanvasSpec Spec(GlassKind kind, double s) => new GlassCanvas(kind, p).Spec(s, 1, 0, 0, 1);
        // the composer, 52 tall: the bezel 12, a bend of 1.3 x 12, 1.0975x at the rim, the frost 32, the shadow 6 / 18 / 0.20 / 0.14
        var composer = Spec(GlassKind.Composer, 52);
        Near([15.6, 12, 0.4, 1.0975], composer.Bend, composer.Bezel, composer.Fringe, composer.Magnify);
        Near([0.12, 0.148, 0.252, 1.22, 0.06], composer.TintTop, composer.TintBottom, composer.Compress, composer.Saturation, composer.Lift);
        Near([0.22, 0.17, 3.38, 0.274, 0.50, 0.05], composer.Mid, composer.Grey, composer.Band, composer.BandDark, composer.Spec, composer.Counter);
        Near([6, 18, 0.20, 0.14], composer.ShadowY, composer.ShadowSigma, composer.ShadowAlpha, composer.ContactAlpha);
        Near([0.07, 0.045, 46.8, 0.30, 0.315, 1.05], composer.Hair, composer.Glow, composer.GlowSigma, composer.Glint, composer.Scrim, composer.Line);
        Assert.Equal(32, new GlassCanvas(GlassKind.Composer, p).BlurRadius, 9);
        // a toolbar capsule, 34 tall: the bezel 0.57 x 12, the frost 0.55 x 32, the shadow 3 / 9 / 0.18 / 0.12
        var capsule = Spec(GlassKind.Capsule, 34);
        Near([6.84, 8.892], capsule.Bezel, capsule.Bend);
        Near([3, 9, 0.18, 0.12], capsule.ShadowY, capsule.ShadowSigma, capsule.ShadowAlpha, capsule.ContactAlpha);
        Assert.Equal(17.6, new GlassCanvas(GlassKind.Capsule, p).BlurRadius, 9);
        // the sidebar: the bezel 0.85 x 12, the frost 1.4 x 32, no glow, the band 3.5
        var sidebar = Spec(GlassKind.Sidebar, 300);
        Near([10.2, 13.26, 0d, 3.5], sidebar.Bezel, sidebar.Bend, sidebar.Glow, sidebar.Band);
        Assert.Equal(44.8, new GlassCanvas(GlassKind.Sidebar, p).BlurRadius, 9);
        // the model picker (a popover): the frost 1.2 x 32, the tint 0.12 more, the shadow 12 / 30 / 0.30 / 0.14
        var popover = Spec(GlassKind.Popover, 404);
        Near([0.24, 0.286, 0.444, 0.328, 0.2], popover.TintTop, popover.TintBottom, popover.Compress, popover.BandDark, popover.Scrim - 0.055);
        Near([12, 30, 0.30, 0.14], popover.ShadowY, popover.ShadowSigma, popover.ShadowAlpha, popover.ContactAlpha);
        Assert.Equal(38.4, new GlassCanvas(GlassKind.Popover, p).BlurRadius, 9);
        // the Settings panel: the bezel 1.15 x 12, the tint 0.14 more, the shadow 20 / 48 / 0.38 / 0.16
        var settings = Spec(GlassKind.Settings, 580);
        Near([13.8, 17.94, 0.26], settings.Bezel, settings.Bend, settings.TintTop);
        Near([20, 48, 0.38, 0.16], settings.ShadowY, settings.ShadowSigma, settings.ShadowAlpha, settings.ContactAlpha);
        // the frost's sigma is 0.48 of its radius, held to 24 (the canvas's Tinted sidebar asks 29.6)
        Assert.Equal(15.36, new GlassCanvas(GlassKind.Composer, p).Sigma, 9);
        Assert.Equal(24, new GlassCanvas(GlassKind.Sidebar, GlassTuning.Of(GlassSettings.Default.At(100), new FixedGlassSystem())).Sigma, 9);
        // the light appearance mirrors the body's greys
        var light = new GlassCanvas(GlassKind.Composer, p).Spec(52, 0, 0, 0, 1);
        Near([0.78, 0.83, 0d], light.Mid, light.Grey, light.Dark);
    }

    [Fact]
    [Trait("Decision", "OD-0147")] // the canvas's matL: Increase contrast, Reduce transparency, Translucent sidebar off, the tint colour, the press and the motion
    public void The_switches_reach_the_material_as_the_canvas_has_them()
    {
        var p = GlassTuning.Of(GlassSettings.Default, new FixedGlassSystem());
        var plain = new GlassCanvas(GlassKind.Composer, p).Spec(52, 1, 0, 0, 1);
        var contrast = new GlassCanvas(GlassKind.Composer, p with { Contrast = 1 }).Spec(52, 1, 0, 0, 1);
        Near([plain.BandDark + 0.14, plain.Spec + 0.2, plain.Hair + 0.1], contrast.BandDark, contrast.Spec, contrast.Hair);
        var reduced = new GlassCanvas(GlassKind.Composer, p with { Reduce = 1 }).Spec(52, 1, 0, 0, 1);
        Near([0.93, 0.95, 0d, 0d, 1d, 0d, 0d], reduced.TintTop, reduced.TintBottom, reduced.Bend, reduced.Fringe, reduced.Magnify, reduced.Lift, reduced.Scrim);
        var solidSidebar = new GlassCanvas(GlassKind.Sidebar, p with { SidebarOff = 1 }).Spec(300, 1, 0, 0, 1);
        Near([0.9, 0.9], solidSidebar.TintTop, solidSidebar.TintBottom);
        Assert.Equal(plain.TintTop, new GlassCanvas(GlassKind.Composer, p with { SidebarOff = 1 }).Spec(52, 1, 0, 0, 1).TintTop, 9); // only the sidebar
        var pressed = new GlassCanvas(GlassKind.Composer, p).Spec(52, 1, 1, 1, 1);
        Near([plain.Bend * 1.2, 3 + 0.4], pressed.Bend, pressed.Swell);
        Assert.Equal(0, new GlassCanvas(GlassKind.Composer, p with { Motion = 0 }).Spec(52, 1, 1, 1, 1).Swell, 9); // reduced motion: no bulge
        var blue = GlassTuning.Of(GlassSettings.Default with { TintColour = GlassTintColour.Blue }, new FixedGlassSystem());
        Near([0d, 0.48, 1d, 0.16], blue.TintR, blue.TintG, blue.TintB, blue.TintAmount);
    }

    [Fact]
    [Trait("Decision", "OD-0147")] // one source of truth: Increase contrast, Reduce transparency and Motion follow Windows' own switches until the user sets them; the user's choice, on or off, then wins
    public void The_switches_follow_windows_until_the_user_sets_them()
    {
        var windowsOn = new FixedGlassSystem(ReduceMotion: true, IncreaseContrast: true, ReduceTransparency: true);
        var follows = GlassTuning.Of(GlassSettings.Default, windowsOn);
        Assert.Equal((1d, 1d, 0d), (follows.Contrast, follows.Reduce, follows.Motion));
        var userOff = GlassTuning.Of(GlassSettings.Default with { IncreaseContrast = false, ReduceTransparency = false, ReducedMotion = false }, windowsOn);
        Assert.Equal((0d, 0d, 1d), (userOff.Contrast, userOff.Reduce, userOff.Motion));
        var userOn = GlassTuning.Of(GlassSettings.Default with { IncreaseContrast = true, ReduceTransparency = true, ReducedMotion = true }, new FixedGlassSystem());
        Assert.Equal((1d, 1d, 0d), (userOn.Contrast, userOn.Reduce, userOn.Motion));
        Assert.Equal(1, GlassTuning.Of(GlassSettings.Default with { TranslucentSidebar = false }, new FixedGlassSystem()).SidebarOff);
    }

    [Fact]
    [Trait("Decision", "OD-0147")] // each Look stop gives every surface the stop's values, the user's switches kept; the user's settings pick them
    public void Each_look_stop_gives_every_surface_its_values() => headless.OnUiThread(() =>
    {
        var capsule = GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous));
        var panel = GlassEffect.Regular(GlassShape.RoundedRectangle(24, RoundedCornerStyle.Continuous));
        foreach (var (_, _, variant, _) in GlassSettings.Looks)
            Tuned(GlassSettings.Default.At(variant) with { IncreaseContrast = true }, () =>
            {
                var expected = GlassTuning.Of(GlassSettings.Default.At(variant) with { IncreaseContrast = true }, new FixedGlassSystem());
                Assert.Equal(expected, GlassLooks.Shown);
                Assert.Equal(new GlassCanvas(GlassKind.Capsule, expected), CanvasOf(capsule));
                Assert.Equal(new GlassCanvas(GlassKind.Popover, expected), CanvasOf(panel));
                Assert.Equal(new GlassCanvas(GlassKind.Flat, expected), CanvasOf(panel, nested: true));
                Assert.Equal(GlassLooks.For(GlassSurface.Capsule, variant), GlassLooks.Of(capsule));
            });
        Tuned(GlassSettings.Default, () => Assert.Equal(1.40, GlassLooks.For(GlassSurface.Composer, GlassVariant.Clear).Canvas!.Tuning.Refraction, 9));
    });

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-2")] // a change glides: every surface passes from its glass under the old settings to the new, frame by frame, with no pop and no new program, and lands on a fresh window's glass
    public void A_change_glides_every_surface_with_no_pop_and_no_new_program() => headless.OnUiThread(() => Tuned(GlassSettings.Default, () =>
    {
        GlassMaterial.WarmupPrograms();
        var window = Show(Pill());
        var effect = GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous));
        try
        {
            var regular = Capture(window);
            var programs = GlassMaterial.ProgramsBuiltForTests;
            var target = GlassSettings.Default.At(GlassVariant.Tinted) with { IncreaseContrast = true, TintColour = GlassTintColour.Blue };
            GlassLooks.Blend(GlassSettings.Default, 0);
            GlassLooks.Tuning = target;
            Assert.Equal(new GlassCanvas(GlassKind.Capsule, GlassTuning.Of(GlassSettings.Default, new FixedGlassSystem())), CanvasOf(effect));
            Assert.Equal(regular, Capture(window));
            var frames = new List<byte[]> { regular };
            for (var step = 1; step <= 10; step++)
            {
                GlassLooks.Blend(GlassSettings.Default, step / 10.0);
                frames.Add(Capture(window));
            }
            Assert.Equal(new GlassCanvas(GlassKind.Capsule, GlassTuning.Of(target, new FixedGlassSystem())), CanvasOf(effect));
            var whole = Distance(regular, frames[^1]);
            Assert.True(Parting(regular, frames[^1]).Parted >= 40, "the Tinted glass differs from the Regular");
            for (var i = 1; i < frames.Count; i++)
            {
                var step = Distance(frames[i - 1], frames[i]);
                Assert.True(step <= whole * 0.35, $"no pop: frame {i} moves {step} of the whole {whole}");
            }
            Assert.Equal(programs, GlassMaterial.ProgramsBuiltForTests);
            Assert.Equal(Fresh(), frames[^1]);
        }
        finally
        {
            GlassLooks.Blend(GlassSettings.Default, 1);
            window.Close();
        }
    }));

    [Theory]
    [Trait("Decision", "OD-0147")] // every setting reaches the material: the glass on screen changes, in both appearances
    [InlineData("transparency", 0)]
    [InlineData("transparency", 100)]
    [InlineData("refraction", 2.4)]
    [InlineData("bezel", 24)]
    [InlineData("frost", 64)]
    [InlineData("frost", 20)]
    [InlineData("fringe", 1)]
    [InlineData("rim", 1)]
    [InlineData("tint", 0.5)]
    [InlineData("tint", 0)]
    [InlineData("saturation", 1.8)]
    [InlineData("shadow", 2)]
    [InlineData("shadow", 0)]
    [InlineData("tintColour", 2)]
    [InlineData("increaseContrast", 1)]
    [InlineData("reduceTransparency", 1)]
    [InlineData("translucentSidebar", 0)]
    public void Every_glass_setting_reaches_the_material(string id, double value) => headless.OnUiThread(() =>
    {
        var moved = id switch
        {
            "tintColour" => GlassSettings.Default with { TintColour = (GlassTintColour)(int)value },
            "increaseContrast" => GlassSettings.Default with { IncreaseContrast = value > 0 },
            "reduceTransparency" => GlassSettings.Default with { ReduceTransparency = value > 0 },
            "translucentSidebar" => GlassSettings.Default with { TranslucentSidebar = value > 0 },
            _ => GlassSettings.Default.With(id, value),
        };
        // the translucent sidebar reaches the sidebar alone; the shadow shows best under a lifted piece
        var effect = id == "translucentSidebar" ? GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous)).OnSurface(GlassSurface.Sidebar)
            : GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous)).OnSurface(GlassSurface.Composer);
        foreach (var light in new[] { false, true })
        {
            byte[] before = [], after = [];
            Tuned(GlassSettings.Default, () => before = Fresh(effect, light));
            Tuned(moved, () => after = Fresh(effect, light));
            var (most, parted) = Parting(before, after);
            Assert.True(most >= 4 && parted >= 40, $"{id} at {value}, light {light}: the glass changes by {most} levels at most, {parted} pixels by more than 2");
        }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-2")] // live and smooth: a slider's drag redraws every window's glass with new uniforms (no new program), and each frame is what a fresh window draws
    public void A_drag_redraws_every_window_live_with_no_new_program() => headless.OnUiThread(() => Tuned(GlassSettings.Default, () =>
    {
        GlassMaterial.WarmupPrograms(); // every program the draws can take is built before the drag
        var first = Show(Pill());
        var second = Show(Pill(GlassEffect.Regular(GlassShape.RoundedRectangle(22, RoundedCornerStyle.Continuous, circularInBand: true))));
        var changes = 0;
        GlassMaterial.LookChangedForTests = () => changes++;
        try
        {
            var startFirst = Capture(first);
            var startSecond = Capture(second);
            var programs = GlassMaterial.ProgramsBuiltForTests;
            byte[] lastFirst = startFirst, lastSecond = startSecond;
            var tuning = GlassSettings.Default;
            // a drag of the refraction and then the tint, in 24 steps, a frame drawn after each
            for (var step = 1; step <= 24; step++)
            {
                tuning = step <= 12 ? tuning.With("refraction", 1.3 + step * 0.09) : tuning.With("tint", 0.12 + (step - 12) * 0.03);
                GlassLooks.Tuning = tuning;
                lastFirst = Capture(first);
                lastSecond = Capture(second);
            }
            Assert.Equal(programs, GlassMaterial.ProgramsBuiltForTests);
            Assert.Equal(24, changes);
            // the same tuning again changes nothing and redraws nothing
            GlassLooks.Tuning = tuning;
            Assert.Equal(24, changes);
            Assert.True(Parting(startFirst, lastFirst).Parted >= 40, "the first window's glass followed the drag");
            Assert.True(Parting(startSecond, lastSecond).Parted >= 40, "the second window's glass followed the drag");
            // the live frame is exactly a fresh window's under the same tuning: no stale uniforms, no half-applied look
            Assert.Equal(Fresh(), lastFirst);
        }
        finally
        {
            GlassMaterial.LookChangedForTests = null;
            first.Close();
            second.Close();
        }
    }));

    [Fact]
    [Trait("Decision", "OD-0147")] // Reset to defaults: back from any Look, any value and any switch, every surface draws the shipped glass again, pixel for pixel
    public void Reset_brings_back_the_shipped_glass_exactly() => headless.OnUiThread(() => Tuned(GlassSettings.Default, () =>
    {
        var window = Show(Pill());
        try
        {
            var shipped = Capture(window);
            foreach (var tuning in new[]
            {
                GlassSettings.Default.At(GlassVariant.Clear), GlassSettings.Default.At(GlassVariant.Tinted), GlassSettings.Default.With("refraction", 2.4).With("bezel", 20),
                GlassSettings.Default.At(30).With("tint", 0.4).With("rim", 1) with { IncreaseContrast = true, TintColour = GlassTintColour.Purple },
                GlassSettings.Default with { ReduceTransparency = true },
            })
            {
                GlassLooks.Tuning = tuning;
                Assert.True(Parting(shipped, Capture(window)).Parted >= 40, tuning + " changes the glass");
                GlassLooks.Tuning = GlassSettings.Default;
                Assert.Equal(shipped, Capture(window));
            }
        }
        finally { window.Close(); }
    }));

    [Fact]
    [Trait("Decision", "OD-0147")] // the tuning is held to its ranges when it is set, whatever a caller passes
    public void The_tuning_is_held_to_its_ranges() => headless.OnUiThread(() =>
    {
        Tuned(new GlassSettings { Transparency = 500, Refraction = 50, Frost = 0, Tint = 9, Fringe = 4, Rim = 0 }, () =>
        {
            Assert.Equal((100d, 2.4, 20d, 0.5, 1d, 0.1), (GlassLooks.Tuning.Transparency, GlassLooks.Tuning.Refraction, GlassLooks.Tuning.Frost, GlassLooks.Tuning.Tint, GlassLooks.Tuning.Fringe, GlassLooks.Tuning.Rim));
            Assert.Equal(0.5, GlassLooks.Shown.Tint);
        });
        Assert.Equal(GlassSettings.Default, GlassLooks.Tuning);
    });

    [Fact]
    [Trait("Decision", "OD-0147")] // a surface set beside the effect (the floating sidebar's) draws that surface's glass, pixel for pixel, and leaves the effect the source's own
    public void A_surface_set_beside_the_effect_draws_that_surfaces_glass() => headless.OnUiThread(() => Tuned(GlassSettings.Default with { TranslucentSidebar = false }, () =>
    {
        var shape = GlassShape.RoundedRectangle(24, RoundedCornerStyle.Continuous);
        var beside = Pill(GlassEffect.Regular(shape));
        SwiftGlass.SetSurface(beside, GlassSurface.Sidebar);
        Assert.Equal(GlassEffect.Regular(shape), SwiftGlass.GetEffect(beside));
        var window = Show(beside);
        byte[] drawn;
        try { drawn = Capture(window); }
        finally { window.Close(); }
        Assert.Equal((0, 0), Parting(drawn, Fresh(GlassEffect.Regular(shape).OnSurface(GlassSurface.Sidebar))));
        var panel = Parting(drawn, Fresh(GlassEffect.Regular(shape)));
        Assert.True(panel.Parted >= 1000, $"the sidebar glass (Translucent sidebar off) is not the panel's: {panel}");
    }));

    [Fact]
    [Trait("Decision", "OD-0147")] // a piece that casts the design's own shadow draws none of the material's past its edge, and the same glass inside it
    public void A_piece_that_casts_its_own_shadow_draws_none_of_the_materials() => headless.OnUiThread(() => Tuned(GlassSettings.Default, () =>
    {
        var effect = GlassEffect.Regular(GlassShape.RoundedRectangle(24, RoundedCornerStyle.Continuous));
        byte[] Drawn(GlassEffect? glass, bool own)
        {
            var pill = Pill(effect);
            SwiftGlass.SetEffect(pill, glass);
            SwiftGlass.SetCastsOwnShadow(pill, own);
            var window = Show(pill);
            try { return Capture(window); }
            finally { window.Close(); }
        }
        var bare = Drawn(null, own: false);
        var shadowed = Drawn(effect, own: false);
        var own = Drawn(effect, own: true);
        // the most a channel parts from the bare scene over rows of the piece's straight run
        int Most(byte[] a, byte[] b, int fromY, int toY)
        {
            var most = 0;
            for (var y = fromY; y < toY; y++)
                for (var x = Left + 30; x < Left + PillWidth - 30; x++)
                {
                    var p = (y * SceneWidth + x) * 4;
                    most = Math.Max(most, Math.Max(Math.Abs(a[p] - b[p]), Math.Max(Math.Abs(a[p + 1] - b[p + 1]), Math.Abs(a[p + 2] - b[p + 2]))));
                }
            return most;
        }
        var under = (Top + PillHeight + 2, Top + PillHeight + 8);
        Assert.True(Most(shadowed, bare, under.Item1, under.Item2) >= 8, "the material's own shadow darkens the scene under a piece");
        Assert.Equal(0, Most(own, bare, under.Item1, under.Item2));
        Assert.Equal(0, Most(own, shadowed, Top + 20, Top + PillHeight - 20));
    }));

    [Fact]
    [Trait("Decision", "OD-0147")] // no glass on glass: a glass control inside another glass draws no glass of its own (no bezel, rim or shadow): nothing at rest, a flat white capsule under the pointer (the canvas's fill: 0.075 on hover)
    public void A_control_on_glass_is_flat_with_a_quiet_fill_under_the_pointer() => headless.OnUiThread(() => Tuned(GlassSettings.Default, () =>
    {
        Window Scene(bool withChip)
        {
            var panel = new Border
            {
                Background = Brushes.Transparent, Width = 400, Height = 100, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(Left, Top - 10, 0, 0), CornerRadius = new CornerRadius(24),
            };
            SwiftGlass.SetEffect(panel, GlassEffect.Regular(GlassShape.RoundedRectangle(24, RoundedCornerStyle.Continuous)));
            if (withChip)
            {
                var chip = new Border { Background = Brushes.Transparent, Width = 120, Height = 36, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                SwiftGlass.SetEffect(chip, GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous)).Interactive());
                panel.Child = chip;
            }
            return Show(panel);
        }
        var bare = Scene(false);
        byte[] alone;
        try { alone = Capture(bare); }
        finally { bare.Close(); }
        var window = Scene(true);
        try
        {
            Assert.Equal(alone, Capture(window)); // at rest the chip draws nothing over its panel's glass
            var centre = new Point(Left + 200, Top + 40);
            window.MouseMove(centre);
            for (var i = 0; i < 40; i++) GlassScene.Drain();
            var hovered = Capture(window);
            var p = ((int)centre.Y * SceneWidth + (int)centre.X) * 4;
            var lift = hovered[p + 1] - alone[p + 1];
            Assert.True(lift >= 3, $"under the pointer the chip lifts its panel's glass by {lift} levels");
            var q = ((Top + 40) * SceneWidth + Left + 20) * 4; // the panel's glass outside the chip is its own
            Assert.Equal(alone[q + 1], hovered[q + 1]);
        }
        finally { window.Close(); }
    }));

    [Fact]
    [Trait("Decision", "OD-0147")] // the canvas's line of light is angled: the edge facing the light (the top) carries the rim, the edge facing away a faint counter-light; no glossy top half
    public void The_rim_light_is_angled_from_the_upper_left() => headless.OnUiThread(() => Tuned(GlassSettings.Default, () =>
    {
        // over an even grey, so what the edges show is the material's light alone
        var window = new Window
        {
            Width = SceneWidth, Height = SceneHeight, RequestedThemeVariant = ThemeVariant.Dark, Background = Brushes.Black,
            Content = new Panel { Children = { new Panel { Background = new SolidColorBrush(Color.FromRgb(110, 110, 110)) }, Pill() } },
        };
        window.Show();
        byte[] pixels;
        try { pixels = Capture(window); }
        finally { window.Close(); }
        int G(int x, int y) => pixels[(y * SceneWidth + x) * 4 + 1];
        const int x = Left + PillWidth / 2;
        var top = Math.Max(G(x, Top), G(x, Top + 1)) - G(x, Top + 7);
        var bottom = Math.Max(G(x, Top + PillHeight - 1), G(x, Top + PillHeight - 2)) - G(x, Top + PillHeight - 8);
        Assert.True(top >= 12, $"the lit top edge's line: +{top}");
        Assert.True(top >= bottom + 8, $"the top edge's line (+{top}) over the bottom's (+{bottom})");
    }));

    [Fact]
    [Trait("Decision", "OD-0147")] // a piece that names the release's look still draws the release's material (kept for what names it), and it is not the canvas's
    public void A_piece_naming_the_release_look_draws_the_release_material() => headless.OnUiThread(() => Tuned(GlassSettings.Default, () =>
    {
        var effect = GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous));
        Assert.Null(GlassLooks.Of(effect.WithLook(GlassLook.Release)).Canvas);
        Assert.True(Parting(Fresh(effect), Fresh(effect.WithLook(GlassLook.Release))).Parted >= 40, "the release's material is not the canvas's");
    }));

    [Theory]
    [Trait("Decision", "OD-0147")] // over a window's transparent pixels (the Acrylic window) the canvas glass is a translucent film of its own grey, never opaque and never black
    [InlineData(false)]
    [InlineData(true)]
    public void Over_a_transparent_backdrop_the_canvas_glass_is_a_translucent_film_of_its_grey(bool light) => headless.OnUiThread(() => Tuned(GlassSettings.Default, () =>
    {
        const int width = 420, height = 320, pieceLeft = 130, pieceTop = 100;
        var piece = new Border
        {
            Background = Brushes.Transparent, Width = 160, Height = 120, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(pieceLeft, pieceTop, 0, 0), CornerRadius = new CornerRadius(16),
        };
        SwiftGlass.SetEffect(piece, GlassEffect.Regular(GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous)));
        var window = new Window
        {
            Width = width, Height = height, RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark, Background = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent], Content = new Panel { Children = { piece } },
        };
        window.Show();
        byte[] bytes;
        bool rgba;
        try
        {
            for (var i = 0; i < 4; i++) GlassScene.Drain();
            using var bitmap = window.CaptureRenderedFrame()!;
            bytes = new byte[bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4];
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try { bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), handle.AddrOfPinnedObject(), bytes.Length, bitmap.PixelSize.Width * 4); }
            finally { handle.Free(); }
            rgba = bitmap.Format == Avalonia.Platform.PixelFormat.Rgba8888;
        }
        finally { window.Close(); }
        (byte A, double Luma) At(int x, int y)
        {
            var p = (y * width + x) * 4;
            var a = bytes[p + 3];
            double S(byte c) => a == 0 ? 0 : c * 255.0 / a; // the frame is premultiplied
            return (a, 0.2126 * S(bytes[p + (rgba ? 0 : 2)]) + 0.7152 * S(bytes[p + 1]) + 0.0722 * S(bytes[p + (rgba ? 2 : 0)]));
        }
        Assert.Equal(0, At(4, 4).A); // the instrument: the window past the glass's shadow is transparent
        // the popover's film: its tint (0.24 at the top to 0.286 at the bottom) of the canvas grey (0.17 dark, mirrored 0.83 light) with the 0.06
        // white lift over it, about 31 % at the middle row, straight luma about 85 dark and 220 light
        var (centreA, centreLuma) = At(pieceLeft + 80, pieceTop + 60);
        Assert.InRange(centreA, (byte)65, (byte)95);
        Assert.InRange(centreLuma, light ? 200 : 70, light ? 240 : 100);
        // no pixel of the shape is opaque or black: the midline and the midcolumn, the rim and the edge included
        for (var x = pieceLeft + 1; x < pieceLeft + 159; x++)
        {
            var (a, luma) = At(x, pieceTop + 60);
            Assert.True(a > 20 && a < 200 && luma >= (light ? 150 : 40), $"x {x}: alpha {a}, straight luma {luma:F0}");
        }
        for (var y = pieceTop + 1; y < pieceTop + 119; y++)
        {
            var (a, luma) = At(pieceLeft + 80, y);
            Assert.True(a > 20 && a < 200 && luma >= (light ? 150 : 40), $"y {y}: alpha {a}, straight luma {luma:F0}");
        }
    }));

    // Each of the material's numbers is the canvas's (to 1e-9).
    private static void Near(double[] expected, params double[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++) Assert.True(Math.Abs(expected[i] - actual[i]) <= 1e-9, $"number {i}: {actual[i]}, the canvas's {expected[i]}");
    }


    // WCAG relative luminance and contrast ratio (GlassLookTests' rule, glass-g2).
    private static double Linear(double v)
    {
        var c = v / 255;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
    private static double Luminance(Color c) => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
    private static double Contrast(double a, double b) => (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    private static Color Over(Color text, Color glass)
    {
        var a = text.A / 255.0;
        byte Mix(byte t, byte g) => (byte)Math.Round(t * a + g * (1 - a));
        return Color.FromRgb(Mix(text.R, glass.R), Mix(text.G, glass.G), Mix(text.B, glass.B));
    }

    // Every Look stop, alone and with every fine-tuning value at its least, at its most, and two mixes of the two (the clearest and the most
    // colourful); the shipped glass with Increase contrast and with Reduce transparency; and every tint colour.
    private static GlassSettings All(GlassSettings from, bool most) => GlassSettings.Knobs.Aggregate(from, (s, k) => s.With(k.Id, most ? k.Max : k.Min));

    private static readonly GlassSettings[] Extremes =
    [
        .. GlassSettings.Looks.SelectMany(l => new[]
        {
            GlassSettings.Default.At(l.Value),
            All(GlassSettings.Default.At(l.Value), most: false),
            All(GlassSettings.Default.At(l.Value), most: true),
            All(GlassSettings.Default.At(l.Value), most: true).With("frost", 20).With("tint", 0),
            All(GlassSettings.Default.At(l.Value), most: true).With("saturation", 0.8).With("shadow", 0),
        }),
        GlassSettings.Default with { IncreaseContrast = true },
        GlassSettings.Default with { ReduceTransparency = true },
        .. GlassSettings.TintColours.Skip(1).Select(c => GlassSettings.Default with { TintColour = c.Value }),
    ];

    [Theory]
    [Trait("Decision", "OD-0147")] // whatever the user picks, body text on glass stays at 4.5:1 or more and secondary text at 3:1, over the app's own backdrops
    [InlineData(false, (byte)30, (byte)0)]
    [InlineData(false, (byte)30, (byte)200)]
    [InlineData(false, (byte)58, (byte)0)]
    [InlineData(true, (byte)245, (byte)0)]
    [InlineData(true, (byte)245, (byte)40)]
    [InlineData(true, (byte)216, (byte)0)]
    public void Text_on_the_glass_keeps_its_contrast_at_every_tuning(bool light, byte level, byte rows) => headless.OnUiThread(() =>
    {
        var appearance = light ? ThemeVariant.Light : ThemeVariant.Dark;
        var label = SwiftColor.LabelColor.Resolve(appearance);
        var secondary = Marrow.UI.Chrome.Chrome.SecondaryText.Resolve(appearance);
        foreach (var tuning in Extremes)
            Tuned(tuning, () =>
            {
                foreach (var (width, height, shape, inX, inY) in new[]
                {
                    (400.0, 80.0, GlassShape.Capsule(RoundedCornerStyle.Continuous), 24, 31),
                    (280.0, 190.0, GlassShape.RoundedRectangle(24, RoundedCornerStyle.Continuous), 16, 16),
                    (818.0, 46.0, GlassShape.RoundedRectangle(22, RoundedCornerStyle.Continuous, circularInBand: true), 18, 14),
                })
                {
                    const int left = 50, top = 40;
                    var backdrop = new Panel { Background = new SolidColorBrush(Color.FromRgb(level, level, level)) };
                    if (rows != 0)
                        for (var i = 0; i < 60; i++)
                            backdrop.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, i * 6, 0, 0), Background = new SolidColorBrush(Color.FromRgb(rows, rows, rows)) });
                    var piece = new Border
                    {
                        Background = Brushes.Transparent, Width = width, Height = height, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(left, top, 0, 0),
                    };
                    SwiftGlass.SetEffect(piece, GlassEffect.Regular(shape));
                    var window = new Window
                    {
                        Width = width + 100, Height = height + 80, RequestedThemeVariant = appearance, Background = Brushes.Black,
                        Content = new Panel { Children = { backdrop, piece } },
                    };
                    window.Show();
                    byte[] bytes;
                    int stride;
                    bool rgba;
                    try
                    {
                        for (var i = 0; i < 4; i++) GlassScene.Drain();
                        using var bitmap = window.CaptureRenderedFrame()!;
                        stride = bitmap.PixelSize.Width;
                        bytes = new byte[bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4];
                        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                        try { bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), handle.AddrOfPinnedObject(), bytes.Length, stride * 4); }
                        finally { handle.Free(); }
                        rgba = bitmap.Format == Avalonia.Platform.PixelFormat.Rgba8888;
                    }
                    finally { window.Close(); }
                    var worst = double.MaxValue;
                    var worstSecondary = double.MaxValue;
                    var at = "";
                    for (var y = top + inY; y < top + height - inY; y++)
                        for (var x = left + inX; x < left + width - inX; x++)
                        {
                            var p = (y * stride + x) * 4;
                            var glass = Color.FromRgb(bytes[p + (rgba ? 0 : 2)], bytes[p + 1], bytes[p + (rgba ? 2 : 0)]);
                            var g = Luminance(glass);
                            var c = Contrast(Luminance(Over(label, glass)), g);
                            if (c < worst) (worst, at) = (c, $"({x - left}, {y - top}) {glass}");
                            worstSecondary = Math.Min(worstSecondary, Contrast(Luminance(Over(secondary, glass)), g));
                        }
                    Assert.True(worst >= 4.5, $"{tuning}: {appearance} over {level}/{rows}, {width} x {height}: the label at {worst:F2}:1 at {at}");
                    Assert.True(worstSecondary >= 3, $"{tuning}: {appearance} over {level}/{rows}, {width} x {height}: the secondary text at {worstSecondary:F2}:1");
                }
            });
    });
}
