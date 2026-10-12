using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.Core.Tests.Models.Panes;

// ED-NEW-web-w2-1 (traceability alias ED-NEW-webpanes-1): PaneLayout.Solve against the approved canvas. Every expected
// rectangle, gutter, label and readout below was printed by the canvas's own solve() under node 24
// (tests/fixtures/web-surfaces/canvas-solve.mjs splices it from E:/Droppy/work/design/web-surfaces/index.html, script
// section "1. pane workspace"), 2026-10-11; none comes from running the C#. The canvas names Visus "browser", Tabula
// "artifact" and Background tasks "tasks", and calls the two-column grid's row boundary "rows" (row1 here, the
// design's key list in 3.2 rule 10). Where the design adds a rule the canvas leaves out (per-kind maximums in a
// one-column stack), the last test shows the difference.
public sealed class PaneLayoutCanvasTests
{
    private static readonly PaneKind Tasks = PaneKind.BackgroundTasks, Tabula = PaneKind.Artifacts, Visus = PaneKind.Browser;

    [Fact]
    [Trait("Decision", "ED-NEW-webpanes-1")] // canvas case "default": the owner's screenshot, Tasks | Tabula over a wide Visus, Side chat column, 1324 x 848
    public void Default_workspace_matches_the_canvas()
    {
        var layout = PaneLayout.Solve(State([Tasks, Tabula, Visus], side: true), 1324, 848);
        Assert.Equal(PaneLayoutMode.Tiles, layout.Mode);
        AssertRect(layout.Chat, 0, 0, 407.96, 848);
        AssertRect(layout.BoundsOf(PaneKind.SideChat), 1044, 0, 280, 848);
        AssertRect(layout.BoundsOf(Tasks), 415.96, 0, 292.03999999999996, 420);
        AssertRect(layout.BoundsOf(Tabula), 716, 0, 320, 420);
        AssertRect(layout.BoundsOf(Visus), 415.96, 428, 620.04, 420);
        AssertGutter(layout, "chat", 411.96, 407.96, 400, 1294, "Resize Chat and panes", "408 DIP");
        AssertGutter(layout, "row1", 424, 420, 200, 600, "Resize top and bottom panes", "50 %");
        AssertGutter(layout, "top", 712, 292.03999999999996, 280, 292.03999999999996, "Resize Tasks and Tabula", "48 %");
        AssertSideGutter(layout, 1040, 280, 540.04, "280 DIP");
        Assert.Equal(new[] { "chat", "side", "row1", "top" }, layout.Gutters.Select(g => g.Key));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-webpanes-1")] // canvas case "noside": the same three panes without Side chat
    public void Three_panes_without_side_chat_match_the_canvas()
    {
        var layout = PaneLayout.Solve(State([Tasks, Tabula, Visus]), 1324, 848);
        AssertRect(layout.Chat, 0, 0, 473.76, 848);
        AssertRect(layout.BoundsOf(Tasks), 481.76, 0, 417.12, 420);
        AssertRect(layout.BoundsOf(Tabula), 906.88, 0, 417.12, 420);
        AssertRect(layout.BoundsOf(Visus), 481.76, 428, 842.24, 420);
        AssertGutter(layout, "chat", 477.76, 473.76, 400, 1294, "Resize Chat and panes", "474 DIP");
        AssertGutter(layout, "row1", 424, 420, 200, 600, "Resize top and bottom panes", "50 %");
        AssertGutter(layout, "top", 902.88, 417.12, 280, 514.24, "Resize Tasks and Tabula", "50 %");
    }

    [Fact]
    [Trait("Decision", "ED-NEW-webpanes-1")] // canvas case "two": a grid narrower than 1.1 x its height stacks its two cells
    public void Two_panes_in_a_tall_grid_stack_as_in_the_canvas()
    {
        var layout = PaneLayout.Solve(State([Tabula, Visus]), 1324, 848);
        Assert.Equal(1, layout.GridColumns);
        AssertRect(layout.Chat, 0, 0, 552.72, 848);
        AssertRect(layout.BoundsOf(Tabula), 560.72, 0, 763.28, 420);
        AssertRect(layout.BoundsOf(Visus), 560.72, 428, 763.28, 420);
        AssertGutter(layout, "chat", 556.72, 552.72, 400, 1294, "Resize Chat and panes", "553 DIP");
        AssertGutter(layout, "row1", 424, 420, 200, 600, "Resize Tabula and Visus", "420 DIP");
    }

    [Fact]
    [Trait("Decision", "ED-NEW-webpanes-1")] // canvas case "four": 2 x 2 beside the Side chat column, 1600 x 900
    public void Four_panes_and_side_chat_match_the_canvas()
    {
        var layout = PaneLayout.Solve(State([Tasks, PaneKind.Changes, Tabula, Visus], side: true), 1600, 900);
        Assert.Equal(2, layout.GridColumns);
        AssertRect(layout.Chat, 0, 0, 493.52, 900);
        AssertRect(layout.BoundsOf(PaneKind.SideChat), 1272.856, 0, 327.144, 900);
        AssertRect(layout.BoundsOf(Tasks), 501.52, 0, 377.668, 446);
        AssertRect(layout.BoundsOf(PaneKind.Changes), 887.188, 0, 377.668, 446);
        AssertRect(layout.BoundsOf(Tabula), 501.52, 454, 377.668, 446);
        AssertRect(layout.BoundsOf(Visus), 887.188, 454, 377.668, 446);
        AssertGutter(layout, "chat", 497.52, 493.52, 400, 1570, "Resize Chat and panes", "494 DIP");
        AssertGutter(layout, "row1", 450, 446, 150, 652, "Resize top and bottom panes", "50 %");
        AssertGutter(layout, "top", 883.188, 377.668, 280, 475.336, "Resize Tasks and Changes", "50 %");
        AssertGutter(layout, "bottom", 883.188, 377.668, 320, 395.336, "Resize Tabula and Visus", "50 %");
        AssertSideGutter(layout, 1268.856, 327.144, 640, "327 DIP");
    }

    [Fact]
    [Trait("Decision", "ED-NEW-webpanes-1")] // canvas case "ratios": chat 0.5 and side 0.25 stored; the grid drops to three stacked rows
    public void Stored_ratios_are_clamped_as_in_the_canvas()
    {
        var state = State([Tasks, Tabula, Visus], side: true) with { Ratios = new ValueMap<string, double>(new Dictionary<string, double> { ["chat"] = 0.5, ["side"] = 0.25 }) };
        var layout = PaneLayout.Solve(state, 1324, 848);
        Assert.Equal(1, layout.GridColumns);
        AssertRect(layout.Chat, 0, 0, 658, 848);
        AssertRect(layout.BoundsOf(PaneKind.SideChat), 1044, 0, 280, 848);
        AssertRect(layout.BoundsOf(Tasks), 666, 0, 370, 277.3333333333333);
        AssertRect(layout.BoundsOf(Tabula), 666, 285.3333333333333, 370, 277.3333333333333);
        AssertRect(layout.BoundsOf(Visus), 666, 570.6666666666666, 370, 277.3333333333334);
        AssertGutter(layout, "chat", 662, 658, 400, 1294, "Resize Chat and panes", "658 DIP");
        AssertGutter(layout, "row1", 281.3333333333333, 277.3333333333333, 150, 354.66666666666663, "Resize Tasks and Tabula", "277 DIP");
        AssertGutter(layout, "row2", 566.6666666666666, 277.3333333333333, 200, 314.66666666666674, "Resize Tabula and Visus", "277 DIP");
        AssertSideGutter(layout, 1040, 280, 290, "280 DIP");
    }

    [Fact]
    [Trait("Decision", "ED-NEW-webpanes-1")] // canvas case "collws": every pane folded into one rail at the trailing edge
    public void Collapsed_workspace_matches_the_canvas()
    {
        var state = State([Tasks, Tabula, Visus], side: true) with { Collapsed = new ValueSet<string>(["chat"]) };
        var layout = PaneLayout.Solve(state, 1324, 848);
        AssertRect(layout.Chat, 0, 0, 1294, 848);
        var rail = Assert.Single(layout.Rails);
        AssertRect(rail.Bounds, 1302, 0, 22, 848);
        Assert.Equal(new[] { Tasks, Tabula, Visus, PaneKind.SideChat }, rail.Panes);
        Assert.Empty(layout.Cells);
        var gutter = AssertGutter(layout, "chat", 1298, 1294, 400, 1294, "Resize Chat and panes", "Panes collapsed");
        Assert.True(gutter.IsCollapsed);
        Assert.Single(layout.Gutters);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-webpanes-1")] // canvas case "collside": Side chat folded into its rail, the grid takes its room
    public void Collapsed_side_chat_matches_the_canvas()
    {
        var state = State([Tasks, Tabula, Visus], side: true) with { Collapsed = new ValueSet<string>(["side"]) };
        var layout = PaneLayout.Solve(state, 1324, 848);
        AssertRect(layout.Chat, 0, 0, 407.96, 848);
        AssertRect(layout.BoundsOf(Tasks), 415.96, 0, 435.02, 420);
        AssertRect(layout.BoundsOf(Tabula), 858.98, 0, 435.02, 420);
        AssertRect(layout.BoundsOf(Visus), 415.96, 428, 878.04, 420);
        Assert.Null(layout.BoundsOf(PaneKind.SideChat));
        var rail = Assert.Single(layout.Rails);
        AssertRect(rail.Bounds, 1302, 0, 22, 848);
        Assert.Equal(new[] { PaneKind.SideChat }, rail.Panes);
        AssertGutter(layout, "chat", 411.96, 407.96, 400, 1294, "Resize Chat and panes", "408 DIP");
        AssertGutter(layout, "row1", 424, 420, 200, 600, "Resize top and bottom panes", "50 %");
        AssertGutter(layout, "top", 854.98, 435.02, 280, 550.04, "Resize Tasks and Tabula", "50 %");
        var side = layout.Gutter("side")!;
        Assert.Equal(1298, side.Position, 9);
        Assert.Equal(540.04, side.NormalMaximum, 9);
        Assert.Equal("Side chat collapsed", side.ValueText);
        Assert.True(side.IsCollapsed);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-webpanes-1")] // canvas case "narrow": 700 DIP with Visus open is under 768, so one surface at a time
    public void Narrow_workspace_matches_the_canvas()
    {
        var layout = PaneLayout.Solve(State([Visus], side: true), 700, 848);
        Assert.Equal(PaneLayoutMode.Narrow, layout.Mode);
        Assert.Equal(new[] { PaneKind.Chat, Visus, PaneKind.SideChat }, layout.NarrowTabs);
        foreach (var kind in layout.NarrowTabs) AssertRect(layout.BoundsOf(kind), 0, 44, 700, 804);
        Assert.Equal(PaneKind.Chat, layout.NarrowSelection);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-webpanes-1")] // canvas case "onecol": a 592 DIP grid stacks three rows, 1000 x 1400
    public void Three_stacked_rows_match_the_canvas()
    {
        var layout = PaneLayout.Solve(State([PaneKind.Changes, Tabula, Visus]), 1000, 1400);
        AssertRect(layout.Chat, 0, 0, 400, 1400);
        AssertRect(layout.BoundsOf(PaneKind.Changes), 408, 0, 592, 461.3333333333333);
        AssertRect(layout.BoundsOf(Tabula), 408, 469.3333333333333, 592, 461.3333333333333);
        AssertRect(layout.BoundsOf(Visus), 408, 938.6666666666666, 592, 461.3333333333334);
        AssertGutter(layout, "chat", 404, 400, 400, 970, "Resize Chat and panes", "400 DIP");
        AssertGutter(layout, "row1", 465.3333333333333, 461.3333333333333, 150, 722.6666666666666, "Resize Changes and Tabula", "461 DIP");
        AssertGutter(layout, "row2", 934.6666666666666, 461.3333333333333, 200, 682.6666666666667, "Resize Tabula and Visus", "461 DIP");
    }

    [Fact]
    [Trait("Decision", "ED-NEW-webpanes-1")] // canvas case "onecoltabs": 500 DIP tall fits two rows, the third pane joins the last row as a tab
    public void Rows_that_do_not_fit_join_the_last_row_as_tabs_as_in_the_canvas()
    {
        var layout = PaneLayout.Solve(State([PaneKind.Changes, Tabula, Visus]), 1000, 500);
        AssertRect(layout.Chat, 0, 0, 400, 500);
        AssertRect(layout.BoundsOf(PaneKind.Changes), 408, 0, 592, 246);
        AssertRect(layout.BoundsOf(Tabula), 408, 254, 592, 246);
        AssertRect(layout.BoundsOf(Visus), 408, 254, 592, 246);
        var group = layout.CellOf(Visus)!;
        Assert.Equal(new[] { Tabula, Visus }, group.Tabs);
        Assert.Equal(Visus, group.Shown);
        Assert.False(layout.IsShown(Tabula));
        AssertGutter(layout, "row1", 250, 246, 150, 252, "Resize Changes and Visus", "246 DIP");
    }

    [Fact]
    [Trait("Decision", "ED-NEW-webpanes-1")] // canvas case "stack2": two stacked rows in a tall, narrow grid
    public void Two_stacked_rows_match_the_canvas()
    {
        var layout = PaneLayout.Solve(State([Tabula, Visus]), 1000, 1400);
        AssertRect(layout.Chat, 0, 0, 416.64, 1400);
        AssertRect(layout.BoundsOf(Tabula), 424.64, 0, 575.36, 696);
        AssertRect(layout.BoundsOf(Visus), 424.64, 704, 575.36, 696);
        AssertGutter(layout, "chat", 420.64, 416.64, 400, 970, "Resize Chat and panes", "417 DIP");
        AssertGutter(layout, "row1", 700, 696, 200, 1152, "Resize Tabula and Visus", "696 DIP");
    }

    [Fact]
    [Trait("Decision", "ED-NEW-webpanes-1")] // canvas case "five" gives Tasks 592 DIP in a one-column stack, over its 560 maximum (3.2 table); the design caps the column and the chat takes the rest
    public void A_one_column_stack_keeps_tasks_at_its_maximum_where_the_canvas_did_not()
    {
        var layout = PaneLayout.Solve(State([Tasks, PaneKind.Changes, Tabula, Visus], side: true), 1000, 848);
        Assert.Equal(1, layout.GridColumns);
        AssertRect(layout.Chat, 0, 0, 432, 848);
        AssertRect(layout.BoundsOf(Tasks), 440, 0, 560, 277.3333333333333);
        Assert.Equal(new[] { Tabula, Visus, PaneKind.SideChat }, layout.CellOf(Visus)!.Tabs);
        Assert.Equal(PaneKind.SideChat, layout.CellOf(Visus)!.Shown);
        var chat = layout.Gutter("chat")!;
        Assert.Equal(432, chat.Value, 9);
        Assert.Equal(432, chat.NormalMinimum, 9);
    }

    private static PaneWorkspaceState State(PaneKind[] panes, bool side = false) => PaneWorkspaceState.Default with
    {
        Order = new ValueList<PaneSlot>(panes.Select(p => PaneSlot.Of(p))),
        IsSideChatOpen = side,
    };

    private static void AssertRect(PaneRect? actual, double x, double y, double w, double h)
    {
        Assert.NotNull(actual);
        var r = actual.Value;
        Assert.Equal(x, r.X, 9);
        Assert.Equal(y, r.Y, 9);
        Assert.Equal(w, r.Width, 9);
        Assert.Equal(h, r.Height, 9);
    }

    private static PaneGutter AssertGutter(PaneLayoutResult layout, string key, double position, double value, double min, double max, string label, string text)
    {
        var g = layout.Gutter(key);
        Assert.NotNull(g);
        Assert.Equal(position, g.Position, 9);
        Assert.Equal(value, g.Value, 9);
        Assert.Equal(min, g.Minimum, 9);
        Assert.Equal(max, g.Maximum, 9);
        Assert.Equal(label, g.Label);
        Assert.Equal(text, g.ValueText);
        return g;
    }

    // The canvas reports the side gutter's range as 0 to its maximum; here the collapsed position is the 22 DIP rail
    // (Value == ValueAt(Position) holds collapsed too), so the side's normal maximum and position are compared.
    private static void AssertSideGutter(PaneLayoutResult layout, double position, double value, double max, string text)
    {
        var g = layout.Gutter("side");
        Assert.NotNull(g);
        Assert.Equal(position, g.Position, 9);
        Assert.Equal(value, g.Value, 9);
        Assert.Equal(max, g.NormalMaximum, 9);
        Assert.Equal(max, g.Maximum, 9);
        Assert.Equal(PaneKinds.Rail, g.Minimum);
        Assert.Equal("Resize panes and Side chat", g.Label);
        Assert.Equal(text, g.ValueText);
    }
}
