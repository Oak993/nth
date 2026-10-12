using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Session;
using Marrow.Core.Port.Voice.Orb;
using Xunit;
using static Marrow.App.Tests.Fixtures.VoicePillProbe;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Views.Composer;

// VT-12, V-T132 and V-T133 (voice-SPEC V-12.12, V-11.3; OD-0097 amendment): the clean pill beside the orb. Frames of the open pill (inline and float) at the orb sizes 32 and 26, listening quietly, listening to speech and
// in Processing, dark and light, rendered once with the pill and once without it (the pill hidden in the same instant: the same orb, glow and composer). V-T132: in the annulus from the ball's edge + 1 px to the pill cap's
// radius + 3 px, outside the sector where the pill's body continues, the two frames differ by at most 3/255 per channel. V-T133: a speck detector over the pill and a 12 px margin finds none other than the label's and the
// cross's own glyphs and the orb's own ball; the orb's 72 px view fades its glow to zero before its own edge. Headless SetRenderScaling exercises 100, 125, 150 and 200 percent rasterisation.
[Collection("Headless")]
public sealed class ComposerVoiceCleanPillTests(HeadlessSession headless)
{
    private static VoiceScript Said() => new(
    [
        new ScriptedWord("switch", 0.5, 0.9),
        new ScriptedWord("the", 1.0, 1.2),
        new ScriptedWord("theme", 1.3, 1.8),
        new ScriptedWord("please.", 2.0, 2.5),
    ]);

