using Marrow.Core.Port;
using N = Marrow.App.Tests.Window.Native.RootViewDropNativeRig;

namespace Marrow.App.Tests.Window.Native;

// RootView.swift:77-79 .dropDestination(for: URL.self) { urls, _ in handleDrop(urls) } and :94-110, on the real main HWND. The drag is
// what OLE delivers: the IDropTarget Avalonia registered on the window (RegisterDragDrop) is called with DragEnter, DragOver and Drop and
// a real IDataObject that holds CF_HDROP, the format Explorer drags files in; nothing here raises an Avalonia event or calls HandleDropCore.
// The headless tests (RootViewDropRoutingTests, RootViewTests, RootViewComposerDropBoundaryTests) hold the same cases through Avalonia's
// raw drag input; these hold what only a window can: that the OLE hand-over reaches them.
[Collection("WindowManagerNative")]
public sealed class RootViewDropNativeTests(WindowManagerNativeSession native)
{
    private static string Slash(string path) => path.Replace('\\', '/');

    private static SwiftURL DroppedUrl(string path) => SwiftURL.Create("marrow-dropped-path:" + Slash(path))!;

    [Fact]
    [Trait("Req", "PR-WIN-064")] // RootView.swift:77-79, :103-105: every folder becomes a project, one new thread in the last
    public void A_real_CF_HDROP_of_three_folders_adds_three_projects_in_order_and_opens_one_thread_in_the_third() => native.Run(subject =>
    {
        using var rig = new RootViewDropNativeRig(native, subject);
        var paths = new[] { rig.Folder("first"), rig.Folder("second"), rig.Folder("third") };
        var data = new NativeDropDataObject(paths);
        var point = rig.Screen(0.75, 0.05);
        Assert.NotEqual(0, rig.Enter(data, point)); // DragEnter alone does not ask the handler; DragOver and Drop do
        Assert.Equal(N.DropEffectCopy, rig.Over(point));
        Assert.Equal(N.DropEffectCopy, rig.Drop(data, point)); // :109 the acceptance reaches the drag source
        Assert.True(data.GetDataCalls > 0);
        rig.Until(() => rig.Model.Threads.Count == 1, "the dropped folders never reached the model");
        var (projects, thread, selected) = rig.OnUi(() => (rig.Model.Projects.Select(project => project.Path).ToArray(), rig.Model.Threads.Single(), rig.Model.SelectedThreadID));
        Assert.Equal(paths.Select(Slash), projects);
        Assert.Equal(rig.OnUi(() => rig.Model.Projects[2].Id), thread.ProjectID);
        Assert.Equal(thread.Id, selected);
    }, factType: typeof(RootViewDropNativeTests));

    [Fact]
    [Trait("Req", "PR-WIN-064")] // RootView.swift:100 guard FileManager.default.fileExists(...) else { continue }; :109 the result
    public void A_path_deleted_after_DragEnter_is_refused_at_Drop_and_creates_nothing() => native.Run(subject =>
    {
        using var rig = new RootViewDropNativeRig(native, subject);
        var folder = rig.Folder("removed");
        var data = new NativeDropDataObject([folder]);
        var point = rig.Screen(0.75, 0.05);
        Assert.NotEqual(0, rig.Enter(data, point)); // DragEnter alone does not ask the handler; DragOver and Drop do
        Assert.Equal(N.DropEffectCopy, rig.Over(point));
        Directory.Delete(folder);
        Assert.Equal(0, rig.Drop(data, point)); // DROPEFFECT_NONE: the drop is not accepted
        Thread.Sleep(500);
        var (projects, threads, selected, alert) = rig.OnUi(() => (rig.Model.Projects.Count, rig.Model.Threads.Count, rig.Model.SelectedThreadID, rig.Model.Alert));
        Assert.Equal(0, projects);
        Assert.Equal(0, threads);
        Assert.Null(selected);
        Assert.Null(alert);
    }, factType: typeof(RootViewDropNativeTests));

    [Fact]
    [Trait("Req", "PR-WIN-064")] // RootView.swift:101-108: folders and files in one drag
    [Trait("Req", "PR-WIN-065")] // RootView.swift:106-108 the files go to the selection as it stands after the folder half
    public void A_real_mixed_CF_HDROP_attaches_its_files_in_order_to_the_thread_its_last_folder_opened() => native.Run(subject =>
    {
        using var rig = new RootViewDropNativeRig(native, subject);
        var point = rig.Screen(0.75, 0.05);
        for (var round = 0; round < 2; round++) // the second with a thread already selected: the new thread takes the files, not that one
        {
            var previous = rig.OnUi(() => rig.Model.SelectedThreadID);
            Assert.Equal(round == 1, previous is not null);
            var one = rig.File($"one-{round} %.txt");
            var first = rig.Folder($"first-{round}");
            var two = rig.File($"two-{round}-ß.txt");
            var last = rig.Folder($"last-{round}");
            var data = new NativeDropDataObject([one, first, two, last]);
            Assert.NotEqual(0, rig.Enter(data, point)); // DragEnter alone does not ask the handler; DragOver and Drop do
            Assert.Equal(N.DropEffectCopy, rig.Drop(data, point));
            rig.Until(() => rig.Model.SelectedThreadID is { } id && id != previous, "the folder half never opened a thread");
            var thread = rig.OnUi(() => rig.Model.SelectedThreadID!.Value);
            rig.Until(() => rig.Model.Runtime(thread).Draft.Attachments.Count == 2, "the files never reached the new thread's draft");
            var (names, projectOfThread, lastProject) = rig.OnUi(() => (rig.Model.Runtime(thread).Draft.Attachments.Select(attachment => attachment.Name).ToArray(),
                rig.Model.Thread(thread)!.ProjectID, rig.Model.Projects.Last().Id));
            Assert.Equal([Path.GetFileName(one), Path.GetFileName(two)], names);
            Assert.Equal(lastProject, projectOfThread);
            if (previous is { } old) Assert.Equal(2, rig.OnUi(() => rig.Model.Runtime(old).Draft.Attachments.Count)); // the earlier round's own two files, no more
        }
        Assert.Equal(4, rig.OnUi(() => rig.Model.Projects.Count));
    }, factType: typeof(RootViewDropNativeTests));

