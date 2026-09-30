using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views;
using Marrow.App.Window;
using Marrow.Core.Port;
using Marrow.Services.Store;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views;

// Source: RootView.swift:77-79,94-110 (B3 52bc792). These tests enter through the window's raw drag
// input and hit testing, rather than invoking HandleDropCore or raising routed events on RootView.
[Collection("Headless")]
public sealed class RootViewDropRoutingTests(HeadlessSession headless)
{
    private static AvaloniaWindow Show(AppModel model)
    {
        var host = new Border();
        AppEnvironment.SetAppModel(host, model);
        host.Child = new RootView();
        var window = new AvaloniaWindow { Width = 1000, Height = 720 };
        AppEnvironment.SetWindowLiveResize(host, new WindowLiveResize(window));
        window.Content = host;
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static DataTransfer Files(AvaloniaWindow window, params string[] paths)
    {
        var data = new DataTransfer();
        foreach (var path in paths)
        {
            IStorageItem? item = Directory.Exists(path)
                ? window.StorageProvider.TryGetFolderFromPathAsync(new Uri(path)).GetAwaiter().GetResult()
                : window.StorageProvider.TryGetFileFromPathAsync(new Uri(path)).GetAwaiter().GetResult();
            Assert.NotNull(item);
            data.Add(DataTransferItem.CreateFile(item));
        }
        return data;
    }

    private static DragDropEffects Drop(AvaloniaWindow window, DataTransfer data, Point point, Action? beforeDrop = null)
    {
        DragDropEffects? result = null;
        void Observe(object? sender, DragEventArgs e) => result = e.DragEffects;
        window.AddHandler(DragDrop.DropEvent, Observe, RoutingStrategies.Bubble, handledEventsToo: true);
        try
        {
            window.DragDrop(point, RawDragEventType.DragEnter, data, DragDropEffects.Copy, RawInputModifiers.None);
            window.DragDrop(point, RawDragEventType.DragOver, data, DragDropEffects.Copy, RawInputModifiers.None);
            beforeDrop?.Invoke();
            window.DragDrop(point, RawDragEventType.Drop, data, DragDropEffects.Copy, RawInputModifiers.None);
            Assert.NotNull(result); // The event must have reached the real hit-tested input route.
            return result.Value;
        }
        finally { window.RemoveHandler(DragDrop.DropEvent, Observe); }
    }

    private static void WaitForAttachments(AppModel model, Guid thread, int count)
    {
        var runtime = model.Runtime(thread);
        if (runtime.Draft.Attachments.Count == count) return;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (runtime.Draft.Attachments.Count == count) completed.TrySetResult();
        }
        runtime.PropertyChanged += Changed;
        try { HeadlessSession.RunDispatcherUntil(completed.Task, TimeSpan.FromSeconds(15)); }
        finally { runtime.PropertyChanged -= Changed; }
    }

