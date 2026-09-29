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
// Job-owned STOP coverage for App.Views.Chat.ThreadTimeline (opencode).
// These Facts carry NO Req trait: each names only part of its record's
// verification_method. The exact missing site is in the STOP comment above it.
[Collection("Headless")]
public sealed class ThreadTimelineOcStopTests(HeadlessSession headless)
{
    private static readonly Guid Turn = Guid.Parse("12345678-abcd-1234-abcd-123456789abc");
    private static TimelineEntry Entry(string id, TimelineItem.Content content, Guid? turn = null) => new(new TimelineItem(turn, content, id));
    private static TimelineEntry User(string id, Guid? turn = null, string text = "prompt") => Entry(id, new TimelineItem.Content.User(new UserMessage { Text = text }), turn);
    private static TimelineEntry Reply(string id, Guid? turn = null) => Entry(id, new TimelineItem.Content.Assistant(new AssistantMessage { Text = "reply" }), turn);
    private static TimelineEntry Tool(string id, Guid? turn = null, bool head = false) => Entry(id, new TimelineItem.Content.Tool(new ToolCall { Title = id, ToolCallKind = head ? ToolCall.Kind.Agent : ToolCall.Kind.Command }), turn);
    private static ValueList<DisplayBlock> Blocks(TimelineEntry[] entries, bool running = false, string? merge = null, HeadPills? pills = null) => DisplayBlock.Build(entries, TimelineMeta.Build(entries), false, running, merge, pills);
    private static T Invoke<T>(object owner, string name, params object?[] args) => (T)owner.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(owner, args)!;
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;

