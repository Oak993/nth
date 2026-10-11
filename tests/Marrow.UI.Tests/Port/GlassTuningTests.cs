// ED-NEW-glasslook-1 (OD-0107 and its amendment: the per-surface look set in one place) and ED-NEW-glasslook-2 (owner 2026-10-11: glass settings in
// Customization): the look table, the user's tuning over it, and what each reaches. Every knob is a uniform or a CPU-side size, so a tuning change
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

    private static void Tuned(GlassSettings tuning, Action body)
    {
        GlassLooks.Tuning = tuning;
        try { body(); }
        finally { GlassLooks.Tuning = GlassSettings.Default; }
    }

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-1")] // THE ONE PLACE: in the Regular Look every surface keeps the release look until the owner approves the design canvas; each kind of piece is told apart
    public void The_look_table_gives_every_surface_the_release_look_and_tells_the_surfaces_apart()
    {
        foreach (var surface in Enum.GetValues<GlassSurface>())
        {
            Assert.Equal(GlassLook.Release, GlassLooks.For(surface));
            Assert.Equal(GlassLook.Release, GlassLooks.For(surface, GlassVariant.Regular));
        }
        Assert.Equal(new GlassLook(), GlassLook.Release);
        Assert.Equal(0, GlassLook.Release.Clarity);
        var regular = GlassEffect.Regular(GlassShape.RoundedRectangle(24, RoundedCornerStyle.Continuous));
        Assert.Equal(GlassSurface.Panel, GlassLooks.SurfaceOf(regular));
        Assert.Equal(GlassSurface.Window, GlassLooks.SurfaceOf(GlassEffect.Regular(GlassShape.RoundedRectangle(0, RoundedCornerStyle.Continuous))));
        Assert.Equal(GlassSurface.Composer, GlassLooks.SurfaceOf(GlassEffect.Regular(GlassShape.RoundedRectangle(22, RoundedCornerStyle.Continuous, circularInBand: true))));
        Assert.Equal(GlassSurface.Capsule, GlassLooks.SurfaceOf(GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous))));
        Assert.Equal(GlassSurface.Button, GlassLooks.SurfaceOf(GlassEffect.Regular(GlassShape.Circle).Interactive()));
        Assert.Equal(GlassSurface.Button, GlassLooks.SurfaceOf(GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous)).Tint(SwiftColor.Red, 0.5)));
        Assert.Equal(GlassSurface.Composer, GlassLooks.SurfaceOf(regular.OnSurface(GlassSurface.Composer)));
        // a piece's own look wins over its surface's
        Assert.Equal(GlassLook.Liquid, GlassLooks.Of(regular.WithLook(GlassLook.Liquid)));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-2")] // the Look (Clear, Regular, Tinted; Apple's Liquid Glass setting, iOS 27): the table gives every surface its look in each, the user's Look picks it, and Clear and Tinted keep the owner's premium finish (ED-NEW-glasslook-3)
    public void Each_look_gives_every_surface_its_own_and_the_users_look_picks_it() => headless.OnUiThread(() =>
    {
        foreach (var surface in Enum.GetValues<GlassSurface>())
        {
            Assert.Equal(GlassLook.Premium, GlassLooks.For(surface, GlassVariant.Clear));
            Assert.Equal(GlassLook.TintedCandidate, GlassLooks.For(surface, GlassVariant.Tinted));
        }
        foreach (var look in new[] { GlassLook.Premium, GlassLook.TintedCandidate })
        {
            // the hairline rim, the squircle bezel, no inner bevel, a fringe of at most 1 pt, a soft wide shadow
            Assert.Equal((1d, 1d, 0d, 1d), (look.ProfileValue, look.HairlineValue, look.GlowValue, look.FringeCapValue));
            Assert.True(look.DepthValue > 0, "a soft shadow");
        }
        // Clear shows more of the backdrop than Tinted, Tinted lays more film over it
        Assert.True(GlassLook.Premium.Clarity > GlassLook.TintedCandidate.Clarity && GlassLook.Premium.Blur < GlassLook.TintedCandidate.Blur);
        Assert.True(GlassLook.TintedCandidate.Tint > GlassLook.Release.Tint && GlassLook.Premium.Tint < GlassLook.Release.Tint);
        var capsule = GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous));
        var panel = GlassEffect.Regular(GlassShape.RoundedRectangle(24, RoundedCornerStyle.Continuous));
        foreach (var (_, _, variant) in GlassSettings.Looks)
            Tuned(GlassSettings.Default with { Look = variant }, () =>
            {
                Assert.Equal(GlassLooks.For(GlassSurface.Capsule, variant), GlassLooks.Of(capsule));
                Assert.Equal(GlassLooks.For(GlassSurface.Panel, variant), GlassLooks.Of(panel));
                Assert.Equal(GlassLooks.For(GlassSurface.Inner, variant), GlassLooks.Of(panel, nested: true));
                // a piece's own look still wins
                Assert.Equal(GlassLook.Liquid, GlassLooks.Of(panel.WithLook(GlassLook.Liquid)));
            });
    });

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-2")] // a new Look crossfades: every surface passes from its look in the old Look to its look in the new one, frame by frame, with no pop and no new program, and lands on a fresh window's glass
    public void A_new_look_crossfades_every_surface_with_no_pop_and_no_new_program() => headless.OnUiThread(() =>
    {
        GlassMaterial.WarmupPrograms();
        var window = Show(Pill());
        var effect = GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous));
        try
        {
            var regular = Capture(window);
            var programs = GlassMaterial.ProgramsBuiltForTests;
            GlassLooks.Blend(GlassVariant.Regular, 0);
            GlassLooks.Tuning = GlassSettings.Default with { Look = GlassVariant.Clear };
            Assert.Equal(GlassLook.Release, GlassLooks.Of(effect));
            Assert.Equal(regular, Capture(window));
            var frames = new List<byte[]> { regular };
            for (var step = 1; step <= 10; step++)
            {
                var progress = step / 10.0;
                GlassLooks.Blend(GlassVariant.Regular, progress);
                Assert.Equal(step < 10 ? GlassLook.Release.Toward(GlassLook.Premium, progress) : GlassLook.Premium, GlassLooks.Of(effect));
                frames.Add(Capture(window));
            }
            var whole = Distance(regular, frames[^1]);
            Assert.True(Parting(regular, frames[^1]).Parted >= 40, "the Clear look differs from the Regular");
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
            GlassLooks.Blend(GlassVariant.Regular, 1);
            GlassLooks.Tuning = GlassSettings.Default;
            window.Close();
        }
        Assert.Equal(GlassLook.Release, GlassLooks.Of(effect));
    });

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-1")] // clamping: every knob of a look is held to its range, and the bend and ring together read at most 60 pt past the edge
    public void A_look_past_its_ranges_is_held_and_its_bend_stays_inside_the_material_reach()
    {
        var wild = new GlassLook(Clarity: 4, Bend: 40, Band: 9, Fringe: 9, Rim: 9, Blur: 9, Tint: 9, Saturation: 9, Shadow: 9, Profile: 9, Glow: 9, Hairline: 9, FringeCap: 9, Lift: 9, Depth: 9);
        Assert.Equal((1d, 4d, 1.25, 3d, 2d, 4d, 2d, 2d, 2d), (wild.ClarityValue, wild.BendValue, wild.BandValue, wild.FringeValue, wild.RimValue, wild.BlurValue, wild.TintValue, wild.SaturationValue, wild.ShadowValue));
        Assert.Equal((1d, 2d, 1d, 4d, 0.25, 0.4), (wild.ProfileValue, wild.GlowValue, wild.HairlineValue, wild.FringeCapValue, wild.LiftValue, wild.DepthValue));
        var low = new GlassLook(Clarity: -1, Bend: -1, Band: 0, Fringe: -1, Rim: -1, Blur: -1, Tint: -1, Saturation: -1, Shadow: -1, Profile: -1, Glow: -1, Hairline: -1, FringeCap: -1, Lift: -1, Depth: -1);
        Assert.Equal((0d, 0d, 0.5, 0d, 0d, 0d, 0d, 0d, 0d), (low.ClarityValue, low.BendValue, low.BandValue, low.FringeValue, low.RimValue, low.BlurValue, low.TintValue, low.SaturationValue, low.ShadowValue));
        Assert.Equal((0d, 0d, 0d, 0d, 0d, 0d), (low.ProfileValue, low.GlowValue, low.HairlineValue, low.FringeCapValue, low.LiftValue, low.DepthValue));
        foreach (var size in new[] { new Size(280, 190), new Size(818, 46), new Size(400, 80), new Size(1200, 800) })
        {
            var o = GlassMaterial.Optics.For(size, wild);
            Assert.True(o.Bend + o.Ring <= GlassMaterial.Optics.MaxLookReach + 1e-9, $"{size}: bend {o.Bend:F1} + ring {o.Ring:F1}");
            Assert.True(o.Sigma <= 14, $"{size}: band {o.Sigma:F1}");
            var release = GlassMaterial.Optics.For(size, GlassLook.Release);
            Assert.Equal(GlassMaterial.Optics.For(size), release);
        }
    }

    [Theory]
    [Trait("Decision", "ED-NEW-glasslook-2")] // each control reaches the material: the tuning scales the look every surface is drawn with, and the glass on screen changes
    [InlineData("refraction", 2.0)]
    [InlineData("bezel", 0.75)]
    [InlineData("clarity", 1.0)]
    [InlineData("frost", 0.5)]
    [InlineData("frost", 1.5, GlassVariant.Clear)] // the Regular pill is at the material's 12 pt blur already: more frost shows under the Clear Look's
    [InlineData("fringe", 0.0)]
    [InlineData("rim", 0.0)]
    [InlineData("tint", 0.0)]
    [InlineData("saturation", 1.5)]
    [InlineData("shadow", 0.0)]
    public void Each_glass_control_reaches_the_material(string id, double value, GlassVariant within = GlassVariant.Regular) => headless.OnUiThread(() =>
    {
        // the bend, the bezel and the fringe show plainest through a clear rim: their base has one
        var start = (id is "refraction" or "bezel" or "fringe" ? GlassSettings.Default.With("clarity", 0.9) : GlassSettings.Default) with { Look = within };
        var moved = start.With(id, value);
        var effect = GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous));
        // in every Look the knob scales that Look's own
        foreach (var (_, _, variant) in GlassSettings.Looks)
        {
            var table = GlassLooks.For(GlassLooks.SurfaceOf(effect), variant);
            Tuned(moved with { Look = variant }, () =>
            {
                var look = GlassLooks.Of(effect);
                var expected = id switch
                {
                    "refraction" => (look.Bend, table.Bend * value), "bezel" => (look.Band, table.Band * value), "clarity" => (look.Clarity, value),
                    "frost" => (look.Blur, table.Blur * value), "fringe" => (look.Fringe, table.Fringe * value), "rim" => (look.Rim, table.Rim * value),
                    "tint" => (look.Tint, table.Tint * value), "saturation" => (look.Saturation, table.Saturation * value), _ => (look.Shadow, table.Shadow * value),
                };
                Assert.Equal(expected.Item2, expected.Item1, 9);
                if (id == "shadow") Assert.Equal(table.Depth * value, look.Depth, 9); // the soft shadow follows the contact line
            });
        }
        foreach (var light in new[] { false, true })
        {
            byte[] before = [], after = [];
            Tuned(start, () => before = Fresh(effect, light));
            Tuned(moved, () => after = Fresh(effect, light));
            var (most, parted) = Parting(before, after);
            Assert.True(most >= 8 && parted >= 40, $"{id} at {value}, light {light}: the glass changes by {most} levels at most, {parted} pixels by more than 2");
        }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-2")] // live and smooth: a slider's drag redraws every window's glass with new uniforms (no new program), and each frame is what a fresh window draws
    public void A_drag_redraws_every_window_live_with_no_new_program() => headless.OnUiThread(() =>
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
            GlassSettings tuning = GlassSettings.Default;
            // a drag of the refraction and then the tint, in 24 steps, a frame drawn after each
            for (var step = 1; step <= 24; step++)
            {
                tuning = step <= 12 ? tuning.With("refraction", 1 + step * 0.08).With("clarity", 0.9) : tuning.With("tint", 1 - (step - 12) * 0.05);
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
            GlassLooks.Tuning = GlassSettings.Default;
            first.Close();
            second.Close();
        }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-2")] // Reset to defaults: back from any Look and any tuning, every surface draws the release glass again, pixel for pixel
    public void Reset_brings_back_the_release_glass_exactly() => headless.OnUiThread(() =>
    {
        var window = Show(Pill());
        try
        {
            var release = Capture(window);
            foreach (var tuning in new[]
            {
                GlassSettings.Default with { Look = GlassVariant.Clear }, GlassSettings.Default with { Look = GlassVariant.Tinted },
                GlassSettings.Default.With("refraction", 2).With("clarity", 0.9), (GlassSettings.Default with { Look = GlassVariant.Clear }).With("tint", 0.4).With("rim", 1.25),
            })
            {
                GlassLooks.Tuning = tuning;
                Assert.True(Parting(release, Capture(window)).Parted >= 40, tuning + " changes the glass");
                GlassLooks.Tuning = GlassSettings.Default;
                Assert.Equal(release, Capture(window));
            }
        }
        finally
        {
            GlassLooks.Tuning = GlassSettings.Default;
            window.Close();
        }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-2")] // the tuning is held to its ranges when it is set, whatever a caller passes
    public void The_tuning_is_held_to_its_ranges() => headless.OnUiThread(() =>
    {
        Tuned(new GlassSettings { Refraction = 50, Frost = 0, Tint = 9, Clarity = 4 }, () =>
        {
            Assert.Equal(2, GlassLooks.Tuning.Refraction);
            Assert.Equal(0.5, GlassLooks.Tuning.Frost);
            Assert.Equal(1, GlassLooks.Tuning.Tint);
            Assert.Equal(1.0, GlassLooks.Tuning.Clarity);
        });
        Assert.Equal(GlassSettings.Default, GlassLooks.Tuning);
    });

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

    // Every Look, alone and at the ends of the ranges.
    private static readonly GlassSettings[] Extremes = GlassSettings.Looks.SelectMany(l => new GlassSettings[]
    {
        new() { Look = l.Value },
        new() { Look = l.Value, Refraction = 0, Bezel = 0.75, Clarity = 0, Frost = 0.5, Fringe = 0, Rim = 0, Tint = 0, Saturation = 0.5, Shadow = 0 },
        new() { Look = l.Value, Refraction = 2, Bezel = 1.25, Clarity = 1, Frost = 1.5, Fringe = 1, Rim = 1.25, Tint = 1, Saturation = 1.5, Shadow = 1.5 },
        new() { Look = l.Value, Refraction = 2, Bezel = 1.25, Clarity = 1, Frost = 0.5, Fringe = 1, Rim = 1.25, Tint = 0, Saturation = 1.5, Shadow = 1.5 },
        new() { Look = l.Value, Refraction = 2, Bezel = 1.25, Clarity = 1, Frost = 0.5, Fringe = 1, Rim = 1.25, Tint = 1, Saturation = 0.5, Shadow = 0 },
    }).ToArray();

    [Theory]
    [Trait("Decision", "ED-NEW-glasslook-2")] // whatever the user picks, body text on glass stays at 4.5:1 or more and secondary text at 3:1, over the app's own backdrops
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
