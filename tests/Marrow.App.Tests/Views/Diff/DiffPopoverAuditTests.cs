using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Diff;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Diff;

// Source: DroppyCode/App/Views/Diff/DiffInspector.swift (B3 52bc792), :520-692.
// PR-GIT-137: recording-presenter routing and lifetime here, the Swift's delayed close of a reopen reproduced (upstream defect 67, contract section 8); the real popup, SendInput outside tap and
// Escape are DiffPopoverNativeTests (native, run under native.lock).
[Collection("Headless")]
public sealed class DiffPopoverAuditTests(HeadlessSession headless)
{
    // The coordinator's private state, read the way the Swift fields are (DiffInspector.swift:526-536).
    private static T Field<T>(DiffPopoverCoordinator coordinator, string name) =>
        (T)typeof(DiffPopoverCoordinator).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(coordinator)!;

    // Upstream defect 67 (porting/upstream_defects.md), reproduced under contract section 8 and ED-196: a reopen onto another anchor stops the
    // monitors, bumps the session, closes the popover and shows it again (:599-612); the popover's didClose then reaches the delegate's Task
    // after the new show and, without checking the session, stops the NEW show's monitors, clears desiredVisible and drops shownRuntime,
    // skipping only the flag and content clear (:640-658). The reopened popover then ignores outside clicks and Escape.
    [Fact]
    [Trait("Req", "PR-GIT-137")] // App/Views/Diff/DiffInspector.swift:599-612,627-658 (the delayed close of a reopen); upstream defect 67
    public void Reopen_to_a_different_anchor_then_the_delayed_close_stops_the_new_shows_monitors_and_clears_desiredVisible_but_keeps_the_flag_and_content() => headless.OnUiThread(() =>
        WithPopover((coordinator, runtime, window, recorder) =>
        {
            var second = Show(new Button { Content = "Review" });
            try
            {
                var target = (Control)second.Content!;
                runtime.ShowDiff(target, Guid.NewGuid());
                coordinator.Reopen(runtime);
                Assert.True(runtime.IsDiffVisible);
                var call = recorder.Calls[^1];
                Assert.True(call.Presented);
                Assert.Same(target, call.Popup.PlacementTarget);
                var content = call.Popup.Child;
                Assert.True(Field<bool>(coordinator, "desiredVisible")); // :603
                Assert.NotNull(Field<object?>(coordinator, "keyMonitor")); // the new show started its monitors (:632)
                Assert.NotNull(Field<object?>(coordinator, "pointerMonitor"));

                Dispatcher.UIThread.RunJobs(); // the delayed popoverDidClose Task (:640)

                Assert.Null(Field<object?>(coordinator, "keyMonitor")); // :643 stopMonitors() stopped the NEW show's monitors
                Assert.Null(Field<object?>(coordinator, "pointerMonitor"));
                Assert.False(Field<bool>(coordinator, "desiredVisible")); // :644
                Assert.Null(Field<object?>(coordinator, "shownRuntime")); // :656
                Assert.True(runtime.IsDiffVisible); // :649-654 skipped: session != pendingSession
                Assert.Same(content, call.Popup.Child);
                Assert.True(Field<bool>(coordinator, "isShown")); // the popup itself was never closed
                Assert.True(recorder.Calls[^1].Presented);

                // Without monitors Escape is not swallowed and closes nothing; a sync does not start them again (:628 show() returns at isShown).
                coordinator.Sync(runtime.IsDiffVisible, runtime);
                Assert.Null(Field<object?>(coordinator, "keyMonitor"));
                var escape = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
                second.RaiseEvent(escape);
                Assert.False(escape.Handled);
                Assert.True(runtime.IsDiffVisible);
                Assert.True(recorder.Calls[^1].Presented);
                var outside = 0;
                window.AddHandler(InputElement.PointerPressedEvent, (_, _) => outside++, RoutingStrategies.Bubble);
                window.MouseDown(new Point(200, 100), MouseButton.Left, RawInputModifiers.None);
                window.MouseUp(new Point(200, 100), MouseButton.Left, RawInputModifiers.None);
                Assert.Equal(1, outside);
                Assert.True(runtime.IsDiffVisible); // an outside press no longer dismisses it
                Assert.True(recorder.Calls[^1].Presented);

                // It still closes the other ways the Swift lists (the X button, the tab, the changes shortcut: the flag turns off and the popover follows),
                // and the next fresh show starts the monitors again.
                runtime.IsDiffVisible = false;
                coordinator.Sync(false, runtime);
                Assert.False(recorder.Calls[^1].Presented);
                runtime.IsDiffVisible = true;
                coordinator.Sync(true, runtime);
                Assert.True(recorder.Calls[^1].Presented);
                var again = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
                second.RaiseEvent(again);
                Assert.True(again.Handled);
                Assert.False(runtime.IsDiffVisible);
                Assert.False(recorder.Calls[^1].Presented);
            }
            finally { second.Close(); }
        }));

