using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views;
using Marrow.App.Views.Composer;
using Marrow.App.Window;
using Marrow.Core.Port;
using Marrow.Services.Store;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views;

// B3 52bc792: RootView.swift:77-79,94-110; ComposerTextView.swift:180-187.
// A consumed child drop must never invoke the ancestor's project/thread fallback.
[Collection("Headless")]
public sealed class RootViewComposerDropBoundaryTests(HeadlessSession headless)
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
        Dispatcher.UIThread.RunJobs();
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

    private static string LocalPath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

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

    [Fact]
    [Trait("Req", "PR-WIN-064")]
    [Trait("Req", "PR-WIN-065")]
    public void Hit_tested_mixed_drop_is_consumed_once_by_the_selected_composer_without_root_fallback() => headless.OnUiThread(() =>
    {
        // The chat's Hydra button opens its intro popover on first appearance (HydraButton.swift:93); a headless window
        // has no popup platform, so popovers are recorded (as ChatHeadlessFixtures does).
        using var popovers = Presentation.Presenter.Override(new RecordingPresenter());
        using var harness = new AppModelHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var cursor = DragHandleView.CursorPlatform.Override(new ChatNoCursor());
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var model = harness.MakeModel();
        var firstFolder = harness.NewFolder("child-first");
        var lastFolder = harness.NewFolder("child-last");
        var one = Path.Combine(harness.Scratch, "one %.txt");
        var two = Path.Combine(harness.Scratch, "two-\u00df.txt");
        File.WriteAllText(one, "one");
        File.WriteAllText(two, "two");
        var paths = new[] { one, firstFolder, two, lastFolder };
        var deliveries = new List<string[]>();
        var childEvents = 0;
        var ancestorEvents = 0;
        var childConsumed = false;
        var ancestorConsumed = false;
        DragDropEffects? effect = null;
        Guid selected = default;
        Guid[] projects = [];
        Guid[] threads = [];
        AvaloniaWindow? window = null;
        try
        {
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(SwiftURL.Create("marrow-dropped-path:" + harness.NewFolder("selected").Replace('\\', '/'))!);
                selected = model.NewThread(project)!.Id;
                projects = model.Projects.Select(p => p.Id).ToArray();
                threads = model.Threads.Select(t => t.Id).ToArray();
                window = Show(model);
                var root = Assert.Single(window.GetVisualDescendants().OfType<RootView>());
                var editor = Assert.Single(window.GetVisualDescendants().OfType<ComposerNSTextView>());
                var forward = editor.OnAttach;
                Assert.NotNull(forward);
                editor.OnAttach = sources =>
                {
                    deliveries.Add(sources.Select(source => LocalPath(Assert.IsType<AttachmentSource.File>(source).Path)).ToArray());
                    forward(sources); // Preserve the production ComposerView.Attach -> Storage.Attach path.
                };
                Assert.True(editor.Bounds.Width > 0 && editor.Bounds.Height > 0,
                    "Production composer has no laid-out drop target; do not replace it with a synthetic target.");
                var point = editor.TranslatePoint(new Point(editor.Bounds.Width / 2, editor.Bounds.Height / 2), window);
                Assert.NotNull(point);
                var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(point.Value));
                Assert.True(ReferenceEquals(editor, hit) || hit.GetVisualAncestors().Contains(editor),
                    "Raw-drop point did not hit the actual composer or its owned visual descendant.");
                editor.AddHandler(DragDrop.DropEvent, (_, e) => { childEvents++; childConsumed = e.Handled; },
                    RoutingStrategies.Bubble, handledEventsToo: true);
                root.AddHandler(DragDrop.DropEvent, (_, e) => { ancestorEvents++; ancestorConsumed = e.Handled; },
                    RoutingStrategies.Bubble, handledEventsToo: true);
                window.AddHandler(DragDrop.DropEvent, (_, e) => effect = e.DragEffects,
                    RoutingStrategies.Bubble, handledEventsToo: true);
                using var data = Files(window, paths);
                window.DragDrop(point.Value, RawDragEventType.DragEnter, data, DragDropEffects.Copy, RawInputModifiers.None);
                window.DragDrop(point.Value, RawDragEventType.DragOver, data, DragDropEffects.Copy, RawInputModifiers.None);
                window.DragDrop(point.Value, RawDragEventType.Drop, data, DragDropEffects.Copy, RawInputModifiers.None);
            });
            // Drain real imports before asserting model invariants, including a mutant's wrongly-created thread.
            // Directories are received by the child in order, then skipped by Storage.swift:181-183.
            if (deliveries.Count > 0) WaitForAttachments(model, selected, 2);
            foreach (var unexpected in model.Threads.Where(t => !threads.Contains(t.Id)).Select(t => t.Id).ToArray())
                WaitForAttachments(model, unexpected, 2);
            harness.Main.OnMain(() =>
            {
                Assert.Equal(paths.Select(LocalPath), Assert.Single(deliveries));
                Assert.Equal(1, childEvents);
                Assert.Equal(1, ancestorEvents);
                Assert.True(childConsumed, "Composer did not consume the actual raw drop.");
                Assert.True(ancestorConsumed, "The event reached RootView without the child's consumed state.");
                Assert.Equal(DragDropEffects.Copy, effect);
                Assert.True(projects.SequenceEqual(model.Projects.Select(p => p.Id)),
                    "CONSUMED-CHILD: RootView processed the child's already-consumed folders.");
                Assert.Equal(threads, model.Threads.Select(t => t.Id));
                Assert.Equal(selected, model.SelectedThreadID);
                Assert.Equal(new[] { Path.GetFileName(one), Path.GetFileName(two) },
                    model.Runtime(selected).Draft.Attachments.Select(attachment => attachment.Name));
            });
        }
        finally
        {
            harness.Main.OnMain(() => { window?.Hide(); if (window is not null) window.Content = null; Dispatcher.UIThread.RunJobs(); });
        }
    });
}
