using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift and ChatBadge.swift (B3 52bc792). The corner clause of the timeline's pills, cards and
// plates, re-proved against the continuous corner that ED-181 (closed) and ED-275 built: SwiftShapes decides per shape whether it is Apple's
// corner (2 * 1.528665 * r <= min(w, h)) or the circular arc, and these tests read that decision off the mounted element.
//
// Three regimes (ED-275 (2)), told apart here by the height the element really has:
//  - CAPSULE (h <= 2 r): the radius is held to half the height and the ends are circular; the reference video backs circular ends for
//    capsules and for radii from half the height up (SPEC.md 2.3). Every single-line pill (glyph 18 + 8 + 8 = 34 pt) is here.
//  - APPLE (h >= 3.057 r): the corner is Apple's three-cubic curve, the clip of the Border follows it (ContinuousCornerChecks.Drawn
//    probes the sliver between the curve and the circle).
//  - BAND (h / 3.057 < r < h / 2): settled by OD-0102 (owner, 2026-10-04, "corner-band: A"): Apple's corner at the largest radius that fits,
//    ContinuousCorner.LargestRadius = h / 3.057, instead of a circular arc. The Edited-files card (40 pt, r 18), the one-step steps card (43 pt),
//    a notice two lines tall (46 pt, measured mounted at 380 pt wide) and the delegation card of one head (54 pt) sit here; the tests at the end read it off the mounted element.
// OD-0098: where no video frame shows the element the pinned Swift is the proof; each test names the Swift lines it reads.
[Collection("Headless")]
public sealed class ContinuousCornerReproofTests(HeadlessSession headless)
{
    // Apple's corner (ED-275 (1)), typed from the decision record and not read from ContinuousCorner: in units of the radius, u along the edge
    // from the vertex and v into the shape, each cubic as (control 1, control 2, end); the curve starts at (1.528665, 0) and C3 is C1 mirrored.
    private static readonly (double U, double V)[][] AppleCubics =
    [
        [(1.08849296, 0), (0.86840694, 0), (0.63149379, 0.07491139)],
        [(0.37282383, 0.16905956), (0.16905956, 0.37282383), (0.07491139, 0.63149379)],
        [(0, 0.86840694), (0, 1.08849296), (0, 1.528665)],
    ];

    // The corner's own mapping of (u, v) to the shape, clockwise from the top-left, where the first cubic starts on the first edge walked.
    private static Point Map(int corner, double u, double v, double r, Size size) => corner switch
    {
        0 => new Point(v * r, u * r),
        1 => new Point(size.Width - u * r, v * r),
        2 => new Point(size.Width - v * r, size.Height - u * r),
        _ => new Point(u * r, size.Height - v * r),
    };

    /// <summary>The element is a rounded rectangle of radius 18 whose corners are CIRCULAR because it is no taller than 2 r (a capsule).</summary>
    private static void AssertCapsule(Border shape, string name, double radius = 18)
    {
        Assert.True(SwiftShapes.GetCornerStyle(shape) == RoundedCornerStyle.Continuous, $"{name}: the source says .continuous");
        Assert.Equal(new CornerRadius(radius), SwiftShapes.StatedCornerRadius(shape));
        var height = shape.Bounds.Height;
        Assert.True(height > 0 && height <= 2 * radius + 1e-6, $"{name}: {height} pt tall is not a capsule of radius {radius} (that would be the band or Apple's corner)");
        Assert.False(ContinuousCornerChecks.Drawn(shape, expectApple: false), $"{name}: a capsule has no Apple corner");
        var outline = SwiftShapes.Outline(new Rect(shape.Bounds.Size), SwiftShapes.StatedCornerRadius(shape), RoundedCornerStyle.Continuous);
        Assert.All(outline.Corners, kind => Assert.Equal(CornerKind.Arc, kind));
        Assert.All(outline.Segments.Where(segment => segment.Verb == PathVerb.Arc), arc => Assert.Equal(height / 2, arc.ArcRadius, 1e-9));
    }

