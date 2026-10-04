using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), :785-1162 the merge pill: its track, the stage it is at,
// and the note that takes its place; with Core/Models/Hydra.swift:509-529 for the stage words.
[Collection("Headless")]
public sealed class TimelineRowsFinMergeRowTests(HeadlessSession headless)
{
    private static readonly (string Words, int Step)[] Stages =
    [
        ("Gathering the team's files", 0), ("Writing the commit", 1), ("Writing the commit message", 2), ("Pushing the branch", 3),
        ("Opening the merge request", 4), ("Merging", 5), ("Bringing the checkout up to date", 6),
    ];

    private static Border PillOf(Control row) => Mounted<Border>(row).First(border => border.Padding == new Thickness(12, 8, 14, 8));

    private static UserMessage Note(string text) => new() { Text = text };

    private static double Top(Control control, Visual root) => control.TranslatePoint(default, root)!.Value.Y;

    [Fact]
    [Trait("Req", "UI-CHAT-053")] // TimelineRows.swift:926-1019, :1044-1094 the merging state
    [Trait("Req", "UI-HYDRAMERGE-002")] // TimelineRows.swift:937-952, :1012-1017, :1056-1062
    [Trait("Req", "PR-CHAT-150")] // TimelineRows.swift:956, :1020-1027, :1056-1062: the current stage's words, shimmering (the visible words sit in the shimmer's own panel beside its masked band layer)
    public void The_merging_pill_is_a_tinted_pill_with_the_track_and_the_stage_words_with_an_ellipsis_and_no_clock() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        HydraMergeRow row = null!;
        AvaloniaWindow window = null!;
        h.Main.OnMain(() =>
        {
            h.Runtime.IsHydraMerging = true;
            h.Runtime.HydraMergeStep = HydraMergeStage.Pushing;
            h.Runtime.HydraMergeStage = "Pushing the branch";
            row = new HydraMergeRow(h.Runtime, new HydraMergePhase.Merging());
            var host = new Border { Width = 500, Child = row };
            window = Show(new StackPanel { Children = { host } });
            try
            {
                var pill = PillOf(row);
                // :1012-1017 12 leading, 14 trailing, 8 vertical, radius 18, .tint at 0.1; :1016-1017 lead-aligned with 96 clear.
                Assert.Equal(new CornerRadius(18), pill.CornerRadius);
                // :1015 `.background(.tint.opacity(0.1), in: RoundedRectangle(cornerRadius: TimelineMetrics.pillRadius, style: .continuous))` (ED-181
                // closed): one line is 33 pt tall, less than the 55 pt two run-ins of an 18 pt corner need, so the corner is the circular arc.
                Assert.True(pill.Bounds.Height < 55, $"the merge pill is {pill.Bounds.Height} pt tall");
                ContinuousCornerChecks.Drawn(pill, expectApple: false);
                Paints(pill.Background, ChromeTokens.Accent.Opacity(0.1), pill, "pill fill");
                Assert.Equal(new Thickness(0, 0, 96, 0), row.Padding);
                Near("nMergeRow:1", Left(host, window), Left(pill, window));
                // :1003-1007 the slot is padded 6 vertically and 2 horizontally, clipped, then given back (-2, -6).
                Assert.Contains(Mounted<Control>(row), control => control.ClipToBounds && control.Margin == new Thickness(-2, -6));
                // :956 the stage words and an ellipsis; :1056-1062 callout medium in Chrome.primaryText at 0.9; no elapsed clock.
                var words = Mounted<TextBlock>(row).Where(text => text.Text == "Pushing the branch…").ToList();
                Assert.NotEmpty(words);
                var shown = words.First(text => text.Foreground is ISolidColorBrush brush && brush.Color == Resolve(ChromeTokens.PrimaryText.Opacity(0.9), text));
                Face(shown, 12, FontWeight.Medium, ".chat(.callout, weight: .medium)");
                Assert.All(Texts(row).Where(text => text.Length != 0), text => Assert.Equal("Pushing the branch…", text));
                Assert.Equal("Merging the team's work: Pushing the branch…", AutomationProperties.GetName(pill));
                // :965 the head stands on the dot of the stage under way: step 3 of 7 dots.
                var track = Assert.Single(Mounted<HydraMergeTrack>(row));
                Assert.Equal(3, Assert.Single(Mounted<HydraMergeTrackBody>(track)).AnimatableData.Position);
                // :959 HStack(spacing: 8): the track and the stage words share one centre line.
                Near("stage words after the track (spacing 8, slot padded 2 then given back)", 8, Left(shown, window) - (Left(track, window) + track.Bounds.Width));
                OnOneCentreLine(window, track, shown);
                // :1056-1062 the running state's words shimmer: the visible words sit in the shimmer's own panel, beside its masked band layer.
                var shimmer = Assert.IsType<Panel>(shown.Parent);
                Assert.Contains(shimmer.Children, child => child is Panel { OpacityMask: not null });
                // :1044-1079 the still copy that sizes the slot is set in the same callout medium as the words that show.
                Assert.All(words, text => Near("still copy and visible words alike", shown.Bounds.Width, text.Bounds.Width));
                Assert.All(words, text => Assert.Equal(FontWeight.Medium, text.FontWeight));
                // :967-970 a tap on the track toggles the report only once an outcome exists: while merging, no effect.
                Click(window, track);
                Assert.DoesNotContain(presenter.Calls, call => call.Presented);
                // :956 with no stage reported the words are exactly Merging…
                h.Runtime.HydraMergeStage = null;
            }
            catch { Close(window); throw; }
        });
        h.Main.OnMain(() =>
        {
            try
            {
                Settle(window);
                Assert.Contains("Merging…", Texts(row));
                // The tap while merging left nothing behind: when the outcome arrives its message stays closed.
                row.Update(new HydraMergePhase.Outcome(Note("Held back.\nwhy")));
            }
            catch { Close(window); throw; }
        });
        h.Main.OnMain(() =>
        {
            try
            {
                Settle(window);
                Assert.Contains("Held back", Texts(row));
                Assert.DoesNotContain(presenter.Calls, call => call.Presented);
            }
            finally { Close(window); }
        });
    });

    [Theory]
    [InlineData(0, "Gathering the team's files")]
    [InlineData(1, "Writing the commit")]
    [InlineData(2, "Writing the commit message")]
    [InlineData(3, "Pushing the branch")]
    [InlineData(4, "Opening the merge request")]
    [InlineData(5, "Merging")]
    [InlineData(6, "Bringing the checkout up to date")]
    [Trait("Req", "UI-CHAT-053")] // Core/Models/Hydra.swift:509-529 the seven stage strings, each shown with a trailing ellipsis
    public void Each_of_the_seven_stages_reads_its_own_words_with_one_trailing_ellipsis(int step, string words) => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        h.Main.OnMain(() =>
        {
            h.Runtime.IsHydraMerging = true;
            h.Runtime.HydraMergeStep = HydraMergeStageExtensions.AllCases[step];
            h.Runtime.HydraMergeStage = HydraMergeStageExtensions.AllCases[step].Words;
            var row = new HydraMergeRow(h.Runtime, new HydraMergePhase.Merging());
            var window = Show(row);
            try
            {
                Assert.Equal(7, HydraMergeStageExtensions.AllCases.Count);
                Assert.Contains(words + "…", Texts(row));
                Assert.Equal(step, Assert.Single(Mounted<HydraMergeTrackBody>(row)).AnimatableData.Position);
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-004")] // TimelineRows.swift:821-841, :844-915, :963-971 the progress track
    public void The_track_is_seven_four_point_dots_three_apart_with_a_head_that_arcs_between_them_lights_them_and_settles() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var body = new HydraMergeTrackBody(position: 2, done: 0, rest: 0, count: 7, hops: true);
            var window = Show(new StackPanel { Children = { body } });
            try
            {
                // :861-868 seven dots of 4 points with 3-point gaps: 46 wide, 20 high; the dots sit at the bottom of the frame.
                Assert.Equal(46, body.Width);
                Assert.Equal(20, body.Height);
                var dots = Mounted<Panel>(body).Where(panel => panel.Width == 4 && panel.Height == 4).ToList();
                Assert.Equal(7, dots.Count);
                Assert.Equal([0.0, 7, 14, 21, 28, 35, 42], dots.Select(Canvas.GetLeft));
                Assert.All(dots, dot => Assert.Equal(16, Canvas.GetTop(dot)));
                double Lit(Panel dot) => ((Control)dot.Children[1]).Opacity;
                // :878-882 a dot is lit once the head has reached it (position + 0.35 >= i): dots 0 to 2 at position 2.
                Assert.Equal([1.0, 1, 1, 0, 0, 0, 0], dots.Select(Lit));
                Paints(((Avalonia.Controls.Shapes.Ellipse)dots[0].Children[0]).Fill, ChromeTokens.Overlay(0.28), dots[0], "unlit dot");
                Paints(((Avalonia.Controls.Shapes.Ellipse)dots[0].Children[1]).Fill, ChromeTokens.Accent, dots[0], "lit dot");
                // :872-889 the head, 12 points, on dot 2's centre: x = (2 * 7 + 2) - 6 = 10.
                var head = Assert.Single(Mounted<HydraMarkImage>(body));
                Assert.Equal(12, head.Width);
                Assert.Equal(10, Canvas.GetLeft(head), 9);
                Assert.Equal(0, Canvas.GetTop(head), 9);
                Paints(Assert.Single(Mounted<SwiftImage>(head)).Foreground, ChromeTokens.SecondaryText, head, "head");
                // :887 half way between two dots it is at the top of its 4-point arc; 0.35 before a dot it lights it.
                body.AnimatableData = (2.5, (0, 0));
                Assert.Equal(-4, Canvas.GetTop(head), 9);
                Assert.Equal(2.5 * 7 + 2 - 6, Canvas.GetLeft(head), 9);
                body.AnimatableData = (2.64, (0, 0));
                Assert.Equal([1.0, 1, 1, 0, 0, 0, 0], dots.Select(Lit));
                body.AnimatableData = (2.66, (0, 0));
                Assert.Equal([1.0, 1, 1, 1, 0, 0, 0], dots.Select(Lit));
                body.AnimatableData = (2.7, (0, 0));
                Assert.Equal([1.0, 1, 1, 1, 0, 0, 0], dots.Select(Lit));
                // :880, :892 the outcome: the wave lights the dots left to right as done rises, and the head pops once (1.18).
                body.AnimatableData = (6, (0.5, 0));
                Assert.Equal(1.18, Assert.IsType<ScaleTransform>(head.RenderTransform).ScaleX, 9);
                body.AnimatableData = (0, (0.5625, 0));
                Assert.Equal([1.0, 1, 1, 1, 0.5, 0, 0], dots.Select(Lit));
                // :889-914 resting: the dots fade out, the head grows to 18 and settles on the leading edge, the frame shrinks to it.
                body.AnimatableData = (6, (1, 1));
                Assert.Equal(18, head.Width);
                Assert.Equal(0, Canvas.GetLeft(head), 9);
                Assert.Equal(1, Canvas.GetTop(head), 9);
                Assert.Equal(18, body.Width);
                Assert.All(dots, dot => Assert.Equal(0, ((Control)dot.Parent!).Opacity));
                Assert.Equal(1, Assert.IsType<ScaleTransform>(head.RenderTransform).ScaleX, 9);
                // :836 reduce motion: the hops and the pop are off (arc 0, pop 1).
                var still = new HydraMergeTrackBody(position: 2.5, done: 0.5, rest: 0, count: 7, hops: false);
                var stillWindow = Show(new StackPanel { Children = { still } });
                var stillHead = Assert.Single(Mounted<HydraMarkImage>(still));
                Assert.Equal(0, Canvas.GetTop(stillHead), 9);
                Assert.Equal(1, Assert.IsType<ScaleTransform>(stillHead.RenderTransform).ScaleX, 9);
                Close(stillWindow);
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-004")] // TimelineRows.swift:838-839 a step animates over 0.5 s, the mode over 0.45 s: each alone, at its own boundary
    public void A_step_alone_glides_over_half_a_second_and_a_mode_alone_settles_over_0_45_seconds() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        HydraMergeTrack track = null!;
        AvaloniaWindow window = null!;
        HydraMergeTrackBody Body() => Assert.Single(Mounted<HydraMergeTrackBody>(track));
        void Frames(double milliseconds)
        {
            frames.Advance(TimeSpan.Zero);
            runtime.AdvanceMilliseconds(milliseconds);
            frames.Advance(TimeSpan.FromMilliseconds(milliseconds));
        }
        // The step alone (:838 .animation(.smooth(duration: 0.5), value: step)): mode unchanged, so only the 0.5 s animation fires.
        runtime.OnMain(() =>
        {
            track = new HydraMergeTrack(step: 0, count: 7, mode: HydraMergeTrack.Mode.Running);
            window = Show(new StackPanel { Children = { track } });
            track.Update(step: 3, count: 7, mode: HydraMergeTrack.Mode.Running);
        });
        Frames(460); // past 0.45 s: a 0.45 s step animation would be over, the 0.5 s one is not
        runtime.OnMain(() => Assert.InRange(Body().AnimatableData.Position, 0.01, 2.999));
        Frames(30); // 490 ms
        runtime.OnMain(() => Assert.InRange(Body().AnimatableData.Position, 0.01, 2.999));
        Frames(20); // 510 ms
        runtime.OnMain(() =>
        {
            Assert.Equal(3, Body().AnimatableData.Position);
            Close(window);
        });
        // The mode alone (:839 .animation(.smooth(duration: 0.45), value: mode)): the flourish at the last dot, resting to done.
        runtime.OnMain(() =>
        {
            track = new HydraMergeTrack(step: 6, count: 7, mode: HydraMergeTrack.Mode.Resting);
            window = Show(new StackPanel { Children = { track } });
            Assert.Equal((6.0, (1.0, 1.0)), Body().AnimatableData);
            track.Update(step: 6, count: 7, mode: HydraMergeTrack.Mode.Done);
        });
        Frames(430);
        runtime.OnMain(() => Assert.InRange(Body().AnimatableData.Outcome.Resting, 0.0001, 0.9999)); // still settling at 430 ms
        Frames(25); // 455 ms: a 0.5 s animation would still be moving
        runtime.OnMain(() =>
        {
            try { Assert.Equal((6.0, (1.0, 0.0)), Body().AnimatableData); }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ED-260: when the step and the mode change in one pass (:962-964) SwiftUI applies the inner .animation (0.5 s); the record's own clauses are proven alone above
    public void A_step_and_a_mode_changing_together_take_the_inner_half_second_animation() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        HydraMergeTrack track = null!;
        AvaloniaWindow window = null!;
        HydraMergeTrackBody Body() => Assert.Single(Mounted<HydraMergeTrackBody>(track));
        void Frames(double milliseconds)
        {
            frames.Advance(TimeSpan.Zero);
            runtime.AdvanceMilliseconds(milliseconds);
            frames.Advance(TimeSpan.FromMilliseconds(milliseconds));
        }
        runtime.OnMain(() =>
        {
            track = new HydraMergeTrack(step: 3, count: 7, mode: HydraMergeTrack.Mode.Running);
            window = Show(new StackPanel { Children = { track } });
            track.Update(step: 6, count: 7, mode: HydraMergeTrack.Mode.Resting);
        });
        Frames(460);
        runtime.OnMain(() =>
        {
            Assert.InRange(Body().AnimatableData.Position, 3.0001, 5.9999);
            Assert.InRange(Body().AnimatableData.Outcome.Resting, 0.0001, 0.9999);
        });
        Frames(50);
        runtime.OnMain(() =>
        {
            try { Assert.Equal((6.0, (1.0, 1.0)), Body().AnimatableData); }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // the combined step and mode glide, drawn values and the accessibility hiding (:971); the durations are proven alone above
    public void A_new_step_glides_and_the_mode_change_settles_and_the_track_is_hidden_from_accessibility() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        HydraMergeTrack track = null!;
        AvaloniaWindow window = null!;
        HydraMergeTrackBody Body() => Assert.Single(Mounted<HydraMergeTrackBody>(track));
        void Frames(double milliseconds)
        {
            frames.Advance(TimeSpan.Zero);
            runtime.AdvanceMilliseconds(milliseconds);
            frames.Advance(TimeSpan.FromMilliseconds(milliseconds));
        }
        runtime.OnMain(() =>
        {
            track = new HydraMergeTrack(step: 0, count: 7, mode: HydraMergeTrack.Mode.Running);
            window = Show(new StackPanel { Children = { track } });
            Assert.Equal((0.0, (0.0, 0.0)), Body().AnimatableData);
            track.Update(step: 3, count: 7, mode: HydraMergeTrack.Mode.Running);
        });
        Frames(450);
        runtime.OnMain(() => Assert.InRange(Body().AnimatableData.Position, 0.01, 2.999)); // still moving at 450 ms
        Frames(100);
        runtime.OnMain(() =>
        {
            Assert.Equal(3, Body().AnimatableData.Position); // there by 0.5 s
            track.Update(step: 6, count: 7, mode: HydraMergeTrack.Mode.Resting);
        });
        Frames(400);
        runtime.OnMain(() => Assert.True(Body().AnimatableData.Outcome.Resting < 1, "the mode change finished early"));
        Frames(100);
        runtime.OnMain(() =>
        {
            try
            {
                Assert.Equal((6.0, (1.0, 1.0)), Body().AnimatableData);
                // :971 hidden from accessibility.
                Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(track));
            }
            finally { Close(window); }
        });
    });
}
