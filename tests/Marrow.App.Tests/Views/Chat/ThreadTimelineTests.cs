using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Marrow.App.Runtime;
using Marrow.App.Port;
using Marrow.App.Views.Chat;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/ThreadTimeline.swift (B3 52bc792), :1-2327.
[Collection("Headless")]
public sealed class ThreadTimelineTests(HeadlessSession headless)
{
    private static readonly Guid Turn = Guid.Parse("12345678-abcd-1234-abcd-123456789abc");
    private static TimelineEntry Entry(string id, TimelineItem.Content content, Guid? turn = null) => new(new TimelineItem(turn, content, id));
    private static TimelineEntry User(string id, Guid? turn = null, string text = "prompt") => Entry(id, new TimelineItem.Content.User(new UserMessage { Text = text }), turn);
    private static TimelineEntry Reply(string id, Guid? turn = null) => Entry(id, new TimelineItem.Content.Assistant(new AssistantMessage { Text = "reply" }), turn);
    private static TimelineEntry Tool(string id, Guid? turn = null, bool head = false) => Entry(id, new TimelineItem.Content.Tool(new ToolCall { Title = id, ToolCallKind = head ? ToolCall.Kind.Agent : ToolCall.Kind.Command }), turn);
    private static TimelineEntry Reason(string id, Guid? turn = null) => Entry(id, new TimelineItem.Content.Reasoning(new ReasoningBlock { Text = "thinking" }), turn);
    private static TurnSummary Summary(Guid turn) => new() { TurnID = turn, Status = TurnStatus.Completed, Duration = 2, FilesChanged = 0, Additions = 0, Deletions = 0 };
    private static TimelineEntry End(string id, Guid turn) => Entry(id, new TimelineItem.Content.TurnEnd(Summary(turn)), turn);
    private static ValueList<DisplayBlock> Blocks(TimelineEntry[] entries, bool running = false, string? merge = null, HeadPills? pills = null) => DisplayBlock.Build(entries, TimelineMeta.Build(entries), false, running, merge, pills);
    private void OnMain(Action body) => headless.OnUiThread(() => { using var runtime = new TestRuntime(); runtime.OnMain(body); });
    private static T Invoke<T>(object owner, string name, params object?[] args) => (T)owner.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(owner, args)!;

