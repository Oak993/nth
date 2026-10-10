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
using Marrow.Services.Store;
using Marrow.UI.Common;

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
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;
    private static void Put(object owner, string name, object? value) => owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(owner, value);

    private void WithTimeline(Action<ThreadTimeline, ThreadRuntime, ChromeScrollModel, TimelineScrollState, AppModelHarness> check) => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var h = new AppModelHarness(); using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); var model = new Marrow.App.AppModel(); var id = Guid.NewGuid(); DiskWriter.Shared.WriteSynchronously(new ThreadDocument(id), Storage.ThreadURL(id)); DocumentPrefetch.Shared.Warm([id], urgent: true)!.Completion.GetAwaiter().GetResult(); var runtime = new ThreadRuntime(id, model); var chrome = new ChromeScrollModel(); var state = new TimelineScrollState(); var view = new ThreadTimeline(runtime, chrome, state, "Project", null, false, 480); var host = new Border(); AppEnvironment.SetAppModel(host, model); host.Child = view; var window = new Avalonia.Controls.Window { Content = host, Width = 960, Height = 480 };
        AppEnvironment.SetWindowLiveResize(host, new Marrow.App.Window.WindowLiveResize(window));
        try { window.Show(); window.UpdateLayout(); check(view, runtime, chrome, state, h); } finally { window.Close(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-021 and PR-CHAT-022 (a unit-level check of its structure; the mounted proofs are the ThreadTimeline*Tests classes): App/Views/Chat/ThreadTimeline.swift:416-420; App/Views/Chat/ThreadTimeline.swift:325,421-422
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
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-045 and PR-CHAT-046 (a unit-level check of its structure; the mounted proofs are the ThreadTimeline*Tests classes): App/Views/Chat/ThreadTimeline.swift:1804-1811,257-268; App/Views/Chat/ThreadTimeline.swift:366-382
    public void History_window_starts_at_three_moves_back_twenty_four_and_anchors_on_identity() => WithTimeline((view, _, _, _, _) =>
    {
        Assert.Equal(3, TimelineWindow.FirstPaint); Assert.Equal(24, TimelineWindow.Initial); Assert.Equal(24, TimelineWindow.Page); Assert.Equal(96, typeof(TimelineWindow).GetField("EagerLimit")!.GetRawConstantValue()); var blocks = Enumerable.Range(0, 60).Select(i => (DisplayBlock)new DisplayBlock.Merging("b" + i)).ToArray(); Assert.Equal(57, Invoke<int>(view, "WindowStartIndex", (object)blocks)); Invoke<object?>(view, "LoadEarlier", blocks, 24); Assert.Equal(36, Invoke<int>(view, "WindowStartIndex", (object)blocks)); Assert.Equal(36, Invoke<int>(view, "WindowStartIndex", (object)blocks.Append(new DisplayBlock.Merging("append")).ToArray()));
        var pill = Invoke<Button>(view, "EarlierPill", 36, (Action)(() => { })); Assert.Contains("Show 36 earlier messages", LogicalQuery.SelfAndDescendants<TextBlock>(pill).Select(text => text.Text)); Assert.Equal("chevron.up", LogicalQuery.SelfAndDescendants<SwiftImage>(pill).Single().SymbolName);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-047")] // App/Views/Chat/ThreadTimeline.swift:257-268,557-571
    [Trait("Req", "PR-CHAT-048")] // App/Views/Chat/ThreadTimeline.swift:438-440
    public void Pending_history_refuses_another_page_and_holds_the_bottom_until_growth_is_measured() => WithTimeline((view, _, _, _, _) =>
    {
        var blocks = Enumerable.Range(0, 60).Select(i => (DisplayBlock)new DisplayBlock.Merging("b" + i)).ToArray(); Put(view, "anchorsBottomOnGrowth", false); Invoke<object?>(view, "LoadEarlier", blocks, 24); Assert.True(Field<bool>(view, "anchorsBottomOnGrowth")); Invoke<object?>(view, "LoadEarlier", blocks, null); Assert.Equal(36, Invoke<int>(view, "WindowStartIndex", (object)blocks)); var tracking = Field<TimelineScrollTracking>(view, "tracking"); tracking.IsPinnedToBottom = false;
        Invoke<object?>(view, "NoteMetrics", new ScrollMetrics { ContentHeight = 600, ContainerHeight = 480, DistanceFromBottom = 120 }, new ScrollMetrics { ContentHeight = 800, ContainerHeight = 480, Offset = 200, DistanceFromBottom = 120 }, false); Assert.False(Field<bool>(view, "historyLoadPending")); Assert.False(Field<bool>(view, "anchorsBottomOnGrowth"));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-052")] // App/Views/Chat/ThreadTimeline.swift:561,575-578
    [Trait("Req", "PR-CHAT-055")] // App/Views/Chat/ThreadTimeline.swift:510-515
    public void Only_movement_beyond_the_anchor_unpins_and_the_end_threshold_is_strictly_below_forty_eight() => WithTimeline((view, _, _, _, _) =>
    {
        var tracking = Field<TimelineScrollTracking>(view, "tracking"); var old = new ScrollMetrics { ContentHeight = 600, ContainerHeight = 480, Offset = 120 }; var grown = old with { ContentHeight = 620, Offset = 140 }; Invoke<object?>(view, "NoteMetrics", old, grown, false); Assert.True(tracking.IsPinnedToBottom); Invoke<object?>(view, "NoteMetrics", grown, grown with { Offset = 90, DistanceFromBottom = 48 }, false); Assert.False(tracking.IsPinnedToBottom); Invoke<object?>(view, "NoteMetrics", grown with { Offset = 90, DistanceFromBottom = 48 }, grown with { Offset = 91, DistanceFromBottom = 47 }, false); Assert.False(tracking.IsPinnedToBottom); Invoke<object?>(view, "NoteMetrics", grown with { Offset = 90, DistanceFromBottom = 48 }, grown with { Offset = 92, DistanceFromBottom = 47 }, false); Assert.True(tracking.IsPinnedToBottom);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-053")] // App/Views/Chat/ThreadTimeline.swift:542-556
    [Trait("Req", "PR-CHAT-054")] // App/Views/Chat/ThreadTimeline.swift:578-593
    public void Shape_correction_is_deferred_and_live_resize_does_not_repin_a_stale_flag() => WithTimeline((view, _, _, _, _) =>
    {
        var tracking = Field<TimelineScrollTracking>(view, "tracking"); tracking.IsPinnedToBottom = false; var old = new ScrollMetrics { ContentHeight = 600, ContainerHeight = 480, DistanceFromBottom = 0 }; Invoke<object?>(view, "NoteMetrics", old, old with { ContentHeight = 700, ContainerHeight = 470, DistanceFromBottom = 230 }, true); Assert.False(tracking.IsPinnedToBottom); Invoke<object?>(view, "NoteMetrics", old, old with { ContentHeight = 700, ContainerHeight = 470, DistanceFromBottom = 230 }, false); Assert.True(tracking.IsPinnedToBottom);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-049")] // App/Views/Chat/ThreadTimeline.swift:224-252
    [Trait("Req", "PR-CHAT-061")] // App/Views/Chat/ThreadTimeline.swift:325-341,1869-1875
    [Trait("Req", "PR-CHAT-062")] // App/Views/Chat/ThreadTimeline.swift:164-172,326
    public void Tick_jump_expands_to_identity_unpins_older_targets_and_keeps_unknown_targets_unchanged() => WithTimeline((view, _, _, _, _) =>
    {
        var blocks = Enumerable.Range(0, 40).Select(i => (DisplayBlock)new DisplayBlock.Merging("b" + i)).ToArray(); Invoke<object?>(view, "Jump", "b10", blocks, false); Assert.Equal(10, Invoke<int>(view, "WindowStartIndex", (object)blocks)); var tracking = Field<TimelineScrollTracking>(view, "tracking"); Assert.False(tracking.IsPinnedToBottom); Assert.True(Field<bool>(view, "historyLoadPending")); Invoke<object?>(view, "Jump", "unknown", blocks, true); Assert.Equal(10, Invoke<int>(view, "WindowStartIndex", (object)blocks)); Assert.False(tracking.IsPinnedToBottom); Invoke<object?>(view, "Jump", "b39", blocks, true); Assert.True(tracking.IsPinnedToBottom); Assert.Equal(new RowContext("root", true, false), new RowContext("root", true, false));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-050")] // App/Views/Chat/ThreadTimeline.swift:307-321
    public void Finished_turns_and_work_groups_over_eight_have_identity_transitions() => OnMain(() =>
    {
        var method = typeof(ThreadTimeline).GetMethod("Transition", BindingFlags.Static | BindingFlags.NonPublic)!; var finished = new DisplayBlock.Turn("turn", Turn, new([User("u", Turn)]), Summary(Turn), false); Assert.Equal(SwiftTransition.Identity, method.Invoke(null, [finished])); var work = new DisplayBlock.Group(new TimelineGroup.Work("w", new(Enumerable.Range(0, 9).Select(i => Tool(i.ToString(System.Globalization.CultureInfo.InvariantCulture)))), false), null, false); Assert.Equal(SwiftTransition.Identity, method.Invoke(null, [work])); Assert.Equal(ThreadTimeline.RowTransition, method.Invoke(null, [finished with { Summary = null }])); Assert.NotEqual(SwiftTransition.Identity, ThreadTimeline.RowTransition);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-051")] // App/Views/Chat/ThreadTimeline.swift:402-409
    [Trait("Req", "PR-CHAT-122")] // App/Views/Chat/ThreadTimeline.swift:813-816
    [Trait("Req", "PR-CHAT-124")] // App/Views/Chat/ThreadTimeline.swift:90,357-358,414,2234-2244
    public void Stable_reveal_box_has_distinct_guarded_and_unconditional_follow_and_sentinel_updates() => WithTimeline((view, _, _, state, _) =>
    {
        var box = Field<TimelineRevealBox>(view, "revealBox"); Assert.Same(box, ThreadTimeline.GetRevealTimelineEnd(view)); var tracking = Field<TimelineScrollTracking>(view, "tracking"); tracking.IsPinnedToBottom = false; state.ShowsJumpButton = true; tracking.NoteGeometry(0, 900, 0); Put(view, "viewportHeight", 480.0); box.Action(); Assert.False(tracking.IsPinnedToBottom); box.Follow(); Assert.True(tracking.IsPinnedToBottom); tracking.IsPinnedToBottom = false; tracking.NoteGeometry(0, 479, 0); box.Action(); Assert.True(tracking.IsPinnedToBottom); Invoke<object?>(view, "SetEndVisible", true); Assert.False(state.ShowsJumpButton); Invoke<object?>(view, "SetEndVisible", false); Assert.True(state.ShowsJumpButton);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-056")] // App/Views/Chat/ThreadTimeline.swift:833-835,1146-1157
    [Trait("Req", "PR-CHAT-057")] // App/Views/Chat/ThreadTimeline.swift:770-803
    public void Drift_accumulates_strictly_over_tolerance_and_eager_rows_do_not_need_a_virtualized_viewport_rebuild() => OnMain(() =>
    {
        var tracking = new TimelineScrollTracking(); Assert.False(tracking.NoteDrift(24, 48)); Assert.False(tracking.NoteDrift(24, 48)); Assert.True(tracking.NoteDrift(0.01, 48)); Assert.False(tracking.NoteDrift(1, 48)); tracking.ResetDrift(); Assert.False(tracking.NoteDrift(48, 48)); var tolerance = typeof(ThreadTimeline).GetMethod("JumpTolerance", BindingFlags.NonPublic | BindingFlags.Static)!; Assert.Equal(48.0, tolerance.Invoke(null, [100.0])); Assert.Equal(120.0, tolerance.Invoke(null, [480.0])); Assert.False(tracking.BeginNudge());
    });

    [Fact]
    [Trait("Req", "PR-CHAT-058")] // App/Views/Chat/ThreadTimeline.swift:1125-1132
    [Trait("Req", "PR-CHAT-063")] // App/Views/Chat/ThreadTimeline.swift:335-338,1002-1016
    [Trait("Req", "PR-CHAT-064")] // App/Views/Chat/ThreadTimeline.swift:1239-1245
    public void Visibility_resolves_the_later_middle_prompt_and_blank_watch_rearms_at_three_hundred_ms() => headless.OnUiThread(() =>
    {
        using var time = new TestRuntime(); TimelineScrollTracking tracking = null!; time.OnMain(() => { tracking = new(); Assert.True(tracking.SetOutline([("u1", true), ("r1", false), ("u2", true), ("r2", false)])); tracking.SetVisible("r1", true); tracking.SetVisible("u2", true); Assert.Equal("u2", tracking.ActiveBlockID); tracking.SetVisible("u2", false); Assert.Equal("u1", tracking.ActiveBlockID); tracking.SetVisible("r1", false); Assert.True(tracking.ShowsNothing); }); time.AdvanceMilliseconds(299); time.OnMain(() => Assert.Equal(0, tracking.CheckRequest)); time.AdvanceMilliseconds(1); time.OnMain(() => { Assert.Equal(1, tracking.CheckRequest); tracking.ArmBlankWatch(); }); time.AdvanceMilliseconds(200); time.OnMain(tracking.ArmBlankWatch); time.AdvanceMilliseconds(299); time.OnMain(() => Assert.Equal(1, tracking.CheckRequest)); time.AdvanceMilliseconds(1); time.OnMain(() => { Assert.Equal(2, tracking.CheckRequest); tracking.BlankRepairs = 2; tracking.SetVisible("r2", true); Assert.Equal(0, tracking.BlankRepairs); Assert.False(tracking.EndUnseen); tracking.EndActivity(); });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-059")] // App/Views/Chat/ThreadTimeline.swift:1093-1120
    public void Lost_scroll_phase_times_out_at_point_four_or_two_seconds_and_requests_a_check() => headless.OnUiThread(() =>
    {
        using var time = new TestRuntime(); var tracking = new TimelineScrollTracking(); var phase = typeof(TimelineScrollTracking).GetMethod("NotePhase")!; time.OnMain(() => phase.Invoke(tracking, ["decelerating"])); time.AdvanceMilliseconds(399); time.OnMain(() => Assert.True(tracking.IsCoasting)); time.AdvanceMilliseconds(1); time.OnMain(() => { Assert.False(tracking.IsCoasting); Assert.Equal(1, tracking.CheckRequest); phase.Invoke(tracking, ["interacting"]); }); time.AdvanceMilliseconds(1999); time.OnMain(() => Assert.True(tracking.IsUserScrolling)); time.AdvanceMilliseconds(1); time.OnMain(() => { Assert.False(tracking.IsUserScrolling); Assert.Equal(2, tracking.CheckRequest); tracking.EndActivity(); });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-060")] // App/Views/Chat/ThreadTimeline.swift:727-752,2320-2325
    public void Freeze_is_a_leaf_observation_and_lifts_after_the_movement_watch_settles() => WithTimeline((view, _, _, _, _) =>
    {
        var tracking = Field<TimelineScrollTracking>(view, "tracking"); var content = new Border(); var frozen = new ScrollFreeze(tracking).Body(content); var host = new Avalonia.Controls.Window { Content = frozen }; try { host.Show(); tracking.IsReaderScrolling = true; Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Assert.False(content.IsHitTestVisible); tracking.IsReaderScrolling = false; Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Assert.True(content.IsHitTestVisible); } finally { host.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-065")] // App/Views/Chat/ThreadTimeline.swift:180-200
    public void Minimap_requires_the_enabled_flag_and_more_than_one_user_block() => OnMain(() =>
    {
        var method = typeof(ThreadTimeline).GetMethod("ShouldShowMinimap", BindingFlags.Static | BindingFlags.NonPublic)!; var blocks = Blocks(new[] { User("u"), User("v") }); Assert.True((bool)method.Invoke(null, [true, blocks])!); Assert.False((bool)method.Invoke(null, [false, blocks])!); Assert.False((bool)method.Invoke(null, [true, Blocks(new[] { User("only") })])!);
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-117 and PR-CHAT-121 and PR-CHAT-125 (a unit-level check of its structure; the mounted proofs are the ThreadTimeline*Tests classes): App/Views/Chat/ThreadTimeline.swift:2096-2097,2121-2149; App/Views/Chat/ThreadTimeline.swift:2107-2119,2152-2176; App/Views/Chat/ThreadTimeline.swift:330,2185-2188
    public void Working_card_and_badge_keep_source_insets_and_thinking_starts_folded() => WithTimeline((_, runtime, _, _, _) =>
    {
        var card = new WorkingIndicator(runtime, SwiftDate.Now, 1, new([new ThinkingStep("thought", false)]), new([]), new([]), true); var badge = new WorkingIndicator(runtime, SwiftDate.Now, 1, new([]), new([]), new([]), false); var cardShell = LogicalQuery.SelfAndDescendants<Border>(card).Single(border => border.Width == 380); Assert.Equal(new Thickness(14, 11), cardShell.Padding); var badgeShell = LogicalQuery.SelfAndDescendants<Border>(badge).Single(border => border.Padding == new Thickness(TimelineMetrics.PillLeading, TimelineMetrics.PillVertical, TimelineMetrics.PillTrailing, TimelineMetrics.PillVertical)); Assert.True(double.IsNaN(badgeShell.Width)); Assert.Empty(LogicalQuery.SelfAndDescendants<Marrow.UI.Markdown.MarkdownView>(card)); Assert.Equal("Show these steps", ToolTip.GetTip(LogicalQuery.SelfAndDescendants<Button>(card).Single()));
    });

    // STOP App.Views.Chat.ThreadTimeline #2 (resolved): TimelineRows.WorkGroupSummary.Text and WorkSteps are ported; the
    // live tool summary (ThreadTimeline.swift:2104) and the opened work rows (:2169-2172) are proven in ThreadTimelineProofTests.

    [Fact]
    [Trait("Req", "PR-CHAT-126")] // App/Views/Chat/ThreadTimeline.swift:2301-2314
    [Trait("Req", "PR-CHAT-127")] // App/Views/Chat/ThreadTimeline.swift:2267-2285,2290-2299
    public void Current_turn_scans_skip_nil_turns_stop_at_previous_turn_and_keep_nonempty_thinking_in_order() => WithTimeline((view, runtime, _, _, _) =>
    {
        ((Decorator)view.Parent!).Child = null; var older = Guid.NewGuid(); var entries = new[] { Reason("old", older), Reason("a", Turn), User("note"), Entry("empty", new TimelineItem.Content.Reasoning(new ReasoningBlock { Text = "" }), Turn), Entry("b", new TimelineItem.Content.Reasoning(new ReasoningBlock { Text = "last", IsStreaming = true }), Turn), Reply("reply", Turn) }; typeof(ThreadRuntime).GetProperty("Entries")!.SetValue(runtime, new ValueList<TimelineEntry>(entries)); var type = typeof(WorkingBlockView); var rows = (ValueList<TimelineEntry>)type.GetMethod("TurnEntries", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [runtime])!; Assert.Equal(new[] { "a", "empty", "b", "reply" }, rows.Select(row => row.Id)); var steps = (ValueList<ThinkingStep>)type.GetMethod("Thinking", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [runtime])!; Assert.Equal(new[] { new ThinkingStep("thinking", false), new ThinkingStep("last", true) }, steps);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-128")] // App/Views/Chat/ThreadTimeline.swift:393-397,689-712
    [Trait("Req", "PR-CHAT-132")] // App/Views/Chat/ThreadTimeline.swift:689-712
    [Trait("Req", "PR-CHAT-191")] // App/Views/Chat/ThreadTimeline.swift:77-81,273-278
    public void Arrival_follows_only_the_end_or_a_question_and_prunes_to_twenty_four_past_ninety_six() => WithTimeline((view, _, _, state, _) =>
    {
        var blocks = Enumerable.Range(0, 120).Select(i => (DisplayBlock)new DisplayBlock.Merging("b" + i)).ToArray(); Put(view, "windowStartID", "b0"); var tracking = Field<TimelineScrollTracking>(view, "tracking"); tracking.IsPinnedToBottom = false; tracking.NoteGeometry(0, 400, 0); state.ShowsJumpButton = true; Invoke<object?>(view, "NoteArrival", "b119", blocks); Assert.False(tracking.IsPinnedToBottom); Assert.Equal(0, Invoke<int>(view, "WindowStartIndex", (object)blocks)); Invoke<object?>(view, "NoteArrival", "question-q", blocks); Assert.True(tracking.IsPinnedToBottom); Assert.False(state.ShowsJumpButton); Assert.Equal(96, Invoke<int>(view, "WindowStartIndex", (object)blocks)); Put(view, "historyLoadPending", true); tracking.IsPinnedToBottom = false; Invoke<object?>(view, "NoteArrival", "question-next", blocks); Assert.False(tracking.IsPinnedToBottom);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-130")] // App/Views/Chat/ThreadTimeline.swift:624-661
    [Trait("Req", "PR-CHAT-131")] // App/Views/Chat/ThreadTimeline.swift:626-638
    public void Unattended_token_change_leaves_an_older_reader_but_first_seen_start_counts_as_a_send() => WithTimeline((view, _, _, _, _) =>
    {
        var tracking = Field<TimelineScrollTracking>(view, "tracking"); tracking.IsPinnedToBottom = false; Invoke<object?>(view, "NoteTurnChange", true, 0); Assert.True(tracking.IsPinnedToBottom); tracking.IsPinnedToBottom = false; Invoke<object?>(view, "NoteTurnChange", false, 0); Invoke<object?>(view, "NoteTurnChange", true, 1); Assert.False(tracking.IsPinnedToBottom); Invoke<object?>(view, "NoteTurnChange", false, 1); Invoke<object?>(view, "NoteTurnChange", true, 1); Assert.True(tracking.IsPinnedToBottom);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-133")] // App/Views/Chat/ThreadTimeline.swift:673-677,2218-2220
    [Trait("Req", "PR-CHAT-134")] // App/Views/Chat/ThreadTimeline.swift:678-688,2224-2226
    [Trait("Req", "PR-CHAT-135")] // App/Views/Chat/ThreadTimeline.swift:713-720
    public void Separate_jump_and_growth_requests_preserve_an_older_reader_and_thread_reset_clears_the_window() => WithTimeline((view, _, _, state, _) =>
    {
        state.JumpToLatest(); state.JumpToLatest(); Assert.Equal(2, state.JumpRequest); state.NoteComposerAreaGrowth(); Assert.Equal(1, state.AreaGrowthRequest); var tracking = Field<TimelineScrollTracking>(view, "tracking"); tracking.IsPinnedToBottom = false; tracking.NoteGeometry(0, 48, 0); Invoke<object?>(view, "ComposerGrew", false); Assert.False(tracking.IsPinnedToBottom); tracking.NoteGeometry(0, 47, 0); Invoke<object?>(view, "ComposerGrew", true); Assert.False(tracking.IsPinnedToBottom); Invoke<object?>(view, "ComposerGrew", false); Assert.True(tracking.IsPinnedToBottom); Put(view, "windowStartID", "old"); Put(view, "historyLoadPending", true); tracking.SetOutline([("old", true)]); tracking.SetVisible("old", true); Invoke<object?>(view, "ResetThread"); Assert.Null(Field<string?>(view, "windowStartID")); Assert.False(Field<bool>(view, "historyLoadPending")); Assert.Null(tracking.ActiveBlockID);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-136")] // App/Views/Chat/ThreadTimeline.swift:433-435,1034-1038,1075-1086
    [Trait("Req", "PR-CHAT-163")] // App/Views/Chat/ThreadTimeline.swift:1062-1071
    public void Arrival_hold_extends_never_shortens_and_scroll_report_is_withdrawn_on_detach() => headless.OnUiThread(() =>
    {
        using var time = new TestRuntime(); using var activity = ScrollActivity.Shared.Override(new ScrollActivity()); var tracking = new TimelineScrollTracking(); time.OnMain(() => { tracking.HoldEnd(1.5); tracking.IsReaderScrolling = true; tracking.ReportActivity(Guid.NewGuid()); Assert.True(ScrollActivity.Shared.Current.IsScrolling); }); time.AdvanceMilliseconds(500); time.OnMain(() => tracking.HoldEnd(0.1)); time.AdvanceMilliseconds(999); time.OnMain(() => Assert.True(tracking.IsArriving)); time.AdvanceMilliseconds(1); time.OnMain(() => { Assert.False(tracking.IsArriving); tracking.EndActivity(); Assert.False(ScrollActivity.Shared.Current.IsScrolling); });
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-137 (a unit-level check of its structure; the mounted proofs are the ThreadTimeline*Tests classes): App/Views/Chat/ThreadTimeline.swift:1290-1350
    public void Front_bumps_concern_only_the_uncovered_window_unless_the_bump_is_app_wide() => OnMain(() =>
    {
        var monitor = FrontMonitor.Shared.Current; var first = new Avalonia.Controls.Window(); var other = new Avalonia.Controls.Window(); Invoke<object?>(monitor, "Bump", first); Assert.True(monitor.Concerns(first)); Assert.False(monitor.Concerns(other)); Assert.False(monitor.Concerns(null)); var revision = monitor.Revision; Invoke<object?>(monitor, "Bump", (object?)null); Assert.Equal(revision + 1, monitor.Revision); Assert.True(monitor.Concerns(other));
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-137 (a unit-level check of its structure; the mounted proofs are the ThreadTimeline*Tests classes): App/Views/Chat/ThreadTimeline.swift:1337-1340 refreshVisibility on the window becoming key
    public void Front_visibility_follows_the_window_becoming_active_even_when_no_activation_event_comes_after_it() => headless.OnUiThread(() =>
    {
        using var actor = Marrow.Core.Port.MainActor.Executor.Override(Marrow.UI.Port.AvaloniaMainExecutor.Instance);
        // Avalonia 12.1.2 raises Activated before IsActive turns true, so the Activated handler can read the old value
        // (native frame run, 2026-10-02: an active window left everything it shows paused). Here IsActive turns true with
        // no Activated after it, as on a real window.
        var monitor = (FrontMonitor)Activator.CreateInstance(typeof(FrontMonitor), nonPublic: true)!;
        var window = new Avalonia.Controls.Window();
        window.Show();
        var setActive = typeof(Avalonia.Controls.WindowBase).GetProperty("IsActive")!.GetSetMethod(nonPublic: true)!;
        setActive.Invoke(window, [false]);
        Invoke<object?>(monitor, "Observe", window);
        Assert.False(monitor.IsVisible);
        setActive.Invoke(window, [true]);
        Assert.True(monitor.IsVisible);
        setActive.Invoke(window, [false]);
        Assert.False(monitor.IsVisible);
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-156 (a unit-level check of its structure; the mounted proofs are the ThreadTimeline*Tests classes): App/Views/Chat/ThreadTimeline.swift:147-155,1784-1795
    public void Mention_targets_choose_newest_same_persona_and_persona_cache_keys_only_indices_and_roster() => OnMain(() =>
    {
        var project = AppModelSamples.Project("project", "C:\\fixture"); var old = AppModelSamples.Thread(project) with { CreatedAt = DateTimeOffset.UnixEpoch, Hydra = new HydraHeadInfo(1, "old", HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated) }; var newer = old with { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UnixEpoch.AddSeconds(1), Hydra = old.Hydra! with { Task = "new" } }; var map = (ValueMap<string, HydraMentionTarget>)typeof(ThreadTimeline).GetMethod("MentionTargets", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [new[] { newer, old }])!; Assert.Equal(newer.Id, map[old.Hydra!.Persona.Name].ThreadID); var cache = new TimelineBlockCache(); var personas = cache.MentionPersonas([old], false); Assert.Equal(new[] { old.Hydra.Persona }, personas); Assert.Same(personas, cache.MentionPersonas([newer], false)); Assert.Equal(HydraRoster.Personas, cache.MentionPersonas([], true));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-162")] // App/Views/Chat/ThreadTimeline.swift:451-457,840-851
    public void Timeline_equivalence_checks_all_handed_values_and_resize_settle_never_unpins() => WithTimeline((view, runtime, chrome, state, _) =>
    {
        var same = new ThreadTimeline(runtime, chrome, state, "Project", null, false, 480); Assert.True(Invoke<bool>(view, "SameInputs", same)); Assert.False(Invoke<bool>(view, "SameInputs", new ThreadTimeline(runtime, chrome, state, "Project", null, false, 481))); Assert.False(Invoke<bool>(view, "SameInputs", new ThreadTimeline(runtime, chrome, state, "Project", null, false, 480, false))); var tracking = Field<TimelineScrollTracking>(view, "tracking"); tracking.IsPinnedToBottom = false; Invoke<object?>(view, "SettleAfterResize"); Assert.False(tracking.IsPinnedToBottom);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-164")] // App/Views/Chat/ThreadTimeline.swift:2006-2019
    [Trait("Req", "PR-CHAT-165")] // App/Views/Chat/ThreadTimeline.swift:1959-1976
    public void Live_partition_reuses_identity_and_keeps_prose_and_tool_work_in_the_card_heads_below_rows() => OnMain(() =>
    {
        var entries = new[] { User("u", Turn), Reply("r", Turn), Tool("t", Turn), Tool("head", Turn, true) }; var cache = new TurnGroupCache(); var groups = cache.Groups(entries); entries[1].Item = entries[1].Item with { TimelineItemContent = new TimelineItem.Content.Assistant(new AssistantMessage { Text = "stream changed" }) }; Assert.Same(groups, cache.Groups(entries)); Assert.NotSame(groups, cache.Groups(entries.Append(Reply("more", Turn)).ToArray())); var partition = ((ValueList<TimelineGroup> Rows, TimelineGroup? Heads, ValueList<TimelineEntry> Work))typeof(TurnRunningBlock).GetMethod("Partition", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [groups])!; Assert.Equal("u", partition.Rows.Single().Id); Assert.Equal("heads-head", partition.Heads!.Id); Assert.Equal(new[] { "r", "t" }, partition.Work.Select(entry => entry.Id));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-167")] // App/Views/Chat/ThreadTimeline.swift:786-788,883-888
    [Trait("Req", "PR-CHAT-168")] // App/Views/Chat/ThreadTimeline.swift:862-889,1193-1235
    public void Eager_stack_has_no_virtualized_generation_or_nudge_to_restore() => OnMain(() =>
    {
        var tracking = new TimelineScrollTracking(); Assert.False(tracking.BeginNudge()); tracking.ArmViewportRefresh(); Assert.Equal(0, tracking.RefreshRequest); Assert.False(tracking.IsNudging); var restored = false; tracking.RestoreNudge(() => restored = true); Assert.False(restored);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-186")] // App/Views/Chat/ThreadTimeline.swift:2101-2104
    [Trait("Req", "PR-CHAT-187")] // App/Views/Chat/ThreadTimeline.swift:2137-2140
    [Trait("Req", "PR-CHAT-188")] // App/Views/Chat/ThreadTimeline.swift:2105-2108,2126-2135,2152-2153
    public void Working_label_bar_and_chevron_follow_running_and_expandability_branches() => WithTimeline((_, runtime, _, _, _) =>
    {
        var finished = new WorkingIndicator(runtime, SwiftDate.Now, 1, new([]), new([]), new([]), false, SwiftDate.Now); Assert.Contains("Earlier steps", LogicalQuery.SelfAndDescendants<TextBlock>(finished).Select(text => text.Text)); Assert.Empty(LogicalQuery.SelfAndDescendants<HydraProgressBar>(finished)); Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<SwiftImage>(finished), icon => icon.SymbolName == "chevron.right"); Assert.Equal("", ToolTip.GetTip(LogicalQuery.SelfAndDescendants<Button>(finished).Single())); var active = new WorkingIndicator(runtime, SwiftDate.Now, 1, new([new ThinkingStep("think", false)]), new([]), new([]), true); Assert.Single(LogicalQuery.SelfAndDescendants<HydraProgressBar>(active)); Assert.Contains(LogicalQuery.SelfAndDescendants<Marrow.UI.Port.Nth.NthLetters>(active), letters => letters.Text == WorkingWords.Word(1, 0) + "\u2026"); // step 2 B: breathing letters var icon = LogicalQuery.SelfAndDescendants<SwiftImage>(active).Single(image => image.SymbolName == "chevron.right"); Assert.Equal(new Avalonia.Media.RotateTransform(0).Angle, Assert.IsType<Avalonia.Media.RotateTransform>(icon.RenderTransform).Angle);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-129")] // App/Views/Chat/ThreadTimeline.swift:289-297,607,637
    public void Warm_pass_collects_only_finished_assistant_text() => OnMain(() =>
    {
        var entries = new[] { Reply("done"), Entry("stream", new TimelineItem.Content.Assistant(new AssistantMessage { Text = "stream", IsStreaming = true })), User("prompt") }; var method = typeof(ThreadTimeline).GetMethod("FinishedMarkdownTexts", BindingFlags.Static | BindingFlags.NonPublic)!; Assert.Equal(new[] { "reply" }, (string[])method.Invoke(null, [entries])!);
    });

    // STOP App.Views.Chat.ThreadTimeline #3
    // Kind: platform dependency. Swift: ThreadTimeline.swift:1303-1323,1347-1350. Record: PR-CHAT-137 (a window wholly covered by another application's, then uncovered, only).
    // Missing: neither Avalonia nor Windows reports that a visible window stopped being fully covered by other windows (macOS's didChangeOcclusionState): when the covering
    // window goes away with no activation, show or restore event for this one, nothing tells the timeline its reader may be back. The app coming to the front (activation of a
    // window while none was visible), a window becoming visible or restored, and every other window's activation are ported and proven by
    // ThreadTimelineMountedTests.The_timeline_checks_itself_when_the_app_or_its_own_window_comes_back_and_not_when_another_window_does.
    // Needed: a Windows occlusion tracker (SetWinEventHook over the top-level windows, as Chromium's native occlusion tracker does); no occlusion state is invented here.

    // STOP App.Views.Chat.ThreadTimeline #6 - CLOSED 2026-10-04 (unit rec-e1) by OD-0076 and the lead's ruling (E_rulings.md, PR-CHAT-104).
    // Swift: TimelineRows.swift:2589-2715 (the fold), ThreadTimeline.swift:1907-1926. Records: PR-CHAT-104 (windows_path amended), PR-CHAT-117/121 (the working card's open and close).
    // Header fold: not a cut in the Swift either (:2609-2616 withAnimation(.snappy(duration: 0.24)), the chevron :2623 in the same transaction, the if/else branches :2631/:2661 animated inside it);
    // the body's height glides on snappy(0.24) in both directions, the chevron on the same clock, the incoming rows fade in, the outgoing branch is gone at once
    // (TimelineRowsTurnFoldMotionTests). Turn-end swap (running block to finished block): the Mac cuts it (2-chat-use.mov 125.743 -> 125.762 s, one frame); by OD-0076 the port transitions it: the
    // finished block takes the running block's height and eases to its own in 0.3 s, its rows fade in over 0.2 s, the running rows are gone at once, Reduce Motion shows the end state
    // (ThreadTimelineFoldTests, ED-315 (2): both entries are in the transition audit; no listed exception).

    // STOP App.Views.Chat.ThreadTimeline #7
    // Kind: shared symbol metrics. Swift: ThreadTimeline.swift:2129-2135 (the chevron joins the header, the time glides over to make room). Record: UI-CHAT-043.
    // Missing: on the reference runtime the time's left edge moves 17 points when the chevron comes (8 of spacing and a 9 point symbol, 2-chat-use.mov 95.803-96.078); the port's chevron.right is 12 wide
    // at the same font, so the whole shift is 20 and the time sits 3 points further left than the Mac's whenever a chevron is shown. The curve is the Mac's (smooth 0.3 s, ThreadTimelineWorkingLineTests).
    // Needed: a per-symbol advance for chevron.right at caption2. ED-182 draws every symbol in a square of 1.2 times the font's size (SwiftImage.SymbolSide), shared by every symbol in the app; not changed here.

    // STOP App.Views.Chat.ThreadTimeline #8
    // Kind: reference capture. Swift: Chrome.swift:581-629, ThreadTimeline.swift:488,1376. Record: PR-CHAT-159 (capture comparison at travel 0, 20 and 44 in both themes).
    // Missing: the reference runtime shows the veil only at full travel in the cousin's dark theme (2-chat-use.mov 8.403, none on short threads), so the falloff over its 72 points is compared (ThreadTimelineVeilTests)
    // and the formula of its level through the travel is proven against the pixels, but the level at full travel (0.525 here, 0.46 there: the theme's surface alpha and wallpaper are the cousin's), travel 20 and
    // the light theme have no reference frame; the Acrylic backdrop behind it needs a real window (OD-0014, OD-0005).
    // Needed: a capture of a thread scrolled to 20 points and a light-theme chat on the reference runtime.

    // STOP App.Views.Chat.ThreadTimeline #9: RESOLVED (unit rec-composer). Swift: ThreadTimeline.swift:359-440. Record: UI-CHAT-001.
    // OD-0013 is closed by OD-0017 (stretch, ED-101; WindowManagerFactoryStretchTests): ThreadTimelineLiveResizeNativeTests drags a real factory main window showing a long
    // thread out and back and finds the app's layer at the moving edge in every step within the accepted residual, and the timeline at its end with rows after it. OD-0071:
    // the timeline scrolls softly (ED-246, its timeline piece taken as an opt-in; ThreadTimelineScrollStabilityTests.The_timeline_scrolls_softly_...).

    // STOP App.Views.Chat.ThreadTimeline #10: RESOLVED by the owner, OD-0105 (1), 2026-10-05 ("Smooth fade"). Swift: ThreadTimeline.swift:132-135, :317-321,339.
    // The loading mark fades out while the rows fade and rise in, in about 0.2 s, and rows that leave together fold shut together, in about 0.3 s (ThreadTimelineHandOverTests,
    // frame by frame). A row whose place a new row takes in the same pass is replaced as before; the swap back to the loading mark stays a cut (the mark is blank for 400 ms).

    [Fact]
    [Trait("Req", "PR-CHAT-192")] // App/Views/Chat/ThreadTimeline.swift:270-278,595-603,1804-1811
    public void Fallback_keeps_three_leaves_a_stale_anchor_and_delayed_load_uses_its_captured_blocks() => WithTimeline((view, _, _, _, h) =>
    {
        var blocks = Enumerable.Range(0, 30).Select(i => (DisplayBlock)new DisplayBlock.Merging("b" + i)).ToArray(); Put(view, "windowStartID", "stale"); Assert.Equal(27, Invoke<int>(view, "WindowStartIndex", (object)blocks)); Assert.Equal("stale", Field<string?>(view, "windowStartID")); Assert.Equal(28, Invoke<int>(view, "WindowStartIndex", (object)blocks.Append(new DisplayBlock.Merging("new")).ToArray())); Put(view, "windowStartID", null); Invoke<object?>(view, "StartInitialHistoryLoad", (object)blocks); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); h.Main.AdvanceMilliseconds(249); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Assert.Null(Field<string?>(view, "windowStartID")); h.Main.AdvanceMilliseconds(1); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Assert.Equal("b6", Field<string?>(view, "windowStartID"));
    });

    [Fact]
    [Trait("Req", "PR-GIT-121")] // App/Views/Chat/ThreadTimeline.swift:167-172,326
    public void Rewindable_turn_ids_are_empty_for_running_or_unsupported_and_built_once_for_idle_supported() => OnMain(() =>
    {
        var method = typeof(ThreadTimeline).GetMethod("RewindableIDs", BindingFlags.NonPublic | BindingFlags.Static)!; var ids = new[] { Turn, Guid.NewGuid() }; Assert.Equal(ids.ToHashSet(), (HashSet<Guid>)method.Invoke(null, [true, false, ids])!); Assert.Empty((HashSet<Guid>)method.Invoke(null, [false, false, ids])!); Assert.Empty((HashSet<Guid>)method.Invoke(null, [true, true, ids])!);
    });

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-086")] // App/Views/Chat/ThreadTimeline.swift:114,1504,1635-1643
    public void Synthetic_merge_uses_exact_fallback_and_has_neither_turn_id_nor_outline_tick() => OnMain(() =>
    {
        var method = typeof(ThreadTimeline).GetMethod("MergeID", BindingFlags.NonPublic | BindingFlags.Static)!; Assert.Equal("hydra-merging", method.Invoke(null, [true, null])); Assert.Equal("note-id", method.Invoke(null, [true, "note-id"])); Assert.Null(method.Invoke(null, [false, "note-id"])); var block = Blocks([], merge: "hydra-merging").Single(); Assert.Equal("hydra-merging", block.Id); Assert.Null(block.TurnID); Assert.False(block.HasUserMessage); Assert.IsType<HydraMergePhase.Merging>(block.HydraMergePhase);
    });

    [Theory]
    [InlineData("Hydra merged project", true)]
    [InlineData("Hydra did not merge: the checkout kept moving.", true)]
    [InlineData("Hydra left a file outside every project.", true)]
    [InlineData("Hydra has no files left to merge.", true)]
    [InlineData("Hydra is waiting for another chat's files.", true)]
    [InlineData("Project: Hydra merged branch", false)]
    [InlineData("Project: Hydra did not merge", false)]
    [InlineData("Hydra", false)]
    [Trait("Req", "PR-HYDRAMERGE-087")] // App/Views/Chat/ThreadTimeline.swift:1645-1652
    public void Multi_project_prefix_is_not_normalized_and_legacy_literal_Hydra_titles_keep_the_pill(string title, bool expected) => OnMain(() =>
    {
        Assert.Equal(expected, DisplayBlock.IsMergeOutcome(new UserMessage { Text = title + "\nbody", HydraHeads = new([]) }));
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof UI-CHAT-001 (a unit-level check of its structure; the mounted proofs are the ThreadTimeline*Tests classes): App/Views/Chat/ThreadTimeline.swift:416-449,1365-1384
    public void Scroll_metrics_cap_travel_and_short_content_reports_zero_distance() => OnMain(() =>
    {
        var method = typeof(ScrollMetrics).GetMethod("From", BindingFlags.NonPublic | BindingFlags.Static)!; var shortContent = (ScrollMetrics)method.Invoke(null, [400.0, 480.0, 50.0, 12.0, 18.0, 290.0])!; Assert.Equal(0, shortContent.Travel); Assert.Equal(0, shortContent.DistanceFromBottom); var tall = (ScrollMetrics)method.Invoke(null, [800.0, 480.0, 500.0, 12.0, 18.0, 740.0])!; Assert.Equal(350, tall.Travel); Assert.Equal(-162, tall.DistanceFromBottom);
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof UI-CHAT-002 (a unit-level check of its structure; the mounted proofs are the ThreadTimeline*Tests classes): App/Views/Chat/ThreadTimeline.swift:364-383
    public void Earlier_pill_has_chat_callout_on_text_and_icon_and_exact_capsule_insets() => WithTimeline((view, _, _, _, _) =>
    {
        var pill = Invoke<Button>(view, "EarlierPill", 42, (Action)(() => { })); Assert.Equal(Avalonia.Layout.HorizontalAlignment.Center, pill.HorizontalAlignment); Assert.Equal(new Thickness(0, 20, 0, 0), pill.Margin); var frame = LogicalQuery.SelfAndDescendants<Border>(pill).Single(border => border.Padding == new Thickness(14, 7)); Assert.True(frame.CornerRadius.TopLeft >= 14); Assert.Equal(SwiftColor.Quaternary.Opacity(0.5).Resolve(pill.ActualThemeVariant), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(frame.Background).Color); var text = LogicalQuery.SelfAndDescendants<TextBlock>(pill).Single(); Assert.Equal("Show 42 earlier messages", text.Text); Assert.Equal(12, text.FontSize); var icon = LogicalQuery.SelfAndDescendants<SwiftImage>(pill).Single(); Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Callout), SwiftImage.GetFont(icon));
    });

    [Fact]
    [Trait("Req", "UI-CHAT-003")] // Partial proof UI-CHAT-003
    [Trait("Rule", "R-TEST-4")] // Partial proof UI-CHAT-003 (a unit-level check of its structure; the mounted proofs are the ThreadTimeline*Tests classes): App/Views/Chat/ThreadTimeline.swift:179-208
    public void Rail_is_an_unframed_thirty_point_overlay_below_the_titlebar() => WithTimeline((view, _, _, _, _) =>
    {
        var blocks = Blocks(new[] { User("first"), User("second") }); var rail = Invoke<Control?>(view, "CreateRail", blocks, true, 600.0); Assert.NotNull(rail); Assert.Equal(30, rail.Width); Assert.Equal(new Thickness(0, WindowChrome.TitlebarHeight, 0, 0), rail.Margin); Assert.Equal(Avalonia.Layout.HorizontalAlignment.Left, rail.HorizontalAlignment); Assert.Null(Invoke<Control?>(view, "CreateRail", blocks, false, 600.0));
    });

    [Fact]
    [Trait("Req", "UI-CHAT-043")] // Partial proof UI-CHAT-043 and UI-CHAT-044 and UI-CHAT-069
    [Trait("Rule", "R-TEST-4")] // Partial proof UI-CHAT-043 and UI-CHAT-044 and UI-CHAT-069 (a unit-level check of its structure; the mounted proofs are the ThreadTimeline*Tests classes): App/Views/Chat/ThreadTimeline.swift:2121-2196; App/Views/Chat/ThreadTimeline.swift:2157-2168; App/Views/Chat/ThreadTimeline.swift:2104,2123
    public void Working_thinking_opens_with_source_color_typography_indent_and_unconditional_follow() => WithTimeline((view, runtime, _, _, h) =>
    {
        var card = new WorkingIndicator(runtime, SwiftDate.Now, 1, new([new ThinkingStep("thought", false)]), new([]), new([]), true, SwiftDate.Now); var icon = LogicalQuery.SelfAndDescendants<SwiftImage>(card).Single(); Assert.Equal(SwiftColor.Tertiary.Resolve(icon.ActualThemeVariant), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(icon.Foreground).Color); var title = LogicalQuery.SelfAndDescendants<TextBlock>(card).Single(text => text.Text == "Earlier steps"); Assert.Equal(13, title.FontSize); Assert.Equal(Avalonia.Media.FontWeight.Medium, title.FontWeight); var follows = 0; ThreadTimeline.SetRevealTimelineEnd(card, new TimelineRevealBox { Follow = () => follows++ }); var button = LogicalQuery.SelfAndDescendants<Button>(card).Single(); button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Assert.Equal(1, follows); var markdown = LogicalQuery.SelfAndDescendants<Marrow.UI.Markdown.MarkdownView>(card).Single(); Assert.Equal(12, Marrow.UI.Markdown.MarkdownView.GetMarkdownPointSize(markdown)); Assert.True(Marrow.UI.Markdown.MarkdownView.GetMarkdownDimmed(markdown)); var stack = Assert.IsType<StackPanel>(markdown.Parent); Assert.Equal(new Thickness(24, 0, 0, 0), stack.Margin); Assert.Equal(20, stack.Spacing); h.Main.AdvanceMilliseconds(600); ((ManualFrameSource)FrameClock.Source.Current).Advance(TimeSpan.FromMilliseconds(600)); Assert.Equal(90, Assert.IsType<Avalonia.Media.RotateTransform>(LogicalQuery.SelfAndDescendants<SwiftImage>(card).Single().RenderTransform).Angle); // the chevron turns over the one animation (:2111-2118 snappy 0.24 s), so it has arrived after it
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof UI-CHAT-060 (a unit-level check of its structure; the mounted proofs are the ThreadTimeline*Tests classes): App/Views/Chat/ThreadTimeline.swift:345,393-397,665-672
    public void Question_badge_follows_blocks_and_uses_question_prefixed_row_identity() => WithTimeline((view, runtime, _, _, _) =>
    {
        var request = new QuestionRequest("q", new([new QuestionRequest.Question("choice", "Header", "Choose", new([]), false, false, false)])); typeof(ThreadRuntime).GetProperty("Entries")!.SetValue(runtime, new ValueList<TimelineEntry>(new[] { Reply("reply", Turn) })); typeof(ThreadRuntime).GetProperty("Questions")!.SetValue(runtime, new ValueList<QuestionRequest>(new[] { request })); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); var badge = LogicalQuery.SelfAndDescendants<Marrow.App.Views.Composer.QuestionBadgeRow>(view).Single(); Assert.Equal("question-q", badge.Name); var stack = Assert.IsType<StackPanel>(badge.Parent); Assert.Same(badge, stack.Children[^1]);
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof UI-CHAT-001 (a unit-level check of its structure; the mounted proofs are the ThreadTimeline*Tests classes): App/Views/Chat/ThreadTimeline.swift:416-422
    public void A_short_row_still_gets_the_full_eight_hundred_twenty_point_column_and_leading_alignment() => WithTimeline((view, _, _, _, _) =>
    {
        var rows = new Border { Height = 40 }; var column = Invoke<Control>(view, "CreateColumn", rows, 480.0, null); column.Measure(new Size(960, 480)); column.Arrange(new Rect(0, 0, 960, 480)); var content = LogicalQuery.SelfAndDescendants<Border>(column).Single(border => border.MaxWidth == 820); Assert.Equal(820, content.Bounds.Width); Assert.Equal(70, rows.TranslatePoint(default, column)!.Value.X);
    });

    [Fact]
    [Trait("Req", "UI-CHAT-043")] // Partial proof UI-CHAT-043 and UI-CHAT-044
    [Trait("Rule", "R-TEST-4")] // Partial proof UI-CHAT-043 and UI-CHAT-044 (a unit-level check of its structure; the mounted proofs are the ThreadTimeline*Tests classes): App/Views/Chat/ThreadTimeline.swift:2089-2092,2107-2119,2155-2176,2185-2188; App/Views/Chat/ThreadTimeline.swift:2157-2168,2303-2314
    public void Streaming_thinking_keeps_the_same_working_indicator_and_its_open_state() => WithTimeline((view, runtime, _, _, _) =>
    {
        var model = AppEnvironment.GetAppModel(view); ((Decorator)view.Parent!).Child = null; model.Settings.ShowReasoning = true; var entry = Reason("thinking", Turn); typeof(ThreadRuntime).GetProperty("Entries")!.SetValue(runtime, new ValueList<TimelineEntry>(new[] { entry })); var work = new WorkingBlockView(runtime, new([]), null); var host = new Border { Child = work }; AppEnvironment.SetAppModel(host, model); var window = new Avalonia.Controls.Window { Content = host, Width = 600, Height = 400 }; try { window.Show(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); var indicator = Assert.IsType<WorkingIndicator>(work.Child); LogicalQuery.SelfAndDescendants<Button>(indicator).Single().RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Assert.Single(LogicalQuery.SelfAndDescendants<Marrow.UI.Markdown.MarkdownView>(indicator)); entry.Item = entry.Item with { TimelineItemContent = new TimelineItem.Content.Reasoning(new ReasoningBlock { Text = "more thinking", IsStreaming = true }) }; Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Assert.Same(indicator, work.Child); Assert.Single(LogicalQuery.SelfAndDescendants<Marrow.UI.Markdown.MarkdownView>(indicator)); } finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-054")] // App/Views/Chat/ThreadTimeline.swift:439-441,578-593
    [Trait("Req", "PR-CHAT-055")] // App/Views/Chat/ThreadTimeline.swift:510-517
    public void Mounted_content_growth_moves_the_bottom_anchor_but_not_an_older_reader() => WithTimeline((view, runtime, _, _, _) =>
    {
        typeof(ThreadRuntime).GetProperty("Entries")!.SetValue(runtime, new ValueList<TimelineEntry>(new[] { Reply("reply", Turn) }));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var window = (Avalonia.Controls.Window)TopLevel.GetTopLevel(view)!;
        var scroll = Field<ScrollViewer>(view, "currentScroll"); var column = Assert.IsType<Grid>(scroll.Content);
        column.Height = 1200; window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        Invoke<object?>(view, "SnapEnd"); window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var tracking = Field<TimelineScrollTracking>(view, "tracking"); tracking.EndActivity(); Put(tracking, "arrivingUntil", 0.0);
        var oldOffset = scroll.Offset.Y;
        column.Height = 1500; window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        Assert.Equal(oldOffset + 300, scroll.Offset.Y, 4); Assert.True(tracking.IsPinnedToBottom); Assert.False(tracking.IsReaderScrolling);
        tracking.IsPinnedToBottom = false; Put(view, "anchorsBottomOnGrowth", false); scroll.Offset = new Vector(0, 100);
        window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); tracking.EndActivity();
        column.Height = 1700; window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        Assert.Equal(100, scroll.Offset.Y, 4); Assert.False(tracking.IsPinnedToBottom);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-050")] // App/Views/Chat/ThreadTimeline.swift:317-321; UI/Chrome/Chrome.swift:940-944
    public void A_new_question_visibly_soft_appears_and_keeps_its_identity_across_an_unrelated_redraw() => WithTimeline((view, runtime, _, _, h) =>
    {
        var frames = new ManualFrameSource(); using var clock = FrameClock.Source.Override(frames);
        typeof(ThreadRuntime).GetProperty("Entries")!.SetValue(runtime, new ValueList<TimelineEntry>(new[] { Reply("reply", Turn) }));
        // The thread is on screen before the question comes: a view joining a stack that is already shown arrives with its transition (a first pass places its views with the stack).
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var request = new QuestionRequest("q", new([new QuestionRequest.Question("choice", "Header", "Choose", new([]), false, false, false)]));
        typeof(ThreadRuntime).GetProperty("Questions")!.SetValue(runtime, new ValueList<QuestionRequest>(new[] { request })); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var badge = LogicalQuery.SelfAndDescendants<Marrow.App.Views.Composer.QuestionBadgeRow>(view).Single();
        Assert.Equal(0, badge.Opacity); Assert.Equal(4, Assert.IsType<Avalonia.Media.TranslateTransform>(badge.RenderTransform).Y);
        h.Main.AdvanceMilliseconds(160); frames.Advance(TimeSpan.FromMilliseconds(160)); Assert.InRange(badge.Opacity, 0.001, 0.999);
        var settle = TimeSpan.FromSeconds(Animation.SoftAppear.Duration); h.Main.Advance(settle); frames.Advance(settle); Assert.Equal(1, badge.Opacity); Assert.Equal(0, Assert.IsType<Avalonia.Media.TranslateTransform>(badge.RenderTransform).Y);
        Field<Action>(view, "redraw")(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Same(badge, LogicalQuery.SelfAndDescendants<Marrow.App.Views.Composer.QuestionBadgeRow>(view).Single()); Assert.Equal(1, badge.Opacity);
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-034 and PR-CHAT-035 (a unit-level check of its structure; the mounted proofs are the ThreadTimeline*Tests classes): App/Views/Chat/ThreadTimeline.swift:104-110,1608-1614; App/Views/Chat/ThreadTimeline.swift:108-110,1615-1617
    public void Finished_head_partition_uses_finish_order_a_strict_cut_and_index_membership() => OnMain(() =>
    {
        var cut = DateTimeOffset.FromUnixTimeSeconds(100);
        HydraHeadInfo Head(int index, DateTimeOffset? finish) => new(index, "task", HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated) { FinishedAt = finish };
        var heads = new[] { Head(3, cut.AddSeconds(1)), Head(4, cut), Head(2, cut.AddSeconds(-2)), Head(1, null), Head(3, cut.AddSeconds(-1)) };
        var method = typeof(ThreadTimeline).GetMethod("SplitHeadPills", BindingFlags.Static | BindingFlags.NonPublic)!;
        var running = (HeadPills)method.Invoke(null, [heads, cut])!; Assert.Equal(new[] { 1, 2, 3 }, running.Earlier); Assert.Equal(new[] { 4 }, running.Later);
        var idle = (HeadPills)method.Invoke(null, [heads, null])!; Assert.Empty(idle.Earlier); Assert.Equal(new[] { 1, 2, 3, 4, 3 }, idle.Later);
    });

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
    [Trait("Req", "PR-CHAT-032")] // App/Views/Chat/ThreadTimeline.swift:1577-1602
    [Trait("Req", "PR-CHAT-033")] // App/Views/Chat/ThreadTimeline.swift:1503
    public void Running_always_has_exactly_one_working_line_in_the_turn_or_as_a_standalone_block() => OnMain(() =>
    {
        Assert.Equal("working-", Blocks([], true).Single().Id); var turn = Assert.IsType<DisplayBlock.Turn>(Blocks(new[] { User("u", Turn), Reply("prose", Turn) }, true).Single()); Assert.True(turn.ShowsWorking); var ended = Blocks(new[] { User("u", Turn), End("done", Turn) }, true); Assert.False(Assert.IsType<DisplayBlock.Turn>(ended[0]).ShowsWorking); Assert.Equal("working-12345678-ABCD-1234-ABCD-123456789ABC", ended[1].Id); Assert.Empty(Blocks([]));
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-034 and PR-CHAT-035 (a unit-level check of its structure; the mounted proofs are the ThreadTimeline*Tests classes): App/Views/Chat/ThreadTimeline.swift:1608-1614; App/Views/Chat/ThreadTimeline.swift:1615-1617
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