    private static void RenderTick(VoiceComposerRig rig)
    {
        rig.Main.OnMain(() =>
        {
            for (var k = 0; k < 3; k++)
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            }
        });
    }

    private static void RunRendered(VoiceComposerRig rig, ComposerView view, Avalonia.Controls.Window window, double seconds)
    {
        for (var k = 0; k < (int)Math.Round(seconds / FrameTime); k++)
        {
            rig.Elapse(window, FrameTime);
            rig.Main.OnMain(() => VoicePillMotion.Step(Slot(view).Orb, window.RenderScaling));
        }
    }

    internal static VoicePillScene Open(VoiceListeningStyle style, int diameter, bool dark, string state, double scale = 1)
    {
        var rig = new VoiceComposerRig(VoiceOrbSettings.Default with { ListeningStyle = style, OrbSize = diameter });
        Avalonia.Controls.Window? cleanupWindow = null;
        try
        {
            rig.Main.OnMain(() => rig.App.Settings.VoiceNotes = new VoiceNotesSettings(Local: true, Cloud: true));
            if (state is "quiet" or "pop" or "level06" or "level1" or "resize") rig.QuietRoom();
            else
            {
                var script = Said();
                rig.Speak(script);
                rig.Engine = script.CreateEngine(TimeSpan.FromSeconds(30));
            }
            var (view, window) = rig.Open(900);
            cleanupWindow = window;
            rig.Main.OnMain(() => { window.Height = 300; ((Border)window.Content!).Margin = new Thickness(0, 0, 0, 64); }); // the full 72 px orb view and float fit within the capture
            rig.Main.OnMain(() => window.RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light);
            rig.Main.OnMain(() => window.SetRenderScaling(scale));
            rig.PumpWindow(window);
            RenderTick(rig);
            rig.Main.OnMain(() => DictationProbe.Blur(view));
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            RunRendered(rig, view, window, state == "pop" ? 10 * FrameTime : state == "speech" ? 1.3 : 1.0);
            if (state is "level06" or "level1")
            {
                var level = state == "level06" ? 0.6 : 1.0;
                rig.Main.OnMain(() => Slot(view).Orb.Bands = new OrbBands(level, level, level, level));
                // Hold the supplied bands while stepping the render clock; the session and glow remain the same scripted scene.
                for (var k = 0; k < 180; k++)
                {
                    rig.Main.OnMain(() => Slot(view).Orb.Bands = new OrbBands(level, level, level, level));
                    rig.Main.Advance(TimeSpan.FromSeconds(FrameTime));
                    rig.Main.OnMain(() => { Slot(view).Orb.Bands = new OrbBands(level, level, level, level); VoicePillMotion.Step(Slot(view).Orb, window.RenderScaling); });
                }
                rig.PumpWindow(window);
                Assert.Equal(level, rig.Main.OnMain(() => Slot(view).Orb.Handler!.Scene!.Motion.Voice), 0.002);
            }
            if (state == "resize")
            {
                rig.Main.OnMain(() => rig.App.Settings.VoiceOrb = rig.App.Settings.VoiceOrb with { OrbSize = diameter == 32 ? 26 : 32 });
                RunRendered(rig, view, window, 3 * FrameTime);
                Assert.InRange(rig.Main.OnMain(() => Slot(view).Diameter), 26.01, 31.99);
                Assert.InRange(rig.Main.OnMain(() => Slot(view).Orb.Handler!.Scene!.Motion.Diameter), 26.01, 31.99);
            }
            if (state == "processing")
            {
                rig.Main.OnMain(() => Click(Slot(view).HitTarget));
                RunRendered(rig, view, window, 1.0);
            }
            return new VoicePillScene(rig, view, window);
        }
        catch
        {
            try { if (cleanupWindow is not null) rig.Close(cleanupWindow); }
            finally { rig.Dispose(); }
            throw;
        }
    }

    private static (int Stride, byte[] Bytes, PixelSize Size) Shot(VoicePillScene scene)
    {
        RenderTick(scene.Rig);
        return scene.Rig.Main.OnMain(() => DictationProbe.Frame(scene.Window));
    }

    private static float[] OrbUniforms(VoicePillScene scene) => scene.Rig.Main.OnMain(() => Slot(scene.View).Orb.Handler!.Scene!.Runtime(Slot(scene.View).Orb.Kind)!.Values.ToArray());

    /// <summary>The V-T132 / V-T133 identities at one scale: 2 styles, 2 orb sizes, dark and light, 7 states (56). Each scale is its own class (ComposerVoiceCleanPillAt*Tests).</summary>
    public static TheoryData<VoiceListeningStyle, int, bool, string, double> MatrixAt(double scale)
    {
        var data = new TheoryData<VoiceListeningStyle, int, bool, string, double>();
        foreach (var style in new[] { VoiceListeningStyle.Pill, VoiceListeningStyle.Float })
        foreach (var d in new[] { 32, 26 })
        foreach (var dark in new[] { true, false })
        foreach (var state in new[] { "quiet", "speech", "processing", "pop", "level06", "level1", "resize" })
            data.Add(style, d, dark, state, scale);
        return data;
    }

    // the orb's centre in window pixels, and the pill cap's centre and radius (the orb settles in the cap's centre)
    private static (Point Orb, Point Cap, double Radius) Marks(VoicePillScene scene)
    {
        return scene.Rig.Main.OnMain(() =>
        {
            var pill = Pill(scene.View);
            var slot = Slot(scene.View);
            var orb = slot.Orb.TranslatePoint(new Point(36, 36), scene.Window)!.Value;
            var h = pill.PillHeight;
            var topLeft = pill.TranslatePoint(default, scene.Window)!.Value;
            var float_ = pill.Style == VoiceListeningStyle.Float;
            var cap = float_ ? new Point(topLeft.X + h / 2, topLeft.Y + h / 2) : new Point(topLeft.X + pill.Bounds.Width - h / 2, topLeft.Y + h / 2);
            return (orb, cap, h / 2);
        });
    }

    // ---- one render per identity, judged by V-T132 and V-T133 ---------------------------------------------------------------------------------------------------
    // The two tests used to open the same scene apart and take the same frame with the pill twice. Now the scene of an identity is opened once: the frame with the pill
    // is the one V-T133's detector runs on and the one V-T132 compares with the frame without the pill (a scene opens the same way every time, ED-573), and each test
    // asserts exactly what it asserted, in the same order, on that one render. The verdict is kept for the run, so the second test of an identity renders nothing.

    private static readonly Dictionary<string, (ComposerVoiceVerdict? Verdict, System.Runtime.ExceptionServices.ExceptionDispatchInfo? Failure)> Verdicts = [];

    /// <summary>The identity's verdict, rendering it on first use (on the UI thread); a scene that fails to open fails both tests the same way.</summary>
    internal static ComposerVoiceVerdict Judge(VoiceListeningStyle style, int diameter, bool dark, string state, double scale)
    {
        var tag = $"{style}-{diameter}-{dark}-{state}-{scale}";
        lock (Verdicts)
        {
            if (!Verdicts.TryGetValue(tag, out var known))
            {
                try { known = (Render(style, diameter, dark, state, scale), null); }
                catch (Exception failure) { known = (null, System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure)); }
                Verdicts[tag] = known;
            }
            known.Failure?.Throw();
            return known.Verdict!;
        }
    }

    /// <summary>V-T132: nothing of the pill's material shows beside the orb.</summary>
    internal static void BesideTheOrb(HeadlessSession headless, VoiceListeningStyle style, int diameter, bool dark, string state, double scale) => headless.OnUiThread(() =>
    {
        var verdict = Judge(style, diameter, dark, state, scale);
        Assert.True(verdict.ShownOpacity > 0.99, "the pill is open");
        Assert.Equal(scale, verdict.RenderScaling);
        Assert.Equal(verdict.ExpectedWidth, verdict.FrameWidth);
        Assert.Equal(verdict.Uniforms, verdict.UniformsWithout); // the same orb at the same instant in the paired renders
        Assert.True(verdict.Worst <= 3, $"the pill leaves a difference of {verdict.Worst}/255 beside the orb at {verdict.Where}");
    });

    /// <summary>V-T133: the pill and a margin round it have no specks the approved design does not have.</summary>
    internal static void NoSpecks(HeadlessSession headless, VoiceListeningStyle style, int diameter, bool dark, string state, double scale) => headless.OnUiThread(() =>
    {
        var verdict = Judge(style, diameter, dark, state, scale);
        Assert.True(verdict.NotInTheDesign.Count == 0, "specks the approved design does not have (within 1 px): " + string.Join("; ", verdict.NotInTheDesign.Take(8)));
    });

    private static ComposerVoiceVerdict Render(VoiceListeningStyle style, int diameter, bool dark, string state, double scale)
    {
        using var motion = new VoicePillMotion();
        var scene = Open(style, diameter, dark, state, scale);
        using var _ = scene;
        var (orb, cap, radius) = Marks(scene);
        var shown = scene.Rig.Main.OnMain(() => Pill(scene.View).ShownOpacity);
        var withPill = Shot(scene);
        var orbUniforms = OrbUniforms(scene);
        var renderScaling = scene.Window.RenderScaling;
        var expectedWidth = (int)Math.Ceiling(scene.Window.ClientSize.Width * scale);
        var notInTheDesign = Specks(scene, style, diameter, dark, state, scale, withPill, orb * scale);
#pragma warning disable CS0618
        if (Environment.GetEnvironmentVariable("VT12_DUMP") is { } dump) scene.Rig.Main.OnMain(() => { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); using var f = scene.Window.CaptureRenderedFrame(); f!.Save(System.IO.Path.Combine(dump, $"with-{style}-{diameter}-{dark}-{state}-{scale}.png")); });
