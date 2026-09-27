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
[Collection("Headless")]
public sealed class DiffPopoverAuditTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-GIT-137")] // App/Views/Diff/DiffInspector.swift:599-612,627-658 (close Task follows show session)
    public void Reopen_to_a_different_anchor_keeps_the_runtime_visible_after_close_bookkeeping() => headless.OnUiThread(() =>
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
                Dispatcher.UIThread.RunJobs();
                Assert.True(runtime.IsDiffVisible);
                Assert.Same(content, call.Popup.Child);
                Assert.True((bool)typeof(DiffPopoverCoordinator).GetField("isShown", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(coordinator)!);
                coordinator.Sync(runtime.IsDiffVisible, runtime);
                Assert.True(recorder.Calls[^1].Presented);
                var escape = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
                second.RaiseEvent(escape);
                Assert.True(escape.Handled); // The old close must not remove the new monitor pair.
                Dispatcher.UIThread.RunJobs();
                Assert.False(runtime.IsDiffVisible);
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
    [Trait("Req", "PR-GIT-137")] // App/Views/Diff/DiffInspector.swift:599-612,632,674-692 (single monitor pair across reopen)
    public void Repeated_show_and_reopen_keep_one_monitor_pair_and_close_removes_it() => headless.OnUiThread(() =>
        WithPopover((coordinator, runtime, window, recorder) =>
        {
            coordinator.Sync(true, runtime);
            runtime.ShowDiff((Control)window.Content!, null);
            coordinator.Reopen(runtime);
            Assert.Single(recorder.Calls);
            coordinator.Close();
            Assert.Equal(2, recorder.Calls.Count);
            var escape = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
            window.RaiseEvent(escape);
            Assert.False(escape.Handled);
            Assert.Equal(2, recorder.Calls.Count);
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