    private void WithTimeline(Action<ThreadTimeline, ThreadRuntime, ChromeScrollModel, TimelineScrollState, AppModelHarness> check) => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var h = new AppModelHarness(); using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); var model = new Marrow.App.AppModel(); var id = Guid.NewGuid(); DiskWriter.Shared.WriteSynchronously(new ThreadDocument(id), Storage.ThreadURL(id)); DocumentPrefetch.Shared.Warm([id], urgent: true)!.Completion.GetAwaiter().GetResult(); var runtime = new ThreadRuntime(id, model); var chrome = new ChromeScrollModel(); var state = new TimelineScrollState(); var view = new ThreadTimeline(runtime, chrome, state, "Project", null, false, 480); var host = new Border(); AppEnvironment.SetAppModel(host, model); host.Child = view; var window = new Avalonia.Controls.Window { Content = host, Width = 960, Height = 480 };
        AppEnvironment.SetWindowLiveResize(host, new Marrow.App.Window.WindowLiveResize(window));
        try { window.Show(); window.UpdateLayout(); check(view, runtime, chrome, state, h); } finally { window.Close(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
    });
    private void OnMain(Action body) => headless.OnUiThread(() => { using var runtime = new TestRuntime(); runtime.OnMain(body); });
    private static void Settle(Avalonia.Controls.Window window) { window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }

    // STOP opencode-App.Views.Chat.ThreadTimeline #1
    // Kind: oracle. Swift: ThreadTimeline.swift:416-420. Records: PR-CHAT-021, UI-CHAT-001 (column capture only).
    // Missing: macOS capture comparison at several window widths (OD-0001: no pinned native pixels).
    // Needed: approved capture oracle. Owned geometry (820/20/ContentTopInset/18) is asserted below.
    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void OcStop_Column_geometry_matches_shared_column_without_capture_oracle() => WithTimeline((view, _, _, _, _) =>
    {
        var rows = new Border { Height = 40 }; var column = Invoke<Control>(view, "CreateColumn", rows, 480.0, null);
        column.Measure(new Size(960, 480)); column.Arrange(new Rect(0, 0, 960, 480));
        var content = LogicalQuery.SelfAndDescendants<Border>(column).Single(border => border.MaxWidth == 820);
        Assert.Equal(new Thickness(20, Chrome.ContentTopInset, 20, 18), content.Margin);
        Assert.Equal(72, Chrome.ContentTopInset);
        Assert.Equal(20, TimelineMetrics.RowSpacing);
    });

    // STOP opencode-App.Views.Chat.ThreadTimeline #2
    // Kind: oracle. Swift: ThreadTimeline.swift:325,421-422. Records: PR-CHAT-022, UI-CHAT-001 (short-thread capture only).
    // Missing: macOS capture comparison with a one-message thread (OD-0001).
    // Needed: approved capture oracle. Owned min-height plus zero-minimum spacer is asserted below.
    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void OcStop_Short_thread_forces_viewport_height_with_top_spacer() => WithTimeline((view, _, _, _, _) =>
    {
        var rows = new Border { Height = 40 }; var column = Invoke<Control>(view, "CreateColumn", rows, 480.0, null);
        column.Measure(new Size(960, 480)); column.Arrange(new Rect(0, 0, 960, 480));
        Assert.Equal(480, column.MinHeight);
        var grid = Assert.IsType<Grid>(LogicalQuery.SelfAndDescendants<Grid>(column).First(g => g.RowDefinitions.Count == 2));
        Assert.True(rows.TranslatePoint(default, column)!.Value.Y >= 400);
    });

    // STOP opencode-App.Views.Chat.ThreadTimeline #3
    // Kind: oracle. Swift: ThreadTimeline.swift:161-162,366-382. Records: PR-CHAT-046, UI-CHAT-002 (capture only).
    // Missing: macOS capture comparison of the pill (OD-0001).
    // Needed: approved capture oracle. Owned string/count/font/padding/click/page is asserted below.
    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void OcStop_Earlier_pill_text_count_and_page_without_capture_oracle() => WithTimeline((view, _, _, _, _) =>
    {
        var pill = Invoke<Button>(view, "EarlierPill", 42, (Action)(() => { }));
        Assert.Equal("Show 42 earlier messages", LogicalQuery.SelfAndDescendants<TextBlock>(pill).Single().Text);
        Assert.Equal("chevron.up", LogicalQuery.SelfAndDescendants<SwiftImage>(pill).Single().SymbolName);
        var blocks = Enumerable.Range(0, 60).Select(i => (DisplayBlock)new DisplayBlock.Merging("b" + i)).ToArray();
        Invoke<object?>(view, "LoadEarlier", blocks, 24);
        Assert.Equal(36, Invoke<int>(view, "WindowStartIndex", (object)blocks));
    });

    // STOP opencode-App.Views.Chat.ThreadTimeline #4
    // Kind: oracle + shared foundation. Swift: ThreadTimeline.swift:2096-2097,2107-2119,2121-2149.
    // Records: PR-CHAT-117, UI-CHAT-043 (capture, continuous curve ED-181, symbol metrics ED-182, animation oracle ED-117).
    // Missing: macOS captures with the setting on/off; approved continuous-curve mapping; symbol optical metrics;
    // pinned smooth/snappy interpolation oracle.
    // Needed: OD-0001 captures, ED-181, ED-182/O-63, ED-117/O-34. Owned widths/insets/header/body is asserted below.
    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void OcStop_Working_card_and_badge_shell_without_capture_curve_or_symbol_oracle() => WithTimeline((_, runtime, _, _, _) =>
    {
        var card = new WorkingIndicator(runtime, SwiftDate.Now, 1, new([new ThinkingStep("thought", false)]), new([]), new([]), true);
        var cardShell = LogicalQuery.SelfAndDescendants<Border>(card).Single(border => border.Width == 380);
        Assert.Equal(new Thickness(14, 11), cardShell.Padding);
        var badge = new WorkingIndicator(runtime, SwiftDate.Now, 1, new([]), new([]), new([]), false);
        var badgeShell = LogicalQuery.SelfAndDescendants<Border>(badge).Single(border => border.Padding == new Thickness(TimelineMetrics.PillLeading, TimelineMetrics.PillVertical, TimelineMetrics.PillTrailing, TimelineMetrics.PillVertical));
        Assert.True(double.IsNaN(badgeShell.Width));
    });

    // STOP opencode-App.Views.Chat.ThreadTimeline #5
    // Kind: platform dependency. Swift: ThreadTimeline.swift:616-623,1137-1142,1290-1350. Record: PR-CHAT-137 (native occlusion only).
    // Missing: Windows signal for a visible window fully covered by another app; Avalonia reports active/visible/minimized only.
    // Needed: window-layer occlusion signal. Identity-scoped bumps are asserted below.
    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void OcStop_Front_identity_guard_without_native_occlusion_signal() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var monitor = FrontMonitor.Shared.Current; var first = new Avalonia.Controls.Window(); var other = new Avalonia.Controls.Window();
            typeof(FrontMonitor).GetMethod("Bump", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(monitor, [first]);
            Assert.True(monitor.Concerns(first)); Assert.False(monitor.Concerns(other));
            var tracking = new TimelineScrollTracking(); tracking.NoteReturnToFront();
            Assert.Equal(1, tracking.CheckRequest);
            tracking.EndActivity();
        });
    });

    // STOP opencode-App.Views.Chat.ThreadTimeline #6
    // Kind: dependency. Swift: ThreadTimeline.swift:123-131,147-155,209-217. Record: PR-CHAT-156 (per-timeline URL dispatch only).
    // Missing: MarkdownView exposes a global OpenUrl callback, not an inherited per-timeline open-URL action.
    // Needed: UI-layer inherited link hook. Persona lists/target ordering/memoization are asserted below.
    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void OcStop_Mention_personas_and_targets_without_inherited_link_hook() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var project = AppModelSamples.Project("project", "C:\\fixture");
            var old = AppModelSamples.Thread(project) with { CreatedAt = DateTimeOffset.UnixEpoch, Hydra = new HydraHeadInfo(1, "old", HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated) };
            var newer = old with { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UnixEpoch.AddSeconds(1), Hydra = old.Hydra! with { Task = "new" } };
            var map = (ValueMap<string, HydraMentionTarget>)typeof(ThreadTimeline).GetMethod("MentionTargets", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [new[] { newer, old }])!;
            Assert.Equal(newer.Id, map[old.Hydra!.Persona.Name].ThreadID);
            var cache = new TimelineBlockCache();
            Assert.Equal(HydraRoster.Personas, cache.MentionPersonas([], true));
        });
    });

    // STOP opencode-App.Views.Chat.ThreadTimeline #7
    // Kind: dependency. Swift: ChatView.swift:108-111; ThreadTimeline.swift:26-35,180. Records: PR-CHAT-160, UI-CHAT-003 (caller mounting/free-width only).
    // Missing: external ChatView caller memoization (.equatable) and docked/free-width showsMinimap expression; ChatView is a skeleton here.
    // Needed: ChatView mounting boundary. The eight owned inputs and the rail flag are asserted below.
    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void OcStop_Timeline_inputs_and_rail_flag_without_caller_mounting() => WithTimeline((view, runtime, chrome, state, _) =>
    {
        var same = new ThreadTimeline(runtime, chrome, state, "Project", null, false, 480);
        Assert.True(Invoke<bool>(view, "SameInputs", same));
        Assert.False(Invoke<bool>(view, "SameInputs", new ThreadTimeline(runtime, chrome, state, "Project", null, false, 481)));
        var blocks = Blocks(new[] { User("first"), User("second") });
        Assert.NotNull(Invoke<Control?>(view, "CreateRail", blocks, true, 600.0));
        Assert.Null(Invoke<Control?>(view, "CreateRail", blocks, false, 600.0));
        Assert.Null(Invoke<Control?>(view, "CreateRail", Blocks(new[] { User("only") }), true, 600.0));
    });

    // STOP opencode-App.Views.Chat.ThreadTimeline #8
    // Kind: oracle. Swift: ThreadTimeline.swift:179-208; TimelineMinimap.swift:201-240,412-424.
    // Record: UI-CHAT-003 (hover/drag/glide + native pointer style + capture only).
    // Missing: hover preview, press-and-drag scrub, 0.35 s glide, pointing-hand cursor oracle, macOS capture;
    // the drag/glide live in TimelineMinimap, not in this unit's CreateRail gate.
    // Needed: TimelineMinimap pointer interaction proof + OD-0001 captures. Owned rail geometry/gating is asserted below.
    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void OcStop_Rail_geometry_and_gating_without_minimap_interaction_oracle() => WithTimeline((view, _, _, _, _) =>
    {
        var blocks = Blocks(new[] { User("first"), User("second") });
        var rail = Invoke<Control?>(view, "CreateRail", blocks, true, 600.0);
        Assert.NotNull(rail); Assert.Equal(30, rail.Width);
        Assert.Equal(new Thickness(0, WindowChrome.TitlebarHeight, 0, 0), rail.Margin);
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Left, rail.HorizontalAlignment);
    });

    // STOP opencode-App.Views.Chat.ThreadTimeline #9
    // Kind: oracle. Swift: ThreadTimeline.swift:2157-2168,2278. Record: UI-CHAT-044 (native text + wrapped placement only).
    // Missing: pinned native 12 pt markdown layout/selection oracle (OD-0001); ED-217 wrapped placement is unverified here.
    // Needed: OD-0001 captures. Owned indent/spacing/dim/gating is asserted below.
    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void OcStop_Thinking_indent_and_dim_without_native_text_oracle() => WithTimeline((view, runtime, _, _, _) =>
    {
        var card = new WorkingIndicator(runtime, SwiftDate.Now, 1, new([new ThinkingStep("thought", false)]), new([Tool("a")]), new([]), true);
        var window = new Avalonia.Controls.Window { Content = new Border { Child = card }, Width = 600, Height = 400 };
        try
        {
            window.Show(); Settle(window);
            LogicalQuery.SelfAndDescendants<Button>(card).Single().RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Settle(window);
            var markdown = LogicalQuery.SelfAndDescendants<Marrow.UI.Markdown.MarkdownView>(card).Single();
            Assert.Equal(12, Marrow.UI.Markdown.MarkdownView.GetMarkdownPointSize(markdown));
            Assert.True(Marrow.UI.Markdown.MarkdownView.GetMarkdownDimmed(markdown));
            var stack = Assert.IsType<StackPanel>(markdown.Parent);
            Assert.Equal(new Thickness(24, 0, 0, 0), stack.Margin);
            Assert.Equal(20, stack.Spacing);
        }
        finally { window.Close(); }
    });

    // STOP opencode-App.Views.Chat.ThreadTimeline #10
    // Kind: dependency + oracle. Swift: ThreadTimeline.swift:345,393-397,665-672; QuestionTab.swift:5.
    // Record: UI-CHAT-060 (badge internals + soft-appear frame oracle + arrival/viewport interplay only).
    // Missing: badge internals belong to Composer.QuestionTab; soft-appear frame cadence oracle (ED-117/O-34);
    // the last-badge-leaving viewport refresh races the composer runtime.
    // Needed: Composer ownership + pinned frame oracle. Owned placement/identity is asserted below.
    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void OcStop_Question_placement_and_identity_without_badge_internals_or_frame_oracle() => WithTimeline((view, runtime, _, _, _) =>
    {
        var request = new QuestionRequest("q", new([new QuestionRequest.Question("choice", "Header", "Choose", new([]), false, false, false)]));
        typeof(ThreadRuntime).GetProperty("Entries")!.SetValue(runtime, new ValueList<TimelineEntry>(new[] { Reply("reply", Turn) }));
        typeof(ThreadRuntime).GetProperty("Questions")!.SetValue(runtime, new ValueList<QuestionRequest>(new[] { request }));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var badge = LogicalQuery.SelfAndDescendants<Marrow.App.Views.Composer.QuestionBadgeRow>(view).Single();
        Assert.Equal("question-q", badge.Name);
        Assert.Same(badge, Assert.IsType<StackPanel>(badge.Parent).Children[^1]);
    });
}
