using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// REVIEW (unit rec-cornerclip): every `.continuous` Border WITH CONTENT is now cut by its exact outline unless its site marks it ContentBeyondSides. The base (d128af285)
// is the same code with every such Border marked, so each mounted element is rendered twice in one process, as it is (HEAD) and with every tracked Border with a child
// marked (BASE), at render scale 1 and 1.5. A pixel that differs OUTSIDE every such Border's rectangle is content the new clip cut beyond a side (the defect class this
// unit risks); one inside is the corner the unit fixes. The same pass reports the hit shape 2 px outside each side's middle (content that was clickable there before): the
// one-step steps card loses the 10 pt band of its last reply's hover room below its bottom side (TimelineRows.swift:1388, nothing drawn there: summary nil in the card).
[Collection("Headless")]
public sealed class ContinuousCornerContentClipReviewTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-297")]
    public void REVIEW_no_pixel_outside_a_continuous_Border_with_content_changes_against_the_base_clip() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        using var noNetwork = new NoLinkNetwork("gitlab.com");
        h.Main.OnMain(() =>
        {
            var report = new List<string>();
            var failures = new List<string>();
            foreach (var scale in new[] { 1.0, 1.5 })
            {
                const string twoLines = "A warning long enough that it wraps onto a second line when the row is only 380 points wide.";
                var turn = Guid.NewGuid();
                var steps = new TurnFinishedBlock(h.Runtime, turn, Summary(turn) with { Duration = 64 }, ValueList<TimelineEntry>.Empty,
                    new ValueList<TimelineEntry>([ToolEntry(Call(ToolCall.Kind.Read, "Read a"), turn), NoticeEntry(Notice.Level.Warning, twoLines), Reply("An intermediate reply, the last step in the card.")]),
                    null, canUndo: false, chronological: true);
                var files = new TurnFileCard(new FileChangeSummary(new ValueList<FileChangeSummary.File>([new FileChangeSummary.File("src/file1.swift", 1, 1, false), new FileChangeSummary.File("src/b/file2.swift", 12, 3, false)]), 40, 7),
                    canUndo: false, onRevert: () => { }, onReview: _ => { });
                var delegation = new HydraDelegationBlock("[{\"task\": \"Tidy 0\", \"prompt\": \"p\", \"name\": \"Hank\"}, {\"task\": \"A much longer task for the second head\", \"prompt\": \"p\"}]", sent: false);
                var markdown = new MarkdownView("Some prose.\n\n```swift\nlet aVeryLongLineOfCodeThatRunsWellPastTheBlocksWidth = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22]\n```\n\n| a | b |\n|---|---|\n| one | two |\n\n> a quote");
                var approval = new ApprovalCard(new ApprovalRequest("req-1", ApprovalRequest.Kind.Command, "echo hello && a long command line that wraps in the block",
                    new ValueList<ApprovalRequest.Option>([new ApprovalRequest.Option("decline", "Decline", ApprovalRequest.Option.Role.Decline), new ApprovalRequest.Option("approve", "Approve", ApprovalRequest.Option.Role.Approve)]),
                    "A detail line", "A reason"), h.Runtime);
                var presented = false;
                var badge = new ChatBadge("Plan", new Binding<bool>(() => presented, value => presented = value), () => new Border { Width = 18, Height = 18 }, () => new Border());
                var items = new StackPanel { Width = 220, Children = { new PopoverItem("Rename", () => { }, symbol: "pencil"), new PopoverItem("Delete", () => { }, symbol: "trash") } };
                var card = new ChromeCard(() => new StackPanel { Children = { new TextBlock { Text = "A row in a settings card", Margin = new Thickness(14, 10) }, new TextBlock { Text = "Another row", Margin = new Thickness(14, 10) } } });
                var effortRow = EffortView.Row(new TextBlock { Text = "High" }, 30, true, () => { });
                var moved = new NoticeRow(NoticeEntry(Notice.Level.Warning, twoLines + " Under a fractional translate.")) { RenderTransform = new TranslateTransform(0.5, 0.37) };
                var panel = new StackPanel { Width = 700, Spacing = 24, Margin = new Thickness(40) };
                foreach (var control in new Control[] { new Border { Width = 380, Child = new NoticeRow(NoticeEntry(Notice.Level.Warning, twoLines)) }, steps, files, delegation, markdown, approval, badge, items, card, effortRow })
                    panel.Children.Add(control);
                var window = new AvaloniaWindow { Width = 800, Height = 1500, Background = Brushes.White, Content = new Avalonia.Controls.Primitives.VisualLayerManager { Child = new ScrollViewer { Content = panel } } };
                window.Show();
                window.SetRenderScaling(scale);
                Settle(window);
                try
                {
                    Hover(window, markdown); // the code block's copy button
                    Settle(window);
                    var line = ContentClipBaseDiff.Read(window, panel, scale, () => Settle(window), out var outside, out _);
                    report.Add(line);
                    if (outside > 0) failures.Add(line);
                    // Reported, not asserted (ED-317 (3)(c), taken): at a fractional place the exact outline's straight sides anti-alias with the fill's own edge.
                    panel.Children.Clear();
                    panel.Children.Add(new Border { Width = 380, Child = moved });
                    Settle(window);
                    report.Add("fractional translate (0.5, 0.37), straight edges: " + ContentClipBaseDiff.Read(window, panel, scale, () => Settle(window), out _, out _));
                }
                finally { Close(window); }
            }
            ContentClipBaseDiff.Report(report);
            Assert.True(failures.Count == 0, string.Join(" || ", failures) + " ||| " + string.Join(" || ", report));
        });
    });
}