#pragma warning restore CS0618
        scene.Rig.Main.OnMain(() => Pill(scene.View).IsVisible = false);
        scene.Rig.PumpWindow(scene.Window);
        var without = Shot(scene);
        var uniformsWithout = OrbUniforms(scene);
        scene.Rig.Main.OnMain(() => Pill(scene.View).IsVisible = true);
        cap *= scale;
        orb *= scale;
        var ballRadius = scene.Rig.Main.OnMain(() => Slot(scene.View).Orb.Handler!.Scene!.Motion.Diameter * Slot(scene.View).Orb.Handler!.Scene!.Motion.Scale / 2);
        var inner = ballRadius * scale + 1; // the ball at this exact frame, plus 1 physical pixel
        var outer = radius * scale + 3;
        var worst = 0;
        var where = "";
        for (var y = (int)(cap.Y - outer) - 1; y <= cap.Y + outer + 1; y++)
        for (var x = (int)(cap.X - outer) - 1; x <= cap.X + outer + 1; x++)
        {
            var dx = x + 0.5 - cap.X;
            var dy = y + 0.5 - cap.Y;
            var r = Math.Sqrt(dx * dx + dy * dy);
            var ballDistance = Math.Sqrt((x + 0.5 - orb.X) * (x + 0.5 - orb.X) + (y + 0.5 - orb.Y) * (y + 0.5 - orb.Y));
            if (ballDistance < inner || r > outer) continue;
            if (style == VoiceListeningStyle.Pill ? dx < 4 * scale : dx > -4 * scale) continue; // the sector where the pill's body continues
            var at = y * withPill.Stride + x * 4;
            for (var c = 0; c < 3; c++)
            {
                var diff = Math.Abs(withPill.Bytes[at + c] - without.Bytes[at + c]);
                if (diff > worst) { worst = diff; where = $"({x}, {y}) r {r:F1} channel {c}"; }
            }
        }
        return new ComposerVoiceVerdict(shown, renderScaling, expectedWidth, withPill.Size.Width, orbUniforms, uniformsWithout, worst, where, notInTheDesign);
    }

    /// <summary>V-T133's detector, unchanged, on the frame with the pill; <paramref name="orb"/> is the orb's centre in device pixels. The specks the approved design does not have.</summary>
    private static List<string> Specks(VoicePillScene scene, VoiceListeningStyle style, int diameter, bool dark, string state, double scale, (int Stride, byte[] Bytes, PixelSize Size) frame, Point orb)
    {
        var pillRect = scene.Rig.Main.OnMain(() =>
        {
            var pill = Pill(scene.View);
            var origin = pill.TranslatePoint(default, scene.Window)!.Value;
            return new Rect(origin, pill.Bounds.Size);
        });
        pillRect = new Rect(pillRect.X * scale, pillRect.Y * scale, pillRect.Width * scale, pillRect.Height * scale);
        var area = pillRect.Inflate(12 * scale);
        var (outlier, w, h) = Outliers(frame, area);
        var ink = scene.Rig.Main.OnMain(() => VoicePillInk.Render(scene, frame.Size, $"{style}-{diameter}-{dark}-{state}-{scale}"));
        var ballRadius = scene.Rig.Main.OnMain(() => Slot(scene.View).Orb.Handler!.Scene!.Motion.Diameter * Slot(scene.View).Orb.Handler!.Scene!.Motion.Scale / 2);
        bool Inked(int x, int y) => ink[y * w + x]
            || Math.Sqrt((x + 0.5 - orb.X) * (x + 0.5 - orb.X) + (y + 0.5 - orb.Y) * (y + 0.5 - orb.Y)) <= ballRadius * scale + 1;
        var seen = new bool[w * h];
        var specks = new List<string>();
        var components = new List<(int X, int Y)[]>(); // diagnostic sidecar; acceptance uses the unchanged detector below
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            if (!outlier[y * w + x] || seen[y * w + x]) continue;
            var stack = new Stack<(int, int)>();
            var members = new List<(int X, int Y)>();
            stack.Push((x, y));
            seen[y * w + x] = true;
            while (stack.Count > 0)
            {
                var (cx, cy) = stack.Pop();
                members.Add((cx, cy));
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    var nx = cx + dx;
                    var ny = cy + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h || seen[ny * w + nx] || !outlier[ny * w + nx]) continue;
                    seen[ny * w + nx] = true;
                    stack.Push((nx, ny));
                }
            }
            if (members.Count <= 9 && !members.Any(m => Inked(m.X, m.Y)))
            {
                specks.Add($"{members.Count} px at ({members[0].X}, {members[0].Y})");
                components.Add(members.ToArray());
            }
        }
        if (VoicePillDiagnostics.Selected($"{style}-{diameter}-{dark}-{state}-{scale}"))
            scene.Rig.Main.OnMain(() => VoicePillDiagnostics.CaptureComponents(scene, $"{style}-{diameter}-{dark}-{state}-{scale}", frame, pillRect, orb, ballRadius * scale, components));
