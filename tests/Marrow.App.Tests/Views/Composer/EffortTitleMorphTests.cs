using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// EffortSlider.swift:573-574 Text(verbatim: title).contentTransition(.numericText()); :631 the slider writes the effort
// inside withAnimation(.snappy(duration: 0.2)); :582-583 the title's HStack animates on smooth(0.3) when maximum or fast
// mode changes. The morph itself is MorphingTextBlockTests'.
[Collection("Headless")]
public sealed class EffortTitleMorphTests(HeadlessSession headless)
{
    // CORRECTED in ED-238 final review round 5 (for the lead's confirmation at merge): as written before this unit (base
    // f3a9ca739) it was named "..._and_changes_at_once_outside_one" and asserted that Max -> Low with no transaction "simply
    // changes" (IsMorphing false). SOURCE FACT, EffortSlider.swift:582: `.animation(.smooth(duration: 0.3), value:
    // isMaxEffort)` animates a change out of maximum whatever the transaction around it (onReset, :219-224, writes the effort
    // with none), and the reference video's chevron follows smooth(0.3) leaving maximum (REVIEW a7c682bf6, chevron_fit.out);
    // OD-0076 (owner ruling) transitions every other change too. The last step now asserts the morph; nothing else changed.
    [Fact]
    [Trait("Req", "PR-COMPOSER-111")]
    [Trait("Req", "UI-COMPOSER-031")] // App/Views/Composer/EffortSlider.swift:566-583 the title's morph
    public void The_title_morphs_when_the_effort_changes_inside_an_animation_and_outside_one() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var effort = new ObservableStringBox { Value = "high" };
        var card = new EffortSliderCard("Sonnet", ProviderKind.Claude, new ValueList<string>(["low", "medium", "high", "max"]), "medium", false,
            effort.AsBinding(), Binding<bool>.Constant(false), () => { });
        var window = new Avalonia.Controls.Window { Width = 360, Height = 200, Content = card };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var title = Assert.Single(card.GetVisualDescendants().OfType<MorphingTextBlock>());
            Assert.Equal("High", title.Text);
            Assert.Equal(0.5, title.GrowthAnchor); // :566-567 a centred title: the "M" of Max appears where it settles (video 35.12 s)

            Transaction.WithAnimation(Animation.Snappy(duration: 0.2), () => effort.Value = "max");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Max", title.Text);
            Assert.True(title.IsMorphing); // the change came inside the slider's animation

