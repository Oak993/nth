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
//  - BAND (h / 3.057 < r < h / 2): UNRESOLVED (ED-275 (2)). The Edited-files card (40 pt, r 18), the delegation card (54 pt, r 18) and any
//    pill two lines tall sit here; the port draws the circular arc there and no test below claims a fit for it.
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
        Assert.Equal(new CornerRadius(radius), shape.CornerRadius);
        var height = shape.Bounds.Height;
        Assert.True(height > 0 && height <= 2 * radius + 1e-6, $"{name}: {height} pt tall is not a capsule of radius {radius} (that would be the band or Apple's corner)");
        Assert.False(ContinuousCornerChecks.Drawn(shape, expectApple: false), $"{name}: a capsule has no Apple corner");
        var outline = SwiftShapes.Outline(new Rect(shape.Bounds.Size), shape.CornerRadius, RoundedCornerStyle.Continuous);
        Assert.All(outline.Corners, kind => Assert.Equal(CornerKind.Arc, kind));
        Assert.All(outline.Segments.Where(segment => segment.Verb == PathVerb.Arc), arc => Assert.Equal(height / 2, arc.ArcRadius, 1e-9));
    }

    /// <summary>The element is a rounded rectangle of this radius drawn through Apple's corner: its outline's control points are the ones in ED-275 (1), and its clip is that outline.</summary>
    private static void AssertApple(Border shape, string name, double radius)
    {
        Assert.True(SwiftShapes.GetCornerStyle(shape) == RoundedCornerStyle.Continuous, $"{name}: the source says .continuous");
        Assert.Equal(new CornerRadius(radius), shape.CornerRadius);
        var size = shape.Bounds.Size;
        Assert.True(size.Height >= 2 * 1.528665 * radius && size.Width >= 2 * 1.528665 * radius, $"{name}: {size} is not tall enough for Apple's corner of radius {radius}");
        Assert.True(ContinuousCornerChecks.Drawn(shape, expectApple: true), $"{name}: no Apple corner");
        var outline = SwiftShapes.Outline(new Rect(size), shape.CornerRadius, RoundedCornerStyle.Continuous);
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

    // No Req trait on purpose: this pins WHERE the shapes of UI-CHAT-034 (a pill two lines tall), UI-CHAT-036 (a turn of one step), UI-CHAT-037 (the Edited-files
    // card) and UI-CHAT-054 (a delegation card of one head) sit, so that the records stay open on them. In the band h / 3.057 < r < h / 2 (r 18: 36 < h < 55) what Apple
    // draws is UNRESOLVED (ED-275 (2), corrected 2026-10-04): no frame has a measurable edge there and the Swift states `.continuous` and nothing more. The port draws
    // the circular arc, asserted as its own regime and not as a fit. The same shapes at other sizes are proven (the tests above and below).
    [Fact]
    [Trait("Rule", "R-TEST-4")] // the band: UNRESOLVED regime, the records UI-CHAT-034, -036, -037, -054 stay open on it
    public void The_wrapped_notice_the_one_step_turn_the_edited_files_card_and_a_one_head_delegation_card_sit_in_the_unresolved_band_and_are_not_claimed() => headless.OnUiThread(() =>
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
            string Json(int n) => "[" + string.Join(",", Enumerable.Range(0, n).Select(i => "{\"task\": \"Tidy " + i + "\", \"prompt\": \"p\", \"name\": \"Hank\"}")) + "]";
            var files = new FileChangeSummary(new ValueList<FileChangeSummary.File>(Enumerable.Range(1, 2).Select(i => new FileChangeSummary.File("src/file" + i + ".swift", i, 1, false))), 40, 7);
            var wrapped = new NoticeRow(NoticeEntry(Notice.Level.Warning, "A warning long enough that it wraps onto a second line when the row is only 380 points wide."));
            var oneStep = Steps(ToolEntry(Call(ToolCall.Kind.Read, "Read a"), turn));
            var twoSteps = Steps(ToolEntry(Call(ToolCall.Kind.Read, "Read a"), turn), ToolEntry(Call(ToolCall.Kind.Read, "Read b"), turn));
            var fileCard = new TurnFileCard(files, canUndo: true, onRevert: () => { }, onReview: _ => { });
            var oneHead = new HydraDelegationBlock(Json(1), sent: true);
            var twoHeads = new HydraDelegationBlock(Json(2), sent: true);
            var noHeads = new HydraDelegationBlock("[]", sent: true);
            var panel = new StackPanel { Width = 700, Spacing = 20 };
            foreach (var control in new Control[] { new Border { Width = 380, Child = wrapped }, oneStep, twoSteps, fileCard, oneHead, twoHeads, noHeads }) panel.Children.Add(control);
            var window = Show(panel);
            try
            {
                var inBand = new List<(string Name, Border Shape)>
                {
                    ("wrapped notice (two lines)", PillOf(wrapped, Pill)),
                    ("turn of one step", PillOf(oneStep, new Thickness(14))),
                    ("Edited-files card", PillOf(fileCard, new Thickness(12, 6, 6, 6))),
                    ("delegation card, one head", PillOf(oneHead, Pill)),
                };
                foreach (var (name, shape) in inBand)
                {
                    Assert.Equal(new CornerRadius(18), shape.CornerRadius);
                    Assert.True(shape.Bounds.Height > 36 + 1e-6 && shape.Bounds.Height < 2 * 1.528665 * 18, $"{name}: {shape.Bounds.Height} pt is not in the band (36, 55.03)");
                    Assert.False(ContinuousCornerChecks.Drawn(shape, expectApple: false), $"{name}: the port's regime for the band is the circular arc");
                }
                // The same shapes one step either side of the band: a capsule below it, Apple's corner above it.
                AssertCapsule(PillOf(noHeads, Pill), "delegation card, no head");
                AssertApple(PillOf(twoSteps, new Thickness(14)), "turn of two steps", 18);
                AssertApple(PillOf(twoHeads, Pill), "delegation card, two heads", 18);
            }
            finally { Close(window); }
        });
    });
}
