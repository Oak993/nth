using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), :821-916 HydraMergeTrack and its body, :926-1162 HydraMergeRow. Where a record's
// capture comparison asks for a frame the reference videos do not contain, the Swift source is the proof (OD-0098): the states the code names
// are mounted on the manual frame clock, rendered headless in both themes, and the values the code states are read back from the rendered
// frame (the dots' colours, the head's place and size, the capsule's width from one frame to the next). The pill is drawn in a .continuous
// rounded rectangle, which ED-181 still draws with circular arcs: its corner is the one thing these renders do not claim.
[Collection("Headless")]
public sealed class TimelineRowsBlitzAbMergeTests(HeadlessSession headless)
{
    private static readonly (ThemeVariant Theme, Color Backdrop)[] Themes =
    [
        (ThemeVariant.Dark, Color.FromRgb(24, 24, 27)),
        (ThemeVariant.Light, Color.FromRgb(244, 244, 246)),
    ];

    // The stage words, verbatim from Hydra.swift:521-529, in the declaration order of :509-517.
    private static readonly string[] StageWords =
    [
        "Gathering the team's files", "Writing the commit", "Writing the commit message", "Pushing the branch",
        "Opening the merge request", "Merging", "Bringing the checkout up to date",
    ];

    private static void Advance(TestRuntime main, ManualFrameSource frames, double milliseconds)
    {
        frames.Advance(TimeSpan.Zero);
        for (var elapsed = 0.0; elapsed < milliseconds; elapsed += 16)
        {
            main.AdvanceMilliseconds(16);
            frames.Advance(TimeSpan.FromMilliseconds(16));
        }
    }

    private static Border PillOf(Control row) => Mounted<Border>(row).First(border => border.Padding == new Thickness(12, 8, 14, 8));

    // :1017 .background(.tint.opacity(0.1)) over the backdrop.
    private static Color FillOver(ThemeVariant theme, Color backdrop) => BlitzAbFrame.Over(ChromeTokens.Accent.Opacity(0.1).Resolve(theme), backdrop);

    // The pixel at the middle of dot i of a track drawn in the box: dots are 4 across, 7 apart, at the bottom of the 20-point frame (:861-865, :872-874, :901-905).
    private static Color DotAt(BlitzAbFrame frame, Rect track, int i) =>
        frame.At((int)Math.Round(track.Left + i * 7 + 2) - 1, (int)Math.Round(track.Top + 18) - 1);

    private static bool Near(Color a, Color b, int within = 12) => BlitzAbFrame.Distance(a, b) <= within;