    [Theory]
    [Trait("Req", "PR-WIN-064")]
    [InlineData(0.08, 0.05)]
    [InlineData(0.75, 0.05)]
    [InlineData(0.90, 0.90)]
    public void Folder_drop_routes_from_window_regions_and_creates_one_thread_in_the_last_folder(double x, double y) => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var cursor = DragHandleView.CursorPlatform.Override(new ChatNoCursor());
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var model = harness.MakeModel();
        var paths = new[] { harness.NewFolder("first"), harness.NewFolder("second"), harness.NewFolder("third") };
        harness.Main.OnMain(() =>
        {
            var window = Show(model);
            try
            {
                using var data = Files(window, paths);
                Assert.Equal(DragDropEffects.Copy, Drop(window, data, new Point(window.ClientSize.Width * x, window.ClientSize.Height * y)));
                Assert.Equal(paths.Select(path => path.Replace('\\', '/')), model.Projects.Select(project => project.Path));
                var thread = Assert.Single(model.Threads);
                Assert.Equal(model.Projects[2].Id, thread.ProjectID);
                Assert.Equal(thread.Id, model.SelectedThreadID);
            }
            finally { window.Hide(); window.Content = null; Dispatcher.UIThread.RunJobs(); }
        });
    });

    [Fact]
    [Trait("Req", "PR-WIN-064")]
    public void Paths_removed_after_drag_enter_are_rejected_at_drop_without_creating_a_project() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var cursor = DragHandleView.CursorPlatform.Override(new ChatNoCursor());
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var model = harness.MakeModel();
        var path = Path.Combine(harness.Scratch, "removed.txt");
        File.WriteAllText(path, "removed before drop");
        harness.Main.OnMain(() =>
        {
            var window = Show(model);
            try
            {
                using var data = Files(window, path);
                Assert.Equal(DragDropEffects.None, Drop(window, data, new Point(window.ClientSize.Width * 0.75, 20), () => File.Delete(path)));
                Assert.Empty(model.Projects);
                Assert.Empty(model.Threads);
                Assert.Null(model.SelectedThreadID);
                Assert.Null(model.Alert);
            }
            finally { window.Hide(); window.Content = null; Dispatcher.UIThread.RunJobs(); }
        });
    });

    [Theory]
    [Trait("Req", "PR-WIN-064")]
    [Trait("Req", "PR-WIN-065")]
    [InlineData(false)]
    [InlineData(true)]
    public void Mixed_drop_attaches_files_in_input_order_to_the_new_last_folder_thread(bool previouslySelected) => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var cursor = DragHandleView.CursorPlatform.Override(new ChatNoCursor());
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var model = harness.MakeModel();
        var first = harness.NewFolder("first");
        var last = harness.NewFolder("last");
        var one = Path.Combine(harness.Scratch, "one %.txt");
        var two = Path.Combine(harness.Scratch, "two-é.txt");
        File.WriteAllText(one, "one");
        File.WriteAllText(two, "two");
        Guid? previous = null;
        Guid created = default;
        AvaloniaWindow? window = null;
        try
        {
            harness.Main.OnMain(() =>
            {
                if (previouslySelected)
                {
                    var project = model.AddProject(SwiftURL.Create("marrow-dropped-path:" + harness.NewFolder("existing").Replace('\\', '/'))!);
                    previous = model.NewThread(project)!.Id;
                }
                window = Show(model);
                using var data = Files(window, one, first, two, last);
                Assert.Equal(DragDropEffects.Copy, Drop(window, data, new Point(window.ClientSize.Width * 0.75, 20)));
                created = model.SelectedThreadID!.Value;
                Assert.NotEqual(previous, created);
                Assert.Equal(last.Replace('\\', '/'), model.Projects[^1].Path);
                Assert.Equal(model.Projects[^1].Id, model.Thread(created)!.ProjectID);
                Assert.Equal(previouslySelected ? 2 : 1, model.Threads.Count);
            });
            // RootView.swift:105 selects the new thread (AppModel.swift:623) before :106 reads selection;
            // Storage.swift:161-174 imports off-main, then appends the files in their original order.
            WaitForAttachments(model, created, 2);
            harness.Main.OnMain(() =>
            {
                Assert.Equal(new[] { Path.GetFileName(one), Path.GetFileName(two) }, model.Runtime(created).Draft.Attachments.Select(attachment => attachment.Name));
                if (previous is { } id) Assert.Empty(model.Runtime(id).Draft.Attachments);
            });
        }
        finally { harness.Main.OnMain(() => { window?.Hide(); if (window is not null) window.Content = null; Dispatcher.UIThread.RunJobs(); }); }
    });

    [Theory]
    [Trait("Req", "PR-WIN-065")]
    [InlineData(false)]
    [InlineData(true)]
    public void File_only_drop_uses_the_current_selection_or_silently_discards(bool selected) => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var cursor = DragHandleView.CursorPlatform.Override(new ChatNoCursor());
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var model = harness.MakeModel();
        var path = Path.Combine(harness.Scratch, "file.txt");
        File.WriteAllText(path, "file");
        AvaloniaWindow? window = null;
        Guid? thread = null;
        try
        {
            harness.Main.OnMain(() =>
            {
                if (selected)
                {
                    var project = model.AddProject(SwiftURL.Create("marrow-dropped-path:" + harness.NewFolder("selected").Replace('\\', '/'))!);
                    thread = model.NewThread(project)!.Id;
                }
                window = Show(model);
                using var data = Files(window, path);
                Assert.Equal(DragDropEffects.Copy, Drop(window, data, new Point(window.ClientSize.Width * 0.75, 20)));
                Assert.Equal(thread, model.SelectedThreadID);
                Assert.Equal(selected ? 1 : 0, model.Projects.Count);
                Assert.Equal(selected ? 1 : 0, model.Threads.Count);
                Assert.Null(model.Alert);
            });
            if (thread is { } id)
            {
                WaitForAttachments(model, id, 1);
                harness.Main.OnMain(() => Assert.Equal("file.txt", Assert.Single(model.Runtime(id).Draft.Attachments).Name));
            }
        }
        finally { harness.Main.OnMain(() => { window?.Hide(); if (window is not null) window.Content = null; Dispatcher.UIThread.RunJobs(); }); }
    });
}