    /// <summary>
    /// OD-0102: the element is in ED-275 (2)'s band (36 &lt; height &lt; 55.03 at radius 18) and is drawn through Apple's corner at the largest radius that fits,
    /// <c>height / (2 * 1.528665)</c>: every corner is Apple's at that radius, its control points are the ones in ED-275 (1) scaled to it, the clip of the Border
    /// is that outline, and the run-ins of the two ends of the short side meet at its middle.
    /// </summary>
    private static void AssertBand(Border shape, string name, double radius = 18)
    {
        Assert.True(SwiftShapes.GetCornerStyle(shape) == RoundedCornerStyle.Continuous, $"{name}: the source says .continuous");
        Assert.Equal(new CornerRadius(radius), SwiftShapes.StatedCornerRadius(shape));
        var size = shape.Bounds.Size;
        Assert.True(size.Height > 2 * radius + 1e-6 && size.Height < 2 * 1.528665 * radius, $"{name}: {size.Height} pt is not in the band ({2 * radius}, {2 * 1.528665 * radius:F2})");
        var drawn = size.Height / (2 * 1.528665);
        Assert.Equal(drawn, ContinuousCorner.LargestRadius(size), 9);
        Assert.True(ContinuousCornerChecks.Drawn(shape, expectApple: true), $"{name}: no Apple corner in the band (OD-0102)");
        var outline = SwiftShapes.Outline(new Rect(size), SwiftShapes.StatedCornerRadius(shape), RoundedCornerStyle.Continuous);
        Assert.All(outline.Corners, kind => Assert.Equal(CornerKind.Continuous, kind));
        Assert.All(Enumerable.Range(0, 4), index => Assert.Equal(drawn, outline.DrawnRadius(index), 9));
        Assert.DoesNotContain(outline.Segments, segment => segment.Verb == PathVerb.Arc);
        var cubics = outline.Segments.Where(segment => segment.Verb == PathVerb.Cubic).ToArray();
        Assert.Equal(12, cubics.Length);
        var order = new[] { 1, 2, 3, 0 };
        for (var c = 0; c < 4; c++)
            for (var k = 0; k < 3; k++)
            {
                var segment = cubics[c * 3 + k];
                var (c1, c2, end) = (AppleCubics[k][0], AppleCubics[k][1], AppleCubics[k][2]);
                AssertNear(Map(order[c], c1.U, c1.V, drawn, size), segment.Control1, $"{name}: corner {order[c]} cubic {k + 1} control 1");
                AssertNear(Map(order[c], c2.U, c2.V, drawn, size), segment.Control2, $"{name}: corner {order[c]} cubic {k + 1} control 2");
                AssertNear(Map(order[c], end.U, end.V, drawn, size), segment.End, $"{name}: corner {order[c]} cubic {k + 1} end");
            }
        // The clip of the mounted Border is the shared cached outline at the drawn radius, so the fill and a child are cut to the same curve.
        Assert.Same(ContinuousCornerChecks.ExpectedClip(shape, size, SwiftShapes.StatedCornerRadius(shape), false), shape.Clip);
        // What the Border FILLS is that curve too (the review of this unit: a clip can only remove fill, and Apple's corner at the largest radius lies outside the circle
        // of the stated radius, so a Border that kept the circle of 18 under this clip drew the circle of 18). With no ring the clip alone makes the corner and the Border's own
        // corner is square; with a ring it is the circle of the drawn radius. Never the stated 18. The pixels are REVIEW_the_four_band_elements_render_... below.
        var hasRing = shape.BorderBrush is not null && shape.BorderThickness != default;
        Assert.Equal(hasRing ? new CornerRadius(drawn) : default, shape.CornerRadius);
        // The corner starts half the short side from the vertex along the long side: the run-in is Extent * drawn = height / 2.
        AssertNear(new Point(size.Height / 2, 0), outline.Segments[0].End, $"{name}: the top-left corner's run-in along the top edge ends at height / 2");
    }

