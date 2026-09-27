using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Store;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/ThreadTimeline.swift (B3 52bc792), :2098-2176 (WorkingIndicator's label and opened
// body); DroppyCode/UI/Chrome/Chrome.swift:938-960 (SoftAppearModifier, AnyTransition.softAppear, Animation.softAppear).
// Ported from cloud/b3-App.Views.Chat.ThreadTimeline (055e70546) with the indicator mounted in a shown window: WorkSteps
// fills its rows through ViewLifetime.Track, which applies only once the control is in a visual tree.
[Collection("Headless")]
public sealed class ThreadTimelineProofTests(HeadlessSession headless)
{
    private static readonly Guid Turn = Guid.Parse("12345678-abcd-1234-abcd-123456789abc");
    private static TimelineEntry Tool(string id) => new(new TimelineItem(Turn, new TimelineItem.Content.Tool(new ToolCall { Title = id, ToolCallKind = ToolCall.Kind.Command }), id));
    private static void Run() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    private static void Open(WorkingIndicator card) { LogicalQuery.SelfAndDescendants<Button>(card).First().RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Run(); }
    private static double OffsetY(Control control) => Assert.IsType<TranslateTransform>(control.RenderTransform).Y;

    // The shown-window fixture of ThreadTimelineTests.WithTimeline, with an empty app-model host the indicator is mounted in.
    private void WithHost(Action<ThreadRuntime, Border, Action<int>> check) => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource(); using var clock = FrameClock.Source.Override(frames);
        using var h = new AppModelHarness(); using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); var model = new Marrow.App.AppModel(); var id = Guid.NewGuid(); DiskWriter.Shared.WriteSynchronously(new ThreadDocument(id), Storage.ThreadURL(id)); DocumentPrefetch.Shared.Warm([id], urgent: true)!.Completion.GetAwaiter().GetResult(); var runtime = new ThreadRuntime(id, model); var host = new Border(); AppEnvironment.SetAppModel(host, model); var window = new Avalonia.Controls.Window { Content = host, Width = 960, Height = 480 };
        AppEnvironment.SetWindowLiveResize(host, new Marrow.App.Window.WindowLiveResize(window));
        void Advance(int milliseconds) { h.Main.AdvanceMilliseconds(milliseconds); frames.Advance(TimeSpan.FromMilliseconds(milliseconds)); Run(); }
        try { window.Show(); window.UpdateLayout(); check(runtime, host, Advance); } finally { window.Close(); Run(); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-186")] // App/Views/Chat/ThreadTimeline.swift:2101-2104,2123; App/Views/Chat/TimelineRows.swift:1626
    public void Live_tool_work_labels_the_line_with_the_group_summary_whether_running_or_finished() => WithHost((runtime, host, _) =>
    {
        var liveWork = new ValueList<TimelineEntry>(new[] { Tool("t1") });
        // TimelineRows.swift:1626: a command call still running reads "Running commands".
        Assert.Equal("Running commands", WorkGroupSummary.Text(liveWork));
        // :2104 hasTools wins over isRunning: the summary, not the running word.
        var running = new WorkingIndicator(runtime, SwiftDate.Now, 1, new([]), liveWork, new([]), false); host.Child = running; Run();
        Assert.Contains(LogicalQuery.SelfAndDescendants<TextBlock>(running), text => text.Text == "Running commands");
        Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<TextBlock>(running), text => text.Text == WorkingWords.Word(1, 0) + "\u2026");
        // :2104 a finished segment that still holds tool work shows the summary, not "Earlier steps".
        var finished = new WorkingIndicator(runtime, SwiftDate.Now, 1, new([]), liveWork, new([]), false, SwiftDate.Now); host.Child = finished; Run();
        Assert.Contains(LogicalQuery.SelfAndDescendants<TextBlock>(finished), text => text.Text == "Running commands");
        Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<TextBlock>(finished), text => text.Text == "Earlier steps");
    });

    [Fact]
    [Trait("Req", "PR-CHAT-121")] // App/Views/Chat/ThreadTimeline.swift:2105,2155,2169-2172; UI/Chrome/Chrome.swift:938-944,949-952,960
    [Trait("Req", "PR-CHAT-125")] // App/Views/Chat/ThreadTimeline.swift:2091-2092,2155,2185-2188; App/Views/Chat/TimelineRows.swift:1523-1536
    public void Opening_the_line_soft_reveals_its_live_work_with_the_two_latest_steps_under_the_earlier_line() => WithHost((runtime, host, advance) =>
    {
        var liveWork = new ValueList<TimelineEntry>(new[] { Tool("a"), Tool("b"), Tool("c") });
        var card = new WorkingIndicator(runtime, SwiftDate.Now, 1, new([]), liveWork, new([]), true); host.Child = card; Run();
        // :2091 isExpanded starts false (chronological is false, :2185-2188): no steps until opened (:2155).
        Assert.Empty(LogicalQuery.SelfAndDescendants<WorkSteps>(card));
        Open(card);
        // :2169-2172 the opened body holds WorkSteps over the live work, arriving with .transition(.softAppear):
        // Chrome.swift:940-944 opacity 0 and a 4-point drop at the start of the insertion.
        var steps = LogicalQuery.SelfAndDescendants<WorkSteps>(card).Single();
        Assert.Equal(0, steps.Opacity); Assert.Equal(4, OffsetY(steps));
        advance(160); Assert.InRange(steps.Opacity, 0.001, 0.999);
        advance(400); Assert.Equal(1, steps.Opacity); Assert.Equal(0, OffsetY(steps)); // Chrome.swift:960 .smooth(duration: 0.32)
        // TimelineRows.swift:1523-1536 folded steps: the two latest rows under one "1 earlier step" line.
        Assert.Equal(2, LogicalQuery.SelfAndDescendants<ToolRow>(steps).Count());
        Assert.Contains(LogicalQuery.SelfAndDescendants<TextBlock>(steps), text => text.Text == "1 earlier step");
    });

    [Fact]
    [Trait("Req", "PR-CHAT-121")] // App/Views/Chat/ThreadTimeline.swift:2155-2172; UI/Chrome/Chrome.swift:949-955
    public void An_open_line_keeps_its_steps_across_new_values_and_fades_out_the_part_that_goes() => WithHost((runtime, host, advance) =>
    {
        var a = Tool("a"); var b = Tool("b");
        var card = new WorkingIndicator(runtime, SwiftDate.Now, 1, new([new ThinkingStep("thought", true)]), new([a]), new([]), true); host.Child = card; Run();
        Open(card); advance(600);
        var steps = LogicalQuery.SelfAndDescendants<WorkSteps>(card).Single();
        var thinking = Assert.IsType<StackPanel>(LogicalQuery.SelfAndDescendants<Marrow.UI.Markdown.MarkdownView>(card).Single().Parent);
        // :2156 and :2169 are structural ifs: while each still holds, a new render keeps the same view, and a transition
        // plays only on insertion or removal, never again on an update.
        card.UpdateValues(new([new ThinkingStep("thought more", true)]), new([a, b]), new([]), true); Run();
        Assert.Same(steps, LogicalQuery.SelfAndDescendants<WorkSteps>(card).Single()); Assert.Equal(1, steps.Opacity); Assert.Equal(0, OffsetY(steps));
        Assert.Equal(2, LogicalQuery.SelfAndDescendants<ToolRow>(steps).Count());
        Assert.Same(thinking, LogicalQuery.SelfAndDescendants<Marrow.UI.Markdown.MarkdownView>(card).Single().Parent); Assert.Equal(1, thinking.Opacity);
        // Chrome.swift:953 .softAppear leaves with .opacity.animation(.easeOut(duration: 0.12)): the thinking fades out.
        card.UpdateValues(new([]), new([a, b]), new([]), true); Run();
        Assert.Same(thinking, LogicalQuery.SelfAndDescendants<Marrow.UI.Markdown.MarkdownView>(card).Single().Parent);
        advance(60); Assert.InRange(thinking.Opacity, 0.001, 0.999);
        advance(200); Assert.Empty(LogicalQuery.SelfAndDescendants<Marrow.UI.Markdown.MarkdownView>(card));
        // The same removal for the work steps when the live work empties while the thinking keeps the line open.
        card.UpdateValues(new([new ThinkingStep("again", true)]), new([]), new([]), true); Run();
        Assert.Same(steps, LogicalQuery.SelfAndDescendants<WorkSteps>(card).Single());
        advance(60); Assert.InRange(steps.Opacity, 0.001, 0.999);
        advance(200); Assert.Empty(LogicalQuery.SelfAndDescendants<WorkSteps>(card));
    });
}
