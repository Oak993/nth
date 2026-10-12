using System.Text.Json;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Store;

namespace Marrow.App.Tests;

// nth web surfaces, unit W3c (DESIGN 3.11): the thread's pane workspace is owned by its runtime and saved with the thread. The
// old IsTerminalVisible and IsDiffVisible flags are the workspace's terminal drawer and Changes pane read as the flags they
// were; every change is written to the thread in the library; a restart restores the exact layout; the ratios the user set
// last start the next thread; a layout a newer build wrote keeps its unknown parts; an unreadable one opens at the default
// layout and its text is kept as workspace.bad.json.
[Trait("Decision", "ED-NEW-web-w3c-1")]
public sealed class ThreadRuntimeWorkspaceTests
{
    private static (AppModel Model, IReadOnlyList<ChatThread> Threads) Seed(AppModelHarness h, int count = 1, Func<ChatThread, int, ChatThread>? shape = null)
    {
        var project = AppModelSamples.Project("Workspace", h.NewFolder("workspace"));
        var threads = Enumerable.Range(0, count).Select(i => AppModelSamples.Thread(project) with { SortOrder = i }).Select((thread, i) => shape?.Invoke(thread, i) ?? thread).ToList();
        h.WriteLibrary([project], threads);
        return (h.MakeModel(), threads);
    }

    private static void Flush(AppModelHarness h)
    {
        h.Main.Advance(TimeSpan.FromSeconds(10));
        DiskWriter.Shared.WaitForQueuedWrites();
    }

    private static ChatThread Saved(Guid id) => AppModelHarness.LoadedLibrary().Threads.Single(thread => thread.Id == id);

    [Fact]
    public void The_terminal_flag_is_the_workspaces_drawer_and_the_diff_flag_stays_the_popovers_own()
    {
        using var h = new AppModelHarness();
        var (model, threads) = Seed(h);
        h.Main.OnMain(() =>
        {
            var runtime = model.Runtime(threads[0].Id);
            Assert.Equal(PaneWorkspaceState.Default, runtime.Workspace);
            Assert.False(runtime.IsTerminalVisible);
            Assert.False(runtime.IsDiffVisible);

            runtime.IsTerminalVisible = true;
            Assert.True(runtime.Workspace.IsTerminalInDrawer);
            Assert.True(runtime.TerminalWantsFocus);
            runtime.TerminalWantsFocus = false;
            runtime.IsTerminalVisible = true; // already open: nothing changes
            Assert.False(runtime.TerminalWantsFocus);
            runtime.IsTerminalVisible = false;
            Assert.False(runtime.Workspace.IsTerminalInDrawer);
            Assert.Equal(PaneWorkspaceState.Default, runtime.Workspace);

            // The other way round: the workspace opens the drawer and the flag follows, asking for focus once.
            runtime.Workspace = runtime.Workspace.Open(PaneKind.Terminal);
            Assert.True(runtime.IsTerminalVisible);
            Assert.True(runtime.TerminalWantsFocus);
            runtime.TerminalWantsFocus = false;
            runtime.Workspace = runtime.Workspace.Open(PaneKind.Browser);
            Assert.True(runtime.IsTerminalVisible);
            Assert.False(runtime.TerminalWantsFocus);

            // The diff flag is the legacy popover's own until the Changes pane unit: a Changes pane opened in the workspace does not
            // open the popover, and the popover does not open a pane.
            runtime.Workspace = runtime.Workspace.Open(PaneKind.Changes);
            Assert.False(runtime.IsDiffVisible);
            runtime.Workspace = runtime.Workspace.Close(PaneKind.Changes);
            runtime.IsDiffVisible = true;
            Assert.False(runtime.Workspace.IsOpen(PaneKind.Changes));
            runtime.IsDiffVisible = false;
            Assert.Equal(new[] { PaneKind.Browser }, runtime.Workspace.GridPanes);
        });
    }