            effort.Value = "low";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Low", title.Text);
            Assert.True(title.IsMorphing); // no transaction, but leaving maximum: :582 animates it on smooth(0.3)
        }
        finally { window.Close(); }
    });

    // The moments (ms after a change) at which the title's way is read: along the whole of a 0.2 to 0.3 s curve, early (where
    // the two curves of :582 and :631 part most), through its middle and into its tail. ED-238 final review round 6 (finding 2):
    // the 0.3 of :582-583 is a Swift literal and a single reading 40 ms in (smooth(0.3) 0.205 of its way, smooth(0.35) 0.162)
    // within 0.06 let smooth(0.35) pass (mutants C4 and C6); the label's width follows its animation's progress to 1e-5 of the
    // way, so a tolerance of 0.01 at ten moments holds smooth(0.3) from smooth(0.25) and smooth(0.35) (and snappy) as well.
    private static readonly int[] CurveMoments = [20, 40, 60, 80, 100, 130, 160, 200, 250, 300];

    // The title's way, from the old text's width to the new one's, <paramref name="moments"/> ms after a change made by
    // <paramref name="change"/>: its width (the label's frame) and its right edge's position (the title is centred in the card, so
    // its edge moves half the width's way: read from where the edge is drawn, not from the width it follows). OD-0139 took the
    // chevron, which used to ride that edge, off the title (the model line has it now): the title's own edge is read in its place.
    private static List<(int Ms, double Width, double Chevron)> Ways(Avalonia.Controls.Window window, MorphingTextBlock title,
        ManualFrameSource frames, Marrow.Testing.ManualTimeProvider clock, Action change, int[] moments)
    {
        double ChevronX() => title.TranslatePoint(new Point(title.Bounds.Width, 0), window)!.Value.X; // the title's right edge, where the chevron used to ride
        window.UpdateLayout();
        var from = title.Bounds.Width;
        var fromX = ChevronX();
        change();
        Dispatcher.UIThread.RunJobs();
        var at = new Dictionary<int, (double Width, double X)>();
        for (var t = 1; t <= 700; t++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(1));
            frames.Advance(TimeSpan.FromMilliseconds(1));
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            if (moments.Contains(t)) at[t] = (title.Bounds.Width, ChevronX());
        }
        var to = title.Bounds.Width;
        var toX = ChevronX();
        Assert.False(title.IsMorphing);
        Assert.True(Math.Abs(to - from) > 5, $"the title's width does not change: {from:F1} -> {to:F1}");
        Assert.True(Math.Abs(toX - fromX) > 2.5, $"the title's right edge does not move: {fromX:F1} -> {toX:F1}");
        return moments.Select(ms => (ms, (at[ms].Width - from) / (to - from), (at[ms].X - fromX) / (toX - fromX))).ToList();
    }

    // The way a change made by <paramref name="change"/> takes along <paramref name="curve"/>: the width and the right edge's
    // position at every moment of CurveMoments within <paramref name="tolerance"/> of the curve's own progress there.
    private static void AssertOnCurve(string name, List<(int Ms, double Width, double Chevron)> ways, Animation curve, string curveName, double tolerance = 0.01)
    {
        var worst = ways.Max(way => Math.Max(Math.Abs(way.Width - curve.Progress(way.Ms / 1000.0)), Math.Abs(way.Chevron - curve.Progress(way.Ms / 1000.0))));
        var seen = string.Join(", ", ways.Select(way => $"{way.Ms} ms {way.Width:F2}/{way.Chevron:F2} ({curve.Progress(way.Ms / 1000.0):F2})"));
        Assert.True(worst <= tolerance, $"{name}: the title's width and right edge are not on {curveName} (worst {worst:F3} off, allowed {tolerance:F2}); width/edge (the curve) {seen}");
    }

    // SOURCE FACT, EffortSlider.swift:579-583: the title's HStack (the text, so its width and the chevron after it) carries
    // `.animation(.smooth(duration: 0.3), value: isMaxEffort)` and `.animation(.smooth(duration: 0.3), value: isFast)`; the
    // slider writes the effort inside withAnimation(.snappy(duration: 0.2)) (:631), onReset with no animation (:219-224,
    // the effort and fastMode = false). So a change into or out of maximum, or one that turns fast mode on or off with the
    // effort, runs on smooth(0.3) whatever comes around it, and any other runs on the slider's snappy(0.2). SOURCE FACT, the
    // reference video (chevron_fit.out, REVIEW a7c682bf6): High -> Extra High follows snappy(0.2), Extra High -> Max and
    // Max -> Extra High smooth(0.3). Read along the whole curve (CurveMoments, AssertOnCurve): the label's width and the
    // right edge's drawn position at ten moments from 20 to 300 ms, each within 0.01 of its curve's own progress.
    [Fact]
    [Trait("Decision", "ED-238")] // ED-238 final round 5: :582-583 at PR-COMPOSER-111 / UI-COMPOSER-031's title
    public void The_title_runs_on_smooth_0_3_when_maximum_or_fast_mode_changes_and_on_the_sliders_snappy_otherwise() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new Marrow.Testing.ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var effort = new ObservableStringBox { Value = "high" };
        var fast = new ObservableBoolBox { Value = false };
        var card = new EffortSliderCard("Sonnet", ProviderKind.Claude, new ValueList<string>(["low", "medium", "high", "xhigh", "max"]), "medium", true,
            effort.AsBinding(), fast.AsBinding(), () => { }, onTitleTap: () => { });
        var window = new Avalonia.Controls.Window { Width = 360, Height = 200, Content = card, UseLayoutRounding = false }; // read the curve, not the pixel grid (CurveMoments)
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var title = Assert.Single(card.GetVisualDescendants().OfType<MorphingTextBlock>());
            Assert.DoesNotContain(title.GetVisualAncestors(), ancestor => ancestor is Button); // OD-0139: the title is plain text, no chevron beside it
            var snappy = Animation.Snappy(duration: 0.2);
            var smooth = Animation.Smooth(duration: 0.3);
            void Slider(string value) => Transaction.WithAnimation(Animation.Snappy(duration: 0.2), () => effort.Value = value);
            var steps = new (string Name, Action Change, Animation Expected, string Curve)[]
            {
                ("High -> Extra High in the slider's snappy(0.2)", () => Slider("xhigh"), snappy, "snappy(0.2)"),
                ("Extra High -> Max in the slider's snappy(0.2), into maximum", () => Slider("max"), smooth, "smooth(0.3)"),
                ("Max -> Extra High in the slider's snappy(0.2), out of maximum", () => Slider("xhigh"), smooth, "smooth(0.3)"),
                ("Extra High -> Medium in the slider's snappy(0.2)", () => Slider("medium"), snappy, "snappy(0.2)"),
                ("Medium -> Extra High with fast mode turned on, in snappy(0.2)", () => Transaction.WithAnimation(Animation.Snappy(duration: 0.2), () => { effort.Value = "xhigh"; fast.Value = true; }), smooth, "smooth(0.3)"),
                ("Extra High -> Medium in fast mode, in snappy(0.2)", () => Slider("medium"), snappy, "snappy(0.2)"),
                ("Medium -> Max with no animation, into maximum", () => effort.Value = "max", smooth, "smooth(0.3)"),
            };
            foreach (var (name, change, expected, curve) in steps)
                AssertOnCurve(name, Ways(window, title, frames, clock, change, CurveMoments), expected, curve);
        }
        finally { window.Close(); }
    });

    // DEVIATION OD-0076 (owner ruling 2026-10-02, "every change is transitioned, never cut"): where the Mac changes the title
    // with no animation (Reset from an effort that is neither maximum nor fast, EffortSlider.swift:219-224; :582-583 not
    // triggered), the port still morphs it, in the title's own motion (:582's smooth(0.3)); OD-0076's "the system's
    // reduce-motion setting stills them" cuts that added transition. A change out of maximum stays on smooth(0.3) under reduce
    // motion as on the Mac (:582 is not a reduce-motion choice).
    [Fact]
    [Trait("Decision", "ED-238")] // ED-238 final round 5: OD-0076 at PR-COMPOSER-111 / UI-COMPOSER-031's title
    public void An_effort_change_with_no_animation_morphs_on_smooth_0_3_and_reduce_motion_cuts_it_unless_maximum_changes() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new Marrow.Testing.ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var effort = new ObservableStringBox { Value = "high" };
        var card = new EffortSliderCard("Sonnet", ProviderKind.Claude, new ValueList<string>(["low", "medium", "high", "xhigh", "max"]), "medium", false,
            effort.AsBinding(), Binding<bool>.Constant(false), () => { }, onTitleTap: () => { });
        var window = new Avalonia.Controls.Window { Width = 360, Height = 200, Content = card, UseLayoutRounding = false }; // read the curve, not the pixel grid (CurveMoments)
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var title = Assert.Single(card.GetVisualDescendants().OfType<MorphingTextBlock>());
            Assert.DoesNotContain(title.GetVisualAncestors(), ancestor => ancestor is Button); // OD-0139: the title is plain text, no chevron beside it
            var smooth = Animation.Smooth(duration: 0.3);
            AssertOnCurve("High -> Medium with no animation, as Reset writes it", Ways(window, title, frames, clock, () => effort.Value = "medium", CurveMoments), smooth, "smooth(0.3)");

            EnvironmentValues.SetAccessibilityReduceMotion(window, true);
            Dispatcher.UIThread.RunJobs();
            effort.Value = "xhigh";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Extra High", title.Text);
            Assert.False(title.IsMorphing, "with reduce motion an effort change the Mac does not animate is still morphed");

            AssertOnCurve("with reduce motion, Extra High -> Max (into maximum, :582)", Ways(window, title, frames, clock, () => effort.Value = "max", CurveMoments), smooth, "smooth(0.3)");
        }
        finally { window.Close(); }
    });

    // EffortSlider.swift:543-555 titleStyle, :582-583 .animation(.smooth(duration: 0.3), value: isMaxEffort). SOURCE FACT
    // (reference runtime, 2-chat-use.mov 35.10-35.21 s, study effort-title-2026-10-03): when the title changes text and
    // colour together (Extra High -> Max), the arriving letters wear the new colour from their first frame (an orange
    // "M", R/G 1.4 over the glass, beside the pink leaving "High"); only a colour change without a new text fades.
    // Final review round 4 (finding 3): as first written this read only the title's Foreground, the new colour whatever is
    // drawn (mutants C1, every letter in the new colour, and C2, the arriving ones in the old, survived it); it now reads the
    // letters the title draws, by the line each comes from, as the card sets text and colour (EffortSlider.cs).
    [Fact]
    [Trait("Decision", "ED-238")] // a part of PR-COMPOSER-111: the numericText morph (EffortSlider.swift:574), not the button clauses; re-traited in final round 3
    public void A_title_that_morphs_into_new_letters_draws_them_in_the_new_colour_at_once() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new Marrow.Testing.ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var effort = new ObservableStringBox { Value = "high" };
        var card = new EffortSliderCard("Sonnet", ProviderKind.Claude, new ValueList<string>(["low", "medium", "high", "max"]), "medium", false,
            effort.AsBinding(), Binding<bool>.Constant(false), () => { });
        var window = new Avalonia.Controls.Window { Width = 360, Height = 200, Content = card };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var title = Assert.Single(card.GetVisualDescendants().OfType<MorphingTextBlock>());
            var old = Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(title.Foreground).Color;
            Transaction.WithAnimation(Animation.Snappy(duration: 0.2), () => effort.Value = "max");
            Dispatcher.UIThread.RunJobs();
            Assert.True(title.IsMorphing);
            var brush = Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(title.Foreground);
            Assert.Equal(EffortBrand.Create(ProviderKind.Claude).TitleColor.Resolve(title.ActualThemeVariant), brush.Color); // :551 brand.title at Max
            Assert.NotEqual(old, brush.Color);
            // The first frame an arriving letter of "Max" is drawn, and 110 ms in (video 35.15 s: an orange "M" beside the
            // pink leaving "High"): every letter of "Max" in the new colour, every leaving letter of "High" in the old.
            var first = -1;
            for (var ms = 1; ms <= 110; ms++)
            {
                clock.Advance(TimeSpan.FromMilliseconds(1));
                frames.Advance(TimeSpan.FromMilliseconds(1));
                Dispatcher.UIThread.RunJobs();
                var drawn = DrawnColours(title);
                if (first < 0 && drawn.ContainsKey("Max")) first = ms;
                if (ms != first && ms != 110) continue;
                var seen = $"{ms} ms: {Show(drawn)}";
                Assert.True(drawn.TryGetValue("Max", out var arriving) && arriving.SetEquals([brush.Color]), $"an arriving letter of \"Max\" is not drawn in the new colour {brush.Color}: {seen}");
                Assert.True(drawn.TryGetValue("High", out var leaving) && leaving.SetEquals([old]), $"a leaving letter of \"High\" is not drawn in the old colour {old}: {seen}");
            }
            Assert.True(first > 0, "no letter of \"Max\" is drawn within 110 ms");
        }
        finally { window.Close(); }
    });

    // The same with no transaction around the change: Reset (onReset, EffortSlider.swift:219-224) writes the effort with none,
    // and a change out of maximum (or into it, written so) runs the title on :582's smooth(0.3) all the same, so it morphs. ED-238
    // final review round 6 (finding 3): cad125620 made the card's colour follow the title's morph (the animation it runs on)
    // instead of the change's transaction, and only the transaction's change was tested, so mutant C12 (the colour flag read
    // from the transaction again) survived every test: a Reset from Max cross-faded its arriving letters from the old colour
    // over 0.3 s. The new colour is set at once, SOURCE FACT as above (35.10-35.21 s: the arriving letters wear it from their
    // first frame, the leaving ones keep the old); that it is the same with no transaction is the card's own rule
    // (ENGINEERING DECISION: one rule for a morphing title, whatever started it).
    [Theory]
    [Trait("Decision", "ED-238")] // a part of PR-COMPOSER-111: the numericText morph (EffortSlider.swift:574) with :582-583's colour, with no transaction
    [InlineData("max", "low", "Max", "Low")] // Reset from Max, :219-224 with :582
    [InlineData("low", "max", "Low", "Max")] // into maximum with no animation
    public void A_change_with_no_transaction_into_or_out_of_maximum_sets_the_new_colour_at_once(string from, string to, string leavingLine, string arrivingLine) => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new Marrow.Testing.ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var effort = new ObservableStringBox { Value = from };
        var card = new EffortSliderCard("Sonnet", ProviderKind.Claude, new ValueList<string>(["low", "medium", "high", "max"]), "medium", false,
            effort.AsBinding(), Binding<bool>.Constant(false), () => { });
        var window = new Avalonia.Controls.Window { Width = 360, Height = 200, Content = card };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var title = Assert.Single(card.GetVisualDescendants().OfType<MorphingTextBlock>());
            var brand = EffortBrand.Create(ProviderKind.Claude).TitleColor.Resolve(title.ActualThemeVariant); // :551 brand.title at Max
            var plain = EffortPalette.Title.Resolve(title.ActualThemeVariant); // :841 EffortPalette.title, :554
            var old = from == "max" ? brand : plain;
            var fresh = from == "max" ? plain : brand;
            Assert.NotEqual(old, fresh);
            Assert.Equal(old, Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(title.Foreground).Color);
            effort.Value = to; // no transaction, as onReset writes it
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(arrivingLine, title.Text);
            Assert.True(title.IsMorphing, "a change into or out of maximum with no transaction is not morphed (:582 animates it on smooth(0.3))");
            // At once: the title's colour is the new one before any frame, not the old one on its way over 0.3 s.
            Assert.Equal(fresh, Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(title.Foreground).Color);
            // Every frame from the first an arriving letter is drawn to 110 ms in: every letter of the new line in the new colour,
            // every leaving letter of the old line in the old one.
            var first = -1;
            for (var ms = 1; ms <= 110; ms++)
            {
                clock.Advance(TimeSpan.FromMilliseconds(1));
                frames.Advance(TimeSpan.FromMilliseconds(1));
                Dispatcher.UIThread.RunJobs();
                var drawn = DrawnColours(title);
                if (first < 0 && drawn.ContainsKey(arrivingLine)) first = ms;
                var seen = $"{from} -> {to}, {ms} ms: {Show(drawn)}";
                Assert.Equal(fresh, Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(title.Foreground).Color);
                if (first < 0) continue;
                Assert.True(drawn.TryGetValue(arrivingLine, out var arriving) && arriving.SetEquals([fresh]), $"an arriving letter of \"{arrivingLine}\" is not drawn in the new colour {fresh} from its first frame: {seen}");
                Assert.True(!drawn.TryGetValue(leavingLine, out var leaving) || leaving.SetEquals([old]), $"a leaving letter of \"{leavingLine}\" is not drawn in the old colour {old}: {seen}");
            }
            Assert.True(first > 0, $"no letter of \"{arrivingLine}\" is drawn within 110 ms");
            Assert.True(DrawnColours(title).ContainsKey(leavingLine), $"no leaving letter of \"{leavingLine}\" is still drawn 110 ms in");
        }
        finally { window.Close(); }
    });

    // The same through a takeover, as the card is dragged back down (video 35.635-35.807 s, Max -> Extra High taken over by
    // Extra High -> High at 35.753 s: the leaving "x" of "Max" still orange beside the pink "Ext"): the leaving letters of the
    // change taken over keep that change's old colour (mutant C3, drawn in the colour of now, and E3c, not drawn at all, were
    // killed by no test of the card's).
    [Fact]
    [Trait("Decision", "ED-238")] // a part of PR-COMPOSER-111: the numericText morph (EffortSlider.swift:574) through a takeover, with :582-583's colour
    public void The_leaving_letters_of_a_change_taken_over_keep_that_changes_old_colour() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new Marrow.Testing.ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var effort = new ObservableStringBox { Value = "max" };
        var card = new EffortSliderCard("Sonnet", ProviderKind.Claude, new ValueList<string>(["low", "medium", "high", "xhigh", "max"]), "medium", false,
            effort.AsBinding(), Binding<bool>.Constant(false), () => { });
        var window = new Avalonia.Controls.Window { Width = 360, Height = 200, Content = card };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        void Advance(int milliseconds)
        {
            for (var ms = 0; ms < milliseconds; ms++)
            {
                clock.Advance(TimeSpan.FromMilliseconds(1));
                frames.Advance(TimeSpan.FromMilliseconds(1));
                Dispatcher.UIThread.RunJobs();
            }
        }
        try
        {
            var title = Assert.Single(card.GetVisualDescendants().OfType<MorphingTextBlock>());
            var brand = Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(title.Foreground).Color;
            Transaction.WithAnimation(Animation.Snappy(duration: 0.2), () => effort.Value = "xhigh"); // 35.635 s
            Dispatcher.UIThread.RunJobs();
            var plain = Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(title.Foreground).Color;
            Assert.NotEqual(brand, plain);
            Advance(118);
            Transaction.WithAnimation(Animation.Snappy(duration: 0.2), () => effort.Value = "high"); // 35.753 s, the takeover
            Dispatcher.UIThread.RunJobs();
            Advance(34); // 35.787 s
            var drawn = DrawnColours(title);
            var seen = Show(drawn);
            Assert.True(drawn.TryGetValue("Max", out var max) && max.SetEquals([brand]), $"the leaving letters of \"Max\" are not drawn in their old colour {brand}: {seen}");
            Assert.True(drawn.TryGetValue("Extra High", out var extra) && extra.SetEquals([plain]), $"the letters of \"Extra High\" are not drawn in {plain}: {seen}");
            Assert.True(drawn.TryGetValue("High", out var high) && high.SetEquals([plain]), $"the letters of \"High\" are not drawn in {plain}: {seen}");
        }
        finally { window.Close(); }
    });

    // The colours the title draws its letters in now, by the line each glyph run comes from (a change draws every letter as a
    // run of one, carrying its line's text), read from what it pushes into a recording.
    private static Dictionary<string, HashSet<Avalonia.Media.Color>> DrawnColours(MorphingTextBlock title)
    {
        var recording = new Avalonia.Media.DrawingGroup();
        using (var context = recording.Open()) title.Render(context);
        var result = new Dictionary<string, HashSet<Avalonia.Media.Color>>();
        void Walk(Avalonia.Media.Drawing drawing)
        {
            switch (drawing)
            {
                case Avalonia.Media.DrawingGroup group:
                    foreach (var child in group.Children) Walk(child);
                    break;
                case Avalonia.Media.GlyphRunDrawing { GlyphRun: { } run } glyphs:
                    var line = run.Characters.ToString();
                    if (!result.TryGetValue(line, out var set)) result[line] = set = [];
                    set.Add(glyphs.Foreground is Avalonia.Media.ISolidColorBrush solid ? solid.Color : Avalonia.Media.Colors.Transparent);
                    break;
            }
        }
        Walk(recording);
        return result;
    }

    private static string Show(Dictionary<string, HashSet<Avalonia.Media.Color>> drawn) =>
        string.Join("; ", drawn.Select(pair => $"\"{pair.Key}\" {string.Join(",", pair.Value)}"));

    // SOURCE FACT (reference runtime, 2-chat-use.mov 35.12-35.29 s): as "Extra High" narrows to "Max", the leaving "High"
    // is carried right past the narrowing label's edge (once past the chevron; OD-0139 moved the chevron to the model line) and fades there, raised; nothing clips it.
    [Fact]
    [Trait("Decision", "ED-238")] // a part of PR-COMPOSER-111: the numericText morph (EffortSlider.swift:574), not the button clauses; re-traited in final round 3
    public void Leaving_letters_carried_past_the_titles_edge_are_drawn_not_clipped() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new Marrow.Testing.ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var effort = new ObservableStringBox { Value = "xhigh" };
        var card = new EffortSliderCard("Sonnet", ProviderKind.Claude, new ValueList<string>(["low", "medium", "high", "xhigh", "max"]), "medium", false,
            effort.AsBinding(), Binding<bool>.Constant(false), () => { }, onTitleTap: () => { });
        var window = new Avalonia.Controls.Window { Width = 330, Height = 160, Content = card, Background = Avalonia.Media.Brushes.Black, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var title = Assert.Single(card.GetVisualDescendants().OfType<MorphingTextBlock>());
            Assert.DoesNotContain(title.GetVisualAncestors(), ancestor => ancestor is Avalonia.Controls.Button); // OD-0139: no button around the title; nothing clips its leaving letters
            Transaction.WithAnimation(Animation.Snappy(duration: 0.2), () => effort.Value = "max");
            for (var ms = 0; ms < 130; ms += 10)
            {
                clock.Advance(TimeSpan.FromMilliseconds(10));
                frames.Advance(TimeSpan.FromMilliseconds(10));
                Dispatcher.UIThread.RunJobs();
            }
            window.UpdateLayout();
            var box = new Avalonia.Rect(title.TranslatePoint(new Avalonia.Point(0, 0), window)!.Value, title.Bounds.Size);
            using var frame = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
            using var pixels = frame.Lock();
            var row = new byte[pixels.RowBytes];
            var ink = 0;
            for (var y = (int)box.Top; y < (int)box.Bottom; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(pixels.Address + y * pixels.RowBytes, row, 0, pixels.RowBytes);
                for (var x = (int)Math.Ceiling(box.Right) + 1; x < Math.Min(pixels.Size.Width, (int)box.Right + 40); x++)
                    if (Math.Max(row[x * 4], Math.Max(row[x * 4 + 1], row[x * 4 + 2])) > 40) ink++;
            }
            Assert.True(ink > 10, $"letters drawn past the title's right edge: {ink} pixels");
        }
        finally { window.Close(); }
    });

    // OD-0094: the card's gradient title (here a pair's lead-to-heads colours) drifts at rest; a single-colour title
    // (a plain effort) stays as it is.
    [Theory]
    [Trait("Decision", "ED-238")] // OD-0094's drift at the site (EffortSlider.swift:573-583), not PR-COMPOSER-111's button clauses
    [InlineData(true)]
    [InlineData(false)]
    public void A_gradient_title_drifts_at_rest_and_a_single_colour_one_does_not(bool pair) => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new Marrow.Testing.ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        using var harness = new AppModelHarness(withRuntime: false);
        var look = pair ? new EffortPairLook(new HydraPair(ProviderKind.Codex), new AppModel().Providers, null) : null;
        var effort = new ObservableStringBox { Value = "high" };
        var card = new EffortSliderCard("Lead", pair ? ProviderKind.Codex : ProviderKind.Claude, new ValueList<string>(["low", "medium", "high", "max"]), "medium", false,
            effort.AsBinding(), Binding<bool>.Constant(false), () => { }, look, onTitleTap: () => { });
        var window = new Avalonia.Controls.Window { Width = 330, Height = 160, Content = card, Background = Avalonia.Media.Brushes.Black, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var title = Assert.Single(card.GetVisualDescendants().OfType<MorphingTextBlock>());
            Assert.Equal(pair, title.Foreground is Avalonia.Media.ILinearGradientBrush);
            byte[] Title()
            {
                window.UpdateLayout();
                var box = new Avalonia.Rect(title.TranslatePoint(new Avalonia.Point(0, 0), window)!.Value, title.Bounds.Size);
                using var frame = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
                using var pixels = frame.Lock();
                var row = new byte[pixels.RowBytes];
                var result = new List<byte>();
                for (var y = (int)box.Top; y < (int)box.Bottom; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(pixels.Address + y * pixels.RowBytes, row, 0, pixels.RowBytes);
                    for (var x = (int)box.Left; x < (int)box.Right; x++) result.AddRange([row[x * 4], row[x * 4 + 1], row[x * 4 + 2]]);
                }
                return result.ToArray();
            }
            var start = Title();
            for (var ms = 0; ms < 1000; ms += 20)
            {
                clock.Advance(TimeSpan.FromMilliseconds(20));
                frames.Advance(TimeSpan.FromMilliseconds(20));
                Dispatcher.UIThread.RunJobs();
            }
            var later = Title();
            var moved = start.Zip(later, (a, b) => Math.Abs(a - b)).Max();
            if (pair) Assert.True(moved > 20, $"the pair's colours drift along the title: {moved}");
            else Assert.True(moved <= 1, $"a single colour stays: {moved}");
        }
        finally { window.Close(); }
    });
}