    [Fact]
    [Trait("Req", "PR-GIT-137")] // App/Views/Diff/DiffInspector.swift:573-579,640-658 (a close after the delayed close finds shownRuntime nil); upstream defect 67
    public void A_close_by_the_coordinator_after_a_reopen_leaves_the_flag_set_because_the_delayed_close_dropped_shownRuntime() => headless.OnUiThread(() =>
        WithPopover((coordinator, runtime, window, recorder) =>
        {
            var second = Show(new Button { Content = "Review" });
            try
            {
                runtime.ShowDiff((Control)second.Content!, Guid.NewGuid());
                coordinator.Reopen(runtime);
                Dispatcher.UIThread.RunJobs(); // the delayed popoverDidClose Task: shownRuntime = nil (:656)

                coordinator.Close(); // e.g. the chat column going behind another (ComposerView.swift:49)

                Assert.False(recorder.Calls[^1].Presented); // the popover closes
                Assert.True(runtime.IsDiffVisible); // :649-654: the flag is cleared through shownRuntime, which the delayed close dropped
                Assert.Null(Field<object?>(coordinator, "shownRuntime"));
                Assert.False(Field<bool>(coordinator, "isShown"));
                Assert.Null(Field<object?>(coordinator, "keyMonitor"));
            }
            finally { second.Close(); }
        }));

