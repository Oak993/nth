using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Diff;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), :1-2859; the file is unchanged from B2 1fbf81e,
// so B2 and B3 line numbers are the same.
// Part 1 of 6 (PR-CHAT-020, 036, 077-082, 088-102, 104) plus the AttachmentStrip and AttachmentThumbnail tests
// (UI-CHAT-009) an earlier merge brought. Sites this part cannot verify yet are listed in the STOP notes at the end.
[Collection("Headless")]
public sealed class TimelineRowsTests(HeadlessSession headless)
{
    private static Attachment Photo(string path) => new() { Name = "photo.png", Path = path, MimeType = "image/png" };

    private static AvaloniaWindow Show(Control control)
    {
        var window = new AvaloniaWindow { Content = control, Width = 800, Height = 600 };
        window.Show();
        window.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void Settle(AvaloniaWindow window)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
    }

    private static Border Plate(Control thumbnail) =>
        LogicalQuery.SelfAndDescendants<Border>(thumbnail).First(border => border.ClipToBounds);

    private static void OnMain(HeadlessSession headless, System.Action body) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(body);
    });

    // A real offline ThreadRuntime inside a headless UI test (FollowUpEditorTests' note): the login environment is
    // loaded once on a pool thread first, so the harness's own Load().Wait() on the UI thread returns at once.
    private static ThreadRuntimePart12Harness RuntimeHarness()
    {
        System.Threading.Tasks.Task.Run(() => LoginEnvironment.Load()).GetAwaiter().GetResult();
        return new ThreadRuntimePart12Harness();
    }

    private static string[] Texts(Control control) =>
        LogicalQuery.SelfAndDescendants<TextBlock>(control).Select(block => block.Text ?? "").ToArray();

    private static string[] Symbols(Control control) =>
        LogicalQuery.SelfAndDescendants<SwiftImage>(control).Select(image => image.SymbolName ?? "").ToArray();

    private static Color Resolved(SwiftColor color, Control on) => color.Resolve(on.ActualThemeVariant);

    private static Color BrushColor(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static ToolCall Call(ToolCall.Kind kind, string title, ToolCall.Status status = ToolCall.Status.Completed) =>
        new() { ToolCallKind = kind, Title = title, ToolCallStatus = status };

    private static TimelineEntry ToolEntry(ToolCall call, System.Guid? turn = null) =>
        new(new TimelineItem(turn, new TimelineItem.Content.Tool(call)));

    private static TimelineEntry ReplyEntry(string text, bool streaming = false, System.Guid? turn = null) =>
        new(new TimelineItem(turn, new TimelineItem.Content.Assistant(new AssistantMessage { Text = text, IsStreaming = streaming })));

    private static TimelineEntry UserEntry(string text, System.Guid? turn, ValueList<Attachment>? attachments = null) =>
        new(new TimelineItem(turn, new TimelineItem.Content.User(new UserMessage { Text = text, Attachments = attachments ?? ValueList<Attachment>.Empty })));

    private static TurnSummary Summary(System.Guid turn, double seconds = 5) => new()
    {
        TurnID = turn,
        Status = TurnStatus.Completed,
        Duration = seconds,
        FilesChanged = 0,
        Additions = 0,
        Deletions = 0,
    };

    private static void Hover(AvaloniaWindow window, Control control)
    {
        var point = control.TranslatePoint(new Point(2, 2), window) ?? throw new Xunit.Sdk.XunitException("not in the window");
        window.MouseMove(point, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    // Drains what the test posted to Avalonia and closes its window while the test's main actor is still bound, so no
    // layout or input job is left for the headless session's reset.
    private static void Close(AvaloniaWindow window)
    {
        Settle(window);
        window.Close();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    // Code-only markdown: renders through MarkdownBlockView, so these tests do not depend on the sibling
    // ProseRunView unit, which is still MARROW_STUB.
    private const string CodeOnlyReply = "```\nlet x = 1\n```";

    // ---------------------------------------------------------------- AttachmentStrip / AttachmentThumbnail

    [Fact]
    [Trait("Req", "UI-CHAT-009")] // App/Views/Chat/TimelineRows.swift:1237, :1292-1304
    public void A_photo_thumbnail_is_a_56_point_plate_rounded_12_and_clipped()
    {
        OnMain(headless, () =>
        {
            var thumbnail = new AttachmentThumbnail(Photo(@"C:\no-such-folder\photo.png"), new AttachmentPreviewSlot());
            var window = Show(thumbnail);
            var plate = Plate(thumbnail);
            Assert.Equal(56, plate.Width);
            Assert.Equal(56, plate.Height);
            Assert.Equal(new CornerRadius(12), plate.CornerRadius);
            Assert.True(plate.ClipToBounds);
            Close(window);
        });
    }

    [Fact]
    [Trait("Req", "UI-CHAT-009")] // App/Views/Chat/TimelineRows.swift:1237, :1256-1262
    public void A_caller_size_sizes_the_plate()
    {
        OnMain(headless, () =>
        {
            var thumbnail = new AttachmentThumbnail(Photo(@"C:\no-such-folder\photo.png"), new AttachmentPreviewCoordinator(), size: 48);
            var window = Show(thumbnail);
            Assert.Equal(48, Plate(thumbnail).Width);
            Assert.Equal(48, Plate(thumbnail).Height);
            Close(window);
        });
    }

    [Fact]
    [Trait("Req", "UI-CHAT-009")] // App/Views/Chat/TimelineRows.swift:1307-1323
    public void A_file_that_is_neither_photo_nor_movie_shows_its_icon_inset_by_eight_hundredths_and_is_named()
    {
        OnMain(headless, () =>
        {
            var file = new Attachment { Name = "notes.txt", Path = @"C:\no-such-folder\notes.txt", MimeType = "text/plain" };
            var thumbnail = new AttachmentThumbnail(file, new AttachmentPreviewSlot());
            var window = Show(thumbnail);
            var plate = Plate(thumbnail);
            var icon = Assert.Single(LogicalQuery.SelfAndDescendants<Image>(plate));
            Assert.Equal(new Thickness(56 * 0.08), icon.Margin);
            Assert.Equal(Stretch.Uniform, icon.Stretch);
            Assert.Equal("notes.txt", AutomationProperties.GetName(plate));
            Close(window);
        });
    }

    [Fact]
    [Trait("Req", "UI-CHAT-009")] // App/Views/Chat/TimelineRows.swift:1305-1306
    public void A_movie_uses_the_video_thumbnail()
    {
        OnMain(headless, () =>
        {
            var movie = new Attachment { Name = "clip.mp4", Path = @"C:\no-such-folder\clip.mp4", MimeType = "video/mp4" };
            var thumbnail = new AttachmentThumbnail(movie, new AttachmentPreviewSlot());
            Assert.Single(LogicalQuery.SelfAndDescendants<AttachmentVideoThumbnail>(thumbnail));
        });
    }

    [Fact]
    [Trait("Req", "UI-CHAT-009")] // App/Views/Chat/TimelineRows.swift:1270-1271, :1295-1301
    public void A_photo_already_in_memory_starts_out_drawn_and_a_missing_one_leaves_the_plate_empty()
    {
        OnMain(headless, () =>
        {
            var drawnPath = @"C:\no-such-folder\seen.png";
            ThumbnailMemory.Store(new DecodedBitmap(2, 2, 8, new byte[16]), drawnPath, 56);
            var seen = new AttachmentThumbnail(Photo(drawnPath), new AttachmentPreviewSlot());
            Assert.Single(LogicalQuery.SelfAndDescendants<Image>(Plate(seen)));

            var missing = new AttachmentThumbnail(Photo(@"C:\no-such-folder\never-seen.png"), new AttachmentPreviewSlot());
            var window = Show(missing);
            Assert.Empty(LogicalQuery.SelfAndDescendants<Image>(Plate(missing)));
            Close(window);
        });
    }

    [Fact]
    [Trait("Req", "UI-CHAT-009")] // App/Views/Chat/TimelineRows.swift:1327
    public void A_thumbnail_button_never_takes_focus()
    {
        OnMain(headless, () =>
        {
            var thumbnail = new AttachmentThumbnail(Photo(@"C:\no-such-folder\photo.png"), new AttachmentPreviewSlot());
            Assert.False(LogicalQuery.SelfAndDescendants<Button>(thumbnail).Single().Focusable);
        });
    }

    [Fact]
    [Trait("Req", "UI-CHAT-009")] // App/Views/Chat/TimelineRows.swift:1222-1225
    public void The_strip_lays_thumbnails_6_apart_each_with_its_name_as_tooltip()
    {
        OnMain(headless, () =>
        {
            var a = new Attachment { Name = "a.txt", Path = @"C:\no-such-folder\a.txt", MimeType = "text/plain" };
            var b = new Attachment { Name = "b.txt", Path = @"C:\no-such-folder\b.txt", MimeType = "text/plain" };
            var strip = new AttachmentStrip(new ValueList<Attachment>([a, b]));
            var window = Show(strip);
            var row = LogicalQuery.SelfAndDescendants<StackPanel>(strip).First();
            Assert.Equal(6, row.Spacing);
            var thumbnails = LogicalQuery.SelfAndDescendants<AttachmentThumbnail>(strip).ToList();
            Assert.Equal(2, thumbnails.Count);
            Assert.Equal("a.txt", ToolTip.GetTip(thumbnails[0]));
            Assert.Equal("b.txt", ToolTip.GetTip(thumbnails[1]));
            Close(window);
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-020 TimelineMetrics

    [Fact]
    [Trait("Req", "PR-CHAT-020")] // App/Views/Chat/TimelineRows.swift:8-28
    public void The_timeline_geometry_is_the_eight_named_constants()
    {
        Assert.Equal(20, TimelineMetrics.RowSpacing);
        Assert.Equal(22, TimelineMetrics.HoverLineHeight);
        Assert.Equal(16, TimelineMetrics.IconWidth);
        Assert.Equal(8, TimelineMetrics.IconSpacing);
        Assert.Equal(12, TimelineMetrics.PillLeading);
        Assert.Equal(14, TimelineMetrics.PillTrailing);
        Assert.Equal(8, TimelineMetrics.PillVertical);
        Assert.Equal(18, TimelineMetrics.PillRadius);
    }

    // ---------------------------------------------------------------- PR-CHAT-036 finishedHeads

    private static ChatThread HeadThread(Project project, HydraHeadInfo head) => AppModelSamples.Thread(project) with { Hydra = head };

    private static HydraHeadInfo Head(long index, HydraHeadInfo.Status status, System.DateTimeOffset startedAt, System.DateTimeOffset? finishedAt = null, System.Guid? batch = null) =>
        new HydraHeadInfo(index, "task", HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated) with
        {
            HydraHeadInfoStatus = status,
            StartedAt = startedAt,
            FinishedAt = finishedAt,
            BatchID = batch,
        };

    [Fact]
    [Trait("Req", "PR-CHAT-036")] // App/Views/Chat/TimelineRows.swift:423-426
    public void With_no_running_head_no_finished_head_is_shown()
    {
        using var runtime = new TestRuntime(); // ChatThread.init reads the defaults store
        var project = AppModelSamples.Project("p", @"C:\no-such-folder\p");
        var t0 = System.DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var threads = new[]
        {
            HeadThread(project, Head(1, HydraHeadInfo.Status.Completed, t0, t0.AddSeconds(5))),
            HeadThread(project, Head(2, HydraHeadInfo.Status.Failed, t0, t0.AddSeconds(6))),
        };
        Assert.Empty(HydraHeadsWorkingRow.FinishedHeads(threads));
    }

    [Fact]
    [Trait("Req", "PR-CHAT-036")] // App/Views/Chat/TimelineRows.swift:428-432
    public void A_finished_head_counts_when_its_batch_still_has_a_head_at_work_whatever_its_time()
    {
        using var runtime = new TestRuntime(); // ChatThread.init reads the defaults store
        var project = AppModelSamples.Project("p", @"C:\no-such-folder\p");
        var t0 = System.DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var batch = System.Guid.NewGuid();
        var otherBatch = System.Guid.NewGuid();
        var sameBatchEarly = Head(1, HydraHeadInfo.Status.Completed, t0.AddSeconds(-100), t0.AddSeconds(-50), batch);
        var otherBatchLate = Head(2, HydraHeadInfo.Status.Completed, t0, t0.AddSeconds(50), otherBatch);
        var running = Head(3, HydraHeadInfo.Status.Running, t0, batch: batch);
        var threads = new[] { HeadThread(project, sameBatchEarly), HeadThread(project, otherBatchLate), HeadThread(project, running) };
        var shown = HydraHeadsWorkingRow.FinishedHeads(threads);
        Assert.Equal([1L], shown.Select(head => head.Index));
    }

    [Fact]
    [Trait("Req", "PR-CHAT-036")] // App/Views/Chat/TimelineRows.swift:427, :430
    public void A_head_with_no_batch_counts_when_it_finished_at_or_after_the_earliest_running_start_in_the_callers_order()
    {
        using var runtime = new TestRuntime(); // ChatThread.init reads the defaults store
        var project = AppModelSamples.Project("p", @"C:\no-such-folder\p");
        var t0 = System.DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var threads = new[]
        {
            HeadThread(project, Head(1, HydraHeadInfo.Status.Stopped, t0, t0.AddSeconds(30))),
            HeadThread(project, Head(2, HydraHeadInfo.Status.Running, t0.AddSeconds(20))),
            HeadThread(project, Head(3, HydraHeadInfo.Status.Completed, t0, t0.AddSeconds(10))),
            HeadThread(project, Head(4, HydraHeadInfo.Status.Running, t0.AddSeconds(10))),
            HeadThread(project, Head(5, HydraHeadInfo.Status.Failed, t0, t0.AddSeconds(9))),
            HeadThread(project, Head(6, HydraHeadInfo.Status.Completed, t0, null)),
        };
        // since = the earliest running start (t0 + 10); 1 (t0 + 30) and 3 (exactly t0 + 10) count, 5 (t0 + 9) and 6
        // (no finish time, so distantPast) do not; the running heads themselves never count.
        Assert.Equal([1L, 3L], HydraHeadsWorkingRow.FinishedHeads(threads).Select(head => head.Index));
    }

    // ---------------------------------------------------------------- PR-CHAT-077 UserBubble

    [Fact]
    [Trait("Req", "PR-CHAT-077")] // App/Views/Chat/TimelineRows.swift:1167-1206
    public void The_bubble_path_rounds_three_corners_and_draws_the_tail_through_the_exact_control_points()
    {
        var geometry = Assert.IsType<PathGeometry>(new UserBubble().Path(new Rect(0, 0, 200, 50)));
        var figure = Assert.Single(geometry.Figures!);
        Assert.True(figure.IsClosed);
        Assert.True(figure.IsFilled);
        Assert.Equal(new Point(18, 0), figure.StartPoint);
        var segments = figure.Segments!.ToList();
        // :1204 closeSubpath(): the figure is closed; the last arc already ends on the start point.
        Assert.Equal(10, segments.Count);
        Assert.Equal(new Point(178, 0), Assert.IsType<LineSegment>(segments[0]).Point);
        var topRight = Assert.IsType<ArcSegment>(segments[1]);
        Assert.Equal(new Point(196, 18), topRight.Point);
        Assert.Equal(new Size(18, 18), topRight.Size);
        Assert.Equal(SweepDirection.Clockwise, topRight.SweepDirection);
        Assert.False(topRight.IsLargeArc);
        Assert.Equal(new Point(196, 39), Assert.IsType<LineSegment>(segments[2]).Point);
        var tip = Assert.IsType<BezierSegment>(segments[3]);
        Assert.Equal((new Point(196, 49), new Point(200, 50), new Point(200, 50)), (tip.Point1, tip.Point2, tip.Point3));
        var curl = Assert.IsType<BezierSegment>(segments[4]);
        Assert.Equal((new Point(196, 50.5), new Point(192, 49), new Point(189, 46)), (curl.Point1, curl.Point2, curl.Point3));
        var ease = Assert.IsType<BezierSegment>(segments[5]);
        Assert.Equal((new Point(184, 50), new Point(180, 50), new Point(175, 50)), (ease.Point1, ease.Point2, ease.Point3));
        Assert.Equal(new Point(18, 50), Assert.IsType<LineSegment>(segments[6]).Point);
        var bottomLeft = Assert.IsType<ArcSegment>(segments[7]);
        Assert.Equal(new Point(0, 32), bottomLeft.Point);
        Assert.Equal(SweepDirection.Clockwise, bottomLeft.SweepDirection);
        Assert.Equal(new Point(0, 18), Assert.IsType<LineSegment>(segments[8]).Point);
        var topLeft = Assert.IsType<ArcSegment>(segments[9]);
        Assert.Equal(new Point(18, 0), topLeft.Point);
        Assert.Equal(SweepDirection.Clockwise, topLeft.SweepDirection);
        Assert.Equal(18, UserBubble.CornerRadius);
        Assert.Equal(4, UserBubble.Tail);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-077")] // App/Views/Chat/TimelineRows.swift:118-129, :163-164
    public void A_sent_message_is_a_trailing_bubble_with_14_18_and_9_point_insets_96_points_clear_on_the_leading_side_tinted_at_14_percent()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var row = new UserMessageRow(UserEntry("hello there", System.Guid.NewGuid()), h.Runtime, canRevert: false);
                var window = Show(row);
                var text = Assert.Single(LogicalQuery.SelfAndDescendants<InlineText>(row));
                Assert.Equal("hello there", text.Source);
                Assert.True(text.HugsContent);
                var inset = Assert.IsType<Border>(text.Parent);
                Assert.Equal(new Thickness(14, 9, 14 + UserBubble.Tail, 9), inset.Padding);
                var shape = Assert.Single(LogicalQuery.SelfAndDescendants<Avalonia.Controls.Shapes.Path>(row));
                Assert.Equal(Resolved(ChromeTokens.Accent.Opacity(0.14), shape), BrushColor(shape.Fill));
                var outer = LogicalQuery.SelfAndDescendants<Border>(row).First(border => border.Padding.Left == 96);
                Assert.Equal(-(TimelineMetrics.HoverLineHeight + 6), outer.Margin.Bottom);
                var column = LogicalQuery.SelfAndDescendants<StackPanel>(outer).First(stack => stack.Spacing == 6);
                Assert.Equal(Avalonia.Layout.HorizontalAlignment.Right, column.HorizontalAlignment);
                Close(window);
            });
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-078 quotes

    [Fact]
    [Trait("Req", "PR-CHAT-078")] // App/Views/Chat/TimelineRows.swift:92-93, :118-122; App/Runtime/ThreadRuntime.swift:71-99
    public void A_message_whose_quote_lines_are_not_at_the_start_shows_no_chips_and_keeps_them_in_the_bubble()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                const string message = "look at this\n> not a leading quote";
                var row = new UserMessageRow(UserEntry(message, System.Guid.NewGuid()), h.Runtime, canRevert: false);
                var window = Show(row);
                Assert.DoesNotContain("text.quote", Symbols(row));
                Assert.Equal(message, Assert.Single(LogicalQuery.SelfAndDescendants<InlineText>(row)).Source);
                Close(window);
            });
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-079 attachments over the bubble

    [Fact]
    [Trait("Req", "PR-CHAT-079")] // App/Views/Chat/TimelineRows.swift:88-91
    public void A_message_with_attachments_shows_the_strip_above_the_bubble_and_one_without_shows_none()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var file = new Attachment { Name = "a.txt", Path = @"C:\no-such-folder\a.txt", MimeType = "text/plain" };
                var with = new UserMessageRow(UserEntry("see file", System.Guid.NewGuid(), new ValueList<Attachment>([file])), h.Runtime, canRevert: false);
                var without = new UserMessageRow(UserEntry("no file", System.Guid.NewGuid()), h.Runtime, canRevert: false);
                var panel = new StackPanel();
                panel.Children.Add(with);
                panel.Children.Add(without);
                var window = Show(panel);
                var column = LogicalQuery.SelfAndDescendants<StackPanel>(with).First(stack => stack.Spacing == 6);
                var strip = Assert.Single(LogicalQuery.SelfAndDescendants<AttachmentStrip>(with));
                var bubbleText = Assert.Single(LogicalQuery.SelfAndDescendants<InlineText>(with));
                var stripIndex = column.Children.IndexOf(column.Children.First(child => LogicalQuery.SelfAndDescendants<AttachmentStrip>(child).Contains(strip)));
                var bubbleIndex = column.Children.IndexOf(column.Children.First(child => LogicalQuery.SelfAndDescendants<InlineText>(child).Contains(bubbleText)));
                Assert.True(stripIndex < bubbleIndex);
                Assert.Equal(Avalonia.Layout.HorizontalAlignment.Right, column.Children[stripIndex].HorizontalAlignment);
                Assert.Empty(LogicalQuery.SelfAndDescendants<AttachmentStrip>(without));
                Close(window);
            });
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-080 the pencil

    private static Control HoverLine(Control row) =>
        LogicalQuery.SelfAndDescendants<Control>(row).Single(control => control.Height == TimelineMetrics.HoverLineHeight && double.IsNaN(control.Width));

    [Fact]
    [Trait("Req", "PR-CHAT-080")] // App/Views/Chat/TimelineRows.swift:130-142, :158-161
    public void A_revertable_message_keeps_a_22_point_hover_line_with_a_22_point_pencil_that_shows_only_on_hover()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var row = new UserMessageRow(UserEntry("edit me", System.Guid.NewGuid()), h.Runtime, canRevert: true);
                var window = Show(row);
                var line = HoverLine(row);
                Assert.Equal(0, line.Opacity);
                var pencil = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(line));
                Assert.Equal("Edit message", ToolTip.GetTip(pencil));
                Assert.Contains("pencil", Symbols(pencil));
                Assert.Contains(LogicalQuery.SelfAndDescendants<Control>(pencil), control => control.Width == 22 && control.Height == 22);
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-080")] // App/Views/Chat/TimelineRows.swift:131, :158-161
    public void Hovering_the_row_shows_the_line_and_a_message_that_cannot_revert_has_no_pencil()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            UserMessageRow? row = null;
            UserMessageRow? locked = null;
            AvaloniaWindow? window = null;
            h.Main.OnMain(() =>
            {
                row = new UserMessageRow(UserEntry("edit me", System.Guid.NewGuid()), h.Runtime, canRevert: true);
                locked = new UserMessageRow(UserEntry("keep me", System.Guid.NewGuid()), h.Runtime, canRevert: false);
                var noTurn = new UserMessageRow(UserEntry("no turn", null), h.Runtime, canRevert: true);
                var panel = new StackPanel { Spacing = 60 };
                panel.Children.Add(row);
                panel.Children.Add(locked);
                panel.Children.Add(noTurn);
                window = Show(panel);
                Assert.Empty(LogicalQuery.SelfAndDescendants<Button>(HoverLine(locked)));
                Assert.Empty(LogicalQuery.SelfAndDescendants<Button>(HoverLine(noTurn)));
                Hover(window, LogicalQuery.SelfAndDescendants<InlineText>(row).Single());
            });
            h.Main.OnMain(() =>
            {
                Assert.Equal(1, HoverLine(row!).Opacity);
                Assert.Equal(0, HoverLine(locked!).Opacity);
                Close(window!);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-080")] // App/Views/Chat/TimelineRows.swift:87, :132-134, :141
    public void The_pencil_opens_the_edit_on_the_runtime_and_a_second_tap_closes_it()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            var turn = System.Guid.NewGuid();
            var file = new Attachment { Name = "a.txt", Path = @"C:\no-such-folder\a.txt", MimeType = "text/plain" };
            UserMessageRow? row = null;
            AvaloniaWindow? window = null;
            // The thread's history lands first: installing it drops an edit whose turn it does not hold
            // (ThreadRuntime.swift:172-182), which is the runtime's rule, not the row's.
            h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
            h.Main.OnMain(() =>
            {
                row = new UserMessageRow(UserEntry("edit me", turn, new ValueList<Attachment>([file])), h.Runtime, canRevert: true);
                window = Show(row);
                Click(Assert.Single(LogicalQuery.SelfAndDescendants<Button>(HoverLine(row))));
                var edit = Assert.IsType<PromptEdit>(h.Runtime.MessageEdit);
                Assert.Equal(turn, edit.Id);
                Assert.Equal("edit me", edit.Text);
                Assert.Equal([file], edit.Attachments);
            });
            h.Main.OnMain(() =>
            {
                Settle(window!);
                // :197, :208-212 the edit opening syncs the editor popover, which presents a MessageEditor on the edit,
                // hung from the pencil's anchor (:148-155).
                var shown = Assert.Single(presenter.Calls, call => call.Presented);
                Assert.IsType<MessageEditor>(shown.Popup.Child);
                var pencil = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(HoverLine(row!)));
                Assert.Same(LogicalQuery.SelfAndDescendants<AttachmentAnchorCapture>(pencil.Parent!).Single(), shown.Popup.PlacementTarget);
                // Editing: the line stays up without a hover.
                Assert.Equal(1, HoverLine(row!).Opacity);
                Click(Assert.Single(LogicalQuery.SelfAndDescendants<Button>(HoverLine(row!))));
                Assert.Null(h.Runtime.MessageEdit);
            });
            h.Main.OnMain(() =>
            {
                Settle(window!);
                Assert.Equal(0, HoverLine(row!).Opacity);
                Close(window!);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-080")] // App/Views/Chat/TimelineRows.swift:143-147
    public void Settling_the_pointer_on_the_pencil_starts_the_revert_preview()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var turn = System.Guid.NewGuid();
            AvaloniaWindow? window = null;
            h.Main.OnMain(() =>
            {
                var row = new UserMessageRow(UserEntry("edit me", turn), h.Runtime, canRevert: true);
                window = Show(row);
                Assert.Null(h.Runtime.CachedRevertPreview(turn));
                var pencil = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(HoverLine(row)));
                pencil.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, pencil, new Pointer(1, PointerType.Mouse, true), window, new Point(1, 1), 0, new PointerPointProperties(), KeyModifiers.None));
            });
            h.Main.OnMain(() =>
            {
                // The preview of an unknown turn fails at once (ThreadRuntime.swift:1805-1807); either way it was asked for.
                Assert.NotNull(h.Runtime.CachedRevertPreview(turn));
                Close(window!);
            });
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-081 the sent message's menu

    private static (Popup Popup, RowActionItems Items) RightClickMenu(RecordingPresenter presenter, AvaloniaWindow window, Control on)
    {
        var point = on.TranslatePoint(new Point(4, 4), window) ?? throw new Xunit.Sdk.XunitException("not in the window");
        window.MouseDown(point, MouseButton.Right, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Right, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var popup = presenter.Calls.Last(call => call.Presented).Popup;
        Assert.NotNull(popup.Child);
        return (popup, Assert.Single(LogicalQuery.SelfAndDescendants<RowActionItems>(popup.Child)));
    }

    // A PopoverItem's title is a MiddleTruncatingText, whose Text is set only when measured; its FullText is the title.
    private static string Title(PopoverItem item) => LogicalQuery.SelfAndDescendants<MiddleTruncatingText>(item).First().FullText;

    private static string[] ItemTitles(RowActionItems items) =>
        LogicalQuery.SelfAndDescendants<PopoverItem>(items).Select(Title).ToArray();

    [Fact]
    [Trait("Req", "PR-CHAT-081")] // App/Views/Chat/TimelineRows.swift:172-178, :216-228; UI/Chrome/Popovers.swift:354-363
    public void Right_clicking_a_sent_message_offers_the_two_copies_and_when_revertable_a_divider_then_Edit_from_here()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            h.Main.OnMain(() =>
            {
                var plain = new UserMessageRow(UserEntry("hello", System.Guid.NewGuid()), h.Runtime, canRevert: false);
                var revertable = new UserMessageRow(UserEntry("hello again", System.Guid.NewGuid()), h.Runtime, canRevert: true);
                var panel = new StackPanel { Spacing = 60 };
                panel.Children.Add(plain);
                panel.Children.Add(revertable);
                var window = Show(panel);

                var (_, first) = RightClickMenu(presenter, window, LogicalQuery.SelfAndDescendants<InlineText>(plain).Single());
                Assert.Equal(["Copy message", "Copy as markdown"], ItemTitles(first));
                Assert.Empty(LogicalQuery.SelfAndDescendants<PopoverDivider>(first));

                var (_, second) = RightClickMenu(presenter, window, LogicalQuery.SelfAndDescendants<InlineText>(revertable).Single());
                Assert.Equal(["Copy message", "Copy as markdown", "Edit from here"], ItemTitles(second));
                var stack = Assert.IsType<StackPanel>(second.Child);
                Assert.IsType<PopoverDivider>(stack.Children[2]);
                Assert.Equal(
                    ["doc.on.doc", "number", "arrow.uturn.backward"],
                    LogicalQuery.SelfAndDescendants<PopoverItem>(second).Select(item => Symbols(item).First()));
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-081")] // App/Views/Chat/TimelineRows.swift:31-34, :219-220; UI/Markdown/SelectableMessageText.swift:9-19
    public void Copy_message_writes_the_plain_text_and_Copy_as_markdown_the_raw_text_and_both_confirm_Copied()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            AvaloniaWindow? window = null;
            AvaloniaWindow? menu = null;
            PopoverItem[] items = [];
            System.Threading.Tasks.Task<string?>? read = null;
            h.Main.OnMain(() =>
            {
                _ = AppCurrentEvent.CurrentWindow; // starts the app-wide event watch before the click
                var row = new UserMessageRow(UserEntry("- one\n- two", System.Guid.NewGuid()), h.Runtime, canRevert: false);
                window = Show(row);
                var (popup, actions) = RightClickMenu(presenter, window, LogicalQuery.SelfAndDescendants<InlineText>(row).Single());
                // The recorded popup never opens headless; its content is hosted here so the items react.
                var content = popup.Child!;
                popup.Child = null;
                menu = Show(content);
                items = LogicalQuery.SelfAndDescendants<PopoverItem>(actions).ToArray();
                Click(LogicalQuery.SelfAndDescendants<Button>(items[0]).First());
            });
            h.Main.OnMain(() =>
            {
                Settle(menu!);
                Assert.True(items[0].IsConfirmed);
                Assert.Equal("Copied", Title(items[0]));
                read = window!.Clipboard!.TryGetTextAsync();
            });
            h.Main.OnMain(() =>
            {
                // MessageText.plain: each item on its own line behind a U+2022 BULLET and two spaces; a line that
                // starts with a bullet is not reflowed (MarkdownView.swift:1344-1353, lines shorter than 45).
                Assert.Equal("\U00002022  one\n\U00002022  two", read!.GetAwaiter().GetResult());
                Click(LogicalQuery.SelfAndDescendants<Button>(items[1]).First());
            });
            h.Main.OnMain(() =>
            {
                Settle(menu!);
                Assert.Equal("Copied", Title(items[1]));
                read = window!.Clipboard!.TryGetTextAsync();
            });
            h.Main.OnMain(() =>
            {
                Assert.Equal("- one\n- two", read!.GetAwaiter().GetResult());
                Close(menu!);
                Close(window!);
            });
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-088 the reply row

    private static AssistantMessageRow ReplyRow(ThreadRuntime runtime, string text, bool streaming, TurnSummary? summary) =>
        new(ReplyEntry(text, streaming), summary, runtime);

    [Fact]
    [Trait("Req", "PR-CHAT-088")] // App/Views/Chat/TimelineRows.swift:1363-1379, :1388
    public void A_reply_renders_its_markdown_and_keeps_a_hidden_22_point_line_with_the_turn_label_in_caption_secondary_text()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var turn = System.Guid.NewGuid();
                var row = ReplyRow(h.Runtime, CodeOnlyReply, streaming: false, Summary(turn));
                var window = Show(row);
                var markdown = Assert.Single(LogicalQuery.SelfAndDescendants<MarkdownView>(row));
                Assert.Equal(CodeOnlyReply, markdown.Text);
                Assert.False(markdown.IsStreaming);
                var line = HoverLine(row);
                Assert.Equal(0, line.Opacity);
                var label = LogicalQuery.SelfAndDescendants<TextBlock>(line).Single();
                Assert.Equal("Worked for " + RelativeTime.Duration(System.TimeSpan.FromSeconds(5)), label.Text);
                Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Caption).Resolve().FontSize, label.FontSize);
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, label), BrushColor(label.Foreground));
                var outer = LogicalQuery.SelfAndDescendants<Control>(row).First(control => control.Margin.Bottom == -(TimelineMetrics.HoverLineHeight + 2));
                Assert.NotNull(outer);
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-088")] // App/Views/Chat/TimelineRows.swift:1378, :1380-1382
    public void Hovering_a_settled_reply_fades_the_line_in_and_a_streaming_reply_never_shows_it()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource();
            using var frameSeam = FrameClock.Source.Override(frames);
            AssistantMessageRow? settled = null;
            AssistantMessageRow? streaming = null;
            AvaloniaWindow? window = null;
            h.Main.OnMain(() =>
            {
                settled = ReplyRow(h.Runtime, CodeOnlyReply, streaming: false, Summary(System.Guid.NewGuid()));
                streaming = ReplyRow(h.Runtime, "still typing", streaming: true, Summary(System.Guid.NewGuid()));
                var panel = new StackPanel { Spacing = 60 };
                panel.Children.Add(settled);
                panel.Children.Add(streaming);
                window = Show(panel);
                Hover(window, LogicalQuery.SelfAndDescendants<MarkdownView>(settled).Single());
            });
            // easeOut over 0.12 s on the frame clock: part way at 60 ms, fully in by 120 ms.
            frames.Advance(System.TimeSpan.Zero);
            h.Main.AdvanceMilliseconds(60);
            frames.Advance(System.TimeSpan.FromMilliseconds(60));
            var partway = HoverLine(settled!).Opacity;
            Assert.InRange(partway, 0.01, 0.99);
            h.Main.AdvanceMilliseconds(60);
            frames.Advance(System.TimeSpan.FromMilliseconds(60));
            Assert.Equal(1, HoverLine(settled!).Opacity);
            h.Main.OnMain(() => Hover(window!, LogicalQuery.SelfAndDescendants<MarkdownView>(streaming!).Single()));
            h.Main.AdvanceMilliseconds(200);
            frames.Advance(System.TimeSpan.FromMilliseconds(200));
            Assert.Equal(0, HoverLine(streaming!).Opacity);
            h.Main.OnMain(() => Close(window!));
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-089 the reply's menu

    [Fact]
    [Trait("Req", "PR-CHAT-089")] // App/Views/Chat/TimelineRows.swift:38-54
    public void The_last_code_block_is_the_last_closed_fence_and_an_unclosed_fence_yields_none()
    {
        Assert.Null(TimelineRows.LastCodeBlock("no code here"));
        Assert.Null(TimelineRows.LastCodeBlock("text\n```swift\nlet a = 1\n"));
        Assert.Equal("let a = 1\nlet b = 2", TimelineRows.LastCodeBlock("```swift\nlet a = 1\nlet b = 2\n```"));
        Assert.Equal("second", TimelineRows.LastCodeBlock("```\nfirst\n```\nprose\n```js\nsecond\n```\n```\nopen"));
        Assert.Equal("", TimelineRows.LastCodeBlock("```\n```"));
        // A three-backtick prefix is all the scan tests: an indented fence is text.
        Assert.Null(TimelineRows.LastCodeBlock("  ```\ncode\n  ```"));
    }

    [Fact]
    [Trait("Req", "PR-CHAT-089")] // App/Views/Chat/TimelineRows.swift:1394-1412
    public void A_replys_menu_is_the_two_copies_then_the_last_code_block_when_there_is_one_then_Add_to_reply_in_its_own_group()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var bare = AssistantMessageRow.MessageActions("plain answer", null, h.Runtime);
                Assert.Equal(["Copy message", "Copy as markdown", "Add to reply"], bare.Select(item => item.Title));
                Assert.Equal(["doc.on.doc", "number", "arrow.turn.down.left"], bare.Select(item => item.Symbol));
                Assert.Equal(["Copied", "Copied", "Added"], bare.Select(item => item.Confirms));
                Assert.Equal([false, false, true], bare.Select(item => item.StartsGroup));

                var coded = AssistantMessageRow.MessageActions("answer", "let x = 1", h.Runtime);
                Assert.Equal(["Copy message", "Copy as markdown", "Copy last code block", "Add to reply"], coded.Select(item => item.Title));
                Assert.Equal("chevron.left.forwardslash.chevron.right", coded[2].Symbol);
                Assert.Equal("Copied", coded[2].Confirms);
                Assert.False(coded[2].StartsGroup);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-089")] // App/Views/Chat/TimelineRows.swift:1406-1410
    public void Add_to_reply_quotes_the_reply_into_the_draft_once()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var add = AssistantMessageRow.MessageActions("the answer", null, h.Runtime).Last();
                add.Action();
                Assert.Equal(["the answer"], h.Runtime.Draft.Quotes.Select(quote => quote.Text));
                add.Action();
                Assert.Single(h.Runtime.Draft.Quotes);
                AssistantMessageRow.MessageActions("another", null, h.Runtime).Last().Action();
                Assert.Equal(["the answer", "another"], h.Runtime.Draft.Quotes.Select(quote => quote.Text));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-089")] // App/Views/Chat/TimelineRows.swift:1383, :1415-1429
    public void Right_clicking_a_reply_offers_its_menu_with_the_last_code_block()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            h.Main.OnMain(() =>
            {
                var row = ReplyRow(h.Runtime, CodeOnlyReply, streaming: false, null);
                var window = Show(row);
                var (_, items) = RightClickMenu(presenter, window, LogicalQuery.SelfAndDescendants<MarkdownView>(row).Single());
                Assert.Equal(["Copy message", "Copy as markdown", "Copy last code block", "Add to reply"], ItemTitles(items));
                var stack = Assert.IsType<StackPanel>(items.Child);
                Assert.IsType<PopoverDivider>(stack.Children[3]);
                Close(window);
            });
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-091 WorkGroupSummary

    [Fact]
    [Trait("Req", "PR-CHAT-091")] // App/Views/Chat/TimelineRows.swift:1588-1610, :1622-1633
    public void The_summary_names_the_kinds_in_a_fixed_order_present_tense_while_running_lowering_only_later_first_letters()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var entries = new[]
            {
                ToolEntry(Call(ToolCall.Kind.Command, "npm test", ToolCall.Status.Running)),
                ToolEntry(Call(ToolCall.Kind.Edit, "Edit file")),
                ToolEntry(Call(ToolCall.Kind.Command, "ls")),
            };
            Assert.Equal("Edited files, running commands", WorkGroupSummary.Text(entries));

            var all = new[]
            {
                ToolEntry(Call(ToolCall.Kind.Other, "x")),
                ToolEntry(Call(ToolCall.Kind.Agent, "x")),
                ToolEntry(Call(ToolCall.Kind.Mcp, "x")),
                ToolEntry(Call(ToolCall.Kind.Web, "x")),
                ToolEntry(Call(ToolCall.Kind.Search, "x")),
                ToolEntry(Call(ToolCall.Kind.Command, "x")),
                ToolEntry(Call(ToolCall.Kind.Read, "x")),
                ToolEntry(Call(ToolCall.Kind.Edit, "x")),
            };
            Assert.Equal(
                "Edited files, read files, ran commands, searched, browsed, called tools, delegated tasks, used tools",
                WorkGroupSummary.Text(all));

            var running = all.Select(entry => ToolEntry(((TimelineItem.Content.Tool)entry.Item.TimelineItemContent).Value with { ToolCallStatus = ToolCall.Status.Running })).ToArray();
            Assert.Equal(
                "Editing files, reading files, running commands, searching, browsing, calling tools, delegating tasks, using tools",
                WorkGroupSummary.Text(running));

            Assert.Equal("Searched", WorkGroupSummary.Text([ToolEntry(Call(ToolCall.Kind.Search, "grep"))]));
            Assert.Equal("Worked", WorkGroupSummary.Text([ReplyEntry("words")]));
            Assert.Equal("Worked", WorkGroupSummary.Text([]));
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-091")] // App/Views/Chat/TimelineRows.swift:1612-1620
    public void The_summary_glyph_is_the_first_present_kind_in_the_same_order_and_a_wrench_when_none()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            Assert.Equal("terminal", WorkGroupSummary.Symbol([ToolEntry(Call(ToolCall.Kind.Search, "a")), ToolEntry(Call(ToolCall.Kind.Command, "b"))]));
            Assert.Equal("pencil", WorkGroupSummary.Symbol([ToolEntry(Call(ToolCall.Kind.Read, "a")), ToolEntry(Call(ToolCall.Kind.Edit, "b"))]));
            Assert.Equal("person.2", WorkGroupSummary.Symbol([ToolEntry(Call(ToolCall.Kind.Other, "a")), ToolEntry(Call(ToolCall.Kind.Agent, "b"))]));
            Assert.Equal("wrench.and.screwdriver", WorkGroupSummary.Symbol([ReplyEntry("words")]));
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-093..096 ToolPresentation

    [Fact]
    [Trait("Req", "PR-CHAT-093")] // App/Views/Chat/TimelineRows.swift:2189-2220
    public void A_tool_label_is_the_verb_and_the_subject_with_a_leading_kind_word_removed()
    {
        Assert.Equal("Edited file", ToolPresentation.Label(Call(ToolCall.Kind.Edit, "Edit file")));
        Assert.Equal("Editing Main.swift", ToolPresentation.Label(Call(ToolCall.Kind.Edit, "Write Main.swift", ToolCall.Status.Running)));
        Assert.Equal("Edited", ToolPresentation.Label(Call(ToolCall.Kind.Edit, "apply_patch")));
        Assert.Equal("Ran npm run build", ToolPresentation.Label(Call(ToolCall.Kind.Command, "Bash npm run build")));
        Assert.Equal("Ran npm run build", ToolPresentation.Label(Call(ToolCall.Kind.Command, "npm run build")));
        Assert.Equal("Read README.md", ToolPresentation.Label(Call(ToolCall.Kind.Read, "CAT README.md")));
        // Only the kind's own roots are dropped: "read" is not an edit word.
        Assert.Equal("Edited Read me", ToolPresentation.Label(Call(ToolCall.Kind.Edit, "Read me")));
        // One split only, so the subject keeps its own spacing and casing.
        Assert.Equal("Searched  Two  Spaces", ToolPresentation.Label(Call(ToolCall.Kind.Search, "grep  Two  Spaces")));
        Assert.Equal("Browsed https://a.test", ToolPresentation.Label(Call(ToolCall.Kind.Web, "WebFetch https://a.test")));
        Assert.Equal("Calling", ToolPresentation.Label(Call(ToolCall.Kind.Mcp, "call", ToolCall.Status.Running)));
        Assert.Equal("Delegated", ToolPresentation.Label(Call(ToolCall.Kind.Agent, "Task")));
        Assert.Equal("Used", ToolPresentation.Label(Call(ToolCall.Kind.Other, "use")));
        Assert.Equal("Used", ToolPresentation.Label(Call(ToolCall.Kind.Other, "")));
    }

    [Fact]
    [Trait("Req", "PR-CHAT-093")] // App/Views/Chat/TimelineRows.swift:2208-2220
    public void The_verb_is_present_tense_only_while_running()
    {
        var kinds = new[] { ToolCall.Kind.Command, ToolCall.Kind.Read, ToolCall.Kind.Edit, ToolCall.Kind.Search, ToolCall.Kind.Web, ToolCall.Kind.Mcp, ToolCall.Kind.Agent, ToolCall.Kind.Other };
        Assert.Equal(
            ["Running", "Reading", "Editing", "Searching", "Browsing", "Calling", "Delegating", "Using"],
            kinds.Select(kind => ToolPresentation.Verb(Call(kind, "x", ToolCall.Status.Running))));
        foreach (var status in new[] { ToolCall.Status.Completed, ToolCall.Status.Failed, ToolCall.Status.Declined })
        {
            Assert.Equal(
                ["Ran", "Read", "Edited", "Searched", "Browsed", "Called", "Delegated", "Used"],
                kinds.Select(kind => ToolPresentation.Verb(Call(kind, "x", status))));
        }
    }

    [Fact]
    [Trait("Req", "PR-CHAT-094")] // App/Views/Chat/TimelineRows.swift:2222-2233
    public void Each_tool_kind_has_its_fixed_glyph()
    {
        Assert.Equal("terminal", ToolPresentation.Symbol(ToolCall.Kind.Command));
        Assert.Equal("doc.text", ToolPresentation.Symbol(ToolCall.Kind.Read));
        Assert.Equal("pencil", ToolPresentation.Symbol(ToolCall.Kind.Edit));
        Assert.Equal("magnifyingglass", ToolPresentation.Symbol(ToolCall.Kind.Search));
        Assert.Equal("globe", ToolPresentation.Symbol(ToolCall.Kind.Web));
        Assert.Equal("puzzlepiece.extension", ToolPresentation.Symbol(ToolCall.Kind.Mcp));
        Assert.Equal("person.2", ToolPresentation.Symbol(ToolCall.Kind.Agent));
        Assert.Equal("wrench.and.screwdriver", ToolPresentation.Symbol(ToolCall.Kind.Other));
    }

    [Fact]
    [Trait("Req", "PR-CHAT-095")] // App/Views/Chat/TimelineRows.swift:2235-2240
    public void A_diff_stat_shows_only_when_the_edits_add_up_to_something()
    {
        Assert.Null(ToolPresentation.Stats(Call(ToolCall.Kind.Edit, "e")));
        var empty = Call(ToolCall.Kind.Edit, "e") with { Edits = new ValueList<FileEdit>([new FileEdit("a.txt"), new FileEdit("b.txt")]) };
        Assert.Null(ToolPresentation.Stats(empty));
        var real = Call(ToolCall.Kind.Edit, "e") with
        {
            Edits = new ValueList<FileEdit>([new FileEdit("a.txt", null, 3, 1), new FileEdit("b.txt", null, 0, 4)]),
        };
        Assert.Equal((3, 5), ToolPresentation.Stats(real));
        var deletionsOnly = Call(ToolCall.Kind.Edit, "e") with { Edits = new ValueList<FileEdit>([new FileEdit("a.txt", null, 0, 2)]) };
        Assert.Equal((0, 2), ToolPresentation.Stats(deletionsOnly));
    }

    [Fact]
    [Trait("Req", "PR-CHAT-096")] // App/Views/Chat/TimelineRows.swift:2242-2254
    public void An_mcp_title_of_catalog_server_middle_dot_tool_resolves_to_the_entry_and_its_label()
    {
        var parts = ToolPresentation.McpParts(Call(ToolCall.Kind.Mcp, "Context7 \U000000B7 resolve-library-id")); // U+00B7 MIDDLE DOT
        Assert.NotNull(parts);
        Assert.Equal("context7", parts.Value.Entry.Id);
        Assert.Equal("resolve-library-id", parts.Value.Tool);
        Assert.Equal("Context7: resolve-library-id", ToolPresentation.McpLabel(parts.Value.Entry, parts.Value.Tool, running: true));
        Assert.Equal("Context7: resolve-library-id", ToolPresentation.McpLabel(parts.Value.Entry, parts.Value.Tool, running: false));
        // Both sides are trimmed of whitespace and newlines.
        var trimmed = ToolPresentation.McpParts(Call(ToolCall.Kind.Mcp, "\n context7  \U000000B7 \tquery-docs \n")); // U+00B7 MIDDLE DOT
        Assert.Equal(("context7", "query-docs"), (trimmed!.Value.Entry.Id, trimmed.Value.Tool));
    }

    [Fact]
    [Trait("Req", "PR-CHAT-096")] // App/Views/Chat/TimelineRows.swift:2243-2248
    public void An_mcp_title_without_the_exact_separator_an_empty_side_an_unknown_server_or_another_kind_is_no_badge()
    {
        Assert.Null(ToolPresentation.McpParts(Call(ToolCall.Kind.Mcp, "Context7\U000000B7resolve"))); // U+00B7, no spaces
        Assert.Null(ToolPresentation.McpParts(Call(ToolCall.Kind.Mcp, "Context7 - resolve")));
        Assert.Null(ToolPresentation.McpParts(Call(ToolCall.Kind.Mcp, "   \U000000B7 resolve"))); // U+00B7 MIDDLE DOT
        Assert.Null(ToolPresentation.McpParts(Call(ToolCall.Kind.Mcp, "Context7 \U000000B7   "))); // U+00B7 MIDDLE DOT
        Assert.Null(ToolPresentation.McpParts(Call(ToolCall.Kind.Mcp, "No Such Server \U000000B7 tool"))); // U+00B7 MIDDLE DOT
        Assert.Null(ToolPresentation.McpParts(Call(ToolCall.Kind.Other, "Context7 \U000000B7 resolve-library-id"))); // U+00B7 MIDDLE DOT
    }

    // ---------------------------------------------------------------- PR-CHAT-102 DiffDetailCache

    [Fact]
    [Trait("Req", "PR-CHAT-102")] // App/Views/Chat/TimelineRows.swift:2171-2182
    public void A_diff_is_parsed_once_per_path_and_text()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            const string diff = "@@ -1 +1 @@\n-old\n+new\n";
            var first = DiffDetailCache.File(diff, "a.txt");
            Assert.Same(first, DiffDetailCache.File(diff, "a.txt"));
            Assert.Equal("a.txt", first.Path);
            Assert.NotSame(first, DiffDetailCache.File(diff, "b.txt"));
            Assert.NotSame(first, DiffDetailCache.File(diff + "+more\n", "a.txt"));
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-097..101 ToolRow

    private static ToolRow ToolRowFor(ThreadRuntime runtime, ToolCall call, System.Guid? turn = null) =>
        new(ToolEntry(call, turn), runtime);

    [Fact]
    [Trait("Req", "PR-CHAT-097")] // App/Views/Chat/TimelineRows.swift:1674-1677, :1707-1709
    public void A_call_with_nothing_to_open_is_a_line_of_text_not_a_control_and_has_no_chevron()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var row = ToolRowFor(h.Runtime, Call(ToolCall.Kind.Search, "grep foo"));
                var window = Show(row);
                Assert.Empty(LogicalQuery.SelfAndDescendants<Button>(row));
                Assert.Contains("Searched foo", Texts(row));
                Assert.DoesNotContain("chevron.down", Symbols(row));
                Assert.DoesNotContain("chevron.right", Symbols(row));
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-097")] // App/Views/Chat/TimelineRows.swift:1695-1700, :1710-1713, :1847-1853
    public void A_call_with_output_expands_in_place_and_asks_the_timeline_to_reveal_the_end()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource();
            using var frameSeam = FrameClock.Source.Override(frames);
            var reveals = 0;
            ToolRow? row = null;
            AvaloniaWindow? window = null;
            h.Main.OnMain(() =>
            {
                row = ToolRowFor(h.Runtime, Call(ToolCall.Kind.Command, "ls") with { Output = "a\nb" });
                var host = new Border { Child = row };
                ThreadTimeline.SetRevealTimelineEnd(host, new TimelineRevealBox { Action = () => reveals++ });
                window = Show(host);
                var button = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(row));
                Assert.Equal("Show the output", ToolTip.GetTip(button));
                Assert.Empty(LogicalQuery.SelfAndDescendants<ToolDetailView>(row));
                Click(button);
                Assert.Equal(1, reveals);
            });
            h.Main.OnMain(() =>
            {
                Assert.Single(LogicalQuery.SelfAndDescendants<ToolDetailView>(row!));
                var button = LogicalQuery.SelfAndDescendants<Button>(row!).First();
                Assert.Equal("Hide the output", ToolTip.GetTip(button));
                Click(button);
                Assert.Equal(1, reveals);
            });
            h.Main.OnMain(() =>
            {
                Assert.Equal("Show the output", ToolTip.GetTip(LogicalQuery.SelfAndDescendants<Button>(row!).First()));
                Close(window!);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-097")] // App/Views/Chat/TimelineRows.swift:1664-1669, :1690-1692, :1821-1828, :1849
    public void A_call_with_a_diff_never_unfolds_and_opens_the_changes_on_its_label_once_the_pointer_has_been_there()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var turn = System.Guid.NewGuid();
            var edits = new ValueList<FileEdit>([new FileEdit("src/a.swift", "@@ -1 +1 @@\n-a\n+b\n", 1, 1), new FileEdit("", null, 0, 0)]);
            ToolRow? row = null;
            AvaloniaWindow? window = null;
            long before = 0;
            h.Main.OnMain(() =>
            {
                row = ToolRowFor(h.Runtime, Call(ToolCall.Kind.Edit, "Edit a.swift") with { Edits = edits, Output = "done" }, turn);
                window = Show(row);
                var button = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(row));
                Assert.Equal("Show this change", ToolTip.GetTip(button));
                before = h.Runtime.DiffOpenRequest;
                // No anchor yet: the pointer has not been on the label, so a click opens nothing.
                Click(button);
                Assert.Equal(before, h.Runtime.DiffOpenRequest);
                Hover(window, LogicalQuery.SelfAndDescendants<TextBlock>(row).First(block => block.Text == "Edited a.swift"));
            });
            h.Main.OnMain(() =>
            {
                Click(LogicalQuery.SelfAndDescendants<Button>(row!).First());
                Assert.Equal(before + 1, h.Runtime.DiffOpenRequest);
                Assert.Equal(turn, h.Runtime.DiffSelection);
                Assert.Equal(["src/a.swift"], h.Runtime.DiffFocusEdits.Select(edit => edit.Path));
                Assert.Equal(PreviewPanelEdge.MinY, h.Runtime.DiffAnchorEdge);
                Assert.Empty(LogicalQuery.SelfAndDescendants<ToolDetailView>(row!));
                Close(window!);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-097")] // App/Views/Chat/TimelineRows.swift:1670-1673, :1688-1689, :1829-1831, :1848; :1862-1867
    public void A_call_that_sent_out_a_head_opens_the_heads_steps_popover_and_never_its_raw_output()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource()); // the head's glyph animates while running
            ToolRow? row = null;
            AvaloniaWindow? window = null;
            h.Main.OnMain(() =>
            {
                var entry = ToolEntry(Call(ToolCall.Kind.Agent, "Pip: the merge popover", ToolCall.Status.Running) with { Output = "raw tool output" });
                h.Runtime.RehearseHydraHead(entry.Id, System.Guid.NewGuid());
                row = new ToolRow(entry, h.Runtime);
                var host = new Border { Child = row };
                Marrow.App.Port.AppEnvironment.SetAppModel(host, h.App);
                window = Show(host);
                var button = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(row));
                Assert.Equal("Show the head's steps", ToolTip.GetTip(button));
                Assert.Contains("chevron.down", Symbols(row));
                Assert.DoesNotContain("chevron.right", Symbols(row));
                Assert.Empty(presenter.Calls);
                Click(button);
            });
            h.Main.OnMain(() =>
            {
                Assert.Single(presenter.Calls, call => call.Presented);
                Assert.Empty(LogicalQuery.SelfAndDescendants<ToolDetailView>(row!));
                Close(window!);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-098")] // App/Views/Chat/TimelineRows.swift:1799-1810
    public void A_popover_row_has_a_static_down_chevron_and_an_output_row_a_right_chevron_turning_down_when_open()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource();
            using var frameSeam = FrameClock.Source.Override(frames);
            ToolRow? output = null;
            AvaloniaWindow? window = null;
            h.Main.OnMain(() =>
            {
                var diff = ToolRowFor(h.Runtime, Call(ToolCall.Kind.Edit, "Edit a") with { Edits = new ValueList<FileEdit>([new FileEdit("a", "d", 1, 0)]) });
                output = ToolRowFor(h.Runtime, Call(ToolCall.Kind.Command, "ls") with { Detail = "details" });
                var panel = new StackPanel();
                panel.Children.Add(diff);
                panel.Children.Add(output);
                window = Show(panel);
                var down = LogicalQuery.SelfAndDescendants<SwiftImage>(diff).Single(image => image.SymbolName == "chevron.down");
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, down), BrushColor(down.Foreground));
                Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Caption2, SwiftFont.Weight.Semibold).Resolve().FontSize, SwiftImage.GetFont(down)!.Resolve().FontSize);
                Assert.DoesNotContain("chevron.right", Symbols(diff));
                var right = LogicalQuery.SelfAndDescendants<SwiftImage>(output).Single(image => image.SymbolName == "chevron.right");
                Assert.Equal(0, Assert.IsType<RotateTransform>(right.RenderTransform).Angle);
                Assert.DoesNotContain("chevron.down", Symbols(output));
                // The chevron follows the row's text in the same run, not the trailing edge.
                var run = Assert.IsType<StackPanel>(right.Parent);
                Assert.Contains(run.Children, child => child is TextBlock text && text.Text == "Ran ls");
                Click(LogicalQuery.SelfAndDescendants<Button>(output).Single());
            });
            // The snappy 0.24 s turn runs on the frame clock; two seconds see it settled.
            frames.Advance(System.TimeSpan.Zero);
            h.Main.AdvanceMilliseconds(2000);
            frames.Advance(System.TimeSpan.FromMilliseconds(2000));
            h.Main.OnMain(() =>
            {
                var right = LogicalQuery.SelfAndDescendants<SwiftImage>(output!).Single(image => image.SymbolName == "chevron.right");
                Assert.Equal(90, Assert.IsType<RotateTransform>(right.RenderTransform).Angle);
                Close(window!);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-099")] // App/Views/Chat/TimelineRows.swift:2067-2091, :1784
    public void The_status_icon_is_a_spinner_a_red_cross_a_raised_hand_or_the_kinds_glyph_in_a_16_point_column()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var running = new ToolStatusIcon(Call(ToolCall.Kind.Read, "x", ToolCall.Status.Running));
                var failed = new ToolStatusIcon(Call(ToolCall.Kind.Read, "x", ToolCall.Status.Failed));
                var declined = new ToolStatusIcon(Call(ToolCall.Kind.Read, "x", ToolCall.Status.Declined));
                var completed = new ToolStatusIcon(Call(ToolCall.Kind.Read, "x"));
                var photo = new ToolStatusIcon(Call(ToolCall.Kind.Read, "x"), symbol: "photo");
                var panel = new StackPanel();
                foreach (var icon in new Control[] { running, failed, declined, completed, photo }) panel.Children.Add(icon);
                var window = Show(panel);
                foreach (var icon in new Control[] { running, failed, declined, completed, photo })
                    Assert.Contains(LogicalQuery.SelfAndDescendants<Control>(icon), control => control.Width == TimelineMetrics.IconWidth);
                Assert.Single(LogicalQuery.SelfAndDescendants<ProgressBar>(running));
                Assert.Empty(Symbols(running));
                var cross = LogicalQuery.SelfAndDescendants<SwiftImage>(failed).Single();
                Assert.Equal("xmark.circle.fill", cross.SymbolName);
                Assert.Equal(Resolved(ChromeTokens.Danger, cross), BrushColor(cross.Foreground));
                var hand = LogicalQuery.SelfAndDescendants<SwiftImage>(declined).Single();
                Assert.Equal("hand.raised.slash", hand.SymbolName);
                Assert.Equal(Resolved(ChromeTokens.Warning, hand), BrushColor(hand.Foreground));
                var glyph = LogicalQuery.SelfAndDescendants<SwiftImage>(completed).Single();
                Assert.Equal("doc.text", glyph.SymbolName);
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, glyph), BrushColor(glyph.Foreground));
                Assert.Equal("photo", LogicalQuery.SelfAndDescendants<SwiftImage>(photo).Single().SymbolName);
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-100")] // App/Views/Chat/TimelineRows.swift:1832-1841
    public void A_failed_call_shows_its_exit_code_in_danger_and_a_declined_one_Declined_in_warning()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var failed = ToolRowFor(h.Runtime, Call(ToolCall.Kind.Command, "make", ToolCall.Status.Failed) with { ExitCode = 2 });
                var noCode = ToolRowFor(h.Runtime, Call(ToolCall.Kind.Command, "make", ToolCall.Status.Failed));
                var declined = ToolRowFor(h.Runtime, Call(ToolCall.Kind.Command, "rm -rf /", ToolCall.Status.Declined));
                var panel = new StackPanel();
                panel.Children.Add(failed);
                panel.Children.Add(noCode);
                panel.Children.Add(declined);
                var window = Show(panel);
                var exit = LogicalQuery.SelfAndDescendants<TextBlock>(failed).Single(block => block.Text == "exit 2");
                Assert.Equal(Resolved(ChromeTokens.Danger, exit), BrushColor(exit.Foreground));
                Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Caption).Resolve().FontSize, exit.FontSize);
                Assert.DoesNotContain(Texts(noCode), text => text.StartsWith("exit", System.StringComparison.Ordinal));
                Assert.DoesNotContain("Declined", Texts(noCode));
                var word = LogicalQuery.SelfAndDescendants<TextBlock>(declined).Single(block => block.Text == "Declined");
                Assert.Equal(Resolved(ChromeTokens.Warning, word), BrushColor(word.Foreground));
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-096")] // App/Views/Chat/TimelineRows.swift:1772-1795, :1812-1820
    public void An_mcp_call_to_a_catalog_server_draws_as_a_pill_badge_with_the_servers_glyph_and_label()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var row = ToolRowFor(h.Runtime, Call(ToolCall.Kind.Mcp, "Context7 \U000000B7 resolve-library-id")); // U+00B7 MIDDLE DOT
                var plain = ToolRowFor(h.Runtime, Call(ToolCall.Kind.Mcp, "Unknown \U000000B7 thing")); // U+00B7 MIDDLE DOT
                var panel = new StackPanel();
                panel.Children.Add(row);
                panel.Children.Add(plain);
                var window = Show(panel);
                Assert.Single(LogicalQuery.SelfAndDescendants<MCPGlyph>(row));
                Assert.Empty(LogicalQuery.SelfAndDescendants<ToolStatusIcon>(row));
                var label = LogicalQuery.SelfAndDescendants<TextBlock>(row).Single(block => block.Text == "Context7: resolve-library-id");
                var pill = LogicalQuery.SelfAndDescendants<Border>(row).Single(border => border.Padding == new Thickness(TimelineMetrics.PillLeading, TimelineMetrics.PillVertical, TimelineMetrics.PillTrailing, TimelineMetrics.PillVertical));
                Assert.Equal(new CornerRadius(TimelineMetrics.PillRadius), pill.CornerRadius);
                Assert.Equal(Resolved(SwiftColor.Quaternary.Opacity(0.32), pill), BrushColor(pill.Background));
                Assert.Equal(Resolved(ChromeTokens.PrimaryText.Opacity(0.9), label), BrushColor(label.Foreground));
                Assert.Empty(LogicalQuery.SelfAndDescendants<MCPGlyph>(plain));
                Assert.Single(LogicalQuery.SelfAndDescendants<ToolStatusIcon>(plain));
                Assert.Contains("Called Unknown \U000000B7 thing", Texts(plain)); // U+00B7 MIDDLE DOT
                Close(window);
            });
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-101 ToolDetailView

    private static string Lines(int count) => string.Join("\n", Enumerable.Range(1, count).Select(i => "line " + i));

    [Fact]
    [Trait("Req", "PR-CHAT-101")] // App/Views/Chat/TimelineRows.swift:2103-2113, :2141-2162
    public void Output_over_thirty_lines_shows_its_first_thirty_and_a_link_that_shows_all_of_it_then_Show_less()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            ToolDetailView? view = null;
            AvaloniaWindow? window = null;
            h.Main.OnMain(() =>
            {
                view = new ToolDetailView(Call(ToolCall.Kind.Command, "cat") with { Output = "\n\n" + Lines(31) + "\n\n" });
                window = Show(view);
                Assert.Contains(Lines(30), Texts(view));
                Assert.DoesNotContain(Lines(31), Texts(view));
                var link = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(view));
                Assert.Contains("Show full output (31 lines)", Texts(link));
                Click(link);
            });
            h.Main.OnMain(() =>
            {
                Assert.Contains(Lines(31), Texts(view!));
                Assert.Contains("Show less", Texts(Assert.Single(LogicalQuery.SelfAndDescendants<Button>(view!))));
                Close(window!);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-101")] // App/Views/Chat/TimelineRows.swift:2120-2126, :2145-2147, :2156
    public void Thirty_lines_show_whole_with_no_link_and_the_detail_is_capped_at_twelve_lines_in_secondary_monospace()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var view = new ToolDetailView(Call(ToolCall.Kind.Command, "cat") with { Output = Lines(30), Detail = "the detail" });
                var window = Show(view);
                Assert.Contains(Lines(30), Texts(view));
                Assert.Empty(LogicalQuery.SelfAndDescendants<Button>(view));
                var detail = LogicalQuery.SelfAndDescendants<TextBlock>(view).Single(block => block.Text == "the detail");
                Assert.Equal(12, detail.MaxLines);
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, detail), BrushColor(detail.Foreground));
                Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Caption, design: SwiftFont.Design.Monospaced).Resolve().FontFamily, detail.FontFamily);
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-101")] // App/Views/Chat/TimelineRows.swift:2127-2139
    public void Each_edit_with_a_diff_shows_its_path_above_its_lines_on_a_rounded_plate()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var call = Call(ToolCall.Kind.Edit, "e") with
                {
                    Edits = new ValueList<FileEdit>([new FileEdit("src/a.swift", "@@ -1 +1 @@\n-a\n+b\n", 1, 1), new FileEdit("src/b.swift", "", 0, 0)]),
                };
                // Inspected without a layout pass: the edits are built with the view, and DiffLinesView's own font
                // (App.Views.Diff.DiffInspector's) does not resolve headless.
                var view = new ToolDetailView(call);
                Assert.Single(LogicalQuery.SelfAndDescendants<DiffLinesView>(view));
                var path = LogicalQuery.SelfAndDescendants<TextBlock>(view).Single(block => block.Text == "src/a.swift");
                Assert.Equal(new Thickness(10, 6), path.Margin);
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, path), BrushColor(path.Foreground));
                var plate = Assert.IsType<Border>(Assert.IsType<StackPanel>(path.Parent).Parent);
                Assert.Equal(new CornerRadius(10), plate.CornerRadius);
                Assert.True(plate.ClipToBounds);
                Assert.Equal(Resolved(SwiftColor.Quaternary.Opacity(0.45), plate), BrushColor(plate.Background));
                Assert.DoesNotContain("src/b.swift", Texts(view));
            });
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-090 WorkGroup, PR-CHAT-092 WorkSteps

    [Fact]
    [Trait("Req", "PR-CHAT-090")] // App/Views/Chat/TimelineRows.swift:1463-1465
    public void A_lone_tool_renders_as_its_row_alone()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var group = new WorkGroup(new ValueList<TimelineEntry>([ToolEntry(Call(ToolCall.Kind.Search, "grep x"))]), h.Runtime);
                var window = Show(group);
                Assert.Single(LogicalQuery.SelfAndDescendants<ToolRow>(group));
                Assert.Empty(LogicalQuery.SelfAndDescendants<WorkSteps>(group));
                Assert.DoesNotContain("Searched", Texts(group));
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-090")] // App/Views/Chat/TimelineRows.swift:1466-1502
    public void A_group_the_agent_moved_on_from_starts_as_one_summary_line_and_opening_it_reveals_the_end_while_closing_does_not()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource();
            using var frameSeam = FrameClock.Source.Override(frames);
            var reveals = 0;
            WorkGroup? group = null;
            AvaloniaWindow? window = null;
            h.Main.OnMain(() =>
            {
                var entries = new ValueList<TimelineEntry>([ToolEntry(Call(ToolCall.Kind.Edit, "Edit a")), ToolEntry(Call(ToolCall.Kind.Command, "ls"))]);
                group = new WorkGroup(entries, h.Runtime, startsCollapsed: true);
                var host = new Border { Child = group };
                ThreadTimeline.SetRevealTimelineEnd(host, new TimelineRevealBox { Action = () => reveals++ });
                window = Show(host);
                var header = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(group));
                Assert.Equal("Show these steps", ToolTip.GetTip(header));
                Assert.Equal("Show these steps", AutomationProperties.GetName(header));
                Assert.Contains("Edited files, ran commands", Texts(header));
                Assert.Equal(["pencil", "chevron.right"], Symbols(header));
                var chevron = LogicalQuery.SelfAndDescendants<SwiftImage>(header).Single(image => image.SymbolName == "chevron.right");
                Assert.Equal(0, Assert.IsType<RotateTransform>(chevron.RenderTransform).Angle);
                var glyph = LogicalQuery.SelfAndDescendants<SwiftImage>(header).Single(image => image.SymbolName == "pencil");
                Assert.Equal(TimelineMetrics.IconWidth, glyph.Width);
                Assert.Empty(LogicalQuery.SelfAndDescendants<WorkSteps>(group));
                Click(header);
                Assert.Equal(1, reveals);
            });
            h.Main.OnMain(() =>
            {
                var header = LogicalQuery.SelfAndDescendants<Button>(group!).First();
                Assert.Equal("Hide these steps", ToolTip.GetTip(header));
                Assert.Single(LogicalQuery.SelfAndDescendants<WorkSteps>(group!));
                Click(header);
                Assert.Equal(1, reveals);
            });
            h.Main.OnMain(() =>
            {
                Assert.Equal("Show these steps", ToolTip.GetTip(LogicalQuery.SelfAndDescendants<Button>(group!).First()));
                Close(window!);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-090")] // App/Views/Chat/TimelineRows.swift:1503-1507
    public void Arriving_reply_text_folds_the_group_once_and_a_group_the_reader_reopened_never_snaps_shut_on_its_own()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource();
            using var frameSeam = FrameClock.Source.Override(frames);
            var entries = new ValueList<TimelineEntry>([]);
            WorkGroup? group = null;
            AvaloniaWindow? window = null;
            string Help() => (string)ToolTip.GetTip(LogicalQuery.SelfAndDescendants<Button>(group!).First())!;
            h.Main.OnMain(() =>
            {
                entries = new ValueList<TimelineEntry>([ToolEntry(Call(ToolCall.Kind.Read, "Read a")), ToolEntry(Call(ToolCall.Kind.Read, "Read b"))]);
                group = new WorkGroup(entries, h.Runtime);
                window = Show(group);
                Assert.Equal("Hide these steps", Help());
                group.Update(entries, startsCollapsed: true); // reply text arrived after the group
            });
            h.Main.OnMain(() =>
            {
                Assert.Equal("Show these steps", Help());
                Click(LogicalQuery.SelfAndDescendants<Button>(group!).First()); // the reader opens it again
            });
            h.Main.OnMain(() =>
            {
                Assert.Equal("Hide these steps", Help());
                group!.Update(entries, startsCollapsed: true); // a later render, the flag unchanged
            });
            h.Main.OnMain(() =>
            {
                Assert.Equal("Hide these steps", Help());
                group!.Update(entries, startsCollapsed: false);
            });
            h.Main.OnMain(() =>
            {
                Assert.Equal("Hide these steps", Help());
                Close(window!);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-090")] // App/Views/Chat/TimelineRows.swift:1455-1461, :1499-1501
    public void The_live_group_starts_open_with_its_steps()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var entries = new ValueList<TimelineEntry>([ToolEntry(Call(ToolCall.Kind.Read, "Read a")), ToolEntry(Call(ToolCall.Kind.Read, "Read b", ToolCall.Status.Running))]);
                var group = new WorkGroup(entries, h.Runtime);
                var window = Show(group);
                var header = LogicalQuery.SelfAndDescendants<Button>(group).First();
                Assert.Equal("Hide these steps", ToolTip.GetTip(header));
                Assert.Contains("Reading files", Texts(header));
                Assert.Single(LogicalQuery.SelfAndDescendants<WorkSteps>(group));
                Assert.Equal(2, LogicalQuery.SelfAndDescendants<ToolRow>(group).Count());
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-092")] // App/Views/Chat/TimelineRows.swift:1522-1545
    public void Only_the_two_latest_steps_show_under_an_earlier_steps_line_that_reveals_them_all()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource();
            using var frameSeam = FrameClock.Source.Override(frames);
            var reveals = 0;
            var showsAll = false;
            WorkSteps? steps = null;
            WorkSteps? four = null;
            h.Main.OnMain(() =>
            {
                var three = new ValueList<TimelineEntry>([ToolEntry(Call(ToolCall.Kind.Read, "Read a")), ToolEntry(Call(ToolCall.Kind.Read, "Read b")), ToolEntry(Call(ToolCall.Kind.Read, "Read c"))]);
                steps = new WorkSteps(three, h.Runtime, new Binding<bool>(() => showsAll, value => showsAll = value));
                four = new WorkSteps(new ValueList<TimelineEntry>([.. three, ToolEntry(Call(ToolCall.Kind.Read, "Read d"))]), h.Runtime, Binding<bool>.Constant(false));
                var host = new StackPanel();
                host.Children.Add(steps);
                host.Children.Add(four);
                ThreadTimeline.SetRevealTimelineEnd(host, new TimelineRevealBox { Action = () => reveals++ });
                var window = Show(host);
                Assert.Contains("1 earlier step", Texts(steps));
                Assert.Equal(["Read b", "Read c"], LogicalQuery.SelfAndDescendants<ToolRow>(steps).SelectMany(row => Texts(row)).Where(text => text.StartsWith("Read ", System.StringComparison.Ordinal)));
                Assert.Contains("2 earlier steps", Texts(four));
                Assert.Equal(2, LogicalQuery.SelfAndDescendants<ToolRow>(four).Count());
                var more = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(steps), button => Texts(button).Contains("1 earlier step"));
                Assert.Contains("ellipsis", Symbols(more));
                Click(more);
                Assert.True(showsAll);
                Assert.Equal(1, reveals);
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-092")] // App/Views/Chat/TimelineRows.swift:1523, :1545-1553
    public void With_every_step_showing_there_is_no_earlier_line_and_narration_is_dimmed_12_point_markdown_after_the_icon_column()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var entries = new ValueList<TimelineEntry>([ToolEntry(Call(ToolCall.Kind.Read, "Read a")), ReplyEntry("now the build", streaming: true), ToolEntry(Call(ToolCall.Kind.Command, "make"))]);
                var steps = new WorkSteps(entries, h.Runtime, Binding<bool>.Constant(true));
                var window = Show(steps);
                Assert.DoesNotContain(Texts(steps), text => text.Contains("earlier step", System.StringComparison.Ordinal));
                Assert.Equal(2, LogicalQuery.SelfAndDescendants<ToolRow>(steps).Count());
                var narration = Assert.Single(LogicalQuery.SelfAndDescendants<MarkdownView>(steps));
                Assert.Equal("now the build", narration.Text);
                Assert.Equal(12, MarkdownView.GetMarkdownPointSize(narration));
                Assert.True(MarkdownView.GetMarkdownDimmed(narration));
                Assert.Equal(TimelineMetrics.IconWidth + TimelineMetrics.IconSpacing, narration.Margin.Left);
                Close(window);
            });
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-104 TurnFinishedBlock

    private static Border[] Rules(Control block) =>
        LogicalQuery.SelfAndDescendants<Border>(block).Where(border => border.Height == 1 && border.Opacity == 0.6).ToArray();

    [Fact]
    [Trait("Req", "PR-CHAT-104")] // App/Views/Chat/TimelineRows.swift:2589-2630, :2664-2680, :2507-2508
    public void A_finished_turn_opens_folded_to_its_prompt_a_summary_line_a_rule_and_its_answer()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var turn = System.Guid.NewGuid();
                var user = UserEntry("do it", turn);
                var content = new ValueList<TimelineEntry>([ReplyEntry("```\nplan\n```"), ToolEntry(Call(ToolCall.Kind.Command, "make"), turn), ReplyEntry(CodeOnlyReply, turn: turn)]);
                var block = new TurnFinishedBlock(h.Runtime, turn, Summary(turn), new ValueList<TimelineEntry>([user]), content, null, canUndo: false, chronological: false);
                var window = Show(block);
                var stack = Assert.IsType<StackPanel>(block.Child);
                Assert.IsType<UserMessageRow>(stack.Children[0]);
                Assert.Equal(TimelineMetrics.RowSpacing, stack.Children[0].Margin.Bottom);
                var header = Assert.IsType<Button>(stack.Children[1]);
                Assert.Equal("Show this turn's steps", ToolTip.GetTip(header));
                Assert.Equal("Show this turn's steps", AutomationProperties.GetName(header));
                var label = LogicalQuery.SelfAndDescendants<TextBlock>(header).Single();
                Assert.Equal("Worked for " + RelativeTime.Duration(System.TimeSpan.FromSeconds(5)), label.Text);
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, label), BrushColor(label.Foreground));
                var chevron = LogicalQuery.SelfAndDescendants<SwiftImage>(header).Single();
                Assert.Equal("chevron.right", chevron.SymbolName);
                Assert.Equal(0, Assert.IsType<RotateTransform>(chevron.RenderTransform).Angle);
                // :2625-2629 a hairline at 0.6 opacity with half a row gap on each side, after the header.
                var rule = Assert.Single(Rules(block));
                Assert.Equal(new Thickness(0, TimelineMetrics.RowSpacing / 2), rule.Margin);
                Assert.True(stack.Children.IndexOf(stack.Children.First(child => LogicalQuery.SelfAndDescendants<Border>(child).Contains(rule))) > 1);
                // Only the reply after the last tool call is the answer; the narration before it stays folded away.
                var answers = LogicalQuery.SelfAndDescendants<FoldedAnswerView>(block).ToList();
                var answer = Assert.Single(answers);
                Assert.Equal(CodeOnlyReply, LogicalQuery.SelfAndDescendants<MarkdownView>(answer).Single().Text);
                Assert.Empty(LogicalQuery.SelfAndDescendants<ToolRow>(block));
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-104")] // App/Views/Chat/TimelineRows.swift:2597, :2624
    public void A_finished_turn_with_nothing_to_show_has_no_rule()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var turn = System.Guid.NewGuid();
                var content = new ValueList<TimelineEntry>([ToolEntry(Call(ToolCall.Kind.Command, "make"), turn)]);
                var block = new TurnFinishedBlock(h.Runtime, turn, Summary(turn), ValueList<TimelineEntry>.Empty, content, null, canUndo: false, chronological: false);
                var window = Show(block);
                Assert.Single(LogicalQuery.SelfAndDescendants<Button>(block));
                Assert.Empty(Rules(block));
                Assert.Empty(LogicalQuery.SelfAndDescendants<FoldedAnswerView>(block));
                Close(window);
            });
        });
    }
}

