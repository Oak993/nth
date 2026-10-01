using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Diff;
using Marrow.UI.Common;
using Xunit;

namespace Marrow.App.Tests.Views.Diff;

// Source: DroppyCode/App/Views/Diff/DiffInspector.swift (B3 52bc792), :520-692 (DiffPopoverCoordinator). PR-GIT-137
// (the dismissal monitors) is covered by DiffPopoverAuditTests.
[Collection("Headless")]
public sealed class DiffPopoverCoordinatorTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-COMPOSER-207")] // App/Views/Diff/DiffInspector.swift:551-559 (the popover closes; both runtimes' flags are cleared; nothing opens on arrival)
    public void Moving_to_another_thread_closes_the_popover_and_clears_the_flag_on_both_runtimes() => headless.OnUiThread(() =>
        DiffPopoverRig.Run(rig =>
        {
            rig.OpenOnFirst();
            Assert.True(rig.IsOpen);
            rig.Second.IsDiffVisible = true;
            var calls = rig.Recorder.Calls.Count;

            rig.Coordinator.Sync(true, rig.Second);

            Assert.False(rig.IsOpen);
            Assert.Equal(calls + 1, rig.Recorder.Calls.Count); // one close, and no open on arrival
            Assert.False(rig.First.IsDiffVisible);
            Assert.False(rig.Second.IsDiffVisible);

            // Back to the first thread: its panel is closed and stays closed.
            rig.Coordinator.Sync(rig.First.IsDiffVisible, rig.First);

            Assert.False(rig.IsOpen);
            Assert.False(rig.First.IsDiffVisible);
            Assert.False(rig.Second.IsDiffVisible);
            Assert.DoesNotContain(rig.Recorder.Calls.Skip(calls), call => call.Presented);
        }));

    [Fact]
    [Trait("Req", "PR-COMPOSER-207")] // App/Views/Diff/DiffInspector.swift:553-554 (the outgoing flag is its own write, made whether or not the popover was open)
    public void The_outgoing_runtimes_flag_is_cleared_even_when_its_popover_was_not_open() => headless.OnUiThread(() =>
        DiffPopoverRig.Run(rig =>
        {
            rig.Coordinator.SetAnchor(rig.Tab);
            rig.Coordinator.Sync(false, rig.First);
            rig.First.IsDiffVisible = true; // flagged, but never synced: no popover is open
            Assert.Empty(rig.Recorder.Calls);

            rig.Coordinator.Sync(false, rig.Second);

            Assert.False(rig.First.IsDiffVisible);
        }));

    [Fact]
    [Trait("Req", "PR-COMPOSER-207")] // App/Views/Diff/DiffInspector.swift:560-566 (the first appearance clears the incoming flag too and does not open)
    public void The_first_appearance_adopts_the_runtime_without_opening_and_clears_its_flag() => headless.OnUiThread(() =>
        DiffPopoverRig.Run(rig =>
        {
            rig.Coordinator.SetAnchor(rig.Tab);
            rig.First.IsDiffVisible = true;

            rig.Coordinator.Sync(true, rig.First);

            Assert.False(rig.First.IsDiffVisible);
            Assert.Empty(rig.Recorder.Calls);
        }));

    [Fact]
    [Trait("Req", "PR-COMPOSER-208")] // App/Views/Diff/DiffInspector.swift:556, :562, :599-612 (an adopted counter is a baseline: only a bump on the served thread opens, once)
    public void Only_a_bump_on_the_served_thread_opens_the_popover_and_only_once() => headless.OnUiThread(() =>
        DiffPopoverRig.Run(rig =>
        {
            rig.First.ShowDiff(rig.Tab, turn: null); // the first thread's counter is 1
            for (var bump = 0; bump < 3; bump++) rig.Second.ShowDiff(rig.Tab, turn: null); // the second's is 3
            Assert.NotEqual(rig.First.DiffOpenRequest, rig.Second.DiffOpenRequest);
            rig.Coordinator.SetAnchor(rig.Tab);

            // First appearance adopts the first thread's counter: no bump, nothing opens.
            rig.Coordinator.Sync(rig.First.IsDiffVisible, rig.First);
            rig.Coordinator.Reopen(rig.First);
            Assert.Empty(rig.Recorder.Calls);

            // The composer moves to the second thread, whose counter merely differs: nothing opens.
            rig.Coordinator.Sync(rig.Second.IsDiffVisible, rig.Second);
            rig.Coordinator.Reopen(rig.Second);
            Assert.False(rig.IsOpen);
            Assert.DoesNotContain(rig.Recorder.Calls, call => call.Presented);

            // A genuine bump on the served thread opens it, once.
            rig.Second.ShowDiff(rig.Tab, turn: null);
            rig.Coordinator.Reopen(rig.Second);
            Assert.True(rig.IsOpen);
            Assert.Single(rig.Recorder.Calls);
            Assert.Same(rig.Tab, rig.LastPopup.PlacementTarget);
            rig.Coordinator.Reopen(rig.Second);
            Assert.Single(rig.Recorder.Calls);

            // The bump was adopted: with the popover closed, the same value is no request.
            rig.Coordinator.Close();
            var afterClose = rig.Recorder.Calls.Count;
            rig.Coordinator.Reopen(rig.Second);
            Assert.Equal(afterClose, rig.Recorder.Calls.Count);

            // Away and back with no bump: nothing opens again.
            rig.Coordinator.Sync(rig.First.IsDiffVisible, rig.First);
            var closed = rig.Recorder.Calls.Count;
            Assert.False(rig.IsOpen);
            rig.Coordinator.Sync(rig.Second.IsDiffVisible, rig.Second);
            rig.Coordinator.Reopen(rig.Second);
            Assert.False(rig.IsOpen);
            Assert.Equal(closed, rig.Recorder.Calls.Count);
        }));

    [Fact]
    [Trait("Req", "PR-COMPOSER-208")] // App/Views/Diff/DiffInspector.swift:599-601 (a reopen naming a runtime that is not served is redirected into sync, a thread switch)
    public void A_reopen_naming_a_runtime_the_coordinator_does_not_serve_opens_nothing() => headless.OnUiThread(() =>
        DiffPopoverRig.Run(rig =>
        {
            rig.Coordinator.SetAnchor(rig.Tab);
            rig.Coordinator.Sync(false, rig.First);
            rig.Second.ShowDiff(rig.Tab, turn: null); // a bump, and the flag set
            Assert.True(rig.Second.IsDiffVisible);

            rig.Coordinator.Reopen(rig.Second);

            Assert.Empty(rig.Recorder.Calls);
            Assert.False(rig.Second.IsDiffVisible); // the thread-switch path cleared the incoming flag

            // It is served now, with its counter adopted: the same value is no request.
            rig.Coordinator.Reopen(rig.Second);
            Assert.Empty(rig.Recorder.Calls);
        }));

    [Fact]
    [Trait("Req", "PR-GIT-138")] // App/Views/Diff/DiffInspector.swift:627-636, :640-657 (a close the coordinator did not start leaves the flag set and the content in place)
    public void A_close_the_coordinator_did_not_start_keeps_the_flag_and_the_inspector() => headless.OnUiThread(() =>
        DiffPopoverRig.Run(rig =>
        {
            rig.OpenOnFirst(); // the very first show: session 1 against pending session 0
            var popup = rig.LastPopup;
            var content = popup.Child;
            Assert.NotNull(content);

            rig.Coordinator.PopoverDidClose(); // the popover dismissing itself
            Dispatcher.UIThread.RunJobs();

            Assert.True(rig.First.IsDiffVisible);
            Assert.Same(content, popup.Child);
        }));

    [Fact]
    [Trait("Req", "PR-GIT-138")] // App/Views/Diff/DiffInspector.swift:573-579, :649-654 (a close the coordinator started clears the flag and drops the content)
    public void A_close_the_coordinator_started_clears_the_flag_and_drops_the_inspector() => headless.OnUiThread(() =>
        DiffPopoverRig.Run(rig =>
        {
            rig.OpenOnFirst();
            var popup = rig.LastPopup;
            Assert.NotNull(popup.Child);

            rig.Coordinator.Close();
            Dispatcher.UIThread.RunJobs();

            Assert.False(rig.First.IsDiffVisible);
            Assert.Null(popup.Child);
            Assert.False(rig.IsOpen);
        }));

    [Theory]
    [InlineData(PreviewPanelEdge.MinY, PlacementMode.Bottom)]
    [InlineData(PreviewPanelEdge.MaxY, PlacementMode.Top)]
    [Trait("Req", "PR-GIT-139")] // App/Views/Diff/DiffInspector.swift:617-620 (the runtime's own anchor and edge win: below for minimum Y, above otherwise)
    public void The_popover_hangs_from_the_requesting_control_on_the_requested_edge(PreviewPanelEdge edge, PlacementMode expected) => headless.OnUiThread(() =>
        DiffPopoverRig.Run(rig =>
        {
            rig.Coordinator.SetAnchor(rig.Tab);
            rig.Coordinator.SetFallbackAnchor(rig.Composer);
            rig.Coordinator.Sync(false, rig.First);

            rig.First.ShowDiff(rig.Row, turn: null, edge);
            rig.Coordinator.Reopen(rig.First);

            Assert.True(rig.IsOpen);
            Assert.Same(rig.Row, rig.LastPopup.PlacementTarget);
            Assert.Equal(expected, rig.LastPopup.Placement);
        }));

    [Fact]
    [Trait("Req", "PR-GIT-139")] // App/Views/Diff/DiffInspector.swift:617-625, :599-612 (a tool row asking while the popover hangs above the tab moves it below the row)
    public void A_tool_row_moves_an_open_popover_from_the_tab_to_itself() => headless.OnUiThread(() =>
        DiffPopoverRig.Run(rig =>
        {
            rig.OpenOnFirst();
            Assert.Same(rig.Tab, rig.LastPopup.PlacementTarget);
            Assert.Equal(PlacementMode.Top, rig.LastPopup.Placement);

            rig.First.ShowDiff(rig.Row, turn: null, PreviewPanelEdge.MinY);
            rig.Coordinator.Reopen(rig.First);

            Assert.True(rig.IsOpen);
            Assert.Same(rig.Row, rig.LastPopup.PlacementTarget);
            Assert.Equal(PlacementMode.Bottom, rig.LastPopup.Placement);
        }));

    [Fact]
    [Trait("Req", "PR-GIT-139")] // App/Views/Diff/DiffInspector.swift:617-625 (the tab, above, when the runtime holds no usable anchor; one outside any window falls through)
    public void An_anchor_outside_any_window_falls_through_to_the_tab_above() => headless.OnUiThread(() =>
        DiffPopoverRig.Run(rig =>
        {
            rig.Coordinator.SetAnchor(rig.Tab);
            rig.Coordinator.SetFallbackAnchor(rig.Composer);
            rig.Coordinator.Sync(false, rig.First);

            rig.First.ShowDiff(new Border(), turn: null, PreviewPanelEdge.MinY); // a row that left the screen
            rig.Coordinator.Reopen(rig.First);

            Assert.True(rig.IsOpen);
            Assert.Same(rig.Tab, rig.LastPopup.PlacementTarget);
            Assert.Equal(PlacementMode.Top, rig.LastPopup.Placement);
        }));

    [Fact]
    [Trait("Req", "PR-GIT-139")] // App/Views/Diff/DiffInspector.swift:621-624 (a tab that is not in a window gives way to the composer, above it)
    public void A_tab_outside_any_window_gives_way_to_the_composer_above() => headless.OnUiThread(() =>
        DiffPopoverRig.Run(rig =>
        {
            rig.Coordinator.SetAnchor(new Border());
            rig.Coordinator.SetFallbackAnchor(rig.Composer);
            rig.Coordinator.Sync(false, rig.First);

            rig.First.ShowDiff(new Border(), turn: null);
            rig.Coordinator.Reopen(rig.First);

            Assert.True(rig.IsOpen);
            Assert.Same(rig.Composer, rig.LastPopup.PlacementTarget);
            Assert.Equal(PlacementMode.Top, rig.LastPopup.Placement);
        }));

    [Fact]
    [Trait("Req", "PR-GIT-139")] // App/Views/Diff/DiffInspector.swift:621 (the tab is tried before the composer when both are in a window)
    public void The_tab_is_preferred_to_the_composer_when_both_are_in_a_window() => headless.OnUiThread(() =>
        DiffPopoverRig.Run(rig =>
        {
            rig.Coordinator.SetFallbackAnchor(rig.Composer);
            rig.Coordinator.SetAnchor(rig.Tab);
            rig.Coordinator.Sync(false, rig.First);

            rig.First.ShowDiff(new Border(), turn: null);
            rig.Coordinator.Reopen(rig.First);

            Assert.Same(rig.Tab, rig.LastPopup.PlacementTarget);
        }));

    [Fact]
    [Trait("Req", "PR-GIT-139")] // App/Views/Diff/DiffInspector.swift:622-624, :628 (with no anchor in a window at all the popover does not show)
    public void With_no_anchor_in_a_window_the_popover_does_not_show() => headless.OnUiThread(() =>
        DiffPopoverRig.Run(rig =>
        {
            rig.Coordinator.SetAnchor(new Border());
            rig.Coordinator.SetFallbackAnchor(new Border());
            rig.Coordinator.Sync(false, rig.First);

            rig.First.ShowDiff(new Border(), turn: null);
            rig.Coordinator.Reopen(rig.First);

            Assert.Empty(rig.Recorder.Calls);
        }));

    [Fact]
    [Trait("Req", "UI-GIT-001")] // App/Views/Diff/DiffInspector.swift:668-672 (Escape, key code 53, closes the popover and is swallowed; any other key passes)
    public void Escape_closes_the_popover_and_is_swallowed_while_other_keys_pass() => headless.OnUiThread(() =>
        DiffPopoverRig.Run(rig =>
        {
            rig.OpenOnFirst();

            var other = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.A };
            rig.Window.RaiseEvent(other);
            Assert.False(other.Handled);
            Assert.True(rig.IsOpen);

            var escape = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
            rig.Window.RaiseEvent(escape);
            Dispatcher.UIThread.RunJobs();

            Assert.True(escape.Handled);
            Assert.False(rig.IsOpen);
            Assert.False(rig.First.IsDiffVisible);
        }));

    [Theory]
    [InlineData(MouseButton.Left)]
    [InlineData(MouseButton.Right)]
    [Trait("Req", "UI-GIT-001")] // App/Views/Diff/DiffInspector.swift:662-666 (a left or right press outside closes it first and still reaches its target)
    public void A_press_outside_closes_the_popover_and_still_reaches_its_target(MouseButton button) => headless.OnUiThread(() =>
        DiffPopoverRig.Run(rig =>
        {
            rig.OpenOnFirst();
            var reached = 0;
            rig.Window.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
            {
                Assert.False(rig.IsOpen); // closed before the target sees the press
                Assert.False(e.Handled);
                reached++;
            }, RoutingStrategies.Bubble);

            rig.Window.MouseDown(new Point(300, 200), button, RawInputModifiers.None);
            rig.Window.MouseUp(new Point(300, 200), button, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, reached);
            Assert.False(rig.First.IsDiffVisible);
        }));

    [Fact]
    [Trait("Req", "UI-GIT-001")] // App/Views/Diff/DiffInspector.swift:521-523, :569-570 (the changes shortcut or the tab turn the flag off, and the popover follows it closed)
    public void Turning_the_flag_off_with_the_shortcut_or_the_tab_closes_the_popover() => headless.OnUiThread(() =>
        DiffPopoverRig.Run(rig =>
        {
            rig.OpenOnFirst();
            Assert.True(rig.IsOpen);

            rig.First.ToggleDiff(); // the shortcut and the tab both flip the thread's flag
            rig.Coordinator.Sync(rig.First.IsDiffVisible, rig.First); // the composer reports the change to the coordinator

            Assert.False(rig.First.IsDiffVisible);
            Assert.False(rig.IsOpen);
        }));
}