    /// <summary>The element is a rounded rectangle of this radius drawn through Apple's corner: its outline's control points are the ones in ED-275 (1), and its clip is that outline.</summary>
    private static void AssertApple(Border shape, string name, double radius)
    {
        Assert.True(SwiftShapes.GetCornerStyle(shape) == RoundedCornerStyle.Continuous, $"{name}: the source says .continuous");
        Assert.Equal(new CornerRadius(radius), SwiftShapes.StatedCornerRadius(shape));
        var size = shape.Bounds.Size;
        Assert.True(size.Height >= 2 * 1.528665 * radius && size.Width >= 2 * 1.528665 * radius, $"{name}: {size} is not tall enough for Apple's corner of radius {radius}");
        Assert.True(ContinuousCornerChecks.Drawn(shape, expectApple: true), $"{name}: no Apple corner");
        var outline = SwiftShapes.Outline(new Rect(size), SwiftShapes.StatedCornerRadius(shape), RoundedCornerStyle.Continuous);
        Assert.All(outline.Corners, kind => Assert.Equal(CornerKind.Continuous, kind));
        // Clockwise from the end of the top-left corner: top-right, bottom-right, bottom-left, then top-left.
        var cubics = outline.Segments.Where(segment => segment.Verb == PathVerb.Cubic).ToArray();
        Assert.Equal(12, cubics.Length);
        var order = new[] { 1, 2, 3, 0 };
        for (var c = 0; c < 4; c++)
            for (var k = 0; k < 3; k++)
            {
                var segment = cubics[c * 3 + k];
                var (c1, c2, end) = (AppleCubics[k][0], AppleCubics[k][1], AppleCubics[k][2]);
                AssertNear(Map(order[c], c1.U, c1.V, radius, size), segment.Control1, $"{name}: corner {order[c]} cubic {k + 1} control 1");
                AssertNear(Map(order[c], c2.U, c2.V, radius, size), segment.Control2, $"{name}: corner {order[c]} cubic {k + 1} control 2");
                AssertNear(Map(order[c], end.U, end.V, radius, size), segment.End, $"{name}: corner {order[c]} cubic {k + 1} end");
            }
    }

    private static void AssertNear(Point expected, Point actual, string what)
    {
        Assert.True(Math.Abs(expected.X - actual.X) < 1e-6 && Math.Abs(expected.Y - actual.Y) < 1e-6, $"{what}: expected {expected}, got {actual}");
    }

    private static Border PillOf(Control root, Thickness padding) =>
        Mounted<Border>(root).Concat(root is Border own ? [own] : []).First(border => border.Padding == padding && SwiftShapes.GetCornerStyle(border) is not null);

    private static readonly Thickness Pill = new(12, 8, 14, 8);

    // ---------------------------------------------------------------- the single-line pills (capsule regime)