    [Fact]
    [Trait("Req", "PR-GIT-137")] // App/Views/Diff/DiffInspector.swift:640-658,662-686; App/Runtime/ThreadRuntime.swift:383-390
    public void Outside_click_on_another_turns_Review_button_reopens_on_that_button() => headless.OnUiThread(() =>
        WithPopover((coordinator, runtime, window, recorder) =>
        {
            var turn = Guid.NewGuid();
            var button = new Button
            {
                Content = "Review", Width = 100, Height = 40,
                Template = new FuncControlTemplate<Button>((_, _) => new Border { Background = Avalonia.Media.Brushes.Transparent }),
            };
            var second = Show(button);
            var presses = 0;
            button.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
            {
                Assert.False(e.Handled);
                Assert.False(recorder.Calls[^1].Presented);
                presses++;
            }, RoutingStrategies.Tunnel);
            var clicks = 0;
            button.Click += (_, _) => { clicks++; runtime.ShowDiff(button, turn); coordinator.Reopen(runtime); };
            try
            {
                second.UpdateLayout();
                var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), second)!.Value;
                second.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
                second.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(1, presses);
                Assert.Equal(1, clicks);
                Assert.True(runtime.IsDiffVisible);
                Assert.Equal(turn, runtime.DiffSelection);
                Assert.True(recorder.Calls[^1].Presented);
                Assert.Same(button, recorder.Calls[^1].Popup.PlacementTarget);
                Assert.NotNull(recorder.Calls[^1].Popup.Child);
            }
            finally { second.Close(); }
        }));

    [Theory]
    [InlineData(MouseButton.Left)]
    [InlineData(MouseButton.Right)]
    [Trait("Req", "PR-GIT-137")] // App/Views/Diff/DiffInspector.swift:662-666,674-686 (outside press closes first, returns event)
    public void Outside_press_dismisses_before_the_target_and_still_reaches_it(MouseButton button) => headless.OnUiThread(() =>
        WithPopover((coordinator, runtime, window, recorder) =>
        {
            var reached = 0;
            window.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
            {
                Assert.False(runtime.IsDiffVisible);
                Assert.False(recorder.Calls[^1].Presented);
                Assert.False(e.Handled);
                reached++;
            }, RoutingStrategies.Bubble);
            window.MouseDown(new Point(200, 100), button, RawInputModifiers.None);
            window.MouseUp(new Point(200, 100), button, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, reached);
            Assert.False(runtime.IsDiffVisible);
        }));

    [Fact]
    [Trait("Req", "PR-GIT-137")] // App/Views/Diff/DiffInspector.swift:668-692 (Escape is app-wide; monitors removed on close)
    public void Escape_from_another_window_is_consumed_and_handlers_are_removed_on_close() => headless.OnUiThread(() =>
        WithPopover((coordinator, runtime, window, recorder) =>
        {
            var other = Show(new TextBox());
            try
            {
                var reached = 0;
                other.AddHandler(InputElement.KeyDownEvent, (_, _) => reached++, RoutingStrategies.Bubble);
                var ordinary = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.A };
                other.RaiseEvent(ordinary);
                Assert.False(ordinary.Handled);
                Assert.True(runtime.IsDiffVisible);
                var escape = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
                other.RaiseEvent(escape);
                Assert.True(escape.Handled);
                Assert.False(runtime.IsDiffVisible);
                Assert.Equal(1, reached);
                Assert.False(recorder.Calls[^1].Presented);
                var after = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
                other.RaiseEvent(after);
                Assert.False(after.Handled);
                Assert.Equal(2, reached);
            }
            finally { other.Close(); }
        }));

    [Fact]
    [Trait("Req", "PR-GIT-137")] // App/Views/Diff/DiffInspector.swift:662-664 (content window clicks pass through)
    public void Content_window_press_does_not_dismiss_and_a_middle_press_outside_does_not_dismiss() => headless.OnUiThread(() =>
        WithPopover((coordinator, runtime, window, recorder) =>
        {
            var popup = (Popup)typeof(DiffPopoverCoordinator).GetField("popover", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(coordinator)!;
            // The recording seam has no popup backend. A separate headless TopLevel stands in for the content
            // window that Swift tests by identity (:663), without replacing any event handling or expected result.
            var contentWindow = Show(new Border());
            var content = popup.Child;
            popup.Child = contentWindow;
            try
            {
                contentWindow.MouseDown(new Point(10, 10), MouseButton.Left, RawInputModifiers.None);
                contentWindow.MouseUp(new Point(10, 10), MouseButton.Left, RawInputModifiers.None);
                Assert.True(runtime.IsDiffVisible);
                window.MouseDown(new Point(200, 100), MouseButton.Middle, RawInputModifiers.None);
                window.MouseUp(new Point(200, 100), MouseButton.Middle, RawInputModifiers.None);
                Assert.True(runtime.IsDiffVisible);
                Assert.Single(recorder.Calls);
            }
            finally { popup.Child = content; contentWindow.Close(); }
        }));

    [Fact]
    [Trait("Req", "PR-GIT-137")] // App/Views/Diff/DiffInspector.swift:599-612,632,674-692 (single monitor pair across show and reopen); upstream defect 67 for the reopen onto another anchor
    public void Repeated_show_and_reopen_keep_one_monitor_pair_a_reopen_elsewhere_loses_it_to_the_delayed_close_and_close_removes_it() => headless.OnUiThread(() =>
        WithPopover((coordinator, runtime, window, recorder) =>
        {
            var pair = (Key: Field<object?>(coordinator, "keyMonitor"), Pointer: Field<object?>(coordinator, "pointerMonitor"));
            Assert.NotNull(pair.Key);
            Assert.NotNull(pair.Pointer);
            // A sync while it is shown and a reopen onto the anchor it is already on start nothing (:628 guard !popover.isShown; :604).
            coordinator.Sync(true, runtime);
            runtime.ShowDiff((Control)window.Content!, null);
            coordinator.Reopen(runtime);
            Assert.Single(recorder.Calls);
            Assert.Same(pair.Key, Field<object?>(coordinator, "keyMonitor"));
            Assert.Same(pair.Pointer, Field<object?>(coordinator, "pointerMonitor"));

            // A reopen onto another anchor replaces the pair (:606, :632) ...
            var second = Show(new Button { Content = "Review" });
            try
            {
                runtime.ShowDiff((Control)second.Content!, Guid.NewGuid());
                coordinator.Reopen(runtime);
                Assert.Equal(3, recorder.Calls.Count);
                var replaced = Field<object?>(coordinator, "keyMonitor");
                Assert.NotNull(replaced);
                Assert.NotSame(pair.Key, replaced);
                // ... and the delayed close of that reopen takes the new pair away (:640-643): no monitor pair is left.
                Dispatcher.UIThread.RunJobs();
                Assert.Null(Field<object?>(coordinator, "keyMonitor"));
                Assert.Null(Field<object?>(coordinator, "pointerMonitor"));
                Assert.False(Field<bool>(coordinator, "desiredVisible"));
                Assert.Equal(3, recorder.Calls.Count);

                // Close removes whatever is left and a fresh show starts exactly one pair again.
                coordinator.Close();
                Assert.Equal(4, recorder.Calls.Count);
                var after = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
                window.RaiseEvent(after);
                Assert.False(after.Handled);
                Assert.Equal(4, recorder.Calls.Count);
                runtime.IsDiffVisible = true;
                coordinator.Sync(true, runtime);
                Assert.Equal(5, recorder.Calls.Count);
                var fresh = Field<object?>(coordinator, "keyMonitor");
                Assert.NotNull(fresh);
                coordinator.Sync(true, runtime);
                Assert.Same(fresh, Field<object?>(coordinator, "keyMonitor"));
            }
            finally { second.Close(); }
        }));

    private static AvaloniaWindow Show(Control content)
    {
        var window = new AvaloniaWindow { Width = 400, Height = 300, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void WithPopover(Action<DiffPopoverCoordinator, ThreadRuntime, AvaloniaWindow, RecordingPresenter> test)
    {
        using var store = new AppModelHarness();
        var project = AppModelSamples.Project("Diff", store.NewFolder("diff"));
        var thread = AppModelSamples.Thread(project);
        store.WriteLibrary([project], [thread]);
        AppModelHarness.WriteDocument(thread.Id);
        var app = store.MakeModel();
        store.Main.OnMain(() =>
        {
            var runtime = new ThreadRuntime(thread.Id, app);
            var anchor = new Border();
            var window = Show(anchor);
            var recorder = new RecordingPresenter();
            using var presenter = Presentation.Presenter.Override(recorder);
            var coordinator = new DiffPopoverCoordinator();
            try
            {
                coordinator.SetAnchor(anchor);
                coordinator.Sync(false, runtime);
                runtime.IsDiffVisible = true;
                coordinator.Sync(true, runtime);
                Assert.True(Assert.Single(recorder.Calls).Presented);
                test(coordinator, runtime, window, recorder);
            }
            finally { coordinator.Close(); window.Close(); Dispatcher.UIThread.RunJobs(); }
        });
        GC.KeepAlive(app);
    }
}
