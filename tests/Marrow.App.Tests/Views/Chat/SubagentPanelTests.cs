using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Marrow.App.Views.Chat;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Xunit;

namespace Marrow.App.Tests.Views.Chat;

[Collection("Headless")]
public sealed class SubagentPanelTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-HYDRA-219")] // App/Views/Chat/SubagentPanel.swift:105-136,265-266
    public void Layout_and_panel_constants_match_the_source()
    {
        Assert.Equal(400d, SubagentPanelLayout.Width);
        Assert.Equal(300d, SubagentPanelLayout.MinWidth);
        Assert.Equal(220d, SubagentPanelLayout.MinHeight);
        Assert.Equal(12d, SubagentPanelLayout.Gap);
        Assert.Equal(20d, SubagentPanelLayout.SideMargin);
        Assert.Equal(14d, SubagentPanelLayout.BottomMargin);
        Assert.Equal(360d, SubagentPanelLayout.MinComposerWidth);
        Assert.Equal(820d, SubagentPanelLayout.ComposerMaxWidth);
        Assert.Equal(56d, SubagentPanelLayout.CornerSlack);
        Assert.Equal(280d, SubagentPanelLayout.CompactHeight);

        var cornerRadius = typeof(SubagentPanel).GetField("CornerRadius", BindingFlags.NonPublic | BindingFlags.Static);
        var handleHeight = typeof(SubagentPanel).GetProperty("HandleHeight", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(cornerRadius);
        Assert.NotNull(handleHeight);
        Assert.Equal(22d, cornerRadius.GetRawConstantValue());
        Assert.Equal(50d, handleHeight.GetValue(null));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-220")] // App/Views/Chat/SubagentPanel.swift:130-181
    public void Panel_size_clamps_width_and_applies_dragged_and_compact_height_rules()
    {
        var defaultLayout = new SubagentPanelLayout
        {
            Pane = new Size(1200, 1000),
            ComposerAreaHeight = 200
        };

        Assert.Equal(new Size(400, 500), defaultLayout.PanelSize);
        Assert.Equal(new Size(500, 700), (defaultLayout with { Preferred = new Size(500, 700) }).PanelSize);
        Assert.Equal(new Size(500, 280),
            (defaultLayout with { Preferred = new Size(500, 700), Compact = true }).PanelSize);
        Assert.Equal(310d, (defaultLayout with { Pane = new Size(350, 1000) }).PanelWidth);
        Assert.Equal(300d, (defaultLayout with { Pane = new Size(250, 1000) }).PanelWidth);
        Assert.Equal(414d,
            (defaultLayout with { Pane = new Size(1200, 500), Preferred = new Size(500, 700) }).PanelHeight);
        Assert.Equal(220d,
            (defaultLayout with { Pane = new Size(700, 1000), ComposerAreaHeight = 1000 }).PanelHeight);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-220")] // App/Views/Chat/SubagentPanel.swift:177-181
    public void Fitted_size_resolves_width_before_capping_height()
    {
        var layout = new SubagentPanelLayout
        {
            Pane = new Size(1000, 700),
            ComposerAreaHeight = 180
        };

        Assert.Equal(new Size(600, 436), layout.Fitted(new Size(600, 900)));
        Assert.Equal(new Size(300, 300), layout.Fitted(new Size(100, 300)));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-221")] // App/Views/Chat/SubagentPanel.swift:143-163
    public void Composer_fit_reserve_and_vertical_room_are_independent_results()
    {
        var exactSingleSideFit = new SubagentPanelLayout
        {
            Pane = new Size(812, 700),
            ComposerAreaHeight = 200
        };

        Assert.True(exactSingleSideFit.SitsBesideComposer);
        Assert.False(exactSingleSideFit.FitsBothSides);
        Assert.Equal(412d, exactSingleSideFit.ComposerReserve);
        Assert.Equal(614d, exactSingleSideFit.VerticalRoom);

        var justTooNarrowForOneSide = exactSingleSideFit with { Pane = new Size(811, 700) };
        Assert.False(justTooNarrowForOneSide.SitsBesideComposer);
        Assert.False(justTooNarrowForOneSide.FitsBothSides);
        Assert.Equal(0d, justTooNarrowForOneSide.ComposerReserve);
        Assert.Equal(416d, justTooNarrowForOneSide.VerticalRoom);

        var exactBothSidesFit = exactSingleSideFit with { Pane = new Size(1224, 700) };
        Assert.True(exactBothSidesFit.SitsBesideComposer);
        Assert.True(exactBothSidesFit.FitsBothSides);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-222")] // App/Views/Chat/SubagentPanel.swift:187-201
    public void Docked_origins_align_with_the_reserved_chat_column_or_the_pane_corner()
    {
        var beside = new SubagentPanelLayout
        {
            Pane = new Size(1400, 1000),
            ComposerAreaHeight = 200
        };
        var reserve = new PanelReserve { Leading = 412, Trailing = 412 };

        Assert.Equal(new Point(20, 72), beside.DockedOrigin(PanelDockCorner.TopLeading, reserve));
        Assert.Equal(new Point(980, 72), beside.DockedOrigin(PanelDockCorner.TopTrailing, reserve));
        Assert.Equal(new Point(20, 486), beside.DockedOrigin(PanelDockCorner.BottomLeading, reserve));
        Assert.Equal(new Point(980, 486), beside.DockedOrigin(PanelDockCorner.BottomTrailing, reserve));

        var aboveComposer = new SubagentPanelLayout
        {
            Pane = new Size(700, 700),
            ComposerAreaHeight = 200
        };

        Assert.Equal(new Point(20, 72), aboveComposer.DockedOrigin(PanelDockCorner.TopLeading, new PanelReserve()));
        Assert.Equal(new Point(280, 72), aboveComposer.DockedOrigin(PanelDockCorner.TopTrailing, new PanelReserve()));
        Assert.Equal(new Point(20, 138), aboveComposer.DockedOrigin(PanelDockCorner.BottomLeading, new PanelReserve()));
        Assert.Equal(new Point(280, 138), aboveComposer.DockedOrigin(PanelDockCorner.BottomTrailing, new PanelReserve()));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-223")] // App/Views/Chat/SubagentPanel.swift:5-6,49-97
    public void Floating_panel_kinds_and_stack_slots_match_the_source()
    {
        Assert.IsType<FloatingPanelID.Subagent>(new FloatingPanelID.Subagent());
        Assert.IsType<FloatingPanelID.Hydra>(new FloatingPanelID.Hydra());
        Assert.IsType<FloatingPanelID.Popped>(new FloatingPanelID.Popped());
        Assert.IsType<FloatingPanelID.Usage>(new FloatingPanelID.Usage());
        Assert.IsType<FloatingPanelID.Auto>(new FloatingPanelID.Auto(Guid.Empty));

        var layout = new SubagentPanelLayout
        {
            Pane = new Size(1200, 900),
            ComposerAreaHeight = 180,
            Preferred = new Size(300, 300)
        };
        var docks = new PanelDocks(layout, new PanelReserve());

        Assert.Equal(new Point(docks.TopLeading.X, 72),
            docks.Stacked(PanelDockCorner.TopLeading, 0, 3, layout));
        Assert.Equal(new Point(docks.TopLeading.X, 329),
            docks.Stacked(PanelDockCorner.TopLeading, 1, 3, layout));
        Assert.Equal(new Point(docks.BottomLeading.X, 586),
            docks.Stacked(PanelDockCorner.BottomLeading, 0, 3, layout));
        Assert.Equal(new Point(docks.TopTrailing.X, 72),
            docks.Stacked(PanelDockCorner.TopTrailing, 0, 1, layout));

        Assert.Equal((PanelDockCorner.TopLeading, 0), PanelDocks.Corner(0, 4, PanelDockSide.Leading));
        Assert.Equal((PanelDockCorner.TopLeading, 1), PanelDocks.Corner(1, 4, PanelDockSide.Leading));
        Assert.Equal((PanelDockCorner.BottomLeading, 1), PanelDocks.Corner(2, 4, PanelDockSide.Leading));
        Assert.Equal((PanelDockCorner.BottomLeading, 0), PanelDocks.Corner(3, 4, PanelDockSide.Leading));
        Assert.Equal((PanelDockCorner.TopTrailing, 0), PanelDocks.Corner(0, 1, PanelDockSide.Trailing));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-224")] // App/Views/Chat/SubagentPanel.swift:206-225
    public void Dock_corner_matches_the_source_hysteresis_boundaries()
    {
        var layout = new SubagentPanelLayout
        {
            Pane = new Size(1200, 900),
            ComposerAreaHeight = 180,
            Preferred = new Size(300, 300)
        };
        var docks = new PanelDocks(layout, new PanelReserve());

        Assert.Equal(PanelDockCorner.TopLeading,
            layout.DockCorner(new Point(505.99, 273), PanelDockCorner.TopLeading, docks));
        Assert.Equal(PanelDockCorner.TopTrailing,
            layout.DockCorner(new Point(506, 273), PanelDockCorner.TopLeading, docks));
        Assert.Equal(PanelDockCorner.TopTrailing,
            layout.DockCorner(new Point(506.01, 272.99), PanelDockCorner.TopLeading, docks));
        Assert.Equal(PanelDockCorner.BottomTrailing,
            layout.DockCorner(new Point(450, 329), PanelDockCorner.BottomTrailing, docks));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-224")] // App/Views/Chat/SubagentPanel.swift:229-241
    public void Slot_index_rounds_to_the_nearest_slot_and_clamping_keeps_an_inset()
    {
        var layout = new SubagentPanelLayout
        {
            Pane = new Size(1200, 900),
            ComposerAreaHeight = 180,
            Preferred = new Size(300, 300)
        };
        var docks = new PanelDocks(layout, new PanelReserve());

        Assert.Equal(2, layout.SlotIndex(new Point(0, 500), 3, docks));
        Assert.Equal(1, layout.SlotIndex(new Point(0, 200.5), 3, docks));
        Assert.Equal(0, layout.SlotIndex(new Point(0, -100), 3, docks));
        Assert.Equal(new Point(8, 8), layout.Clamped(new Point(-100, -100)));

        var paneSmallerThanPanel = layout with { Pane = new Size(250, 200) };
        Assert.Equal(new Point(8, 8), paneSmallerThanPanel.Clamped(new Point(100, 100)));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-225")] // App/Views/Chat/SubagentPanel.swift:449-495
    public void Drag_state_clamps_moves_reclamps_and_releases_an_unmoved_panel_as_nil()
    {
        using var runtime = new TestRuntime();
        var layout = new SubagentPanelLayout
        {
            Pane = new Size(900, 700),
            ComposerAreaHeight = 180,
            Preferred = new Size(300, 300)
        };
        var drag = new PanelDragState();

        runtime.OnMain(() =>
        {
            Assert.Null(drag.Release());
            Assert.Equal(new Point(8, 8), drag.Move(new Size(-100, -100), new Point(20, 20), layout));
            drag.Reclamp(layout with { Pane = new Size(250, 200) });
            Assert.Equal(new Point(8, 8), drag.Position);
            Assert.Equal(new Point(8, 8), drag.Release());
            Assert.Null(drag.Position);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-228")] // App/Views/Chat/SubagentPanel.swift:550-589
    public void Resize_axes_and_resize_state_preserve_the_grabbed_size_until_end()
    {
        using var runtime = new TestRuntime();
        var state = new PanelResizeState();

        runtime.OnMain(() =>
        {
            Assert.False(PanelResizeAxes.Width.Contains(PanelResizeAxes.Height));
            Assert.True(PanelResizeAxes.Both.Contains(PanelResizeAxes.Width));
            Assert.True(PanelResizeAxes.Both.Contains(PanelResizeAxes.Height));
            Assert.False(state.IsActive);

            var grabbed = new Size(420, 360);
            Assert.Equal(grabbed, state.Begin(grabbed));
            Assert.Equal(grabbed, state.Begin(new Size(500, 500)));
            Assert.True(state.IsActive);

            state.LiveSize = new Size(450, 380);
            state.LiveUsageHeight = 440;
            Assert.Equal(new Size(450, 380), state.LiveSize);
            Assert.Equal(440d, state.LiveUsageHeight);

            state.End();
            Assert.False(state.IsActive);
            Assert.Null(state.Start);
            Assert.Equal(new Size(450, 380), state.LiveSize);
            Assert.Equal(440d, state.LiveUsageHeight);
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-072")] // App/Views/Chat/SubagentPanel.swift:593-638
    public void Resize_grip_dimensions_match_the_source()
    {
        var edge = typeof(PanelResizeGrips).GetField("Edge", BindingFlags.NonPublic | BindingFlags.Static);
        var corner = typeof(PanelResizeGrips).GetField("Corner", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(edge);
        Assert.NotNull(corner);
        Assert.Equal(6d, edge.GetRawConstantValue());
        Assert.Equal(16d, corner.GetRawConstantValue());
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-231")] // App/Views/Chat/SubagentPanel.swift:365-410
    [Trait("Req", "UI-HYDRA-066")] // App/Views/Chat/SubagentPanel.swift:365-410
    public void Working_box_displays_the_running_name_as_one_accessible_label()
    {
        headless.OnUiThread(() =>
        {
            var constructor = typeof(SubagentWorkingBox).GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null!,
                [typeof(string), typeof(bool)],
                modifiers: null);
            Assert.NotNull(constructor);

            var box = Assert.IsAssignableFrom<Control>(constructor.Invoke(["Helper", true]));
            var texts = LogicalExtensions.GetSelfAndLogicalDescendants(box)
                .OfType<TextBlock>()
                .Select(block => block.Text)
                .ToArray();

            Assert.Contains("Helper working", texts);
            Assert.Equal("Helper working", AutomationProperties.GetName(box));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-232")] // App/Views/Chat/SubagentPanel.swift:418-431
    [Trait("Req", "UI-HYDRA-068")] // App/Views/Chat/SubagentPanel.swift:424-430
    [Trait("Req", "UI-HYDRA-069")] // App/Views/Chat/SubagentPanel.swift:425
    [Trait("Req", "UI-HYDRA-070")] // App/Views/Chat/SubagentPanel.swift:427
    [Trait("Req", "UI-HYDRA-071")] // App/Views/Chat/SubagentPanel.swift:428
    public void Running_helper_popover_has_the_source_wording_and_width()
    {
        headless.OnUiThread(() =>
        {
            const string title = "Helper";
            var constructor = typeof(ClosePanelPopover).GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null!,
                [typeof(string), typeof(Action)],
                modifiers: null);
            Assert.NotNull(constructor);

            var popover = Assert.IsAssignableFrom<Control>(constructor.Invoke([title, (Action)(() => { })]));
            var textBlocks = LogicalExtensions.GetSelfAndLogicalDescendants(popover)
                .OfType<TextBlock>()
                .Select(block => block.Text)
                .ToArray();

            Assert.Contains("Stop this helper?", textBlocks);
            Assert.Contains("\U0000201CHelper\U0000201D is still working. Closing the panel stops its turn. What it has done so far stays, and its chat moves to the sidebar under this one.", textBlocks);
            var itemNames = LogicalExtensions.GetSelfAndLogicalDescendants(popover)
                .OfType<Button>()
                .Select(button => AutomationProperties.GetName(button))
                .ToArray();
            Assert.Contains("Close and stop", itemNames);
            Assert.Contains("Keep it working", itemNames);

            var width = LogicalExtensions.GetSelfAndLogicalDescendants(popover)
                .OfType<Control>()
                .Where(control => control.Width == 300)
                .ToArray();
            Assert.NotEmpty(width);
        });
    }
}