    [Fact]
    [Trait("Req", "PR-WIN-065")] // RootView.swift:106-108 if !files.isEmpty, let threadID = model.selectedThreadID { attach }
    public void A_real_CF_HDROP_of_files_attaches_them_in_order_to_the_selected_threads_draft() => native.Run(subject =>
    {
        using var rig = new RootViewDropNativeRig(native, subject);
        var directory = rig.Folder("project");
        var thread = rig.OnUi(() => rig.Model.NewThread(rig.Model.AddProject(DroppedUrl(directory)))!.Id); // selects it (AppModel.swift:623)
        Thread.Sleep(1200); // the chat column lays out
        var one = rig.File("alpha.txt");
        var two = rig.File("beta.txt");
        var data = new NativeDropDataObject([two, one]);
        var point = rig.Screen(0.90, 0.30);
        Assert.NotEqual(0, rig.Enter(data, point)); // DragEnter alone does not ask the handler; DragOver and Drop do
        Assert.Equal(N.DropEffectCopy, rig.Drop(data, point));
        rig.Until(() => rig.Model.Runtime(thread).Draft.Attachments.Count == 2, "the files never reached the draft");
        Assert.Equal([Path.GetFileName(two), Path.GetFileName(one)], rig.OnUi(() => rig.Model.Runtime(thread).Draft.Attachments.Select(attachment => attachment.Name).ToArray()));
        Assert.Equal((1, 1, thread), rig.OnUi(() => (rig.Model.Projects.Count, rig.Model.Threads.Count, rig.Model.SelectedThreadID!.Value))); // a file adds no project and no thread
    }, factType: typeof(RootViewDropNativeTests));

    [Fact]
    [Trait("Req", "PR-WIN-065")] // RootView.swift:106 with no selected thread the files are dropped silently (failure_cases: No thread selected)
    public void A_real_CF_HDROP_of_files_with_no_thread_selected_is_discarded_without_a_message() => native.Run(subject =>
    {
        using var rig = new RootViewDropNativeRig(native, subject);
        var data = new NativeDropDataObject([rig.File("lonely.txt")]);
        var point = rig.Screen(0.75, 0.05);
        Assert.NotEqual(0, rig.Enter(data, point)); // DragEnter alone does not ask the handler; DragOver and Drop do
        Assert.Equal(N.DropEffectCopy, rig.Drop(data, point)); // :109 !files.isEmpty: accepted, then nothing happens
        Thread.Sleep(800);
        var (projects, threads, selected, alert) = rig.OnUi(() => (rig.Model.Projects.Count, rig.Model.Threads.Count, rig.Model.SelectedThreadID, rig.Model.Alert));
        Assert.Equal(0, projects);
        Assert.Equal(0, threads);
        Assert.Null(selected);
        Assert.Null(alert);
    }, factType: typeof(RootViewDropNativeTests));

    [Fact]
    [Trait("Req", "PR-WIN-064")] // ComposerTextView.swift:180-187 consumes a drop on the composer: RootView never sees it
    [Trait("Req", "PR-WIN-065")]
    public void A_real_mixed_CF_HDROP_at_the_composer_is_taken_by_the_composer_once_and_not_again_by_the_window() => native.Run(subject =>
    {
        using var rig = new RootViewDropNativeRig(native, subject);
        var directory = rig.Folder("selected");
        var thread = rig.OnUi(() => rig.Model.NewThread(rig.Model.AddProject(DroppedUrl(directory)))!.Id);
        Thread.Sleep(1200); // the chat column and its composer lay out
        var one = rig.File("one.txt");
        var folder = rig.Folder("not-a-project");
        var two = rig.File("two.txt");
        var data = new NativeDropDataObject([one, folder, two]);
        var point = rig.ComposerCenter();
        Assert.NotEqual(0, rig.Enter(data, point)); // DragEnter alone does not ask the handler; DragOver and Drop do
        Assert.Equal(N.DropEffectCopy, rig.Drop(data, point));
        rig.Until(() => rig.Model.Runtime(thread).Draft.Attachments.Count >= 2, "the composer never took the files");
        Thread.Sleep(1000); // a second delivery by the window would show now
        var (names, projects, threads, selected) = rig.OnUi(() => (rig.Model.Runtime(thread).Draft.Attachments.Select(attachment => attachment.Name).ToArray(),
            rig.Model.Projects.Count, rig.Model.Threads.Count, rig.Model.SelectedThreadID));
        Assert.Equal([Path.GetFileName(one), Path.GetFileName(two)], names);
        Assert.Equal(1, projects); // the folder did not become a project: the window's fallback did not run
        Assert.Equal(1, threads);
        Assert.Equal(thread, selected);
    }, factType: typeof(RootViewDropNativeTests));
}