    [Fact]
    public void A_flag_is_told_to_its_observers_only_when_it_changes_not_for_every_change_of_the_workspace()
    {
        using var h = new AppModelHarness();
        var (model, threads) = Seed(h);
        h.Main.OnMain(() =>
        {
            var runtime = model.Runtime(threads[0].Id);
            var fired = 0;
            void Arm() => ObservationTracking.Track(() => runtime.IsTerminalVisible, () =>
            {
                fired++;
                Arm();
            });
            Arm();
            runtime.Workspace = runtime.Workspace.Open(PaneKind.Browser).WithRatio("chat", 0.4).Open(PaneKind.Artifacts);
            Assert.Equal(0, fired);
            runtime.Workspace = runtime.Workspace.Open(PaneKind.Terminal);
            Assert.Equal(1, fired);
            runtime.Workspace = runtime.Workspace.Expand(PaneKind.Browser);
            Assert.Equal(1, fired);
            runtime.Workspace = runtime.Workspace.Open(PaneKind.Changes);
            Assert.Equal(1, fired);
            runtime.Workspace = runtime.Workspace.Close(PaneKind.Terminal);
            Assert.Equal(2, fired);
        });
    }

    [Fact]
    public void Every_change_is_saved_with_the_thread_and_a_restart_restores_the_exact_layout()
    {
        using var h = new AppModelHarness();
        var (model, threads) = Seed(h);
        var id = threads[0].Id;
        PaneWorkspaceState state = null!;
        h.Main.OnMain(() =>
        {
            var runtime = model.Runtime(id);
            runtime.Workspace = PaneWorkspaceState.Default.Open(PaneKind.Browser).Open(PaneKind.BackgroundTasks).Open(PaneKind.Artifacts)
                .Open(PaneKind.SideChat).Open(PaneKind.Terminal).JoinAsTab(PaneKind.Artifacts, PaneKind.BackgroundTasks)
                .WithRatio(PaneGutterKeys.Chat, 0.4).WithRatio(PaneGutterKeys.Cols, 0.5625).Expand(PaneKind.Browser);
            state = runtime.Workspace;
        });
        Flush(h);
        Assert.Equal(state, Saved(id).Workspace!.State);
        Assert.Equal(PaneKind.Browser, Saved(id).Workspace!.State.Expanded);

        // A restart: a new model reads the library; the runtime starts from what was saved and asks for no focus.
        var restarted = h.MakeModel();
        h.Main.OnMain(() =>
        {
            var runtime = restarted.Runtime(id);
            Assert.Equal(state, runtime.Workspace);
            Assert.True(runtime.IsTerminalVisible);
            Assert.False(runtime.TerminalWantsFocus);
            Assert.False(runtime.IsDiffVisible);
        });
    }

    [Fact]
    public void A_thread_back_at_the_default_layout_saves_no_workspace()
    {
        using var h = new AppModelHarness();
        var (model, threads) = Seed(h);
        var id = threads[0].Id;
        h.Main.OnMain(() =>
        {
            var runtime = model.Runtime(id);
            runtime.IsTerminalVisible = true;
            runtime.Workspace = runtime.Workspace.Open(PaneKind.Browser);
        });
        Flush(h);
        Assert.NotNull(Saved(id).Workspace);
        h.Main.OnMain(() =>
        {
            var runtime = model.Runtime(id);
            runtime.IsTerminalVisible = false;
            runtime.Workspace = runtime.Workspace.Close(PaneKind.Browser);
            Assert.Equal(PaneWorkspaceState.Default, runtime.Workspace);
        });
        Flush(h);
        Assert.Null(Saved(id).Workspace);
        Assert.DoesNotContain("\"workspace\":", File.ReadAllText(Storage.LibraryURL), StringComparison.Ordinal);
    }

    [Fact]
    public void Each_thread_has_its_own_workspace()
    {
        using var h = new AppModelHarness();
        var (model, threads) = Seed(h, count: 2);
        h.Main.OnMain(() =>
        {
            var first = model.Runtime(threads[0].Id);
            var second = model.Runtime(threads[1].Id);
            first.Workspace = first.Workspace.Open(PaneKind.Browser);
            second.Workspace = second.Workspace.Open(PaneKind.BackgroundTasks).Open(PaneKind.SideChat);
            Assert.Equal(new[] { PaneKind.Browser }, first.Workspace.GridPanes);
            Assert.False(first.Workspace.IsSideChatOpen);
            Assert.Equal(new[] { PaneKind.BackgroundTasks }, second.Workspace.GridPanes);
            first.IsTerminalVisible = true;
            Assert.False(second.IsTerminalVisible);
        });
        Flush(h);
        Assert.Equal(new[] { PaneKind.Browser }, Saved(threads[0].Id).Workspace!.State.GridPanes);
        Assert.Equal(new[] { PaneKind.BackgroundTasks }, Saved(threads[1].Id).Workspace!.State.GridPanes);
        Assert.True(Saved(threads[0].Id).Workspace!.State.IsTerminalInDrawer);
        Assert.False(Saved(threads[1].Id).Workspace!.State.IsTerminalInDrawer);
    }

