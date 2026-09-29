using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Store;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/ThreadTimeline.swift (B3 52bc792).
// Job-owned proof for App.Views.Chat.ThreadTimeline (opencode). Each Fact below
// proves the WHOLE of one TODO record; STOP records live in ThreadTimelineOcStopTests.cs
// with no Req trait.
[Collection("Headless")]
public sealed class ThreadTimelineOcUnitTests(HeadlessSession headless)
{
    private static readonly Guid Turn = Guid.Parse("12345678-abcd-1234-abcd-123456789abc");
    private static TimelineEntry Entry(string id, TimelineItem.Content content, Guid? turn = null) => new(new TimelineItem(turn, content, id));
    private static TimelineEntry User(string id, Guid? turn = null, string text = "prompt") => Entry(id, new TimelineItem.Content.User(new UserMessage { Text = text }), turn);
    private static TimelineEntry Reply(string id, Guid? turn = null) => Entry(id, new TimelineItem.Content.Assistant(new AssistantMessage { Text = "reply" }), turn);
    private static TimelineEntry Tool(string id, Guid? turn = null, bool head = false) => Entry(id, new TimelineItem.Content.Tool(new ToolCall { Title = id, ToolCallKind = head ? ToolCall.Kind.Agent : ToolCall.Kind.Command }), turn);
    private static TimelineEntry Reason(string id, Guid? turn = null) => Entry(id, new TimelineItem.Content.Reasoning(new ReasoningBlock { Text = "thinking" }), turn);
    private static TurnSummary Summary(Guid turn) => new() { TurnID = turn, Status = TurnStatus.Completed, Duration = 2, FilesChanged = 0, Additions = 0, Deletions = 0 };
    private static ValueList<DisplayBlock> Blocks(TimelineEntry[] entries, bool running = false, string? merge = null, HeadPills? pills = null) => DisplayBlock.Build(entries, TimelineMeta.Build(entries), false, running, merge, pills);
    private static T Invoke<T>(object owner, string name, params object?[] args) => (T)owner.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(owner, args)!;
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;
    private static void Put(object owner, string name, object? value) => owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(owner, value);

