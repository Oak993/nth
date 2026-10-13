using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Diff;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Store;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Window.Native;

// Native evidence for ED-NEW-perf-memory-1: every time the Changes popover opened it left a live DiffInspector listening to the thread's runtime, so the app got slower and heavier
// the more often it was opened in a session. On a real Win32 window and the real Popup, opening and closing the popover many times, by its flag and by a reopen onto another anchor,
// must leave exactly one listener while it is open and none once it is closed. The headless twin (DiffInspectorListenerTests) proves the same on a window host; this proves it
// through the coordinator and the real popup host. Needs a free interactive desktop; the lead takes native.lock around the run. Written, not run.
[Collection("WindowManagerNative")]
public sealed class DiffInspectorListenerNativeTests(WindowManagerNativeSession native)
{
    private static int Listeners(ThreadRuntime runtime) =>
        ((Delegate?)typeof(ObservableModel).GetField("PropertyChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime))?.GetInvocationList().Length ?? 0;

    [Fact]
    [Trait("Req", "PR-GIT-137")] // DiffInspector.swift:515-692 the popover's show and close; native, real popup host
    public void Opening_and_closing_the_changes_popover_many_times_on_a_real_window_leaves_no_listener_behind() => native.Run(_ =>
    {
        var ui = native.OnUi(() =>
        {
            var window = new AvaloniaWindow { Width = 900, Height = 800, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(40, 40) };
            native.Register(window);
            FrameClock.Source.SetProcessDefault(new AvaloniaFrameSource(window));
            window.Template = new FuncControlTemplate<AvaloniaWindow>((owner, _) => new ContentPresenter { Content = owner.Content });
            var tab = new Border { Width = 120, Height = 28, Background = Avalonia.Media.Brushes.Gray };
            var other = new Border { Width = 120, Height = 28, Background = Avalonia.Media.Brushes.DimGray };
            var canvas = new Canvas { Width = 900, Height = 800 };
            Canvas.SetLeft(tab, 40); Canvas.SetTop(tab, 700);
            Canvas.SetLeft(other, 760); Canvas.SetTop(other, 700);
            canvas.Children.Add(tab); canvas.Children.Add(other);
            window.Content = canvas;
            var model = new Marrow.App.AppModel();
            var id = Guid.NewGuid();
            DiskWriter.Shared.WriteSynchronously(new ThreadDocument(id), Storage.ThreadURL(id));
            var runtime = new ThreadRuntime(id, model);
            var coordinator = new DiffPopoverCoordinator();
            coordinator.SetAnchor(tab);
            coordinator.Sync(false, runtime);
            window.Show();
            window.Activate();
            var popup = (Popup)typeof(DiffPopoverCoordinator).GetField("popover", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(coordinator)!;
            return (window, tab, other, runtime, coordinator, popup);
        });
        var (window, tab, other, runtime, coordinator, popup) = ui;
        N.Until(() => native.OnUi(() => window.IsActive), "the test window never became active");
        Thread.Sleep(500);
        var baseline = native.OnUi(() => Listeners(runtime));

        for (var open = 1; open <= 8; open++)
        {
            native.OnUi(() => { runtime.IsDiffVisible = true; coordinator.Sync(true, runtime); });
            N.Until(() => native.OnUi(() => popup.IsOpen), "the changes popover never opened");
            Thread.Sleep(300);
            Assert.Equal(baseline + 1, native.OnUi(() => Listeners(runtime))); // one open inspector, one listener
            native.OnUi(() => { runtime.IsDiffVisible = false; coordinator.Sync(false, runtime); });
            N.Until(() => native.OnUi(() => !popup.IsOpen), "the changes popover never closed");
            Thread.Sleep(300);
            Assert.Equal(baseline, native.OnUi(() => Listeners(runtime)));
        }

        // A reopen onto another anchor replaces the content: the old inspector stops listening at once, so there is still exactly one.
        native.OnUi(() => { runtime.ShowDiff(tab, null); coordinator.Sync(true, runtime); });
        N.Until(() => native.OnUi(() => popup.IsOpen), "the changes popover never opened for the reopen");
        native.OnUi(() => { runtime.ShowDiff(other, Guid.NewGuid()); coordinator.Reopen(runtime); });
        N.Until(() => native.OnUi(() => popup.IsOpen && ReferenceEquals(popup.PlacementTarget, other)), "the popover did not move to the other anchor");
        Thread.Sleep(600); // the delayed close has run
        Assert.Equal(baseline + 1, native.OnUi(() => Listeners(runtime)));
        native.OnUi(() => { runtime.IsDiffVisible = false; coordinator.Sync(false, runtime); });
        N.Until(() => native.OnUi(() => !popup.IsOpen), "the reopened popover never closed");
        Thread.Sleep(300);
        Assert.Equal(baseline, native.OnUi(() => Listeners(runtime)));
    }, nameof(Opening_and_closing_the_changes_popover_many_times_on_a_real_window_leaves_no_listener_behind), typeof(DiffInspectorListenerNativeTests));
}