    [Fact]
    public void The_ratios_the_user_set_last_start_the_next_thread_and_a_reset_forgets_them()
    {
        using var h = new AppModelHarness();
        var (model, threads) = Seed(h, count: 3);
        h.Main.OnMain(() =>
        {
            var first = model.Runtime(threads[0].Id);
            Assert.Empty(first.Workspace.Ratios);
            first.Workspace = first.Workspace.Open(PaneKind.Browser).WithRatio(PaneGutterKeys.Chat, 0.3);
            Assert.Equal(0.3, model.Settings.PaneRatioDefaults[PaneGutterKeys.Chat]);
            first.Workspace = first.Workspace.WithRatio(PaneGutterKeys.Cols, 0.7);

            // A thread opened now starts from them; it still has no panes of its own.
            var second = model.Runtime(threads[1].Id);
            Assert.Equal(0.3, second.Workspace.Ratios[PaneGutterKeys.Chat]);
            Assert.Equal(0.7, second.Workspace.Ratios[PaneGutterKeys.Cols]);
            Assert.Empty(second.Workspace.Order);

            // The first thread resets its chat gutter (double-click): the default is forgotten, the other ratio stays.
            first.Workspace = first.Workspace.WithoutRatio(PaneGutterKeys.Chat);
            Assert.False(model.Settings.PaneRatioDefaults.ContainsKey(PaneGutterKeys.Chat));
            Assert.Equal(0.7, model.Settings.PaneRatioDefaults[PaneGutterKeys.Cols]);
            var third = model.Runtime(threads[2].Id);
            Assert.False(third.Workspace.Ratios.ContainsKey(PaneGutterKeys.Chat));
            Assert.Equal(0.7, third.Workspace.Ratios[PaneGutterKeys.Cols]);
        });
        // They are a setting: they outlive the model.
        var again = h.MakeModel();
        Assert.Equal(0.7, h.Main.OnMain(() => again.Settings.PaneRatioDefaults[PaneGutterKeys.Cols]));
    }

    [Fact]
    public void A_thread_with_a_saved_workspace_keeps_it_whatever_the_last_ratios_are()
    {
        using var h = new AppModelHarness();
        var saved = new StoredWorkspace { State = PaneWorkspaceState.Default.Open(PaneKind.Artifacts).WithRatio(PaneGutterKeys.Chat, 0.25) };
        var (model, threads) = Seed(h, shape: (thread, _) => thread with { Workspace = saved });
        h.Main.OnMain(() =>
        {
            model.Settings.PaneRatioDefaults = new ValueMap<string, double>(new Dictionary<string, double> { [PaneGutterKeys.Chat] = 0.9, [PaneGutterKeys.Cols] = 0.1 });
            var runtime = model.Runtime(threads[0].Id);
            Assert.Equal(saved.State, runtime.Workspace);
            Assert.Equal(0.25, runtime.Workspace.Ratios[PaneGutterKeys.Chat]);
            Assert.False(runtime.Workspace.Ratios.ContainsKey(PaneGutterKeys.Cols));
        });
    }

