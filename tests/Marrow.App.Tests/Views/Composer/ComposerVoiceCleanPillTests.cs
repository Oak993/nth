using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Session;
using Xunit;
using static Marrow.App.Tests.Fixtures.VoicePillProbe;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Views.Composer;

// VT-12, V-T132 and V-T133 (voice-SPEC V-12.12, V-11.3; OD-0097 amendment): the clean pill beside the orb. Frames of the open pill (inline and float) at the orb sizes 32 and 26, listening quietly, listening to speech and
// in Processing, dark and light, rendered once with the pill and once without it (the pill hidden in the same instant: the same orb, glow and composer). V-T132: in the annulus from the ball's edge + 1 px to the pill cap's
// radius + 3 px, outside the sector where the pill's body continues, the two frames differ by at most 3/255 per channel. V-T133: a speck detector over the pill and a 12 px margin finds none other than the label's and the
// cross's own glyphs and the orb's own ball; the orb's 72 px view fades its glow to zero before its own edge. The display scales 125 to 200 % are not reproduced by the headless window (its scaling is 1): NOT COVERED here.
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

    private static VoicePillScene Open(VoiceListeningStyle style, int diameter, bool dark, string state)
    {
        var rig = new VoiceComposerRig(VoiceOrbSettings.Default with { ListeningStyle = style, OrbSize = diameter });
        rig.Main.OnMain(() => rig.App.Settings.VoiceNotes = new VoiceNotesSettings(Local: true, Cloud: true));
        if (state == "quiet") rig.QuietRoom();
        else
        {
            var script = Said();
            rig.Speak(script);
            rig.Engine = script.CreateEngine(TimeSpan.FromSeconds(30));
        }
        var (view, window) = rig.Open(900);
        rig.Main.OnMain(() => window.RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light);
        rig.PumpWindow(window);
        rig.Main.OnMain(() => DictationProbe.Blur(view));
        rig.Main.OnMain(() => Click(Slot(view).HitTarget));
        Run(rig, window, state == "speech" ? 1.3 : 1.0);
        if (state == "processing")
        {
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            Run(rig, window, 1.0);
        }
        return new VoicePillScene(rig, view, window);
    }

    private static (int Stride, byte[] Bytes, PixelSize Size) Shot(VoicePillScene scene) => scene.Rig.Main.OnMain(() => DictationProbe.Frame(scene.Window));

    public static TheoryData<VoiceListeningStyle, int, bool, string> Matrix()
    {
        var data = new TheoryData<VoiceListeningStyle, int, bool, string>();
        foreach (var style in new[] { VoiceListeningStyle.Pill, VoiceListeningStyle.Float })
        foreach (var d in new[] { 32, 26 })
        foreach (var dark in new[] { true, false })
        foreach (var state in new[] { "quiet", "speech", "processing" })
            data.Add(style, d, dark, state);
        return data;
    }

    // the orb's centre in window pixels, and the pill cap's centre and radius (the orb settles in the cap's centre)
    private static (Point Orb, Point Cap, double Radius) Marks(VoicePillScene scene)
    {
        return scene.Rig.Main.OnMain(() =>
        {
            var pill = Pill(scene.View);
            var slot = Slot(scene.View);
            var orbAtRest = slot.TranslatePoint(new Point(slot.Diameter / 2, slot.Diameter / 2), scene.Window)!.Value;
            var h = pill.PillHeight;
            var topLeft = pill.TranslatePoint(default, scene.Window)!.Value;
            var float_ = pill.Style == VoiceListeningStyle.Float;
            var cap = float_ ? new Point(topLeft.X + h / 2, topLeft.Y + h / 2) : new Point(topLeft.X + pill.Bounds.Width - h / 2, topLeft.Y + h / 2);
            var orb = float_ ? new Point(orbAtRest.X, orbAtRest.Y + VoicePillGeometry.FloatLift * slot.Lift) : orbAtRest;
            return (orb, cap, h / 2);
        });
    }

    [Theory] // V-T132
    [MemberData(nameof(Matrix))]
    [Trait("Decision", "ED-292")]
    public void Nothing_of_the_pills_material_shows_beside_the_orb(VoiceListeningStyle style, int diameter, bool dark, string state)
    {
        headless.OnUiThread(() =>
        {
            var scene = Open(style, diameter, dark, state);
            using var _ = scene.Rig;
            var (orb, cap, radius) = Marks(scene);
            Assert.True(scene.Rig.Main.OnMain(() => Pill(scene.View).ShownOpacity) > 0.99, "the pill is open");
            var withPill = Shot(scene);
            scene.Rig.Main.OnMain(() => Pill(scene.View).IsVisible = false);
            scene.Rig.PumpWindow(scene.Window);
            var without = Shot(scene);
            scene.Rig.Main.OnMain(() => Pill(scene.View).IsVisible = true);
            var inner = diameter / 2.0 * 1.2 + 1; // the ball at its largest pop, plus 1 px
            var outer = radius + 3;
            var worst = 0;
            var where = "";
            for (var y = (int)(cap.Y - outer) - 1; y <= cap.Y + outer + 1; y++)
            for (var x = (int)(cap.X - outer) - 1; x <= cap.X + outer + 1; x++)
            {
                var dx = x + 0.5 - cap.X;
                var dy = y + 0.5 - cap.Y;
                var r = Math.Sqrt(dx * dx + dy * dy);
                if (r < inner || r > outer) continue;
                if (style == VoiceListeningStyle.Pill ? dx < 4 : dx > -4) continue; // the sector where the pill's body continues
                var at = y * withPill.Stride + x * 4;
                for (var c = 0; c < 3; c++)
                {
                    var diff = Math.Abs(withPill.Bytes[at + c] - without.Bytes[at + c]);
                    if (diff > worst) { worst = diff; where = $"({x}, {y}) r {r:F1} channel {c}"; }
                }
            }
            Assert.True(worst <= 3, $"the pill leaves a difference of {worst}/255 beside the orb at {where}");
            scene.Rig.Close(scene.Window);
        });
    }

    private static (bool[] Outlier, int Width, int Height) Outliers((int Stride, byte[] Bytes, PixelSize Size) frame, Rect area)
    {
        var w = frame.Size.Width;
        var h = frame.Size.Height;
        var outlier = new bool[w * h];
        var window = new int[49];
        for (var y = Math.Max(3, (int)area.Top); y < Math.Min(h - 3, (int)area.Bottom); y++)
        for (var x = Math.Max(3, (int)area.Left); x < Math.Min(w - 3, (int)area.Right); x++)
        {
            var most = 0;
            for (var c = 0; c < 3; c++)
            {
                var n = 0;
                for (var dy = -3; dy <= 3; dy++)
                for (var dx = -3; dx <= 3; dx++) window[n++] = frame.Bytes[(y + dy) * frame.Stride + (x + dx) * 4 + c];
                Array.Sort(window);
                most = Math.Max(most, Math.Abs(frame.Bytes[y * frame.Stride + x * 4 + c] - window[24]));
            }
            outlier[y * w + x] = most > 8;
        }
        return (outlier, w, h);
    }

    [Theory] // V-T133
    [MemberData(nameof(Matrix))]
    [Trait("Decision", "ED-292")]
    public void The_pill_and_a_margin_round_it_have_no_specks(VoiceListeningStyle style, int diameter, bool dark, string state)
    {
        headless.OnUiThread(() =>
        {
            var scene = Open(style, diameter, dark, state);
            using var _ = scene.Rig;
            var (orb, _, _) = Marks(scene);
            var frame = Shot(scene);
            var (pillRect, labelRect, crossRect) = scene.Rig.Main.OnMain(() =>
            {
                var pill = Pill(scene.View);
                var origin = pill.TranslatePoint(default, scene.Window)!.Value;
                var label = pill.Labels.First(l => l.Opacity > 0.5);
                var labelOrigin = label.TranslatePoint(default, scene.Window)!.Value;
                var cross = pill.Cross.TranslatePoint(default, scene.Window)!.Value;
                return (new Rect(origin, pill.Bounds.Size), new Rect(labelOrigin, label.Bounds.Size), new Rect(cross, pill.Cross.Bounds.Size));
            });
            var area = pillRect.Inflate(12);
            var (outlier, w, h) = Outliers(frame, area);
            // the label's and the cross's own glyph edges, and the orb's own ball, are the ink: masked (a pixel within 2 px of their boxes)
            bool Inked(int x, int y) => labelRect.Inflate(2).Contains(new Point(x, y)) || crossRect.Inflate(2).Contains(new Point(x, y))
                || Math.Sqrt((x - orb.X) * (x - orb.X) + (y - orb.Y) * (y - orb.Y)) <= diameter / 2.0 * 1.25 + 3;
            var seen = new bool[w * h];
            var specks = new List<string>();
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
                if (members.Count <= 9 && !members.Any(m => Inked(m.X, m.Y))) specks.Add($"{members.Count} px at ({members[0].X}, {members[0].Y})");
            }
            Assert.True(specks.Count == 0, "specks: " + string.Join("; ", specks.Take(8)));
            scene.Rig.Close(scene.Window);
        });
    }

    [Theory] // V-T133: the orb's 72 px view fades its glow to zero before its own edge
    [InlineData(32)]
    [InlineData(26)]
    [Trait("Decision", "ED-292")]
    public void The_orbs_view_has_no_square_edge(int diameter)
    {
        headless.OnUiThread(() =>
        {
            var scene = Open(VoiceListeningStyle.Cross, diameter, dark: true, "quiet");
            using var _ = scene.Rig;
            var (orb, _, _) = Marks(scene);
            var with = Shot(scene);
            scene.Rig.Main.OnMain(() => Slot(scene.View).Orb.IsVisible = false);
            scene.Rig.PumpWindow(scene.Window);
            var without = Shot(scene);
            var worst = 0;
            for (var k = -36; k <= 36; k++)
            foreach (var (x, y) in new[] { ((int)orb.X + k, (int)orb.Y - 36), ((int)orb.X + k, (int)orb.Y + 35), ((int)orb.X - 36, (int)orb.Y + k), ((int)orb.X + 35, (int)orb.Y + k) })
            {
                var at = y * with.Stride + x * 4;
                for (var c = 0; c < 3; c++) worst = Math.Max(worst, Math.Abs(with.Bytes[at + c] - without.Bytes[at + c]));
            }
            Assert.True(worst <= 1, $"the orb's glow reaches the edge of its 72 px view: {worst}/255");
            scene.Rig.Close(scene.Window);
        });
    }
}
