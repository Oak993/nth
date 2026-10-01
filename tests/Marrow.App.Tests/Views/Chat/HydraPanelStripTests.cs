using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

/// <summary>
/// The strip across the top of the panel (App/Views/Chat/HydraPanel.swift:33-37, :126-200): an overlay of fixed height, an
/// HStack aligned to the top with spacing 8, whose left and right groups are padded 12 from the top and 14 from their outer
/// edge, with the invisible drag handle filling the room between them at the strip's full height.
/// </summary>
[Collection("Headless")]
public sealed class HydraPanelStripTests(HeadlessSession headless)
{
    private void Run(Action<HydraPanelContext> test, bool poppedOut = false) => ChatHeadlessFixtures.Run(headless, () =>
    {
        using var context = new HydraPanelContext(visible: true, poppedOut: poppedOut);
        context.Tick(1); // the soft-appear of the stage capsule and the pop-out button has settled
        test(context);
    });

    private static Control NameCapsule(HydraPanelContext c) =>
        Assert.Single(c.Find<HydraGlyph>(), g => g.Bounds.Width == 16).FindLogicalAncestorOfType<Border>()!;

    [Fact]
    [Trait("Req", "PR-HYDRA-170")] // HydraPanel.swift:33-37 stripHeight = Chrome.chromeTopPadding + Chrome.capsuleHeight + 6
    public void The_strip_is_twelve_plus_thirty_two_plus_six_points_tall()
    {
        Assert.Equal(12 + 32 + 6, HydraPanel.StripHeight);
        Assert.Equal(50, HydraPanel.StripHeight);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-170")] // HydraPanel.swift:92-94 .overlay(alignment: .top) { strip }; :33 cornerRadius 22; :34-36 "what sits under it leaves it room"
    public void The_strip_is_an_overlay_aligned_to_the_panels_top_and_the_stage_keeps_the_whole_panel() => Run(c =>
    {
        var handle = c.WithTip("Drag to move");
        Assert.Equal(0, c.Where(handle).Y);
        Assert.Equal(HydraPanel.StripHeight, c.Where(handle).Height);
        // The heads capsule and the pop-out and dismiss buttons sit 12 points from the panel's top, not mid-panel.
        Assert.Equal(12, c.Where(c.WithTip("2 heads")).Y);
        Assert.Equal(12, c.Where(c.WithTip("Dismiss the panel; heads keep working")).Y);
        // An overlay takes no room of its own: the stage under it is the whole panel.
        var stage = Assert.Single(c.Stages());
        Assert.Equal(new Rect(0, 0, 420, 280), c.Where(stage));
        // The panel's shape: a 22-point rounded rectangle clip around everything (HydraPanel.cornerRadius).
        Assert.Contains(c.Find<Border>(), b => b.CornerRadius == new CornerRadius(22) && b.ClipToBounds && b.Bounds.Size == new Size(420, 280));
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-170")] // HydraPanel.swift:127 HStack(alignment: .top, spacing: 8); :167-168 left group padded top 12 and leading 14
    public void The_left_group_is_padded_twelve_from_the_top_and_fourteen_from_the_leading_edge_with_eight_between_its_capsules() => Run(c =>
    {
        var heads = c.Where(c.WithTip("2 heads"));
        var name = c.Where(NameCapsule(c));
        Assert.Equal(14, heads.X);
        Assert.Equal(12, heads.Y);
        Assert.Equal(32, heads.Height);
        Assert.Equal(12, name.Y);
        Assert.Equal(32, name.Height);
        // HStack(spacing: 8) around the heads button and the stage capsule (:128).
        Assert.Equal(heads.Right + 8, name.X);
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-170")] // HydraPanel.swift:176-196 right group HStack(spacing: 8), padded top 12 and trailing 14
    public void The_right_group_is_padded_twelve_from_the_top_and_fourteen_from_the_trailing_edge_with_eight_between_its_buttons() => Run(c =>
    {
        var pop = c.Where(c.WithTip("Pop Hank out into its own panel"));
        var dismiss = c.Where(c.WithTip("Dismiss the panel; heads keep working"));
        Assert.Equal(420 - 14, dismiss.Right);
        Assert.Equal(12, dismiss.Y);
        Assert.Equal(12, pop.Y);
        Assert.Equal(pop.Right + 8, dismiss.X);
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-170")] // HydraPanel.swift:127 the three groups of the strip's HStack are 8 points apart
    [Trait("Req", "UI-HYDRA-020")] // HydraPanel.swift:169-175 the handle fills the remaining width at the full strip height
    public void The_drag_area_fills_the_room_between_the_groups_less_eight_points_each_side() => Run(c =>
    {
        var handle = c.Where(c.WithTip("Drag to move"));
        var leftEdge = c.Where(NameCapsule(c)).Right;
        var rightEdge = c.Where(c.WithTip("Pop Hank out into its own panel")).X;
        Assert.Equal(leftEdge + 8, handle.X);
        Assert.Equal(rightEdge - 8, handle.Right);
        Assert.Equal(HydraPanel.StripHeight, handle.Height);
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-170")] // HydraPanel.swift:129, :167-168 popped out: no heads button, so the stage capsule is the left group
    public void A_popped_out_panels_left_group_starts_at_the_leading_padding_with_no_heads_button() => Run(c =>
    {
        Assert.DoesNotContain(c.Tips(), t => t.EndsWith("head", StringComparison.Ordinal) || t.EndsWith("heads", StringComparison.Ordinal));
        var name = c.Where(NameCapsule(c));
        Assert.Equal(14, name.X);
        Assert.Equal(12, name.Y);
        var back = c.Where(c.WithTip("Put Hank back in the team panel"));
        Assert.Equal(420 - 14, back.Right);
        Assert.Equal(12, back.Y);
    }, poppedOut: true);

    [Fact]
    [Trait("Req", "UI-HYDRA-020")] // HydraPanel.swift:171-175 .help("Drag to move"), .accessibilityLabel(Text("Drag to move"))
    public void The_drag_area_is_invisible_and_named_drag_to_move() => Run(c =>
    {
        var handle = c.WithTip("Drag to move");
        Assert.Equal("Drag to move", Avalonia.Automation.AutomationProperties.GetName(handle));
        Assert.Single(c.Find<Control>(), x => ToolTip.GetTip(x) as string == "Drag to move");
        // "Invisible, like a window's title bar with no title": the handle draws nothing of its own.
        var view = Assert.IsType<DragHandleView>(Assert.IsType<PanelDragHandle>(handle).Child);
        Assert.True(view.Background is null or ISolidColorBrush { Color.A: 0 });
        Assert.Null(view.HandleGrip);
    });

    [Fact]
    [Trait("Req", "UI-HYDRA-020")] // HydraPanel.swift:171 PanelDragHandle (SubagentPanel.swift:646-666): open hand over it, closed hand while held, 2 points of slop
    public void The_drag_area_shows_the_open_hand_then_the_closed_hand_and_waits_for_two_points_of_slop() => Run(c =>
    {
        var view = Assert.IsType<DragHandleView>(Assert.IsType<PanelDragHandle>(c.WithTip("Drag to move")).Child);
        var scale = TopLevel.GetTopLevel(view)?.RenderScaling ?? 1;
        Assert.Same(PointerStyleCursors.Get(PointerStyle.GrabIdle, scale), view.HandleCursor);
        Assert.Same(PointerStyleCursors.Get(PointerStyle.GrabActive, scale), view.DragCursor);
        Assert.Same(view.HandleCursor, view.Cursor);
        Assert.Equal(2, view.Slop);
        Assert.Equal(DragHandleView.Axis.Free, view.HandleAxis);
        Assert.True(view.AcceptsFirstMouse(null));
    });
}