    [Fact]
    [Trait("Req", "UI-CHAT-024")] // TimelineRows.swift:1812-1820 the badge variant: pill insets and RoundedRectangle(cornerRadius: pillRadius, style: .continuous)
    [Trait("Req", "UI-CHAT-056")] // TimelineRows.swift:1766-1793, :1723-1755 the head departure badge
    [Trait("Req", "UI-CHAT-057")] // TimelineRows.swift:1772-1793 the MCP tool badge
    public void The_tool_call_badges_are_continuous_pills_of_radius_18_on_a_34_point_line_whose_ends_are_circular() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        h.Main.OnMain(() =>
        {
            var sites = new (string Name, Control Root)[]
            {
                ("MCP badge, completed", new ToolRow(ToolEntry(Call(ToolCall.Kind.Mcp, "Context7 · resolve-library-id")), h.Runtime, h.Project.Path)),
                ("MCP badge, running", new ToolRow(ToolEntry(Call(ToolCall.Kind.Mcp, "Context7 · resolve-library-id", ToolCall.Status.Running)), h.Runtime, h.Project.Path)),
                ("MCP badge, failed", new ToolRow(ToolEntry(Call(ToolCall.Kind.Mcp, "Context7 · resolve-library-id", ToolCall.Status.Failed)), h.Runtime, h.Project.Path)),
                ("departing head badge, running", new ToolRow(ToolEntry(Call(ToolCall.Kind.Agent, "Pip: the merge popover", ToolCall.Status.Running)), h.Runtime, h.Project.Path)),
                ("departing head badge, failed", new ToolRow(ToolEntry(Call(ToolCall.Kind.Agent, "Pip: the merge popover", ToolCall.Status.Failed)), h.Runtime, h.Project.Path)),
            };
            var panel = new StackPanel { Width = 700, Spacing = 20 };
            foreach (var site in sites) panel.Children.Add(site.Root);
            var window = Show(panel);
            try
            {
                foreach (var (name, root) in sites) AssertCapsule(PillOf(root, Pill), name);
            }
            finally { Close(window); }
        });
    });

    [Theory]
    [InlineData(ProposedPlan.State.Proposed)]
    [InlineData(ProposedPlan.State.Accepted)]
    [InlineData(ProposedPlan.State.Dismissed)]
    [Trait("Req", "UI-CHAT-029")] // ChatBadge.swift:45-49 the pill, :65 .clipShape(RoundedRectangle(cornerRadius: pillRadius, style: .continuous)); TimelineRows.swift:2263-2337
    public void The_plan_badge_pill_and_the_clip_over_its_attention_wave_are_the_same_continuous_shape(ProposedPlan.State state) => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        h.Main.OnMain(() =>
        {
            var card = new PlanCard(PlanEntry(state), h.Runtime);
            var window = Show(new StackPanel { Width = 700, Children = { card } });
            try
            {
                var pill = PillOf(card, Pill);
                AssertCapsule(pill, $"plan badge ({state})");
                // :65 the clip: the same helper, over the pill and its wave, at the same size and radius.
                var layers = Assert.Single(Mounted<BadgeLayers>(card));
                var clip = Assert.IsType<StreamGeometry>(layers.Clip);
                var rect = new Rect(0, 0, layers.Children[0].Bounds.Width, layers.Children[0].Bounds.Height);
                Assert.Equal(rect, clip.Bounds);
                Assert.True(rect.Height <= 36 + 1e-6, $"the badge is {rect.Height} pt tall");
                Assert.Same(SwiftShapes.RoundedRectangleGeometry(rect, 18, RoundedCornerStyle.Continuous), clip);
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-048")] // TimelineRows.swift:313-316 .background(.tint.opacity(0.1), in: RoundedRectangle(cornerRadius: pillRadius, style: .continuous)), :280 lineLimit(1)
    [Trait("Req", "UI-CHAT-051")] // TimelineRows.swift:579-582, :568 lineLimit(1)
    public void The_heads_report_and_brief_pills_are_continuous_pills_of_radius_18_on_a_single_line() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        h.Main.OnMain(() =>
        {
            h.Runtime.RehearseTurn("Build the thing");
            var briefEntry = h.Runtime.Entries.Last(candidate => candidate.TimelineEntryKind == TimelineEntry.Kind.User);
            var longText = "## Hank: " + string.Join(" ", Enumerable.Repeat("a very long first line that cannot fit on one line of a narrow row", 6));
            var sites = new (string Name, Control Root)[]
            {
                ("report pill, head", new HydraReportRow(new UserMessage { Text = "## Hank: a\nbody", HydraHeads = new ValueList<long>([0]) })),
                ("report pill, long title", new HydraReportRow(new UserMessage { Text = longText + "\nbody", HydraHeads = new ValueList<long>([0]) })),
                ("brief pill", new HydraBriefRow(((TimelineItem.Content.User)briefEntry.Item.TimelineItemContent).Value, briefEntry, h.Runtime)),
            };
            var panel = new StackPanel { Width = 360, Spacing = 20 };
            foreach (var site in sites) panel.Children.Add(site.Root);
            var window = Show(panel);
            try
            {
                foreach (var (name, root) in sites) AssertCapsule(PillOf(root, Pill), name);
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-053")] // TimelineRows.swift:1012-1017 .background(.tint.opacity(0.1), in: RoundedRectangle(cornerRadius: pillRadius, style: .continuous))
    public void The_merge_pill_is_a_continuous_pill_of_radius_18_in_both_states_and_no_taller_than_a_capsule() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        h.Main.OnMain(() =>
        {
            h.Runtime.IsHydraMerging = true;
            h.Runtime.HydraMergeStage = HydraMergeStage.Pushing.Words;
            var sites = new (string Name, Control Root)[]
            {
                ("merge pill, merging", new HydraMergeRow(h.Runtime, new HydraMergePhase.Merging())),
                ("merge pill, outcome", new HydraMergeRow(h.Runtime, new HydraMergePhase.Outcome(new UserMessage { Text = "Hydra merge held.\nwhy" }))),
            };
            var panel = new StackPanel { Width = 700, Spacing = 20 };
            foreach (var site in sites) panel.Children.Add(site.Root);
            var window = Show(panel);
            try
            {
                foreach (var (name, root) in sites) AssertCapsule(PillOf(root, Pill), name);
            }
            finally { Close(window); }
        });
    });

    // ---------------------------------------------------------------- the cards and plates in Apple's regime

    [Fact]
    [Trait("Req", "UI-CHAT-033")] // TimelineRows.swift:2364 .background(.quaternary.opacity(0.32), in: .rect(cornerRadius: pillRadius, style: .continuous)), padding 14
    public void The_checklist_card_is_cut_to_Apples_continuous_corner_of_radius_18() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        h.Main.OnMain(() =>
        {
            var one = new TodoListRow(new TimelineEntry(new TimelineItem(null, Steps(("One", TodoStep.Status.Active)))));
            var three = new TodoListRow(new TimelineEntry(new TimelineItem(null, Steps(("One", TodoStep.Status.Done), ("Two", TodoStep.Status.Active), ("Three", TodoStep.Status.Pending)))));
            var panel = new StackPanel { Width = 700, Spacing = 20, Children = { one, three } };
            var window = Show(panel);
            try
            {
                var thick = new Thickness(14);
                AssertApple(PillOf(one, thick), "checklist card, one step", 18);
                AssertApple(PillOf(three, thick), "checklist card, three steps", 18);
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-025")] // TimelineRows.swift:2150-2155 padding(10) .background(.quaternary.opacity(0.45), in: .rect(cornerRadius: 10, style: .continuous))
    public void The_tool_output_plate_is_cut_to_Apples_continuous_corner_of_radius_10_from_one_line_up() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            var oneLine = new ToolDetailView(Call(ToolCall.Kind.Command, "cat") with { Output = "a" });
            var twoLines = new ToolDetailView(Call(ToolCall.Kind.Command, "cat") with { Output = "a\nb" });
            var window = Show(new StackPanel { Width = 700, Spacing = 20, Children = { oneLine, twoLines } });
            try
            {
                AssertApple(PillOf(oneLine, new Thickness(10)), "output plate, one line", 10);
                AssertApple(PillOf(twoLines, new Thickness(10)), "output plate, two lines", 10);
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-028")] // TimelineRows.swift:2137-2140 .background(.quaternary.opacity(0.45), in: .rect(cornerRadius: 10, style: .continuous)) and .clipShape(.rect(cornerRadius: 10, style: .continuous))
    public void The_per_file_diff_block_is_cut_to_Apples_continuous_corner_of_radius_10() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            var diff = new ToolDetailView(Call(ToolCall.Kind.Edit, "e") with { Edits = new ValueList<FileEdit>([new FileEdit("src/a.swift", "@@ -1 +1 @@\n-a\n+b\n", 1, 1)]) });
            var window = Show(new StackPanel { Width = 700, Children = { diff } });
            try
            {
                var plate = Assert.Single(((StackPanel)diff.Child!).Children.OfType<Border>());
                AssertApple(plate, "diff block", 10);
                // The Swift clips as well as fills: the plate clips its children (the diff lines) to the same shape.
                Assert.True(plate.ClipToBounds, "the diff block clips its lines to its shape (.clipShape)");
                Assert.NotNull(plate.Clip);
            }
            finally { Close(window); }
        });
    });

    // ---------------------------------------------------------------- the shapes that stay open: ED-275 (2)'s band

    // ---------------------------------------------------------------- the shapes that sat in ED-275 (2)'s band: OD-0102

    // OD-0102 (owner, 2026-10-04, "corner-band: A"): in the band h / 3.057 < r < h / 2 (r 18: 36 < h < 55) the corner is Apple's at the largest radius that fits,
    // not the circular arc. The Swift states `.continuous` and the radius and nothing more (TimelineRows.swift); the band's regime is the owner's (OD-0102), settled
    // from the Swift per OD-0098, and these tests read the corner off the mounted elements: the radius and style the Swift states, the size the layout gives, then
    // AssertBand (every corner Apple's at height / 3.057, the control points, the clip). The neighbouring sizes are asserted too: a capsule (circular) below, the full
    // corner above.

    [Fact]
    [Trait("Req", "UI-CHAT-034")] // TimelineRows.swift:2402 `.background(..., in: .rect(cornerRadius: TimelineMetrics.pillRadius, style: .continuous))`, the corner of a notice two lines tall
    public void A_warning_or_error_notice_two_lines_tall_is_cut_to_Apples_corner_at_the_largest_radius_that_fits() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        h.Main.OnMain(() =>
        {
            const string twoLines = "A warning long enough that it wraps onto a second line when the row is only 380 points wide.";
            var warning = new NoticeRow(NoticeEntry(Notice.Level.Warning, twoLines));
            var error = new NoticeRow(NoticeEntry(Notice.Level.Error, twoLines));
            var oneLine = new NoticeRow(NoticeEntry(Notice.Level.Warning, "Short."));
            var threeLines = new NoticeRow(NoticeEntry(Notice.Level.Warning, string.Join(" ", Enumerable.Repeat("A notice long enough to take three lines in a narrow row.", 4))));
            var panel = new StackPanel { Width = 700, Spacing = 20 };
            foreach (var control in new Control[] { warning, error, oneLine, threeLines }) panel.Children.Add(new Border { Width = 380, Child = control });
            var window = Show(panel);
            try
            {
                AssertBand(PillOf(warning, Pill), "warning notice, two lines");
                AssertBand(PillOf(error, Pill), "error notice, two lines");
                AssertCapsule(PillOf(oneLine, Pill), "warning notice, one line");
                AssertApple(PillOf(threeLines, Pill), "warning notice, three lines", 18);
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-036")] // TimelineRows.swift:2660 `.background(.quaternary.opacity(0.32), in: .rect(cornerRadius: TimelineMetrics.pillRadius, style: .continuous))`, the steps card of a turn of one step
    public void The_steps_card_of_a_turn_of_one_step_is_cut_to_Apples_corner_at_the_largest_radius_that_fits() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        using var noNetwork = new NoLinkNetwork("gitlab.com");
        h.Main.OnMain(() =>
        {
            var turn = Guid.NewGuid();
            Control Steps(params TimelineEntry[] entries) => new TurnFinishedBlock(h.Runtime, turn, Summary(turn) with { Duration = 64 }, ValueList<TimelineEntry>.Empty,
                new ValueList<TimelineEntry>(entries), null, canUndo: false, chronological: true);
            var oneStep = Steps(ToolEntry(Call(ToolCall.Kind.Read, "Read a"), turn));
            var twoSteps = Steps(ToolEntry(Call(ToolCall.Kind.Read, "Read a"), turn), ToolEntry(Call(ToolCall.Kind.Read, "Read b"), turn));
            var window = Show(new StackPanel { Width = 700, Spacing = 20, Children = { oneStep, twoSteps } });
            try
            {
                var steps = new Thickness(14);
                AssertBand(PillOf(oneStep, steps), "steps card, one step");
                AssertApple(PillOf(twoSteps, steps), "steps card, two steps", 18);
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-037")] // TimelineRows.swift:2822 `.background(.quaternary.opacity(0.32), in: RoundedRectangle(cornerRadius: TimelineMetrics.pillRadius, style: .continuous))`, the Edited-files card (40 pt)
    public void The_Edited_files_card_is_cut_to_Apples_corner_at_the_largest_radius_that_fits() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        using var noNetwork = new NoLinkNetwork("gitlab.com");
        h.Main.OnMain(() =>
        {
            FileChangeSummary Files(int count) => new(new ValueList<FileChangeSummary.File>(Enumerable.Range(1, count).Select(i => new FileChangeSummary.File("src/file" + i + ".swift", i, 1, false))), 40, 7);
            var revertable = new TurnFileCard(Files(2), canUndo: true, onRevert: () => { }, onReview: _ => { });
            var plain = new TurnFileCard(Files(1), canUndo: false, onRevert: () => { }, onReview: _ => { });
            var window = Show(new StackPanel { Width = 700, Spacing = 20, Children = { revertable, plain } });
            try
            {
                var card = new Thickness(12, 6, 6, 6);
                AssertBand(PillOf(revertable, card), "Edited-files card, revertable");
                AssertBand(PillOf(plain, card), "Edited-files card, not revertable");
                Assert.Equal(40, PillOf(revertable, card).Bounds.Height, 0.5); // the 28 pt discs and 6 pt above and below: the card sits at the 13.08 pt corner
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-054")] // TimelineRows.swift:691-694 `.background(.tint.opacity(0.1), in: .rect(cornerRadius: TimelineMetrics.pillRadius, style: .continuous))`, the delegation card of one head
    public void The_delegation_card_of_one_head_is_cut_to_Apples_corner_at_the_largest_radius_that_fits() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        using var noNetwork = new NoLinkNetwork("gitlab.com");
        h.Main.OnMain(() =>
        {
            string Json(int n) => "[" + string.Join(",", Enumerable.Range(0, n).Select(i => "{\"task\": \"Tidy " + i + "\", \"prompt\": \"p\", \"name\": \"Hank\"}")) + "]";
            var oneHead = new HydraDelegationBlock(Json(1), sent: true);
            var twoHeads = new HydraDelegationBlock(Json(2), sent: true);
            var noHeads = new HydraDelegationBlock("[]", sent: true);
            var window = Show(new StackPanel { Width = 700, Spacing = 20, Children = { oneHead, twoHeads, noHeads } });
            try
            {
                AssertBand(PillOf(oneHead, Pill), "delegation card, one head");
                AssertApple(PillOf(twoHeads, Pill), "delegation card, two heads", 18);
                AssertCapsule(PillOf(noHeads, Pill), "delegation card, no head");
            }
            finally { Close(window); }
        });
    });

    // REVIEW (adversarial, unit blitz-cornerband): AssertBand reads the clip path and SwiftShapes.Outline, never the pixels. The four band elements are plain
    // Borders whose fill Avalonia draws as the circle of CornerRadius 18; the clip (Apple's corner at L = h / 3.057) can only take away, and Apple's corner at L
    // lies outside the circle of 18 near the diagonal (2.06 pt at 40 pt, 1.66 at 43, 0.98 at 48), so what is drawn is the circle there, not OD-0102's corner.
    // This renders each mounted element with an opaque fill and compares its top-left corner with a SwiftShapeBorder (which fills the path itself) of the same size.
    [Fact]
    [Trait("Rule", "R-TEST-4")] // the corner clause only of UI-CHAT-034, UI-CHAT-036, UI-CHAT-037, UI-CHAT-054, read off the rendered pixels
    [Trait("Req", "UI-CHAT-034")] // the two-line notice, all four corners: the pixels follow Apple's corner at h / 3.057, not the circle of 18 (the corner clause)
    public void REVIEW_the_four_band_elements_render_Apples_corner_at_the_largest_radius_not_the_circle_of_18() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        using var noNetwork = new NoLinkNetwork("gitlab.com");
        h.Main.OnMain(() =>
        {
            const string twoLines = "A warning long enough that it wraps onto a second line when the row is only 380 points wide.";
            var notice = new NoticeRow(NoticeEntry(Notice.Level.Warning, twoLines));
            var turn = Guid.NewGuid();
            var steps = new TurnFinishedBlock(h.Runtime, turn, Summary(turn) with { Duration = 64 }, ValueList<TimelineEntry>.Empty,
                new ValueList<TimelineEntry>([ToolEntry(Call(ToolCall.Kind.Read, "Read a"), turn)]), null, canUndo: false, chronological: true);
            var files = new TurnFileCard(new FileChangeSummary(new ValueList<FileChangeSummary.File>([new FileChangeSummary.File("src/file1.swift", 1, 1, false)]), 40, 7),
                canUndo: false, onRevert: () => { }, onReview: _ => { });
            var delegation = new HydraDelegationBlock("[{\"task\": \"Tidy 0\", \"prompt\": \"p\", \"name\": \"Hank\"}]", sent: true);
            var panel = new StackPanel { Width = 700, Spacing = 20 };
            foreach (var control in new Control[] { new Border { Width = 380, Child = notice }, steps, files, delegation }) panel.Children.Add(control);
            var window = Show(panel);
            var results = new List<string>();
            var failures = new List<string>();
            try
            {
                var shapes = new (string Name, Border Shape)[]
                {
                    ("notice, two lines", PillOf(notice, Pill)), ("steps card, one step", PillOf(steps, new Thickness(14))),
                    ("Edited-files card", PillOf(files, new Thickness(12, 6, 6, 6))), ("delegation card, one head", PillOf(delegation, Pill)),
                };
                foreach (var (_, shape) in shapes) shape.Background = Brushes.Red;
                Settle(window);
                Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var mounted = RedChannel(window);
                foreach (var (name, shape) in shapes)
                {
                    var origin = shape.TranslatePoint(default, window) ?? throw new InvalidOperationException(name + " is not mounted");
                    var size = shape.Bounds.Size;
                    var reference = new SwiftShapeBorder { Width = size.Width, Height = size.Height, Background = Brushes.Red, Margin = new Thickness(origin.X, origin.Y, 0, 0),
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top }.RoundedRectangle(18, RoundedCornerStyle.Continuous);
                    var refWindow = Show(new Panel { Children = { reference } });
                    Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    var expected = RedChannel(refWindow);
                    Close(refWindow);
                    var worst = 0;
                    int x0 = (int)Math.Floor(origin.X), y0 = (int)Math.Floor(origin.Y);
                    // All four corners (the cornerband check found the 46 pt notice 102 levels off at its bottom-left run-in, which a top-left window never sees): the 8 pt
                    // square at each corner of the element, inside every element's padding so only the fill is read, never a glyph or a word.
                    int right = (int)Math.Ceiling(origin.X + size.Width) - 8, bottom = (int)Math.Ceiling(origin.Y + size.Height) - 8;
                    var perCorner = new List<string>();
                    foreach (var (label, bx, by) in new[] { ("top-left", x0, y0), ("top-right", right, y0), ("bottom-right", right, bottom), ("bottom-left", x0, bottom) })
                    {
                        var cornerWorst = 0;
                        for (var y = by; y < by + 8; y++)
                            for (var x = bx; x < bx + 8; x++) cornerWorst = Math.Max(cornerWorst, Math.Abs(mounted[y, x] - expected[y, x]));
                        perCorner.Add($"{label} {cornerWorst}");
                        worst = Math.Max(worst, cornerWorst);
                    }
                    results.Add($"{name} ({size.Height:F1} pt at {origin}): worst {worst} ({string.Join(", ", perCorner)}); mounted diag {mounted[y0 + 4, x0 + 4]} {mounted[y0 + 5, x0 + 5]} ref {expected[y0 + 4, x0 + 4]} {expected[y0 + 5, x0 + 5]} inside {mounted[y0 + 10, x0 + 30]}/{expected[y0 + 10, x0 + 30]}");
                    if (worst > 60) failures.Add($"{name} ({size.Height:F1} pt at {origin}): the rendered corner differs from Apple's corner at {size.Height / (2 * 1.528665):F2} by {worst} of 255");
                }
            }
            finally { Close(window); }
            Assert.True(failures.Count == 0, string.Join("; ", failures) + " | " + string.Join("; ", results));
        });
    });

    private static int[,] RedChannel(TopLevel window)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        using var frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window) ?? throw new Xunit.Sdk.XunitException("no rendered frame");
        using var buffer = frame.Lock();
        var row = new byte[buffer.RowBytes];
        var red = new int[buffer.Size.Height, buffer.Size.Width];
        for (var y = 0; y < buffer.Size.Height; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(buffer.Address + y * buffer.RowBytes, row, 0, buffer.RowBytes);
            for (var x = 0; x < buffer.Size.Width; x++) red[y, x] = 255 - row[x * 4 + 1]; // the fill is opaque red over a light window: 255 less green is its coverage
        }
        return red;
    }
}