    private void WithTimeline(Action<ThreadTimeline, ThreadRuntime, ChromeScrollModel, TimelineScrollState, AppModelHarness> check) => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var h = new AppModelHarness(); using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); var model = new Marrow.App.AppModel(); var id = Guid.NewGuid(); DiskWriter.Shared.WriteSynchronously(new ThreadDocument(id), Storage.ThreadURL(id)); DocumentPrefetch.Shared.Warm([id], urgent: true)!.Completion.GetAwaiter().GetResult(); var runtime = new ThreadRuntime(id, model); var chrome = new ChromeScrollModel(); var state = new TimelineScrollState(); var view = new ThreadTimeline(runtime, chrome, state, "Project", null, false, 480); var host = new Border(); AppEnvironment.SetAppModel(host, model); host.Child = view; var window = new Avalonia.Controls.Window { Content = host, Width = 960, Height = 480 };
        AppEnvironment.SetWindowLiveResize(host, new Marrow.App.Window.WindowLiveResize(window));
        try { window.Show(); window.UpdateLayout(); check(view, runtime, chrome, state, h); } finally { window.Close(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
    });
    private void OnMain(Action body) => headless.OnUiThread(() => { using var runtime = new TestRuntime(); runtime.OnMain(body); });
    private static Point Centre(Avalonia.Controls.Window window, Control control) => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
    private static void Settle(Avalonia.Controls.Window window) { window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static void RealClick(Avalonia.Controls.Window window, Button button)
    {
        var point = Centre(window, button); window.MouseMove(point, RawInputModifiers.None);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None); window.MouseUp(point, MouseButton.Left, RawInputModifiers.None); Settle(window);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-026")] // App/Views/Chat/ThreadTimeline.swift:136-141
    public void Oc_Empty_idle_shows_prompt_with_ids_and_zeroes_chrome_and_jump() => WithTimeline((view, runtime, chrome, state, _) =>
    {
        typeof(ThreadRuntime).GetProperty("IsLoadingHistory")!.SetValue(runtime, true);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        chrome.Update(100); state.ShowsJumpButton = true;
        typeof(ThreadRuntime).GetProperty("IsLoadingHistory")!.SetValue(runtime, false);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var prompt = Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(view));
        Assert.Contains(LogicalQuery.SelfAndDescendants<TextBlock>(prompt), text => text.Text == "Project");
        Assert.Contains(LogicalQuery.SelfAndDescendants<TextBlock>(prompt).Select(text => text.Text ?? ""), text => text.Contains("What should we build", StringComparison.Ordinal));
        Assert.Equal(0, chrome.Progress);
        Assert.False(state.ShowsJumpButton);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-026")] // App/Views/Chat/ThreadTimeline.swift:136-141 (the prompt is shown with the thread id: the picker checks the thread's own project)
    public void Oc_Empty_prompt_checks_the_threads_own_project_in_the_picker() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var h = new AppModelHarness();
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var dirA = h.NewFolder("a");
        var dirB = h.NewFolder("b");
        var model = h.MakeModel();
        var projectA = h.Main.OnMain(() => model.AddProject(new Project("Alpha", dirA).Url));
        var projectB = h.Main.OnMain(() => model.AddProject(new Project("Beta", dirB).Url));
        var thread = h.Main.OnMain(() => model.NewThread(projectA)!);
        var runtime = new ThreadRuntime(thread.Id, model);
        var chrome = new ChromeScrollModel(); var state = new TimelineScrollState();
        var view = new ThreadTimeline(runtime, chrome, state, projectA.Name, null, false, 480);
        var host = new Border(); AppEnvironment.SetAppModel(host, model); host.Child = view;
        var window = new Avalonia.Controls.Window { Content = host, Width = 960, Height = 480 };
        AppEnvironment.SetWindowLiveResize(host, new Marrow.App.Window.WindowLiveResize(window));
        try
        {
            window.Show(); window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var prompt = Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(view));
            var button = LogicalQuery.SelfAndDescendants<Button>(prompt).Single(b => (Avalonia.Controls.ToolTip.GetTip(b) as string) == "Choose another project");
            RealClick(window, button);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var popup = recorder.Calls.Last(c => c.Presented).Popup;
            var choices = popup.Child ?? throw new Xunit.Sdk.XunitException("the project picker never presented");
            var rows = LogicalQuery.SelfAndDescendants<PopoverItem>(choices).ToArray();
            var alphaRow = rows.Single(row => LogicalQuery.SelfAndDescendants<MiddleTruncatingText>(row).Single().FullText == projectA.Name);
            var betaRow = rows.Single(row => LogicalQuery.SelfAndDescendants<MiddleTruncatingText>(row).Single().FullText == projectB.Name);
            Assert.Equal(1, LogicalQuery.SelfAndDescendants<SwiftImage>(alphaRow).Single(i => i.SymbolName == "checkmark").Opacity);
            Assert.Equal(0, LogicalQuery.SelfAndDescendants<SwiftImage>(betaRow).Single(i => i.SymbolName == "checkmark").Opacity);
        }
        finally { window.Close(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-026")] // App/Views/Chat/ThreadTimeline.swift:136-141 (jump-to-latest hidden without animation)
    public void Oc_Empty_idle_hides_jump_button_without_animation() => WithTimeline((view, runtime, chrome, state, _) =>
    {
        typeof(ThreadRuntime).GetProperty("IsLoadingHistory")!.SetValue(runtime, true);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        chrome.Update(100); state.ShowsJumpButton = true;
        Animation? during = null;
        using var probe = new OnChange<bool>(() => state.ShowsJumpButton, (_, _) => during = Transaction.Current);
        typeof(ThreadRuntime).GetProperty("IsLoadingHistory")!.SetValue(runtime, false);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(state.ShowsJumpButton);
        Assert.Null(during);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-026")] // App/Views/Chat/ThreadTimeline.swift:136-141 (the prompt only when there is nothing to show)
    public void Oc_Nonempty_idle_takes_timeline_branch_without_prompt() => WithTimeline((view, runtime, _, _, _) =>
    {
        typeof(ThreadRuntime).GetProperty("Entries")!.SetValue(runtime, new ValueList<TimelineEntry>(new[] { User("u") }));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Empty(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(view));
        Assert.NotEmpty(LogicalQuery.SelfAndDescendants<ScrollViewer>(view));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-027")] // App/Views/Chat/ThreadTimeline.swift:136
    public void Oc_Empty_running_takes_timeline_branch_with_working_block() => WithTimeline((view, runtime, _, _, _) =>
    {
        typeof(ThreadRuntime).GetProperty("Phase")!.GetSetMethod(true)!.Invoke(runtime, [RuntimePhase.Running]);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Empty(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(view));
        Assert.NotEmpty(LogicalQuery.SelfAndDescendants<ScrollViewer>(view));
        Assert.NotEmpty(LogicalQuery.SelfAndDescendants<WorkingIndicator>(view));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-034")] // App/Views/Chat/ThreadTimeline.swift:104-110,1608-1614
    public void Oc_Earlier_pills_insert_before_live_turn_only_without_heads_in_order() => OnMain(() =>
    {
        var cut = DateTimeOffset.FromUnixTimeSeconds(100);
        HydraHeadInfo Head(int index, DateTimeOffset? finish) => new(index, "task", HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated) { FinishedAt = finish };
        var heads = new[] { Head(3, cut.AddSeconds(1)), Head(4, cut), Head(2, cut.AddSeconds(-2)), Head(1, null), Head(3, cut.AddSeconds(-1)) };
        var method = typeof(ThreadTimeline).GetMethod("SplitHeadPills", BindingFlags.Static | BindingFlags.NonPublic)!;
        var split = (HeadPills)method.Invoke(null, [heads, cut])!;
        Assert.Equal(new[] { 1, 2, 3 }, split.Earlier);
        Assert.Equal(new[] { 4 }, split.Later);
        var idleSplit = (HeadPills)method.Invoke(null, [heads, null])!;
        Assert.Empty(idleSplit.Earlier);
        Assert.Equal(new[] { 1, 2, 3, 4, 3 }, idleSplit.Later);
        var pills = new HeadPills { Earlier = new([2, 1]), Later = new([3]) };
        var blocks = Blocks(new[] { User("u", Turn) }, true, pills: pills);
        Assert.Equal(new[] { "hydra-heads-working", "turn-12345678-ABCD-1234-ABCD-123456789ABC-u", "hydra-heads-working-tail" }, blocks.Select(block => block.Id));
        var first = Assert.IsType<DisplayBlock.HeadsWorking>(blocks[0]);
        Assert.False(first.Trailing);
        Assert.Equal(new[] { 2, 1 }, first.Heads);
        Assert.DoesNotContain(Blocks(new[] { Tool("head", Turn, true) }, true, pills: pills), block => block is DisplayBlock.HeadsWorking { Trailing: false });
        Assert.Empty(((HeadPills)method.Invoke(null, [Array.Empty<HydraHeadInfo>(), cut])!).Earlier);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-034")] // App/Views/Chat/ThreadTimeline.swift:1608-1614 (immediately before the LAST UNFINISHED turn)
    public void Oc_Earlier_pills_insert_before_the_last_unfinished_turn() => OnMain(() =>
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var blocks = Blocks(new[] { User("u1", first), User("u2", second) }, true, pills: new HeadPills { Earlier = new([9]) });
        var head = Assert.IsType<DisplayBlock.HeadsWorking>(blocks[1]);
        Assert.False(head.Trailing);
        Assert.Equal(new[] { 9 }, head.Heads);
        Assert.Equal(first, Assert.IsType<DisplayBlock.Turn>(blocks[0]).TurnID);
        Assert.Equal(second, Assert.IsType<DisplayBlock.Turn>(blocks[2]).TurnID);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-034")] // App/Views/Chat/ThreadTimeline.swift:1608-1614 (a turn is running: finished turns do not count)
    public void Oc_Earlier_pills_skip_finished_turns() => OnMain(() =>
    {
        var open = Guid.NewGuid(); var done = Guid.NewGuid();
        var end = Entry("end", new TimelineItem.Content.TurnEnd(Summary(done)), done);
        var blocks = Blocks(new[] { User("u1", open), User("u2", done), end }, true, pills: new HeadPills { Earlier = new([7]) });
        var head = Assert.IsType<DisplayBlock.HeadsWorking>(blocks[0]);
        Assert.False(head.Trailing);
        Assert.Equal(new[] { 7 }, head.Heads);
        Assert.Null(Assert.IsType<DisplayBlock.Turn>(blocks[1]).Summary);
        Assert.NotNull(Assert.IsType<DisplayBlock.Turn>(blocks[2]).Summary);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-034")] // App/Views/Chat/ThreadTimeline.swift:1608-1614 (only while a turn runs)
    public void Oc_Earlier_pills_insert_only_while_a_turn_runs() => OnMain(() =>
    {
        Assert.DoesNotContain(
            Blocks(new[] { User("u", Turn) }, false, pills: new HeadPills { Earlier = new([2, 1]) }),
            block => block is DisplayBlock.HeadsWorking { Trailing: false });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-034")] // App/Views/Chat/ThreadTimeline.swift:1608-1614 (only with heads to show)
    public void Oc_Earlier_pills_insert_only_with_heads_to_show() => OnMain(() =>
    {
        Assert.DoesNotContain(
            Blocks(new[] { User("u", Turn) }, true),
            block => block is DisplayBlock.HeadsWorking);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-034")] // App/Views/Chat/ThreadTimeline.swift:104-110 (idle splits nothing earlier: since is null without a running turn)
    [Trait("Req", "PR-CHAT-035")] // App/Views/Chat/ThreadTimeline.swift:108-110 (idle appends every head later for the same reason)
    public void Oc_Idle_draw_splits_no_head_earlier_and_appends_every_head_later() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var h = new AppModelHarness();
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var t0 = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        HydraHeadInfo Head(long index, DateTimeOffset? finished) =>
            new HydraHeadInfo(index, "task", HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated) with
            { HydraHeadInfoStatus = finished is null ? HydraHeadInfo.Status.Running : HydraHeadInfo.Status.Completed, StartedAt = finished is null ? t0.AddSeconds(5) : t0, FinishedAt = finished };
        var model = h.MakeModel();
        var project = h.Main.OnMain(() => model.AddProject(new Project("P", h.NewFolder("proj")).Url));
        var parent = h.Main.OnMain(() => model.NewThread(project)!);
        foreach (var (index, finished) in new[] { (1L, (DateTimeOffset?)t0.AddSeconds(10)), (2L, (DateTimeOffset?)t0.AddSeconds(20)), (3L, (DateTimeOffset?)null) })
        {
            var info = Head(index, finished);
            var head = h.Main.OnMain(() => model.NewThread(project)!);
            h.Main.OnMain(() => model.UpdateThread(head.Id, thread => thread with { ParentThreadID = parent.Id, Hydra = info }));
        }
        var runtime = new ThreadRuntime(parent.Id, model);
        typeof(ThreadRuntime).GetProperty("TurnStartedAt")!.GetSetMethod(true)!.Invoke(runtime, [t0.AddSeconds(15)]);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        h.Main.OnMain(() =>
        {
            Assert.Equal(3, model.Helpers(parent.Id).Count);
            var hydras = model.Helpers(parent.Id).Select(t => t.Hydra).ToList();
            Assert.All(hydras, h => Assert.NotNull(h));
            Assert.Equal(new[] { 1L, 2L }, HydraHeadsWorkingRow.FinishedHeads(model.Helpers(parent.Id)).Select(head => head.Index));
        });
        var chrome = new ChromeScrollModel(); var state = new TimelineScrollState();
        var view = new ThreadTimeline(runtime, chrome, state, project.Name, null, false, 480);
        var host = new Border(); AppEnvironment.SetAppModel(host, model); host.Child = view;
        var window = new Avalonia.Controls.Window { Content = host, Width = 960, Height = 480 };
        AppEnvironment.SetWindowLiveResize(host, new Marrow.App.Window.WindowLiveResize(window));
        try
        {
            window.Show(); window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var rows = Field<System.Collections.Generic.Dictionary<string, (DisplayBlock Block, RowContext Context, Control Row)>>(view, "rowViews");
            var tail = Assert.IsType<DisplayBlock.HeadsWorking>(rows["hydra-heads-working"].Block);
            Assert.False(tail.Trailing); // idle appends the plain identity (:1615-1617 with isRunning false)
            Assert.Equal("hydra-heads-working", tail.Id);
            Assert.Equal(new[] { 1, 2 }, tail.Heads); // since is null idle: every finished head lands Later
            Assert.Single(rows.Values, pair => pair.Block is DisplayBlock.HeadsWorking); // no Earlier insert without a running turn
        }
        finally { window.Close(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-035")] // App/Views/Chat/ThreadTimeline.swift:108-110,1615-1617
    public void Oc_Later_pills_append_with_trailing_identity_across_turn_boundary() => OnMain(() =>
    {
        var pills = new HeadPills { Later = new([3]) };
        var running = Blocks(new[] { User("u", Turn) }, true, pills: pills);
        var tail = Assert.IsType<DisplayBlock.HeadsWorking>(running[^1]);
        Assert.True(tail.Trailing);
        Assert.Equal("hydra-heads-working-tail", tail.Id);
        var idle = Blocks(new[] { User("u", Turn) }, false, pills: pills);
        var plain = Assert.IsType<DisplayBlock.HeadsWorking>(idle[^1]);
        Assert.False(plain.Trailing);
        Assert.Equal("hydra-heads-working", plain.Id);
        Assert.Equal("hydra-heads-working", Blocks([], pills: pills).Single().Id);
        Assert.DoesNotContain(Blocks(new[] { User("u", Turn) }, true), block => block is DisplayBlock.HeadsWorking);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-035")] // App/Views/Chat/ThreadTimeline.swift:1615-1617 (appended with the later heads: all of them, not the earlier ones)
    public void Oc_Later_append_carries_later_heads_in_full() => OnMain(() =>
    {
        var blocks = Blocks(new[] { User("u", Turn) }, true, pills: new HeadPills { Earlier = new([2, 1]), Later = new([3, 4]) });
        var tail = Assert.IsType<DisplayBlock.HeadsWorking>(blocks[^1]);
        Assert.True(tail.Trailing);
        Assert.Equal(new[] { 3, 4 }, tail.Heads);
        Assert.Equal("hydra-heads-working-tail", tail.Id);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-035")] // App/Views/Chat/ThreadTimeline.swift:1615-1626 (the later-heads pill comes before the merging pill)
    public void Oc_Later_heads_come_before_the_merging_pill() => OnMain(() =>
    {
        var blocks = Blocks(new[] { User("u", Turn) }, true, "merge", new HeadPills { Later = new([3]) });
        var headsAt = blocks.Select((block, index) => (block, index)).First(pair => pair.block is DisplayBlock.HeadsWorking { Trailing: true }).index;
        var mergeAt = blocks.Select((block, index) => (block, index)).First(pair => pair.block is DisplayBlock.Merging).index;
        Assert.True(headsAt < mergeAt);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-035")] // App/Views/Chat/ThreadTimeline.swift:1615-1617 (the later-heads pill renders as a HydraHeadsWorkingRow through row dispatch)
    public void Oc_Later_heads_render_through_row_dispatch() => WithTimeline((view, runtime, _, _, _) =>
    {
        var row = new DisplayBlockView(new DisplayBlock.HeadsWorking(new([3]), false), runtime, new RowContext(null, false, false));
        var host = new Border { Child = row };
        AppEnvironment.SetAppModel(host, AppEnvironment.GetAppModel(view));
        var panel = new Avalonia.Controls.Window { Content = host, Width = 600, Height = 400 };
        try
        {
            panel.Show(); Settle(panel);
            var rendered = Assert.Single(LogicalQuery.SelfAndDescendants<HydraHeadsWorkingRow>(row));
            Assert.Equal(new[] { 3 }, (ValueList<int>)rendered.GetType().GetField("requestedHeads", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(rendered)!);
        }
        finally { panel.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-039")] // App/Views/Chat/ThreadTimeline.swift:1520-1526
    public void Oc_Only_turns_and_plain_user_singles_carry_ticks() => OnMain(() =>
    {
        Assert.True(Blocks(new[] { User("u", Turn), User("u2", Turn) }).Single().HasUserMessage);
        Assert.True(Blocks(new[] { User("plain") }).Single().HasUserMessage);
        var turn = Blocks(new[] { User("u", Turn), Reply("r", Turn) }).Single();
        Assert.True(turn.HasUserMessage);
        Assert.All(Blocks(new[] { Reply("r"), Tool("t") }, true, "merge", new HeadPills { Later = new([1]) }), block => Assert.False(block.HasUserMessage));
        Assert.False(Blocks(new[] { Reply("single") }).Single().HasUserMessage);
        Assert.False(new DisplayBlock.Working(null, new ValueList<TimelineEntry>([])).HasUserMessage);
        Assert.False(new DisplayBlock.Merging("m").HasUserMessage);
        Assert.False(new DisplayBlock.HeadsWorking(new([1]), false).HasUserMessage);
        var grouped = TimelineGroup.Build(new[] { Tool("a", Turn) }, false);
        Assert.False(Blocks(new[] { Tool("a", Turn) }).Single().HasUserMessage);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-039")] // App/Views/Chat/ThreadTimeline.swift:1520-1526 (a turn contributes exactly one tick, from the FIRST user message)
    public void Oc_Minimap_takes_the_first_user_message_of_a_turn() => OnMain(() =>
    {
        var turn = Assert.IsType<DisplayBlock.Turn>(Blocks(new[] { User("u1", Turn, "alpha first"), User("u2", Turn, "beta second") }).Single());
        var entry = Assert.Single(TimelineMinimap.Entries([turn]));
        Assert.Equal(turn.Id, entry.Id);
        Assert.Equal("alpha first", entry.Title);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-039")] // App/Views/Chat/ThreadTimeline.swift:1520-1526 (non-turn and non-single group blocks get no tick)
    public void Oc_Minimap_skips_group_and_working_blocks() => OnMain(() =>
    {
        var blocks = Blocks(new[] { Reply("r"), Tool("t") }, true, "merge", new HeadPills { Later = new([1]) });
        Assert.Contains(blocks, block => block is DisplayBlock.Group);
        Assert.Contains(blocks, block => block is DisplayBlock.Working);
        Assert.Empty(TimelineMinimap.Entries(blocks));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-039")] // App/Views/Chat/ThreadTimeline.swift:1520-1526 (a turn without any user message gets no tick)
    public void Oc_Minimap_skips_a_turn_with_no_user_message() => OnMain(() =>
    {
        var turn = Assert.IsType<DisplayBlock.Turn>(Blocks(new[] { Reply("r1", Turn), Reply("r2", Turn) }).Single());
        Assert.False(turn.HasUserMessage);
        Assert.Empty(TimelineMinimap.Entries([turn]));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-039")] // App/Views/Chat/ThreadTimeline.swift:1520-1526 (every other arm carries no tick)
    public void Oc_Minimap_skips_merging_heads_and_working_arms() => OnMain(() =>
    {
        var single = Assert.Single(TimelineMinimap.Entries([Assert.IsType<DisplayBlock.Group>(Blocks(new[] { User("plain") }).Single())]));
        Assert.Equal("plain", single.Id);
        Assert.Empty(TimelineMinimap.Entries([new DisplayBlock.Merging("m")]));
        Assert.Empty(TimelineMinimap.Entries([new DisplayBlock.HeadsWorking(new([1]), false)]));
        Assert.Empty(TimelineMinimap.Entries([new DisplayBlock.Working(null, new ValueList<TimelineEntry>([]))]));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-039")] // App/Views/Chat/ThreadTimeline.swift:1520-1526 (long titles keep the first eight words plus one ellipsis)
    public void Oc_Minimap_title_keeps_eight_words_plus_ellipsis() => OnMain(() =>
    {
        var turn = Assert.IsType<DisplayBlock.Turn>(Blocks(new[] { User("u", Turn, "w1 w2 w3 w4 w5 w6 w7 w8 w9 w10") }).Single());
        Assert.Equal("w1 w2 w3 w4 w5 w6 w7 w8\U00002026", Assert.Single(TimelineMinimap.Entries([turn])).Title);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-039")] // App/Views/Chat/ThreadTimeline.swift:1520-1526 (ticks follow block order)
    public void Oc_Minimap_ticks_follow_block_order() => OnMain(() =>
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var blocks = Blocks(new[] { User("u1", first, "alpha"), User("u2", second, "beta") });
        Assert.Equal(new[] { "alpha", "beta" }, TimelineMinimap.Entries(blocks).Select(entry => entry.Title).ToArray());
    });

    [Fact]
    [Trait("Req", "PR-CHAT-039")] // App/Views/Chat/ThreadTimeline.swift:1520-1526 (the outline marks only blocks with user messages)
    public void Oc_Outline_marks_only_blocks_with_user_messages() => WithTimeline((view, runtime, _, _, _) =>
    {
        typeof(ThreadRuntime).GetProperty("Entries")!.SetValue(runtime, new ValueList<TimelineEntry>(new[] { User("u", Turn), Reply("r") }));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var tracking = Field<TimelineScrollTracking>(view, "tracking");
        var userIDs = (System.Collections.Generic.HashSet<string>)typeof(TimelineScrollTracking).GetField("userBlockIDs", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tracking)!;
        Assert.Equal("turn-" + Turn.ToString("D").ToUpperInvariant() + "-u", Assert.Single(userIDs));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-045")] // App/Views/Chat/ThreadTimeline.swift:77-81,257-278,595-608,703-707,1804-1811
    public void Oc_Window_constants_identity_anchor_paging_cancel_and_eager_drop() => WithTimeline((view, _, _, _, h) =>
    {
        Assert.Equal(3, TimelineWindow.FirstPaint);
        Assert.Equal(24, TimelineWindow.Initial);
        Assert.Equal(24, TimelineWindow.Page);
        Assert.Equal(96, TimelineWindow.EagerLimit);
        var blocks = Enumerable.Range(0, 60).Select(i => (DisplayBlock)new DisplayBlock.Merging("b" + i)).ToArray();
        Assert.Equal(57, Invoke<int>(view, "WindowStartIndex", (object)blocks));
        Invoke<object?>(view, "LoadEarlier", blocks, 24);
        Assert.Equal(36, Invoke<int>(view, "WindowStartIndex", (object)blocks));
        Invoke<object?>(view, "LoadEarlier", blocks, null);
        Assert.Equal(36, Invoke<int>(view, "WindowStartIndex", (object)blocks));
        Put(view, "historyLoadPending", false);
        Invoke<object?>(view, "LoadEarlier", blocks, null);
        Assert.Equal(12, Invoke<int>(view, "WindowStartIndex", (object)blocks));
        Put(view, "windowStartID", null); Put(view, "historyLoadPending", false);
        var thirty = Enumerable.Range(0, 30).Select(i => (DisplayBlock)new DisplayBlock.Merging("b" + i)).ToArray();
        Invoke<object?>(view, "StartInitialHistoryLoad", (object)thirty);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        h.Main.AdvanceMilliseconds(249); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Null(Field<string?>(view, "windowStartID"));
        h.Main.AdvanceMilliseconds(1); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("b6", Field<string?>(view, "windowStartID"));
        Assert.True(Field<TimelineScrollTracking>(view, "tracking").IsArriving);
        var grown = Enumerable.Range(0, 120).Select(i => (DisplayBlock)new DisplayBlock.Merging("b" + i)).ToArray();
        Put(view, "windowStartID", "b0"); Put(view, "historyLoadPending", false);
        var tracking = Field<TimelineScrollTracking>(view, "tracking");
        tracking.IsPinnedToBottom = true; tracking.NoteGeometry(0, 0, 0);
        Invoke<object?>(view, "NoteArrival", "b119", grown);
        Assert.Equal(96, Invoke<int>(view, "WindowStartIndex", (object)grown));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-045")] // App/Views/Chat/ThreadTimeline.swift:257-278 (paging floors at the first block)
    public void Oc_Paging_floors_at_the_first_block() => WithTimeline((view, _, _, _, _) =>
    {
        var blocks = Enumerable.Range(0, 30).Select(i => (DisplayBlock)new DisplayBlock.Merging("b" + i)).ToArray();
        Put(view, "windowStartID", "b10"); Put(view, "historyLoadPending", false);
        Invoke<object?>(view, "LoadEarlier", blocks, null);
        Assert.Equal("b0", Field<string?>(view, "windowStartID"));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-045")] // App/Views/Chat/ThreadTimeline.swift:595-608 (the initial load floors at the first block)
    public void Oc_Initial_load_floors_at_the_first_block() => WithTimeline((view, _, _, _, h) =>
    {
        var ten = Enumerable.Range(0, 10).Select(i => (DisplayBlock)new DisplayBlock.Merging("b" + i)).ToArray();
        Invoke<object?>(view, "StartInitialHistoryLoad", (object)ten);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        h.Main.AdvanceMilliseconds(251); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("b0", Field<string?>(view, "windowStartID"));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-045")] // App/Views/Chat/ThreadTimeline.swift:257-278 (paging only moves to an earlier target)
    public void Oc_Paging_ignores_a_target_at_the_window_start() => WithTimeline((view, _, _, _, _) =>
    {
        var blocks = Enumerable.Range(0, 60).Select(i => (DisplayBlock)new DisplayBlock.Merging("b" + i)).ToArray();
        Put(view, "historyLoadPending", false);
        Invoke<object?>(view, "LoadEarlier", blocks, 3);
        Assert.False(Field<bool>(view, "historyLoadPending"));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-045")] // App/Views/Chat/ThreadTimeline.swift:257-278 (paging holds the bottom while loading)
    public void Oc_Paging_holds_the_bottom_while_loading() => WithTimeline((view, _, _, _, _) =>
    {
        var blocks = Enumerable.Range(0, 60).Select(i => (DisplayBlock)new DisplayBlock.Merging("b" + i)).ToArray();
        Put(view, "historyLoadPending", false);
        Put(view, "anchorsBottomOnGrowth", false);
        Invoke<object?>(view, "LoadEarlier", blocks, 24);
        Assert.True(Field<bool>(view, "anchorsBottomOnGrowth"));
        Assert.True(Field<bool>(view, "historyLoadPending"));
        Assert.Equal("b36", Field<string?>(view, "windowStartID"));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-045")] // App/Views/Chat/ThreadTimeline.swift:595-608 (the initial load arms the blank watch)
    public void Oc_Initial_load_arms_the_blank_watch() => WithTimeline((view, _, _, _, h) =>
    {
        var thirty = Enumerable.Range(0, 30).Select(i => (DisplayBlock)new DisplayBlock.Merging("b" + i)).ToArray();
        Invoke<object?>(view, "StartInitialHistoryLoad", (object)thirty);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        h.Main.AdvanceMilliseconds(251); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var tracking = Field<TimelineScrollTracking>(view, "tracking");
        Assert.NotNull(typeof(TimelineScrollTracking).GetField("blankWatch", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tracking));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-045")] // App/Views/Chat/ThreadTimeline.swift:595-608 (the initial load warms finished markdown)
    public void Oc_Initial_load_warms_finished_markdown() => WithTimeline((view, runtime, _, _, h) =>
    {
        const string warmed = "warmed dessert spoonsyntax 7z9q";
        typeof(ThreadRuntime).GetProperty("Entries")!.SetValue(runtime, new ValueList<TimelineEntry>(new[] { User("u", Turn), Entry("r", new TimelineItem.Content.Assistant(new AssistantMessage { Text = warmed }), Turn) }));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var thirty = Enumerable.Range(0, 30).Select(i => (DisplayBlock)new DisplayBlock.Merging("b" + i)).ToArray();
        Invoke<object?>(view, "StartInitialHistoryLoad", (object)thirty);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        h.Main.AdvanceMilliseconds(251); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        h.Main.AdvanceMilliseconds(1000); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var cache = typeof(Marrow.UI.Markdown.MarkdownView).GetField("blockCache", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        Assert.True((bool)cache.GetType().GetMethod("Contains")!.Invoke(cache, [warmed])!);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-045")] // App/Views/Chat/ThreadTimeline.swift:595-608 (a cancelled initial load does nothing further)
    public void Oc_Cancelled_initial_load_does_nothing_further() => WithTimeline((view, _, _, _, h) =>
    {
        var thirty = Enumerable.Range(0, 30).Select(i => (DisplayBlock)new DisplayBlock.Merging("b" + i)).ToArray();
        Invoke<object?>(view, "StartInitialHistoryLoad", (object)thirty);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Field<Marrow.Core.Port.TaskHandle?>(view, "initialHistoryTask")!.Cancel();
        h.Main.AdvanceMilliseconds(300); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Null(Field<string?>(view, "windowStartID"));
        Assert.False(Field<TimelineScrollTracking>(view, "tracking").IsArriving);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-045")] // App/Views/Chat/ThreadTimeline.swift:703-707 (the eager drop keeps a ninety-six-block window)
    public void Oc_Eager_drop_keeps_ninety_six_blocks() => WithTimeline((view, _, _, state, _) =>
    {
        var grown = Enumerable.Range(0, 120).Select(i => (DisplayBlock)new DisplayBlock.Merging("b" + i)).ToArray();
        Put(view, "windowStartID", "b24"); Put(view, "historyLoadPending", false);
        var tracking = Field<TimelineScrollTracking>(view, "tracking");
        tracking.IsPinnedToBottom = false; tracking.NoteGeometry(0, 0, 0);
        state.ShowsJumpButton = true;
        Invoke<object?>(view, "NoteArrival", "b119", grown);
        Assert.Equal("b24", Field<string?>(view, "windowStartID"));
        Assert.True(tracking.IsPinnedToBottom);
        Assert.False(state.ShowsJumpButton);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-045")] // App/Views/Chat/ThreadTimeline.swift:703-707 (no eager drop while the reader is followed)
    public void Oc_No_eager_drop_while_a_load_is_pending() => WithTimeline((view, _, _, _, _) =>
    {
        var grown = Enumerable.Range(0, 120).Select(i => (DisplayBlock)new DisplayBlock.Merging("b" + i)).ToArray();
        Put(view, "windowStartID", "b0"); Put(view, "historyLoadPending", true);
        Invoke<object?>(view, "NoteArrival", "b119", grown);
        Assert.Equal("b0", Field<string?>(view, "windowStartID"));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-045")] // App/Views/Chat/ThreadTimeline.swift:257-278 (rows are built for the window only)
    public void Oc_Rows_cover_only_the_window() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var h = new AppModelHarness();
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); var model = new Marrow.App.AppModel(); var id = Guid.NewGuid(); DiskWriter.Shared.WriteSynchronously(new ThreadDocument(id), Storage.ThreadURL(id)); DocumentPrefetch.Shared.Warm([id], urgent: true)!.Completion.GetAwaiter().GetResult(); var runtime = new ThreadRuntime(id, model);
        // A two-pixel window: the EarlierPill can never reach its 0.1 visibility fraction,
        // so its auto-load callback cannot grow the window mid-test; six turns render safely.
        var chrome = new ChromeScrollModel(); var state = new TimelineScrollState(); var view = new ThreadTimeline(runtime, chrome, state, "Project", null, false, 2); var host = new Border(); AppEnvironment.SetAppModel(host, model); host.Child = view; var window = new Avalonia.Controls.Window { Content = host, Width = 960, Height = 2 };
        AppEnvironment.SetWindowLiveResize(host, new Marrow.App.Window.WindowLiveResize(window));
        try
        {
            window.Show(); window.UpdateLayout();
            var entries = Enumerable.Range(0, 6).Select(i => User("u" + i, Guid.NewGuid())).ToArray();
            typeof(ThreadRuntime).GetProperty("Entries")!.SetValue(runtime, new ValueList<TimelineEntry>(entries));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var dict = Field<System.Collections.Generic.Dictionary<string, (DisplayBlock Block, RowContext Context, Control Row)>>(view, "rowViews");
            Assert.Equal(3, dict.Count);
            Assert.Null(Field<string?>(view, "windowStartID"));
        }
        finally { window.Close(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-045")] // App/Views/Chat/ThreadTimeline.swift:703-707 (no arrival processing while the user scrolls)
    public void Oc_No_arrival_processing_while_the_user_scrolls() => WithTimeline((view, _, _, _, _) =>
    {
        var grown = Enumerable.Range(0, 120).Select(i => (DisplayBlock)new DisplayBlock.Merging("b" + i)).ToArray();
        Put(view, "windowStartID", "b0"); Put(view, "historyLoadPending", false);
        var tracking = Field<TimelineScrollTracking>(view, "tracking");
        tracking.IsPinnedToBottom = true; tracking.IsUserScrolling = true;
        Invoke<object?>(view, "NoteArrival", "b119", grown);
        Assert.Equal("b0", Field<string?>(view, "windowStartID"));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-045")] // App/Views/Chat/ThreadTimeline.swift:595-608 (the initial load redraws with the new window)
    public void Oc_Initial_load_redraws_with_the_new_window() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var h = new AppModelHarness();
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); var model = new Marrow.App.AppModel(); var id = Guid.NewGuid(); DiskWriter.Shared.WriteSynchronously(new ThreadDocument(id), Storage.ThreadURL(id)); DocumentPrefetch.Shared.Warm([id], urgent: true)!.Completion.GetAwaiter().GetResult(); var runtime = new ThreadRuntime(id, model);
        // Two-pixel window again: the hidden pill cannot auto-load, so only the initial
        // load's own redraw moves the window.
        var chrome = new ChromeScrollModel(); var state = new TimelineScrollState(); var view = new ThreadTimeline(runtime, chrome, state, "Project", null, false, 2); var host = new Border(); AppEnvironment.SetAppModel(host, model); host.Child = view; var window = new Avalonia.Controls.Window { Content = host, Width = 960, Height = 2 };
        AppEnvironment.SetWindowLiveResize(host, new Marrow.App.Window.WindowLiveResize(window));
        try
        {
            window.Show(); window.UpdateLayout();
            var turns = new[] { Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb0"), Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1"), Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2"), Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb3"), Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb4"), Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb5") };
            // Finished turns render statically, so the 251 time steps below stay cheap.
            var entries = turns.SelectMany((turn, i) => new[] { User("u" + i, turn), Entry("e" + i, new TimelineItem.Content.TurnEnd(Summary(turn)), turn) }).ToArray();
            typeof(ThreadRuntime).GetProperty("Entries")!.SetValue(runtime, new ValueList<TimelineEntry>(entries));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            string Id(int i) => "turn-" + turns[i].ToString("D").ToUpperInvariant() + "-u" + i;
            Assert.Equal(3, Field<System.Collections.Generic.Dictionary<string, (DisplayBlock Block, RowContext Context, Control Row)>>(view, "rowViews").Count);
            var blocks = DisplayBlock.Build(entries, TimelineMeta.Build(entries), false, false, null, null);
            Invoke<object?>(view, "StartInitialHistoryLoad", (object)blocks.ToArray());
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            h.Main.AdvanceMilliseconds(251); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(Id(0), Field<string?>(view, "windowStartID"));
            Assert.Equal(6, Field<System.Collections.Generic.Dictionary<string, (DisplayBlock Block, RowContext Context, Control Row)>>(view, "rowViews").Count);
            Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<TextBlock>(view).Select(text => text.Text ?? ""), text => text.Contains("earlier messages", StringComparison.Ordinal));
        }
        finally { window.Close(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-121")] // App/Views/Chat/ThreadTimeline.swift:2107-2119,2152-2176,822-829
    public void Oc_Working_click_opens_thinking_and_steps_and_follows_end_despite_jump() => WithTimeline((view, runtime, _, state, _) =>
    {
        typeof(ThreadRuntime).GetProperty("Entries")!.SetValue(runtime, new ValueList<TimelineEntry>(new[] { User("probe") }));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var card = new WorkingIndicator(runtime, SwiftDate.Now, 1, new([new ThinkingStep("thought", false)]), new([Tool("a")]), new([]), true);
        var window = (Avalonia.Controls.Window)TopLevel.GetTopLevel(view)!;
        var host = new Border { Child = card };
        AppEnvironment.SetAppModel(host, AppEnvironment.GetAppModel(view));
        var panel = new Avalonia.Controls.Window { Content = host, Width = 600, Height = 400 };
        try
        {
            panel.Show(); Settle(panel);
            Assert.Empty(LogicalQuery.SelfAndDescendants<Marrow.UI.Markdown.MarkdownView>(card));
            var tracking = Field<TimelineScrollTracking>(view, "tracking");
            ThreadTimeline.SetRevealTimelineEnd(card, Field<TimelineRevealBox>(view, "revealBox"));
            tracking.IsPinnedToBottom = false; state.ShowsJumpButton = true; tracking.NoteGeometry(0, 900, 0);
            Put(view, "viewportHeight", 480.0);
            Put(view, "anchorsBottomOnGrowth", false);
            Animation? during = null;
            using var probe = new OnChange<bool>(() => tracking.IsPinnedToBottom, (_, _) => during = Transaction.Current);
            RealClick(panel, LogicalQuery.SelfAndDescendants<Button>(card).Single());
            Assert.Single(LogicalQuery.SelfAndDescendants<Marrow.UI.Markdown.MarkdownView>(card));
            Assert.Single(LogicalQuery.SelfAndDescendants<WorkSteps>(card));
            Assert.Equal("Hide these steps", Avalonia.Controls.ToolTip.GetTip(LogicalQuery.SelfAndDescendants<Button>(card).Single()));
            Assert.True(tracking.IsPinnedToBottom);
            Assert.Equal(Animation.Snappy(0.24), during); // Follow runs inside the open animation, on the open curve
            Assert.True(Field<bool>(view, "anchorsBottomOnGrowth")); // FollowEnd holds the bottom
            Assert.NotNull(Field<object?>(view, "scrollFrame")); // FollowEnd scrolls to the end with animation
            Assert.True(tracking.IsReaderScrolling); // FollowEnd notes the movement
        }
        finally { panel.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-121")] // App/Views/Chat/ThreadTimeline.swift:2107-2119 (the toggle closes again and removes the body)
    public void Oc_Working_click_twice_closes_and_removes_the_body() => WithTimeline((view, runtime, _, _, _) =>
    {
        var card = new WorkingIndicator(runtime, SwiftDate.Now, 1, new([new ThinkingStep("thought", false)]), new([Tool("a")]), new([]), true);
        var host = new Border { Child = card };
        AppEnvironment.SetAppModel(host, AppEnvironment.GetAppModel(view));
        var panel = new Avalonia.Controls.Window { Content = host, Width = 600, Height = 400 };
        try
        {
            panel.Show(); Settle(panel);
            RealClick(panel, LogicalQuery.SelfAndDescendants<Button>(card).Single());
            Assert.Single(LogicalQuery.SelfAndDescendants<Marrow.UI.Markdown.MarkdownView>(card));
            RealClick(panel, LogicalQuery.SelfAndDescendants<Button>(card).Single());
            Assert.Empty(LogicalQuery.SelfAndDescendants<Marrow.UI.Markdown.MarkdownView>(card));
            Assert.Empty(LogicalQuery.SelfAndDescendants<WorkSteps>(card));
            Assert.Single(((StackPanel)((Border)card.Child!).Child!).Children);
            Assert.Equal("Show these steps", Avalonia.Controls.ToolTip.GetTip(LogicalQuery.SelfAndDescendants<Button>(card).Single()));
        }
        finally { panel.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-121")] // App/Views/Chat/ThreadTimeline.swift:2107-2119 (nothing to open: no tooltip, no body, no toggle)
    public void Oc_Working_without_steps_or_work_does_not_open() => WithTimeline((view, runtime, _, _, _) =>
    {
        var card = new WorkingIndicator(runtime, SwiftDate.Now, 1, new([]), new([]), new([]), true);
        var host = new Border { Child = card };
        AppEnvironment.SetAppModel(host, AppEnvironment.GetAppModel(view));
        var panel = new Avalonia.Controls.Window { Content = host, Width = 600, Height = 400 };
        try
        {
            panel.Show(); Settle(panel);
            var button = LogicalQuery.SelfAndDescendants<Button>(card).Single();
            Assert.Equal("", Avalonia.Controls.ToolTip.GetTip(button));
            RealClick(panel, button);
            Assert.Empty(LogicalQuery.SelfAndDescendants<Marrow.UI.Markdown.MarkdownView>(card));
            Assert.Empty(LogicalQuery.SelfAndDescendants<WorkSteps>(card));
            Assert.Single(((StackPanel)((Border)card.Child!).Child!).Children);
        }
        finally { panel.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-121")] // App/Views/Chat/ThreadTimeline.swift:2107-2119 (the accessibility hint names the agent's work)
    public void Oc_Working_button_help_names_the_agents_work() => WithTimeline((view, runtime, _, _, _) =>
    {
        var card = new WorkingIndicator(runtime, SwiftDate.Now, 1, new([new ThinkingStep("thought", false)]), new([Tool("a")]), new([]), true);
        var host = new Border { Child = card };
        AppEnvironment.SetAppModel(host, AppEnvironment.GetAppModel(view));
        var panel = new Avalonia.Controls.Window { Content = host, Width = 600, Height = 400 };
        try
        {
            panel.Show(); Settle(panel);
            Assert.Equal("Shows what the agent is doing", Avalonia.Automation.AutomationProperties.GetHelpText(LogicalQuery.SelfAndDescendants<Button>(card).Single()));
        }
        finally { panel.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-121")] // App/Views/Chat/ThreadTimeline.swift:2152-2176 (thinking visuals: size, dim, indent, padding, order)
    public void Oc_Working_thinking_uses_source_geometry_font_and_order() => WithTimeline((view, runtime, _, _, _) =>
    {
        var card = new WorkingIndicator(runtime, SwiftDate.Now, 1, new([new ThinkingStep("thought", false)]), new([Tool("a")]), new([]), true);
        var host = new Border { Child = card };
        AppEnvironment.SetAppModel(host, AppEnvironment.GetAppModel(view));
        var panel = new Avalonia.Controls.Window { Content = host, Width = 600, Height = 400 };
        try
        {
            panel.Show(); Settle(panel);
            RealClick(panel, LogicalQuery.SelfAndDescendants<Button>(card).Single());
            var column = (StackPanel)((Border)card.Child!).Child!;
            var opened = Assert.IsType<Border>(column.Children[1]);
            Assert.Equal(new Thickness(14, 0, 14, 11), opened.Padding);
            var details = Assert.IsType<StackPanel>(opened.Child);
            var thinking = Assert.IsType<StackPanel>(details.Children[0]);
            Assert.IsType<WorkSteps>(details.Children[1]);
            Assert.NotEmpty(LogicalQuery.SelfAndDescendants<Marrow.UI.Markdown.MarkdownView>(thinking));
            Assert.Equal(new Thickness(TimelineMetrics.IconWidth + TimelineMetrics.IconSpacing, 0, 0, 0), thinking.Margin);
            Assert.Equal(12, Marrow.UI.Markdown.MarkdownView.GetMarkdownPointSize(thinking));
            Assert.True(Marrow.UI.Markdown.MarkdownView.GetMarkdownDimmed(thinking));
        }
        finally { panel.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-121")] // App/Views/Chat/ThreadTimeline.swift:2107-2119 (the chevron rests at 0 and points down open at 90)
    public void Oc_Working_chevron_rotates_open() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var h = new AppModelHarness(); using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); var model = new Marrow.App.AppModel(); var id = Guid.NewGuid(); DiskWriter.Shared.WriteSynchronously(new ThreadDocument(id), Storage.ThreadURL(id)); DocumentPrefetch.Shared.Warm([id], urgent: true)!.Completion.GetAwaiter().GetResult(); var runtime = new ThreadRuntime(id, model);
        var card = new WorkingIndicator(runtime, SwiftDate.Now, 1, new([new ThinkingStep("thought", false)]), new([Tool("a")]), new([]), false);
        var host = new Border { Child = card };
        AppEnvironment.SetAppModel(host, model);
        var panel = new Avalonia.Controls.Window { Content = host, Width = 600, Height = 400 };
        static double Angle(Control card) => ((Avalonia.Media.RotateTransform)LogicalQuery.SelfAndDescendants<SwiftImage>(card).Single(i => i.SymbolName == "chevron.right").RenderTransform!).Angle;
        try
        {
            panel.Show(); Settle(panel);
            Assert.Equal(0, Angle(card));
            h.Main.OnMain(() =>
            {
                RealClick(panel, LogicalQuery.SelfAndDescendants<Button>(card).Single());
            });
            h.Main.OnMain(() =>
            {
                Assert.Equal(90, Angle(card));
            });
        }
        finally { panel.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-125")] // App/Views/Chat/ThreadTimeline.swift:325-331,2185-2188
    public void Oc_Chronological_flag_is_always_false_so_cards_open_folded_with_two_steps() => WithTimeline((view, runtime, _, _, _) =>
    {
        typeof(ThreadRuntime).GetProperty("Entries")!.SetValue(runtime, new ValueList<TimelineEntry>(new[] { User("probe") }));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var rows = Field<System.Collections.Generic.Dictionary<string, (DisplayBlock Block, RowContext Context, Control Row)>>(view, "rowViews");
        Assert.NotEmpty(rows);
        Assert.All(rows.Values, pair => Assert.False(pair.Context.Chronological));
        var model = AppEnvironment.GetAppModel(view);
        model.Settings.ChronologicalTimeline = true;
        typeof(ThreadRuntime).GetProperty("Entries")!.SetValue(runtime, new ValueList<TimelineEntry>(new[] { User("probe2") }));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.All(Field<System.Collections.Generic.Dictionary<string, (DisplayBlock Block, RowContext Context, Control Row)>>(view, "rowViews").Values, pair => Assert.False(pair.Context.Chronological));
        model.Settings.ChronologicalTimeline = false;
        typeof(ThreadRuntime).GetProperty("Entries")!.SetValue(runtime, new ValueList<TimelineEntry>(new[] { User("probe3") }));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.All(Field<System.Collections.Generic.Dictionary<string, (DisplayBlock Block, RowContext Context, Control Row)>>(view, "rowViews").Values, pair => Assert.False(pair.Context.Chronological));
        var card = new WorkingIndicator(runtime, SwiftDate.Now, 1, new([new ThinkingStep("t", false)]), new([Tool("a"), Tool("b"), Tool("c")]), new([]), true);
        var host = new Border { Child = card };
        AppEnvironment.SetAppModel(host, AppEnvironment.GetAppModel(view));
        var window = new Avalonia.Controls.Window { Content = host, Width = 600, Height = 400 };
        try
        {
            window.Show(); Settle(window);
            Assert.Empty(LogicalQuery.SelfAndDescendants<WorkSteps>(card));
            LogicalQuery.SelfAndDescendants<Button>(card).Single().RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Settle(window);
            var steps = LogicalQuery.SelfAndDescendants<WorkSteps>(card).Single();
            Assert.Equal(2, LogicalQuery.SelfAndDescendants<ToolRow>(steps).Count());
            Assert.Contains(LogicalQuery.SelfAndDescendants<TextBlock>(steps), text => text.Text == "1 earlier step");
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "UI-CHAT-069")] // App/Views/Chat/ThreadTimeline.swift:2104,2123
    public void Oc_Finished_tool_less_segment_labels_Earlier_steps_without_breathing() => WithTimeline((_, runtime, _, _, _) =>
    {
        var finished = new WorkingIndicator(runtime, SwiftDate.Now, 1, new([]), new([]), new([]), false, SwiftDate.Now);
        var title = LogicalQuery.SelfAndDescendants<TextBlock>(finished).Single(text => text.Text == "Earlier steps");
        Assert.Equal(13, title.FontSize);
        Assert.Equal(Avalonia.Media.FontWeight.Medium, title.FontWeight);
        Assert.Equal(Chrome.PrimaryText.Resolve(finished.ActualThemeVariant), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(title.Foreground).Color);
        var running = new WorkingIndicator(runtime, SwiftDate.Now, 1, new([]), new([Tool("t1")]), new([]), false);
        Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<TextBlock>(running), text => text.Text == "Earlier steps");
        var finishedOpenable = new WorkingIndicator(runtime, SwiftDate.Now, 1, new([new ThinkingStep("thought", false)]), new([]), new([]), false, SwiftDate.Now);
        Assert.Contains(LogicalQuery.SelfAndDescendants<TextBlock>(finishedOpenable), text => text.Text == "Earlier steps");
        Assert.NotEmpty(LogicalQuery.SelfAndDescendants<Button>(finishedOpenable));
    });

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-031")] // App/Views/Chat/ThreadTimeline.swift:1645-1652
    public void Oc_Merge_outcome_requires_Hydra_prefix_and_never_normalizes_project_prefix() => OnMain(() =>
    {
        Assert.True(DisplayBlock.IsMergeOutcome(new UserMessage { Text = "Hydra merged project\nbody", HydraHeads = new([]) }));
        Assert.True(DisplayBlock.IsMergeOutcome(new UserMessage { Text = "Hydra did not merge: the checkout kept moving.\nbody", HydraHeads = new([]) }));
        Assert.True(DisplayBlock.IsMergeOutcome(new UserMessage { Text = "Hydra left a file outside every project.\nbody", HydraHeads = new([]) }));
        Assert.True(DisplayBlock.IsMergeOutcome(new UserMessage { Text = "Hydra has no files left to merge.\nbody", HydraHeads = new([]) }));
        Assert.True(DisplayBlock.IsMergeOutcome(new UserMessage { Text = "Hydra is waiting for another chat's files.\nbody", HydraHeads = new([]) }));
        Assert.False(DisplayBlock.IsMergeOutcome(new UserMessage { Text = "Project: Hydra merged branch\nbody", HydraHeads = new([]) }));
        Assert.False(DisplayBlock.IsMergeOutcome(new UserMessage { Text = "hydra merged\nbody", HydraHeads = new([]) }));
        Assert.False(DisplayBlock.IsMergeOutcome(new UserMessage { Text = "Hydra\nbody", HydraHeads = new([]) }));
        Assert.False(DisplayBlock.IsMergeOutcome(new UserMessage { Text = "Hydra merged\nbody", HydraHeads = null }));
        Assert.False(DisplayBlock.IsMergeOutcome(new UserMessage { Text = "Hydra merged\nbody", HydraHeads = new([1]) }));
    });
}