    [Fact]
    public void A_panes_data_is_kept_with_the_thread_and_comes_back()
    {
        using var h = new AppModelHarness();
        var (model, threads) = Seed(h);
        var id = threads[0].Id;
        string? browser = null;
        h.Main.OnMain(() =>
        {
            var runtime = model.Runtime(id);
            Assert.Null(runtime.GetPaneState(PaneKind.Browser));
            runtime.SetPaneState(PaneKind.Browser, "{ \"tabs\": [ { \"url\": \"https://example.test/\", \"scroll\": 40 } ] }");
            // Kept in the library's form (keys sorted, no white space), so what is read back is what is written.
            using (var kept = JsonDocument.Parse(runtime.GetPaneState(PaneKind.Browser)!))
            {
                var tab = kept.RootElement.GetProperty("tabs")[0];
                Assert.Equal("https://example.test/", tab.GetProperty("url").GetString());
                Assert.Equal(40, tab.GetProperty("scroll").GetInt32());
            }
            Assert.StartsWith("{\"tabs\":[{\"scroll\":40,\"url\":", runtime.GetPaneState(PaneKind.Browser), StringComparison.Ordinal);
            browser = runtime.GetPaneState(PaneKind.Browser);
            runtime.SetPaneState(PaneKind.Artifacts, "{\"id\":\"a1\",\"version\":3}");
            runtime.SetPaneState(PaneKind.Artifacts, "{ nope");
            Assert.Null(runtime.GetPaneState(PaneKind.Artifacts));
        });
        Flush(h);
        var restarted = h.MakeModel();
        Assert.Equal(browser, h.Main.OnMain(() => restarted.Runtime(id).GetPaneState(PaneKind.Browser)));
        Assert.Null(h.Main.OnMain(() => restarted.Runtime(id).GetPaneState(PaneKind.Artifacts)));
    }

    [Fact]
    public void What_a_newer_build_wrote_survives_this_builds_saves()
    {
        using var h = new AppModelHarness();
        var future = PaneWorkspaceJson.Read("{\"v\":4,\"order\":[[\"browser\"],[\"notes\"]],\"lanes\":{\"a\":[1,2]},\"panes\":{\"notes\":{\"text\":\"kept\"}}}").Stored!;
        var (model, threads) = Seed(h, shape: (thread, _) => thread with { Workspace = future });
        var id = threads[0].Id;
        h.Main.OnMain(() =>
        {
            var runtime = model.Runtime(id);
            Assert.Equal(new[] { PaneKind.Browser }, runtime.Workspace.GridPanes);
            runtime.Workspace = runtime.Workspace.Open(PaneKind.BackgroundTasks);
        });
        Flush(h);
        using var document = JsonDocument.Parse(File.ReadAllText(Storage.LibraryURL));
        var workspace = document.RootElement.GetProperty("threads")[0].GetProperty("workspace");
        Assert.Equal(4, workspace.GetProperty("v").GetInt32());
        Assert.Equal("{\"a\":[1,2]}", workspace.GetProperty("lanes").GetRawText());
        Assert.Equal("{\"notes\":{\"text\":\"kept\"}}", workspace.GetProperty("panes").GetRawText());
        Assert.Equal("[[\"tasks\"],[\"browser\"],[\"notes\"]]", workspace.GetProperty("order").GetRawText());
    }

    [Fact]
    public void An_unreadable_workspace_opens_at_the_default_layout_and_its_text_is_kept_as_workspace_bad_json()
    {
        using var h = new AppModelHarness();
        var lines = new List<string>();
        using var sink = Logger.Sink.Override((message, _) => lines.Add(message));
        var project = AppModelSamples.Project("Workspace", h.NewFolder("workspace"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        // Damage the thread's workspace in the file itself: a string where an object belongs.
        var text = File.ReadAllText(Storage.LibraryURL);
        var damaged = text.Replace("\"title\":", "\"workspace\":{\"order\":[[3]]},\"title\":", StringComparison.Ordinal);
        Assert.NotEqual(text, damaged);
        File.WriteAllText(Storage.LibraryURL, damaged);

        var model = h.MakeModel();
        h.Main.OnMain(() =>
        {
            Assert.Null(model.Thread(thread.Id)!.Workspace);
            Assert.Null(model.Thread(thread.Id)!.DamagedWorkspace);
            var runtime = model.Runtime(thread.Id);
            Assert.Equal(PaneWorkspaceState.Default, runtime.Workspace);
            runtime.Workspace = runtime.Workspace.Open(PaneKind.Browser);
        });
        var kept = Path.Combine(Storage.ThreadsDirectory, SwiftUUID.UuidString(thread.Id), "workspace.bad.json");
        Assert.Equal("{\"order\":[[3]]}", File.ReadAllText(kept));
        Assert.Contains(lines, line => line.Contains("workspace.bad.json", StringComparison.Ordinal));
        // The new layout is saved over it, and the library no longer holds the bad text.
        Flush(h);
        Assert.DoesNotContain("[[3]]", File.ReadAllText(Storage.LibraryURL), StringComparison.Ordinal);
        Assert.Equal(new[] { PaneKind.Browser }, Saved(thread.Id).Workspace!.State.GridPanes);
    }
}