    private void WithTimeline(Action<ThreadTimeline, ThreadRuntime, ChromeScrollModel, TimelineScrollState, AppModelHarness> check) => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness(); using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); var model = new Marrow.App.AppModel(); var runtime = new ThreadRuntime(Guid.NewGuid(), model); var chrome = new ChromeScrollModel(); var state = new TimelineScrollState(); var view = new ThreadTimeline(runtime, chrome, state, "Project", null, false, 480); var host = new Border(); AppEnvironment.SetAppModel(host, model); host.Child = view; var window = new Avalonia.Controls.Window { Content = host, Width = 960, Height = 480 };
        try { window.Show(); window.UpdateLayout(); check(view, runtime, chrome, state, h); } finally { window.Close(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-021")] // App/Views/Chat/ThreadTimeline.swift:416-420
    [Trait("Req", "PR-CHAT-022")] // App/Views/Chat/ThreadTimeline.swift:325,421-422
    public void Column_uses_shared_top_inset_exact_outer_padding_cap_and_a_bottom_flex_spacer() => WithTimeline((view, _, _, _, _) =>
    {
        var rows = new Border { Height = 40 }; var column = Invoke<Control>(view, "CreateColumn", rows, 480.0, null); column.Measure(new Size(960, 480)); column.Arrange(new Rect(0, 0, 960, 480)); var content = LogicalQuery.SelfAndDescendants<Border>(column).Single(border => border.MaxWidth == 820); Assert.Equal(new Thickness(20, Chrome.ContentTopInset, 20, 18), content.Margin); Assert.Equal(480, column.MinHeight); Assert.True(rows.TranslatePoint(default, column)!.Value.Y >= 400);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-023")] // App/Views/Chat/ThreadTimeline.swift:58-67
    [Trait("Req", "PR-CHAT-024")] // App/Views/Chat/ThreadTimeline.swift:446-449,891-893
    public void Resize_width_and_visible_height_use_the_source_quantization_and_slide_target()
    {
        var type = typeof(ThreadTimeline); var width = type.GetMethod("SteppedWidth", BindingFlags.NonPublic | BindingFlags.Static)!; var height = type.GetMethod("SteppedHeight", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Null(width.Invoke(null, [false, 731.0, false, 0.0, 0.0])); Assert.Equal(720.0, width.Invoke(null, [true, 731.0, false, 0.0, 0.0])); Assert.Equal(860.0, width.Invoke(null, [true, 999.0, false, 0.0, 0.0])); Assert.Equal(734.0, width.Invoke(null, [true, 700.0, true, 750.0, -16.0])); Assert.Equal(12.0, width.Invoke(null, [true, 10.0, true, 20.0, -30.0])); Assert.Equal(456.0, height.Invoke(null, [490.0, 12.0, 18.0])); Assert.Equal(0.0, height.Invoke(null, [10.0, 20.0, 30.0]));
    }

    [Fact]
    [Trait("Req", "PR-CHAT-025")] // App/Views/Chat/ThreadTimeline.swift:134-135; OD-0016/ED-097
    [Trait("Req", "PR-CHAT-026")] // App/Views/Chat/ThreadTimeline.swift:136-141
    public void Loading_never_flashes_the_prompt_and_an_empty_idle_thread_clears_chrome_and_jump_state() => WithTimeline((view, runtime, chrome, state, _) =>
    {
        typeof(ThreadRuntime).GetProperty("IsLoadingHistory")!.SetValue(runtime, true); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Assert.Empty(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(view)); Assert.Empty(LogicalQuery.SelfAndDescendants<ScrollViewer>(view)); chrome.Update(100); state.ShowsJumpButton = true; typeof(ThreadRuntime).GetProperty("IsLoadingHistory")!.SetValue(runtime, false); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(view)); Assert.Equal(0, chrome.Progress); Assert.False(state.ShowsJumpButton);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-045")] // App/Views/Chat/ThreadTimeline.swift:1804-1811,257-268
    [Trait("Req", "PR-CHAT-046")] // App/Views/Chat/ThreadTimeline.swift:366-382
    public void History_window_starts_at_three_moves_back_twenty_four_and_anchors_on_identity() => WithTimeline((view, _, _, _, _) =>
    {
        Assert.Equal(3, TimelineWindow.FirstPaint); Assert.Equal(24, TimelineWindow.Initial); Assert.Equal(24, TimelineWindow.Page); Assert.Equal(96, typeof(TimelineWindow).GetField("EagerLimit")!.GetRawConstantValue()); var blocks = Enumerable.Range(0, 60).Select(i => (DisplayBlock)new DisplayBlock.Working(null, new([])) { }).ToArray(); blocks = Enumerable.Range(0, 60).Select(i => (DisplayBlock)new DisplayBlock.Merging("b" + i)).ToArray(); Assert.Equal(57, Invoke<int>(view, "WindowStartIndex", blocks)); Invoke<object?>(view, "LoadEarlier", blocks, 24); Assert.Equal(36, Invoke<int>(view, "WindowStartIndex", blocks)); Assert.Equal(36, Invoke<int>(view, "WindowStartIndex", blocks.Append(new DisplayBlock.Merging("append")).ToArray()));
        var pill = Invoke<Button>(view, "EarlierPill", 36, (Action)(() => { })); Assert.Contains("Show 36 earlier messages", LogicalQuery.SelfAndDescendants<TextBlock>(pill).Select(text => text.Text)); Assert.Equal("chevron.up", LogicalQuery.SelfAndDescendants<SwiftImage>(pill).Single().SymbolName);
    });

    // STOP App.Views.Chat.ThreadTimeline #1
    // Kind: dependency. Swift: ThreadTimeline.swift:104-107,186-197,1886-1934,2049-2065.
    // Records: PR-CHAT-021/022/026/027/034/035/039/045/046 (rendering/integration only).
    // Missing: external TimelineRows message/work/head/finished-turn constructors and FinishedHeads are MARROW_STUB.
    // Needed: TimelineRows unit implementations. Owned grouping, metadata, cache and layout are independently covered.
    // No fabricated row content or fabricated finished-head roster substitutes for those sites.

    [Fact]
    [Trait("Req", "PR-CHAT-028")] // App/Views/Chat/ThreadTimeline.swift:1566-1593
    [Trait("Req", "PR-CHAT-029")] // App/Views/Chat/ThreadTimeline.swift:1594-1598
    public void Contiguous_turn_runs_keep_composite_ids_and_exclude_reasoning_and_end_markers() => OnMain(() =>
    {
        var entries = new[] { User("first", Turn), Reason("think", Turn), Reply("answer", Turn), End("end", Turn), User("note"), Tool("tail", Turn) };
        var blocks = Blocks(entries); Assert.Equal(new[] { "turn-12345678-ABCD-1234-ABCD-123456789ABC-first", "note", "turn-12345678-ABCD-1234-ABCD-123456789ABC-tail" }, blocks.Select(block => block.Id)); Assert.Equal(new[] { "first", "answer" }, Assert.IsType<DisplayBlock.Turn>(blocks[0]).Entries.Select(entry => entry.Id)); Assert.NotNull(Assert.IsType<DisplayBlock.Turn>(blocks[2]).Summary);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-030")] // App/Views/Chat/ThreadTimeline.swift:1425-1453
    [Trait("Req", "PR-CHAT-031")] // App/Views/Chat/ThreadTimeline.swift:1405-1409,1436-1456
    public void Tools_group_across_heads_and_reasoning_but_break_on_turn_changes_and_heads_finish_last() => OnMain(() =>
    {
        var second = Guid.NewGuid(); var groups = TimelineGroup.Build(new[] { Tool("a", Turn), Tool("head", Turn, true), Reason("thought", Turn), Tool("b", Turn), Tool("c", second), Reply("reply", second) }, true);
        Assert.Equal(new[] { "work-a", "work-c", "reply", "heads-head" }, groups.Select(group => group.Id)); Assert.Equal(new[] { "a", "b" }, Assert.IsType<TimelineGroup.Work>(groups[0]).Entries.Select(entry => entry.Id)); Assert.True(Assert.IsType<TimelineGroup.Work>(groups[0]).StartsCollapsed); Assert.True(Assert.IsType<TimelineGroup.Work>(groups[1]).StartsCollapsed); Assert.True(TimelineGroup.IsHead(Assert.IsType<TimelineGroup.Heads>(groups[3]).Entries[0])); Assert.False(Assert.IsType<TimelineGroup.Work>(TimelineGroup.Build(new[] { Tool("last") }, false).Single()).StartsCollapsed);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-027")] // App/Views/Chat/ThreadTimeline.swift:136
    [Trait("Req", "PR-CHAT-032")] // App/Views/Chat/ThreadTimeline.swift:1577-1602
    [Trait("Req", "PR-CHAT-033")] // App/Views/Chat/ThreadTimeline.swift:1503
    public void Running_always_has_exactly_one_working_line_in_the_turn_or_as_a_standalone_block() => OnMain(() =>
    {
        Assert.Equal("working-", Blocks([], true).Single().Id); var turn = Assert.IsType<DisplayBlock.Turn>(Blocks(new[] { User("u", Turn), Reply("prose", Turn) }, true).Single()); Assert.True(turn.ShowsWorking); var ended = Blocks(new[] { User("u", Turn), End("done", Turn) }, true); Assert.False(Assert.IsType<DisplayBlock.Turn>(ended[0]).ShowsWorking); Assert.Equal("working-12345678-ABCD-1234-ABCD-123456789ABC", ended[1].Id); Assert.Empty(Blocks([]));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-034")] // App/Views/Chat/ThreadTimeline.swift:1608-1614
    [Trait("Req", "PR-CHAT-035")] // App/Views/Chat/ThreadTimeline.swift:1615-1617
    public void Finished_head_pills_obey_all_insertion_guards_order_and_trailing_identity() => OnMain(() =>
    {
        var pills = new HeadPills { Earlier = new([2, 1]), Later = new([3]) }; var blocks = Blocks(new[] { User("u", Turn) }, true, pills: pills); Assert.Equal(new[] { "hydra-heads-working", "turn-12345678-ABCD-1234-ABCD-123456789ABC-u", "hydra-heads-working-tail" }, blocks.Select(block => block.Id)); Assert.Equal(new[] { 2, 1 }, Assert.IsType<DisplayBlock.HeadsWorking>(blocks[0]).Heads); Assert.DoesNotContain(Blocks(new[] { Tool("head", Turn, true) }, true, pills: pills), block => block is DisplayBlock.HeadsWorking { Trailing: false }); Assert.Equal("hydra-heads-working", Blocks([], pills: pills).Single().Id);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-037")] // App/Views/Chat/ThreadTimeline.swift:1624-1643
    [Trait("Req", "PR-CHAT-038")] // App/Views/Chat/ThreadTimeline.swift:1648-1652
    public void Merge_pill_is_last_and_only_a_true_Hydra_outcome_suppresses_it() => OnMain(() =>
    {
        var outcome = new UserMessage { Text = "\nHydra merged\nbody", HydraHeads = new([]) }; Assert.True(DisplayBlock.IsMergeOutcome(outcome)); Assert.False(DisplayBlock.IsMergeOutcome(outcome with { Text = "hydra merged" })); Assert.False(DisplayBlock.IsMergeOutcome(outcome with { Text = "Hydra" })); Assert.False(DisplayBlock.IsMergeOutcome(outcome with { HydraHeads = null })); Assert.False(DisplayBlock.IsMergeOutcome(outcome with { HydraHeads = new([1]) })); var blocks = Blocks(new[] { User("u") }, true, "merge-note"); Assert.IsType<DisplayBlock.Merging>(blocks[^1]); Assert.Equal("merge-note", blocks[^1].Id); var completed = Blocks(new[] { Entry("merge-note", new TimelineItem.Content.User(outcome)) }, merge: "merge-note"); Assert.Single(completed); Assert.IsType<HydraMergePhase.Outcome>(completed[0].HydraMergePhase);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-039")] // App/Views/Chat/ThreadTimeline.swift:1520-1526
    public void Only_turns_with_prompts_and_plain_single_user_groups_carry_minimap_ticks() => OnMain(() =>
    {
        Assert.True(Blocks(new[] { User("u", Turn), User("u2", Turn) }).Single().HasUserMessage); Assert.True(Blocks(new[] { User("plain") }).Single().HasUserMessage); Assert.All(Blocks(new[] { Reply("r"), Tool("t") }, true, "merge", new HeadPills { Later = new([1]) }), block => Assert.False(block.HasUserMessage));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-040")] // App/Views/Chat/ThreadTimeline.swift:1684-1746
    public void Unchanged_identity_key_returns_the_same_array_even_when_streaming_text_changes() => OnMain(() =>
    {
        var cache = new TimelineBlockCache(); var entry = Reply("stream", Turn); var first = cache.Blocks(new[] { entry }, false, true, null); entry.Item = entry.Item with { TimelineItemContent = new TimelineItem.Content.Assistant(new AssistantMessage { Text = "changed" }) }; Assert.Same(first, cache.Blocks(new[] { entry }, false, true, null)); Assert.NotSame(first, cache.Blocks(new[] { Reply("stream", Turn) }, false, true, null));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-041")] // App/Views/Chat/ThreadTimeline.swift:1754-1779
    public void Append_fast_path_reuses_the_prefix_but_a_new_end_marker_folding_it_forces_full_build() => OnMain(() =>
    {
        var other = Guid.NewGuid(); var entries = new[] { User("first", Turn), User("next", other) }; var cache = new TimelineBlockCache(); var old = cache.Blocks(entries, false, false, null); var appended = entries.Append(Reply("tail", other)).ToArray(); var grown = cache.Blocks(appended, false, false, null); Assert.Same(old[0], grown[0]); Assert.Equal(Blocks(appended), grown); var folded = appended.Append(End("end", Turn)).ToArray(); var rebuilt = cache.Blocks(folded, false, false, null); Assert.NotSame(grown[0], rebuilt[0]); Assert.NotNull(Assert.IsType<DisplayBlock.Turn>(rebuilt[0]).Summary); Assert.Equal(Blocks(folded), rebuilt);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-043")] // App/Views/Chat/ThreadTimeline.swift:1666-1682
    public void Thread_cache_is_LRU_bounded_to_twenty_four_and_hits_move_to_the_end() => OnMain(() =>
    {
        var ids = Enumerable.Range(0, 24).Select(_ => Guid.NewGuid()).ToArray(); var caches = ids.Select(TimelineBlockCache.Cache).ToArray(); Assert.Same(caches[0], TimelineBlockCache.Cache(ids[0])); TimelineBlockCache.Cache(Guid.NewGuid()); Assert.Same(caches[0], TimelineBlockCache.Cache(ids[0])); Assert.NotSame(caches[1], TimelineBlockCache.Cache(ids[1]));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-044")] // App/Views/Chat/ThreadTimeline.swift:1817-1863
    public void Metadata_attaches_summary_only_to_the_last_reply_and_flags_only_end_markers_with_a_reply() => OnMain(() =>
    {
        var a = Reply("a", Turn); var b = Reply("b", Turn); var end = End("end", Turn); var meta = TimelineMeta.Build(new[] { a, b, end }); Assert.Null(meta.Summary(new TimelineGroup.Single(a))); Assert.Equal(Summary(Turn), meta.Summary(new TimelineGroup.Single(b))); Assert.True(meta.HasReply(new TimelineGroup.Single(end))); Assert.False(meta.HasReply(new TimelineGroup.Single(b))); Assert.False(TimelineMeta.Build(new[] { end }).HasReply(new TimelineGroup.Single(end)));
    });
}