#pragma warning disable CS0618
        if (specks.Count > 0 && Environment.GetEnvironmentVariable("VT12_DUMP") is { } dump)
            scene.Rig.Main.OnMain(() => { using var f = scene.Window.CaptureRenderedFrame(); f!.Save(System.IO.Path.Combine(dump, $"specks-{style}-{diameter}-{dark}-{state}-{scale}.png")); VoicePillDiagnostics.Capture(scene, $"{style}-{diameter}-{dark}-{state}-{scale}"); });
#pragma warning restore CS0618
        // OD-0125: the detector above is unchanged. A speck fails only when the approved design (voice-composer.html drawn in Chrome at this scale, state, theme and backdrop; locked
        // references checked against their SHA-256) has no speck within 1 pixel of the same spot.
        if (Environment.GetEnvironmentVariable("VT12_DESIGN_DUMP") is { } designDump)
            VoicePillDesignReference.Dump(scene, $"{style}-{diameter}-{dark}-{state}-{scale}", designDump, area);
        var designSpecks = specks.Count == 0 ? [] : VoicePillDesignReference.Has($"{style}-{diameter}-{dark}-{state}-{scale}")
            ? VoicePillDesignReference.Specks($"{style}-{diameter}-{dark}-{state}-{scale}", (x, y) => Math.Sqrt((x + 0.5 - orb.X) * (x + 0.5 - orb.X) + (y + 0.5 - orb.Y) * (y + 0.5 - orb.Y)) <= ballRadius * scale + 1)
            : [];
        var notInTheDesign = new List<string>();
        for (var k = 0; k < specks.Count; k++)
            if (!components[k].Any(m => designSpecks.Any(d => Math.Abs(d.X - m.X) <= 1 && Math.Abs(d.Y - m.Y) <= 1))) notInTheDesign.Add(specks[k]);
        return notInTheDesign;
    }

    internal static (bool[] Outlier, int Width, int Height) Outliers((int Stride, byte[] Bytes, PixelSize Size) frame, Rect area)
    {
        var w = frame.Size.Width;
        var h = frame.Size.Height;
        var outlier = new bool[w * h];
        var left = Math.Max(3, (int)area.Left);
        var right = Math.Min(w - 3, (int)area.Right);
        var reference = new int[49];
        var histogram = new int[256];
        // The same rank-24 median of all 49 samples, maintained as a sliding histogram rather than sorting every neighbourhood.
        for (var y = Math.Max(3, (int)area.Top); y < Math.Min(h - 3, (int)area.Bottom); y++)
        for (var c = 0; c < 3 && left < right; c++)
        {
            Array.Clear(histogram);
            for (var dy = -3; dy <= 3; dy++)
            for (var dx = -3; dx <= 3; dx++) histogram[frame.Bytes[(y + dy) * frame.Stride + (left + dx) * 4 + c]]++;
            var median = 0;
            var below = histogram[0];
            while (below <= 24) below += histogram[++median];
            for (var x = left; x < right; x++)
            {
                outlier[y * w + x] |= Math.Abs(frame.Bytes[y * frame.Stride + x * 4 + c] - median) > 8;
                // Independently retain the original sort oracle at sampled positions in every rendered scene.
                if ((x - left) % 31 == 0 && y % 31 == 0)
                {
                    var n = 0;
                    for (var dy = -3; dy <= 3; dy++)
                    for (var dx = -3; dx <= 3; dx++) reference[n++] = frame.Bytes[(y + dy) * frame.Stride + (x + dx) * 4 + c];
                    Array.Sort(reference);
                    Assert.Equal(reference[24], median);
                }
                if (x + 1 >= right) continue;
                for (var dy = -3; dy <= 3; dy++)
                {
                    var remove = frame.Bytes[(y + dy) * frame.Stride + (x - 3) * 4 + c];
                    var add = frame.Bytes[(y + dy) * frame.Stride + (x + 4) * 4 + c];
                    histogram[remove]--;
                    histogram[add]++;
                    if (remove <= median) below--;
                    if (add <= median) below++;
                }
                while (below <= 24) below += histogram[++median];
                while (below - histogram[median] > 24) below -= histogram[median--];
            }
        }
        return (outlier, w, h);
    }

    [Theory] // V-T133: the orb's 72 px view fades its glow to zero before its own edge
    [InlineData(32, 1.0)]
    [InlineData(26, 1.0)]
    [InlineData(32, 1.25)]
    [InlineData(26, 1.25)]
    [InlineData(32, 1.5)]
    [InlineData(26, 1.5)]
    [InlineData(32, 2.0)]
    [InlineData(26, 2.0)]
    [Trait("Decision", "ED-292")]
    public void The_orbs_view_has_no_square_edge(int diameter, double scale)
    {
        headless.OnUiThread(() =>
        {
            var scene = Open(VoiceListeningStyle.Cross, diameter, dark: true, "quiet", scale);
            using var _ = scene;
            var (orb, _, _) = Marks(scene);
            var with = Shot(scene);
            orb *= scale;
            scene.Rig.Main.OnMain(() => Slot(scene.View).Orb.IsVisible = false);
            scene.Rig.PumpWindow(scene.Window);
            var without = Shot(scene);
            var worst = 0;
            var half = (int)(36 * scale);
            for (var k = -half; k <= half; k++)
            foreach (var (x, y) in new[] { ((int)orb.X + k, (int)orb.Y - half), ((int)orb.X + k, (int)orb.Y + half - 1), ((int)orb.X - half, (int)orb.Y + k), ((int)orb.X + half - 1, (int)orb.Y + k) })
            {
                var at = y * with.Stride + x * 4;
                for (var c = 0; c < 3; c++) worst = Math.Max(worst, Math.Abs(with.Bytes[at + c] - without.Bytes[at + c]));
            }
            Assert.True(worst <= 1, $"the orb's glow reaches the edge of its 72 px view: {worst}/255");
        });
    }

    // Named reproductions keep these regressions selectable even when the adapter discovers MemberData as a single theory.
    [Fact]
    [Trait("Decision", "ED-292")]
    public void Canonical_inline_small_dark_cap_is_clear() => BesideTheOrb(headless, VoiceListeningStyle.Pill, 26, true, "quiet", 1.5);

    [Fact]
    [Trait("Decision", "ED-292")]
    public void Canonical_float_small_dark_cap_is_clear() => BesideTheOrb(headless, VoiceListeningStyle.Float, 26, true, "quiet", 1.25);

    [Fact]
    [Trait("Decision", "ED-292")]
    public void Canonical_inline_small_light_pill_has_no_specks() => NoSpecks(headless, VoiceListeningStyle.Pill, 26, false, "quiet", 1);

    [Fact]
    [Trait("Decision", "ED-292")]
    public void Canonical_float_small_dark_pill_has_no_specks() => NoSpecks(headless, VoiceListeningStyle.Float, 26, true, "quiet", 1.5);

    [Theory]
    [InlineData(VoiceListeningStyle.Pill, 26, false, "quiet", 2.0)]
    [InlineData(VoiceListeningStyle.Float, 32, false, "pop", 2.0)]
    [InlineData(VoiceListeningStyle.Pill, 26, false, "pop", 1.5)]
    [InlineData(VoiceListeningStyle.Float, 26, false, "pop", 1.0)]
    [InlineData(VoiceListeningStyle.Float, 32, false, "quiet", 1.25)]
    [InlineData(VoiceListeningStyle.Float, 32, true, "quiet", 1.5)]
    [InlineData(VoiceListeningStyle.Pill, 32, true, "level1", 1.0)]
    [InlineData(VoiceListeningStyle.Pill, 26, true, "quiet", 1.5)]
    [Trait("Decision", "ED-292")]
    public void Representative_remaining_specks(VoiceListeningStyle style, int diameter, bool dark, string state, double scale)
        => NoSpecks(headless, style, diameter, dark, state, scale);

    [Theory]
    [InlineData(VoiceListeningStyle.Pill)]
    [InlineData(VoiceListeningStyle.Float)]
    [Trait("Decision", "ED-292")]
    public void Coverage_keeps_the_visible_glass_and_orb(VoiceListeningStyle style)
    {
        headless.OnUiThread(() =>
        {
            using var motion = new VoicePillMotion();
            using var scene = Open(style, 32, true, "quiet", 1.5);
            var with = Shot(scene);
            var (orb, _, _) = Marks(scene);
            var glass = scene.Rig.Main.OnMain(() =>
            {
                var pill = Pill(scene.View);
                return pill.TranslatePoint(new Point(pill.Bounds.Width / 2, pill.PillHeight * 0.8), scene.Window)!.Value;
            });
            var uniforms = OrbUniforms(scene);
            int Difference((int Stride, byte[] Bytes, PixelSize Size) other, Point point)
            {
                var worst = 0;
                var x = (int)(point.X * scene.Window.RenderScaling);
                var y = (int)(point.Y * scene.Window.RenderScaling);
                for (var dy = -2; dy <= 2; dy++)
                for (var dx = -2; dx <= 2; dx++)
                for (var c = 0; c < 3; c++)
                {
                    var at = (y + dy) * with.Stride + (x + dx) * 4 + c;
                    worst = Math.Max(worst, Math.Abs(with.Bytes[at] - other.Bytes[at]));
                }
                return worst;
            }
            scene.Rig.Main.OnMain(() => Pill(scene.View).IsVisible = false);
            scene.Rig.PumpWindow(scene.Window);
            Assert.True(Difference(Shot(scene), glass) > 8, "the shared glass body is still painted");
            scene.Rig.Main.OnMain(() => { Pill(scene.View).IsVisible = true; Slot(scene.View).Orb.IsVisible = false; });
            scene.Rig.PumpWindow(scene.Window);
            Assert.True(Difference(Shot(scene), orb) > 8, "the orb's ball is still painted");
            Assert.Equal(uniforms, OrbUniforms(scene));
        });
    }
}