    // :852-914 the colour a dot is drawn in: the overlay at 0.28 with the accent over it at the dot's lit value, the whole faded with the rest.
    // SwiftUI composites an opacity on a container as a group (the lit dot covers the unlit one, then the whole is faded) and Avalonia's Skia
    // path multiplies the opacity into each element (the unlit dot is faded, the lit one over it); the two differ by about 5 percent on a
    // 4-point dot while it fades, so a dot is accepted at either (UNRESOLVED whether the difference is visible on the Mac; not claimed).
    private static Color[] ExpectedDot(ThemeVariant theme, Color fill, double lit, double rest)
    {
        var overlayColour = ChromeTokens.Overlay(0.28).Resolve(theme);
        var overlay = BlitzAbFrame.Over(overlayColour, fill);
        var accent = ChromeTokens.Accent.Resolve(theme);
        static Color Mix(Color a, Color b, double t) => Color.FromRgb((byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
        var group = Mix(Mix(overlay, accent, lit), fill, rest);
        var faded = BlitzAbFrame.Over(Color.FromArgb((byte)Math.Round(overlayColour.A * (1 - rest)), overlayColour.R, overlayColour.G, overlayColour.B), fill);
        var element = Mix(faded, accent, lit * (1 - rest));
        return [group, element];
    }

    private static bool NearAny(Color pixel, Color[] expected, int within = 12) => expected.Any(colour => Near(pixel, colour, within));

    private static double Lit(int i, int count, double position, double done) => Math.Max(position + 0.35 >= i ? 1 : 0, Math.Clamp(done * (count + 1) - i, 0, 1)); // :877-882

    // ---- PR-HYDRAMERGE-101, -102, -106: the merging pill at each of the seven stages ----

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-101")] // TimelineRows.swift:954-958, :1056-1079; Hydra.swift:521-529; OD-0098: the stage words drawn at each stage, in both themes
    [Trait("Req", "PR-HYDRAMERGE-102")] // TimelineRows.swift:963-965, :821-841; Hydra.swift:509-517: one dot per stage, the head on the dot of the stage under way
    [Trait("Req", "PR-HYDRAMERGE-106")] // TimelineRows.swift:1012-1019: the shared pill frame and the announced label, laid out and drawn
    public void The_merging_pill_is_drawn_at_each_of_the_seven_stages_with_its_words_and_no_clock_and_the_head_on_the_dot_of_the_stage_in_both_themes() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        var all = HydraMergeStageExtensions.AllCases;
        Assert.Equal(StageWords, all.Select(stage => stage.Words)); // Hydra.swift:521-529 verbatim, in the order of :509-517
        foreach (var (theme, backdrop) in Themes)
        {
            HydraMergeRow row = null!;
            AvaloniaWindow window = null!;
            var fill = FillOver(theme, backdrop);
            h.Main.OnMain(() =>
            {
                h.Runtime.IsHydraMerging = true;
                h.Runtime.HydraMergeStep = all[0];
                h.Runtime.HydraMergeStage = all[0].Words;
                row = new HydraMergeRow(h.Runtime, new HydraMergePhase.Merging());
                window = BlitzAbRender.Open(new Border { Padding = new Thickness(24), Child = row }, theme, backdrop, 640, 90);
            });
            for (var step = 0; step < all.Count; step++)
            {
                var at = step;
                h.Main.OnMain(() => { h.Runtime.HydraMergeStep = all[at]; h.Runtime.HydraMergeStage = all[at].Words; });
                h.Main.OnMain(() => Settle(window));
                Advance(h.Main, frames, 900);
                h.Main.OnMain(() =>
                {
                    var frame = BlitzAbRender.Capture(window);
                    var words = all[at].Words + "…";
                    var because = $"{theme}, stage {at} ({all[at].Words})";
                    // :1012-1019 the pill's frame: 12, 14 and 8 of padding, 96 points kept clear of the trailing edge, leading edge on the row's.
                    var pill = PillOf(row);
                    var box = BlitzAbRender.BoxIn(pill, window);
                    Assert.Equal(new Thickness(0, 0, 96, 0), row.Padding);
                    Assert.Equal(24, box.Left, 0.5);
                    Assert.True(box.Right <= 640 - 24 - 96 + 0.5, $"{because}: the pill runs to {box.Right}");
                    Assert.True(BlitzAbFrame.Distance(frame.At((int)box.Left + 5, (int)(box.Top + box.Height / 2)), fill) <= 6, $"{because}: the pill's fill");
                    Assert.True(BlitzAbFrame.Distance(frame.At((int)box.Right + 4, (int)(box.Top + box.Height / 2)), backdrop) <= 6, $"{because}: the pill ends where it measures");
                    Assert.Equal("Merging the team's work: " + words, AutomationProperties.GetName(pill)); // :1019
                    // :954-958 the stage with one ellipsis and no clock, in the callout's medium weight in the primary ink at 0.9.
                    Assert.All(Texts(row), text => Assert.Equal(words, text));
                    Assert.DoesNotContain(Texts(row), text => text.Any(char.IsDigit));
                    var drawn = Assert.Single(Mounted<TextBlock>(row), text => text.Text == words && text.GetSelfAndVisualAncestors().All(visual => visual.Opacity > 0.99));
                    BlitzAbRender.TextIsColour(frame, BlitzAbRender.BoxIn(drawn, window), fill, ChromeTokens.PrimaryText.Opacity(0.9).Resolve(theme), because, slack: 40, closest: 130);
                    // :963-965, :844-915 seven dots: those up to the stage lit in the accent, the rest unlit; the head on the dot of the stage, standing on the ground.
                    var track = BlitzAbRender.BoxIn(Assert.Single(Mounted<HydraMergeTrack>(row)), window);
                    Assert.Equal(46, track.Width, 0.01);
                    Assert.Equal(20, track.Height, 0.01);
                    Assert.Equal(box.Left + 12, track.Left, 0.5);
                    for (var i = 0; i < 7; i++)
                        Assert.True(NearAny(DotAt(frame, track, i), ExpectedDot(theme, fill, i <= at ? 1 : 0, 0)), $"{because}: dot {i} is {DotAt(frame, track, i)}, expected {ExpectedDot(theme, fill, i <= at ? 1 : 0, 0)[0]}");
                    var ink = frame.Ink(new Rect(track.Left - 5, track.Top - 2, track.Width + 10, 16), fill) ?? throw new Xunit.Sdk.XunitException($"{because}: no head drawn");
                    var centre = track.Left + at * 7 + 2;
                    Assert.True(ink.Left >= centre - 8 && ink.Right <= centre + 8, $"{because}: the head's ink spans {ink.Left}..{ink.Right}, the dot's centre is {centre}");
                    Assert.True(Math.Abs((ink.Left + ink.Right + 1) / 2.0 - centre) <= 2.5, $"{because}: the head is centred on {(ink.Left + ink.Right + 1) / 2.0}, the dot's centre is {centre}");
                    Assert.True(ink.Top >= track.Top - 2 && ink.Bottom <= track.Top + 13, $"{because}: the head stands at {ink.Top}..{ink.Bottom} in a frame from {track.Top}");
                });
            }
            h.Main.OnMain(() => BlitzAbRender.Shut(window));
        }
    });

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-101")] // TimelineRows.swift:956 the words with no stage reported: exactly Merging…; :1059-1079 the one slot of words
    public void With_no_stage_reported_the_words_are_exactly_Merging_and_an_ellipsis_and_the_head_stands_on_the_first_dot() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        foreach (var (theme, backdrop) in Themes)
        {
            h.Main.OnMain(() =>
            {
                h.Runtime.IsHydraMerging = true;
                h.Runtime.HydraMergeStep = null;
                h.Runtime.HydraMergeStage = null;
                var row = new HydraMergeRow(h.Runtime, new HydraMergePhase.Merging());
                BlitzAbRender.Render(new Border { Padding = new Thickness(24), Child = row }, theme, backdrop, 640, 90, (window, frame) =>
                {
                    var fill = FillOver(theme, backdrop);
                    Assert.All(Texts(row), text => Assert.Equal("Merging…", text)); // U+2026, and nothing after it
                    Assert.Equal("Merging the team's work: Merging…", AutomationProperties.GetName(PillOf(row)));
                    var track = BlitzAbRender.BoxIn(Assert.Single(Mounted<HydraMergeTrack>(row)), window);
                    for (var i = 0; i < 7; i++)
                        Assert.True(NearAny(DotAt(frame, track, i), ExpectedDot(theme, fill, i == 0 ? 1 : 0, 0)), $"{theme}: dot {i} with no step reported");
                });
            });
        }
    });

    // ---- PR-CHAT-151, PR-HYDRAMERGE-103: the track at interpolation points ----

    // :844-915 the three values and the geometry the code states, written out once, independently of the C#.
    private static (double Left, double Top, double Side, double Scale) ExpectedHead(double position, double done, double rest, bool hops = true)
    {
        var arc = hops ? 4 * Math.Abs(Math.Sin(Math.PI * position)) : 0;
        var side = 12 + (18 - 12) * rest;
        var left = (position * 7 + 2 - 6) * (1 - rest);
        var top = -arc * (1 - rest) + (20 - 18) / 2.0 * rest;
        var pop = hops ? 1 + 0.18 * Math.Sin(Math.PI * Math.Min(done, 1)) : 1;
        return (left, top, side, pop);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-151")] // TimelineRows.swift:821-840, :844-915; OD-0098: sampled frames of one drawing of three values, both themes
    [Trait("Req", "PR-HYDRAMERGE-103")] // TimelineRows.swift:844-915: the eleven literals and the three formulas, read back from the rendered track
    public void The_track_drawn_at_sampled_values_of_position_done_and_rest_puts_the_dots_and_the_head_where_the_formulas_of_the_source_put_them() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        // (position, done, rest): on the ground; mid-hop over several dots (the arc's peak at a half); the wave half way through the dots with the pop at its top;
        // the pop's descent; the head part way to its resting size, once on the ground and once still in the air; settled.
        var samples = new (double Position, double Done, double Resting)[]
        {
            (0, 0, 0), (0.5, 0, 0), (2.5, 0, 0), (3.2, 0, 0), (3.75, 0, 0), (6, 0.5, 0), (6, 0.5625, 0), (6, 1, 0), (6, 1, 0.5), (5.5, 0.8, 0.4), (6, 1, 1),
        };
        h.Main.OnMain(() =>
        {
            foreach (var (theme, backdrop) in Themes)
            {
                var fill = FillOver(theme, backdrop);
                Rect? headBox = null;
                double? ink12 = null;
                foreach (var (position, done, rest) in samples.Prepend((0, 0, 0)))
                {
                    var body = new HydraMergeTrackBody(position, done, rest, count: 7, hops: true);
                    var host = new Border { Padding = new Thickness(14, 14, 14, 10), Background = new SolidColorBrush(fill), Child = body };
                    BlitzAbRender.Render(host, theme, fill, 120, 60, (window, frame) =>
                    {
                        var because = $"{theme} at position {position}, done {done}, rest {rest}";
                        var box = BlitzAbRender.BoxIn(body, window);
                        // :917-918 the frame is the track's width glided to the resting head size, 20 tall.
                        Assert.Equal(46 + (18 - 46) * rest, box.Width, 0.6); // layout rounds to whole pixels
                        Assert.Equal(20, box.Height, 0.01);
                        // :896-906 the dots fade out with rest and are lit by the head's reach or by the wave.
                        var (headLeft, headTop, headSide, headScale) = ExpectedHead(position, done, rest);
                        for (var i = 0; i < 7; i++)
                        {
                            // A dot the head stands over is the head's pixel, not the dot's (the settled head covers the first three).
                            var dotX = i * 7 + 2.0;
                            var hidden = Math.Abs(dotX - (headLeft + headSide / 2)) < headSide * headScale / 2 + 2 && headTop + headSide / 2 + headSide * headScale / 2 + 1 >= 16;
                            if (hidden) continue;
                            var want = ExpectedDot(theme, fill, Lit(i, 7, position, done), rest);
                            Assert.True(NearAny(DotAt(frame, box, i), want), $"{because}: dot {i} is {DotAt(frame, box, i)}, expected {want[0]} or {want[1]}");
                        }
                        // :907-915 the head: its side from 12 to 18, its place along the track and its lift, scaled about its centre by the pop.
                        var (left, top, side, scale) = ExpectedHead(position, done, rest);
                        var ink = frame.Ink(new Rect(box.Left - 8, box.Top - 12, box.Width + 16, 28), fill) ?? throw new Xunit.Sdk.XunitException($"{because}: no head drawn");
                        var inkWidth = ink.Right - ink.Left + 1;
                        ink12 ??= inkWidth; // the first sample is the head at 12 points, unscaled: the ink of a 12-point mark
                        var wantWidth = ink12.Value * side * scale / 12;
                        Assert.True(Math.Abs(inkWidth - wantWidth) <= 1.6, $"{because}: the head's ink is {inkWidth} wide, expected about {wantWidth}");
                        var wantX = box.Left + left + side / 2;
                        var wantY = box.Top + top + side / 2;
                        // The ink's own centre against the 12-point mark's: the mark need not fill its square symmetrically, so compare to the first sample's offset.
                        headBox ??= new Rect((ink.Left + ink.Right + 1) / 2.0 - wantX, (ink.Top + ink.Bottom + 1) / 2.0 - wantY, 0, 0);
                        Assert.True(Math.Abs((ink.Left + ink.Right + 1) / 2.0 - wantX - headBox.Value.X * side * scale / 12) <= 2.2, $"{because}: the head is centred on x {(ink.Left + ink.Right + 1) / 2.0}, expected {wantX}");
                        Assert.True(Math.Abs((ink.Top + ink.Bottom + 1) / 2.0 - wantY - headBox.Value.Y * side * scale / 12) <= 2.2, $"{because}: the head is centred on y {(ink.Top + ink.Bottom + 1) / 2.0}, expected {wantY}");
                    });
                }
            }
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-151")] // TimelineRows.swift:834-836, :896 hops are off under reduced motion: no arc and no pop in the rendered frame
    [Trait("Req", "PR-HYDRAMERGE-102")] // :823-835 hops = !reduceMotion
    public void Under_reduced_motion_the_head_stays_on_the_ground_at_the_same_size_through_the_hop_and_the_pop_in_the_rendered_frame() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            var (theme, backdrop) = Themes[0];
            var fill = FillOver(theme, backdrop);
            Rect[] Boxes(bool hops) => new[] { (2.5, 0.0), (6.0, 0.5) }.Select(sample =>
            {
                var body = new HydraMergeTrackBody(sample.Item1, sample.Item2, 0, count: 7, hops: hops);
                Rect found = default;
                BlitzAbRender.Render(new Border { Padding = new Thickness(14, 14, 14, 10), Child = body }, theme, fill, 120, 60, (window, frame) =>
                {
                    var box = BlitzAbRender.BoxIn(body, window);
                    var ink = frame.Ink(new Rect(box.Left - 8, box.Top - 12, box.Width + 16, 28), fill)!.Value;
                    found = new Rect(ink.Left - box.Left, ink.Top - box.Top, ink.Right - ink.Left + 1, ink.Bottom - ink.Top + 1);
                });
                return found;
            }).ToArray();
            var hopping = Boxes(true);
            var still = Boxes(false);
            Assert.True(hopping[0].Top <= -3, $"the head at the top of its arc has ink from {hopping[0].Top}"); // lifted by 4 at 2.5
            Assert.True(still[0].Top >= -0.5, $"reduced motion: the head's ink from {still[0].Top}");
            Assert.True(hopping[1].Width > still[1].Width + 1.2, $"the pop: {hopping[1].Width} against {still[1].Width}"); // 1.18 at done 0.5
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-151")] // TimelineRows.swift:912-915: the settled head is pinned to the leading edge of its 18-point frame, inside the pill, a gap before the words
    [Trait("Req", "PR-HYDRAMERGE-103")] // :844-915 settling shrinks the frame from 46 points to 18; the head does not sit half outside the pill
    public void The_settled_head_stands_in_its_18_point_frame_at_the_pills_leading_padding_a_gap_before_the_title_and_never_outside_the_pill() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        foreach (var (theme, backdrop) in Themes)
        {
            HydraMergeRow row = null!;
            AvaloniaWindow window = null!;
            var fill = FillOver(theme, backdrop);
            h.Main.OnMain(() =>
            {
                h.Runtime.IsHydraMerging = true;
                row = new HydraMergeRow(h.Runtime, new HydraMergePhase.Merging());
                window = BlitzAbRender.Open(new Border { Padding = new Thickness(24), Child = row }, theme, backdrop, 640, 90);
            });
            h.Main.OnMain(() =>
            {
                h.Runtime.IsHydraMerging = false;
                row.Update(new HydraMergePhase.Outcome(new UserMessage { Text = "The merge stopped." }));
            });
            Advance(h.Main, frames, 1500);
            h.Main.OnMain(() =>
            {
                var frame = BlitzAbRender.Capture(window);
                var pill = BlitzAbRender.BoxIn(PillOf(row), window);
                var track = BlitzAbRender.BoxIn(Assert.Single(Mounted<HydraMergeTrack>(row)), window);
                Assert.Equal(18, track.Width, 0.01); // the frame shrank from 46 to the resting head
                Assert.Equal(pill.Left + 12, track.Left, 0.5);
                var ink = frame.Ink(new Rect(pill.Left + 4, pill.Top + 8, 34, pill.Height - 16), fill) ?? throw new Xunit.Sdk.XunitException("no head drawn");
                Assert.True(ink.Left >= pill.Left + 12 - 1 && ink.Right <= pill.Left + 12 + 18 + 1, $"{theme}: the head's ink {ink.Left}..{ink.Right} against a frame {pill.Left + 12}..{pill.Left + 30}");
                var title = Mounted<TextBlock>(row).First(block => block.Text == "The merge stopped"); // the note's first line, less its stop (:1140-1161 title)
                Assert.True(Left(title, window) >= track.Right + 8 - 2.5, $"{theme}: the title starts at {Left(title, window)}, the frame ends at {track.Right}");
                BlitzAbRender.Shut(window);
            });
        }
    });

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-102")] // TimelineRows.swift:838-839: a step change animates over .smooth(duration: 0.5), a mode change over .smooth(duration: 0.45)
    [Trait("Req", "PR-CHAT-151")] // :821-840 one interpolated drawing: the three values arrive together
    public void A_step_change_takes_half_a_second_and_a_mode_change_forty_five_hundredths_and_the_three_values_arrive_together() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        double Elapsed(Func<HydraMergeTrackBody, bool> arrived, Action<HydraMergeTrack> change, int step, HydraMergeTrack.Mode mode)
        {
            HydraMergeTrack track = null!;
            AvaloniaWindow window = null!;
            HydraMergeTrackBody Body() => LogicalQuery.SelfAndDescendants<HydraMergeTrackBody>(track).Single();
            runtime.OnMain(() =>
            {
                track = new HydraMergeTrack(step, 7, mode);
                window = Show(track);
                change(track);
            });
            frames.Advance(TimeSpan.Zero);
            var milliseconds = 0;
            var moved = false;
            while (!runtime.OnMain(() => arrived(Body())) && milliseconds < 2000)
            {
                runtime.AdvanceMilliseconds(10);
                frames.Advance(TimeSpan.FromMilliseconds(10));
                milliseconds += 10;
                runtime.OnMain(() => { var (position, (done, resting)) = Body().AnimatableData; moved |= position > 0.01 || done > 0.01 || resting > 0.01; });
            }
            Assert.True(moved);
            runtime.OnMain(() => Close(window));
            return milliseconds;
        }
        // 0 -> 5 while running: only the position moves, over 0.5 s.
        var stepTime = Elapsed(body => body.AnimatableData.Position >= 5 - 1e-6, track => track.Update(5, 7, HydraMergeTrack.Mode.Running), 0, HydraMergeTrack.Mode.Running);
        Assert.InRange(stepTime, 500, 520);
        // Running -> resting on the last dot: the mode changes, over 0.45 s, and the done and rest values reach 1 together.
        var modeTime = Elapsed(body => body.AnimatableData.Outcome.Resting >= 1 - 1e-6, track => track.Update(6, 7, HydraMergeTrack.Mode.Resting), 6, HydraMergeTrack.Mode.Running);
        Assert.InRange(modeTime, 450, 470);
        var doneTime = Elapsed(body => body.AnimatableData.Outcome.Done >= 1 - 1e-6, track => track.Update(6, 7, HydraMergeTrack.Mode.Done), 6, HydraMergeTrack.Mode.Running);
        Assert.InRange(doneTime, 450, 470);
        // A step and a mode change at once take the mode's 0.45 s (:838-839 the change that moved is the mode).
        var bothTime = Elapsed(body => body.AnimatableData.Position >= 6 - 1e-6, track => track.Update(6, 7, HydraMergeTrack.Mode.Resting), 0, HydraMergeTrack.Mode.Running);
        Assert.InRange(bothTime, 450, 470);
    });

    // ---- PR-CHAT-150, PR-HYDRAMERGE-107: the pill morphing from the merge to its note, frame by frame ----

    [Fact]
    [Trait("Req", "PR-CHAT-150")] // TimelineRows.swift:937-1034, :972-1010: the capsule glides from one width to the other while the contents crossfade, rendered
    [Trait("Req", "PR-HYDRAMERGE-107")] // TimelineRows.swift:972-1010, :1017: only the still copy sizes the slot; never held wide for the fade
    public void The_capsule_glides_from_the_merging_width_to_the_notes_while_the_contents_crossfade_and_it_is_never_wider_than_both() => headless.OnUiThread(() =>
    {
        using var noNetwork = new NoLinkNetwork("gitlab.com"); // R-TEST-10: the note holds a link
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        using var browser = Marrow.Services.Port.Workspace.Open.Override(_ => true);
        foreach (var (theme, backdrop) in Themes)
        {
            HydraMergeRow row = null!;
            AvaloniaWindow window = null!;
            var fill = FillOver(theme, backdrop);
            var widths = new List<double>();
            h.Main.OnMain(() =>
            {
                h.Runtime.IsHydraMerging = true;
                h.Runtime.HydraMergeStep = HydraMergeStage.Opening;
                h.Runtime.HydraMergeStage = HydraMergeStage.Opening.Words;
                row = new HydraMergeRow(h.Runtime, new HydraMergePhase.Merging());
                window = BlitzAbRender.Open(new Border { Padding = new Thickness(24), Child = row }, theme, backdrop, 700, 90);
            });
            Advance(h.Main, frames, 800);
            double before = 0;
            h.Main.OnMain(() =>
            {
                Settle(window);
                before = BlitzAbRender.BoxIn(PillOf(row), window).Width;
                h.Runtime.IsHydraMerging = false;
                h.Runtime.HydraMergeStep = null;
                row.Update(new HydraMergePhase.Outcome(new UserMessage { Text = "Hydra merged the team's work.\nhttps://gitlab.com/g/p/-/merge_requests/7\nThree heads landed." }));
            });
            var samples = new List<(double Width, double Old, double New, int Fill, int Beyond)>();
            for (var ms = 0; ms <= 400; ms += 50)
            {
                if (ms > 0) Advance(h.Main, frames, 50);
                h.Main.OnMain(() =>
                {
                    var frame = BlitzAbRender.Capture(window);
                    var box = BlitzAbRender.BoxIn(PillOf(row), window);
                    double Effective(string text) => Mounted<TextBlock>(row).Where(block => block.Text == text).Select(block => block.GetSelfAndVisualAncestors().Aggregate(1.0, (product, visual) => product * visual.Opacity)).DefaultIfEmpty(0).Max();
                    var yMid = (int)(box.Top + box.Height / 2);
                    samples.Add((box.Width, Effective("Opening the merge request…"), Effective("Hydra merged the team's work"),
                        BlitzAbFrame.Distance(frame.At((int)box.Right - 4, yMid), fill), BlitzAbFrame.Distance(frame.At((int)box.Right + 4, yMid), backdrop)));
                });
            }
            h.Main.OnMain(() =>
            {
                var after = samples[^1].Width;
                Assert.NotEqual(before, after, 6);
                var low = Math.Min(before, after) - 0.75;
                var high = Math.Max(before, after) + 0.75;
                for (var i = 0; i < samples.Count; i++)
                {
                    var s = samples[i];
                    // :972-1010 the capsule is as wide as the slot's still copy alone: between the two widths at every frame, so never held wide for the length of the fade.
                    Assert.InRange(s.Width, low, high);
                    // The capsule drawn is the one measured: filled to its right edge and nothing beyond it.
                    Assert.True(s.Fill <= 8, $"{theme} at {i * 50} ms: the fill is missing {s.Fill} inside the capsule's right edge");
                    Assert.True(s.Beyond <= 8, $"{theme} at {i * 50} ms: the capsule is drawn past its width");
                    if (i > 0) Assert.True((s.Width - samples[i - 1].Width) * (after - before) >= -0.75, $"{theme}: the width went back at {i * 50} ms");
                }
                Assert.Equal(before, samples[0].Width, 1.0); // the first frame has not moved yet
                Assert.Contains(samples, s => Math.Abs(s.Width - before) > 2 && Math.Abs(s.Width - after) > 2); // it glides: some frame is part way
                // The old words fade out and the new ones fade in, together at some frame; the old ones are gone and the new ones whole at the end.
                Assert.Contains(samples, s => s.Old is > 0.02 and < 0.98 && s.New is > 0.02 and < 0.98);
                Assert.True(samples[^1].New > 0.98 && samples[^1].Old < 0.02, $"{theme}: at the end the words are {samples[^1].Old} and the title {samples[^1].New}");
                Assert.True(samples[0].Old > 0.98 && samples[0].New < 0.5, $"{theme}: at the start the words are {samples[0].Old}");
                BlitzAbRender.Shut(window);
            });
        }
    });

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-101")] // TimelineRows.swift:1056-1079: a new stage is a new text; the old rises and fades out as the new rises from below and fades in, clipped to the slot
    public void A_new_stage_lifts_the_old_words_out_and_brings_the_new_ones_in_from_below_clipped_to_the_slot_and_the_capsule_follows_the_new_width() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        foreach (var (theme, backdrop) in Themes)
        {
            HydraMergeRow row = null!;
            AvaloniaWindow window = null!;
            var fill = FillOver(theme, backdrop);
            h.Main.OnMain(() =>
            {
                h.Runtime.IsHydraMerging = true;
                h.Runtime.HydraMergeStep = HydraMergeStage.Merging;
                h.Runtime.HydraMergeStage = HydraMergeStage.Merging.Words;
                row = new HydraMergeRow(h.Runtime, new HydraMergePhase.Merging());
                window = BlitzAbRender.Open(new Border { Padding = new Thickness(24), Child = row }, theme, backdrop, 640, 90);
            });
            Advance(h.Main, frames, 800);
            (int Left, int Top, int Right, int Bottom) Words(BlitzAbFrame frame)
            {
                var pill = BlitzAbRender.BoxIn(PillOf(row), window);
                var track = BlitzAbRender.BoxIn(Assert.Single(Mounted<HydraMergeTrack>(row)), window);
                return frame.Ink(new Rect(track.Right + 4, pill.Top + 3, pill.Right - track.Right - 20, pill.Height - 6), fill) ?? throw new Xunit.Sdk.XunitException("no words drawn");
            }
            (int Left, int Top, int Right, int Bottom) rest = default;
            h.Main.OnMain(() => rest = Words(BlitzAbRender.Capture(window)));
            h.Main.OnMain(() => { h.Runtime.HydraMergeStep = HydraMergeStage.Syncing; h.Runtime.HydraMergeStage = HydraMergeStage.Syncing.Words; });
            h.Main.OnMain(() => Settle(window));
            Advance(h.Main, frames, 100);
            h.Main.OnMain(() =>
            {
                var middle = Words(BlitzAbRender.Capture(window));
                // :1071-1076 the old words move up by up to 5 and the new ones come from 5 below: the ink spans above and below the words at rest.
                Assert.True(middle.Top <= rest.Top - 1, $"{theme}: the old words lift to {middle.Top}, at rest they start at {rest.Top}");
                Assert.True(middle.Bottom >= rest.Bottom + 1, $"{theme}: the new words come from {middle.Bottom}, at rest they end at {rest.Bottom}");
                // :1057-1079 clipped to the slot (6 points of room above and below the words) and so never out of the capsule.
                var pill = BlitzAbRender.BoxIn(PillOf(row), window);
                Assert.True(middle.Top >= pill.Top && middle.Bottom <= pill.Bottom, $"{theme}: ink at {middle.Top}..{middle.Bottom} leaves the capsule {pill.Top}..{pill.Bottom}");
            });
            Advance(h.Main, frames, 900);
            h.Main.OnMain(() =>
            {
                var done = Words(BlitzAbRender.Capture(window));
                Assert.Equal(rest.Top, done.Top, 1.5); // settled on the same line again
                Assert.Equal(rest.Bottom, done.Bottom, 1.5);
                Assert.All(Texts(row), text => Assert.Equal("Bringing the checkout up to date…", text));
                BlitzAbRender.Shut(window);
            });
        }
    });
}
