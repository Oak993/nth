using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Git;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using Chrome = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), :680-687, :1833-1840,
// :1906-1909, :2839-2856. ED-059/OD-0009 supply the approved Windows font mapping.
[Collection("Headless")]
public sealed class TimelineRowsUiLabelsTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "UI-CHAT-042")] // App/Views/Chat/TimelineRows.swift:2850-2856
    public void Overflow_has_exact_count_case_type_colour_insets_and_boundary() => OnMain(() =>
    {
        // :2850 files.count > 12; :2851 singular at 13, otherwise count - 12.
        foreach (var count in new[] { 0, 1, 11, 12, 13, 14, 20 })
        {
            var panel = Files(Enumerable.Range(0, count).Select(i => new FileChangeSummary.File($"file{i}", 0, 0, false)));
            using var host = Host(panel);
            var lines = Texts(panel).Where(t => t.Text?.StartsWith("and ", StringComparison.Ordinal) == true).ToArray();
            if (count <= 12) { Assert.Empty(lines); continue; }
            var line = Assert.Single(lines);
            Assert.Equal(count == 13 ? "and 1 more file" : $"and {count - 12} more files", line.Text);
            Assert.Equal(new Thickness(12, 7), line.Margin); // :2854-2855
            Assert.Same(panel, line.Parent); // noninteractive direct label, after the twelve rows
            Assert.Same(line, panel.Children.Last());
            Font(line, 12); // :2852 .chat(.callout); ED-059 B1 ChatZoom.swift:52
            Colour(line, Chrome.SecondaryText); // :2853
            Assert.Empty(LogicalQuery.SelfAndDescendants<Button>(line));
        }
    });

    [Fact]
    [Trait("Req", "UI-CHAT-072")] // App/Views/Chat/TimelineRows.swift:2839-2842; MessageEditor.swift:137-138
    public void Binary_replaces_stats_only_for_binary_files_at_the_trailing_edge() => OnMain(() =>
    {
        var panel = Files([new("binary.png", 3, 2, true), new("text.cs", 5, 4, false)]);
        using var host = Host(panel);
        var binaryRow = Assert.IsType<DockPanel>(panel.Children[0]);
        var line = Assert.Single(Texts(binaryRow), t => t.Text == "Binary");
        Assert.Equal("Binary", line.Text);
        Font(line, 10); // :2841 .chat(.caption); ED-059 B1 ChatZoom.swift:54
        Colour(line, Chrome.SecondaryText); // :2842
        Assert.Equal(Dock.Right, DockPanel.GetDock(line));
        Assert.True(line.Bounds.Right > Texts(binaryRow).Single(t => t.Text == "binary.png").Bounds.Right);
        Assert.Empty(LogicalQuery.SelfAndDescendants<DiffStatLabel>(binaryRow));
        Assert.DoesNotContain(Texts(panel.Children[1]), t => t.Text == "Binary");
        Assert.Single(LogicalQuery.SelfAndDescendants<DiffStatLabel>(panel.Children[1]));
        Assert.Empty(LogicalQuery.SelfAndDescendants<Button>(line));
        // UI-CHAT-072's note cites the same literal in MessageEditor.swift:137-138.
        var editor = Assert.IsAssignableFrom<Control>(typeof(MessageEditor).GetMethod("BuildFileList", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [new ValueList<RevertPreview.File>([new("binary.png", 0, 0, true)]), true]));
        using var editorHost = Host(editor);
        Assert.Equal("Binary", Assert.Single(Texts(editor), t => t.Text == "Binary").Text);
    });

    [Fact]
    [Trait("Req", "UI-CHAT-071")] // App/Views/Chat/TimelineRows.swift:680-687
    public void Next_brief_is_exact_caption_after_the_clear_column_only_while_streaming_with_entries() => OnMain(() =>
    {
        const string head = "{\"task\":\"Tidy\",\"prompt\":\"p\",\"name\":\"Hank\"}";
        var streaming = "[" + head + ", {\"task\":\"unfinished";
        foreach (var (json, sent, shown) in new[] { (streaming, false, true), (streaming, true, false),
                     ("[" + head + "]", false, false), ("[{\"task\":\"unfinished", false, false), ("[]", false, false) })
        {
            var block = new HydraDelegationBlock(json, sent);
            using var host = Host(block);
            var labels = Texts(block).Where(t => t.Text?.StartsWith("Writing", StringComparison.Ordinal) == true).ToArray();
            if (!shown) { Assert.Empty(labels); continue; }
            var line = Assert.Single(labels);
            Assert.Equal("Writing the next brief\u2026", line.Text); // :683 U+2026
            Font(line, 10); // :684 .chat(.caption); ED-059 B1 ChatZoom.swift:54
            Colour(line, Chrome.SecondaryText); // :685
            var row = Assert.IsType<StackPanel>(line.Parent);
            Assert.Equal(Orientation.Horizontal, row.Orientation);
            Assert.Equal(8, row.Spacing); // :681 TimelineMetrics.iconSpacing (:18)
            Assert.Equal(2, row.Children.Count);
            var clear = Assert.IsType<Control>(row.Children[0]); // :682 Color.clear.frame(width:16,height:16)
            Assert.Equal(16, clear.Width); Assert.Equal(16, clear.Height);
            Assert.Same(line, row.Children[1]);
            Assert.True(line.Bounds.Left >= clear.Bounds.Right + 8);
            Assert.IsType<StackPanel>(row.Parent); // a plain row, never a button around its label
            Assert.Empty(LogicalQuery.SelfAndDescendants<Button>(row));
        }
    });

    [Fact]
    [Trait("Req", "UI-CHAT-070")] // App/Views/Chat/TimelineRows.swift:1906-1909; :2030-2048
    public void Empty_steps_is_exact_callout_in_Steps_and_omitted_when_any_worthwhile_step_exists() => OnMain(() =>
    {
        // :2035-2046 initial brief and blank assistant are omitted, tools and notices count.
        var brief = Entry(new TimelineItem.Content.User(new UserMessage { Text = "brief" }));
        var blank = Entry(new TimelineItem.Content.Assistant(new AssistantMessage { Text = " \n" }));
        var tool = Entry(new TimelineItem.Content.Tool(new ToolCall { ToolCallKind = ToolCall.Kind.Read, Title = "Read file", ToolCallStatus = ToolCall.Status.Completed }));
        var notice = Entry(new TimelineItem.Content.Notice(new Notice { Message = "notice", NoticeLevel = Notice.Level.Info }));
        var reply = Entry(new TimelineItem.Content.Assistant(new AssistantMessage { Text = "reply" }));
        var laterBrief = Entry(new TimelineItem.Content.User(new UserMessage { Text = "later brief" }));
        TimelineEntry[] ignored = [brief, blank, Entry(new TimelineItem.Content.User(new UserMessage { Text = " \n" })),
            Entry(new TimelineItem.Content.Reasoning(new ReasoningBlock { Text = "thinking" })),
            Entry(new TimelineItem.Content.Plan(new ProposedPlan { Markdown = "plan", ProposedPlanState = ProposedPlan.State.Proposed })),
            Entry(new TimelineItem.Content.Todos(ValueList<TodoStep>.Empty)),
            Entry(new TimelineItem.Content.TurnEnd(new TurnSummary { TurnID = Guid.NewGuid(), Status = TurnStatus.Completed, Duration = 0, FilesChanged = 0, Additions = 0, Deletions = 0 }))];
        foreach (var (entries, shown) in new[] { (Array.Empty<TimelineEntry>(), true), (ignored, true),
                     (new[] { brief, tool }, false), (new[] { brief, notice }, false), (new[] { brief, reply }, false), (new[] { brief, laterBrief }, false) })
        {
            var steps = (IReadOnlyList<TimelineEntry>)Static("Steps", (object)entries);
            var body = (Control)Static("Body", null, steps);
            using var host = Host(body);
            var empty = Texts(body).Where(t => t.Text?.StartsWith("No steps", StringComparison.Ordinal) == true).ToArray();
            if (!shown) { Assert.Empty(empty); continue; }
            var line = Assert.Single(empty);
            Assert.Equal("No steps yet.", line.Text);
            Font(line, 12); // :1908 .chat(.callout); ED-059 B1 ChatZoom.swift:52
            Colour(line, Chrome.SecondaryText); // :1909
            var section = Assert.IsType<StackPanel>(line.Parent);
            Assert.Equal("Steps", Assert.IsType<TextBlock>(section.Children[0]).Text);
            Assert.Same(line, section.Children[1]);
            Assert.Empty(LogicalQuery.SelfAndDescendants<Button>(line));
        }
    });

    [Fact]
    [Trait("Req", "UI-CHAT-063")] // App/Views/Chat/TimelineRows.swift:1837-1840
    public void Declined_is_exact_caption_warning_noninteractive_trailing_with_minimum_gap_only_when_declined() => Outcome(false);

    [Fact]
    [Trait("Req", "UI-CHAT-064")] // App/Views/Chat/TimelineRows.swift:1833-1836
    public void Exit_is_exact_integer_caption_tabular_danger_noninteractive_trailing_only_for_failed_known_code() => Outcome(true);

    private void Outcome(bool exit) => headless.OnUiThread(() =>
    {
        Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        using var h = new ThreadRuntimePart12Harness();
        h.Main.OnMain(() =>
        {
            var previousTheme = ThemeManager.Current;
            try
            {
            foreach (var theme in new[] { AppTheme.System, AppTheme.Matrix, AppTheme.Codex })
            {
            ThemeManager.Current = theme; // Chrome warning/danger follow the actual selected palette, not fixed RGB.
            foreach (var status in new[] { ToolCall.Status.Running, ToolCall.Status.Completed, ToolCall.Status.Failed, ToolCall.Status.Declined })
            foreach (var code in new int?[] { null, -1, 0, 127 })
            {
                // A tight row with an overflowing subject makes the *minimum* gap observable, rather than
                // letting spare width conceal a missing Spacer(minLength: 8) (:1832).
                var call = new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = "Run " + new string('a', 200), ToolCallStatus = status, ExitCode = code };
                var row = new ToolRow(Entry(new TimelineItem.Content.Tool(call)), h.Runtime) { Width = 320 };
                using var host = Host(row);
                var texts = Texts(row).Where(t => exit ? t.Text?.StartsWith("exit", StringComparison.Ordinal) == true : t.Text == "Declined").ToArray();
                var shown = exit ? status == ToolCall.Status.Failed && code is not null : status == ToolCall.Status.Declined;
                if (!shown) { Assert.Empty(texts); continue; }
                var line = Assert.Single(texts);
                Assert.Equal(exit ? "exit " + code : "Declined", line.Text); // :1834 / :1838
                Font(line, 10); // .chat(.caption), ED-059 B1 ChatZoom.swift:54
                Colour(line, exit ? Chrome.Danger : Chrome.Warning); // :1836 / :1840
                if (exit) { var feature = Assert.Single(line.FontFeatures!); Assert.Equal("tnum", feature.Tag); Assert.Equal(1, feature.Value); }
                var panel = Assert.IsType<DockPanel>(line.Parent);
                Assert.Equal(Dock.Right, DockPanel.GetDock(line));
                Assert.Equal(panel.Bounds.Width, line.Bounds.Right, 5);
                var other = Assert.Single(panel.Children, c => c != line);
                Assert.True(line.Bounds.Left - other.Bounds.Right >= 8); // :1832 Spacer(minLength:8)
                Assert.Empty(LogicalQuery.SelfAndDescendants<Button>(line));
            }
            }
            }
            finally { ThemeManager.Current = previousTheme; }
        });
    });

    private void OnMain(Action action) => headless.OnUiThread(() =>
    {
        using var actor = new TestRuntime();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        actor.OnMain(action);
    });
    private static TimelineEntry Entry(TimelineItem.Content content) => new(new TimelineItem(null, content));
    private static TextBlock[] Texts(Control control) => LogicalQuery.SelfAndDescendants<TextBlock>(control).ToArray();
    private static void Font(TextBlock text, double size)
    {
        // ED-059 and OD-0009/ED-107: fixed DIP size in the bundled Inter family, regular.
        Assert.Equal(size, text.FontSize); Assert.Equal(new FontFamily("fonts:Inter#Inter"), text.FontFamily);
        Assert.Equal(FontWeight.Normal, text.FontWeight); Assert.Equal(FontStyle.Normal, text.FontStyle);
    }
    private static void Colour(TextBlock text, SwiftColor colour) => Assert.Equal(colour.Resolve(text.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color);
    private static object Static(string name, params object?[] args) => typeof(HydraHeadStepsPopover).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args)!;
    private static StackPanel Files(IEnumerable<FileChangeSummary.File> files)
    {
        var card = new TurnFileCard(new FileChangeSummary(new ValueList<FileChangeSummary.File>(files), 0, 0), false, () => { }, _ => { });
        return Assert.IsType<StackPanel>(typeof(TurnFileCard).GetMethod("FilesPopover", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(card, null));
    }
    private static IDisposable Host(Control control)
    {
        var window = new Avalonia.Controls.Window { Content = control, Width = 800, Height = 600 };
        window.Show(); window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        return new TimelineRowsUiHost(window);
    }
}