// STOP App.Views.Chat.TimelineRows #1 (part 1, 2026-09-26)
// Kind: missing dependency
// Swift: DroppyCode/App/Views/Chat/TimelineRows.swift:110 (B3 52bc792) `.modifier(ChipChrome())`
// Requirements: PR-CHAT-078
// What is unclear or missing: the quote chips wear ChipChrome (App/Views/Composer/ComposerView.swift:962-970), whose
// C# Body is still MARROW_STUB in src/Marrow.App/Views/Composer/ComposerView.cs (App.Views.Composer.ComposerView unit).
// UserMessageRow calls it as written, so a sent message that leads with a quote throws that unit's MARROW_STUB until it
// lands; the chips' glyph, excerpt, 260-point cap, tooltip and accessibility label are written but cannot be rendered.
// Readings or options: none; the modifier is the other unit's.
// Needed to proceed: unit App.Views.Composer.ComposerView (ChipChrome.Body).
// Done so far: the no-leading-quote path (no chips, whole text in the bubble) is tested above and passes.
//
// STOP App.Views.Chat.TimelineRows #2 (part 1, 2026-09-26)
// Kind: platform gap
// Swift: DroppyCode/App/Views/Chat/TimelineRows.swift:191-196 (B3 52bc792) `.confirmationDialog("Edit from this
// message?", isPresented: $isConfirmingRevert) { Button("Revert and keep file changes") ...; Button("Revert files too",
// role: .destructive) ... } message: { Text(...) }`
// Requirements: PR-CHAT-082 (and PR-GIT-123 in part 3)
// What is unclear or missing: the port has no seam for a SwiftUI confirmationDialog. Presentation.Presenter carries
// popovers (R-VIEW-9) and App/Port/AlertPresenter.cs carries NSAlert.runModal with plain buttons only: neither has a
// destructive role or the system Cancel a confirmation dialog adds, and no ED or PA says how a confirmation dialog
// presents on Windows. The intent path is written (menu -> MessageMenuRequests -> isConfirmingRevert, :184-190) and so
// is revert(restoreFiles:) (:202-206); the presentation itself stays MARROW_STUB and throws when Edit from here is chosen.
// Readings or options: (a) a modal Avalonia window like WindowsAlertPresenter with the title, the message, the two
// buttons (the second in Chrome.danger) and Cancel; (b) a popover on the row through Presentation.Presenter.
// Needed to proceed: a decision (ED or PA) on the confirmationDialog seam.
// Done so far: the menu offers Edit from here with its divider (tested above); nothing is presented.
//
// STOP App.Views.Chat.TimelineRows #3 (part 1, 2026-09-26)
// Kind: missing dependency
// Swift: DroppyCode/App/Views/Chat/TimelineRows.swift:2580-2585 (B3 52bc792) `TimelineGroup.build(content,
// showReasoning: false)`, read when the finished turn is expanded (:2631-2663)
// Requirements: PR-CHAT-104 (the expanded half)
// What is unclear or missing: TimelineGroup.Build is still MARROW_STUB in src/Marrow.App/Views/Chat/ThreadTimeline.cs
// (App.Views.Chat.ThreadTimeline unit). The expanded card is written as the Swift writes it, but opening a finished
// turn throws that unit's MARROW_STUB until it lands, so the chevron's opening, the filled 14-point card and its
// bottom gap are untested.
// Needed to proceed: unit App.Views.Chat.ThreadTimeline (TimelineGroup.Build).
// Done so far: the folded turn (header, tooltip, rule, answer selection) is tested above and passes.
