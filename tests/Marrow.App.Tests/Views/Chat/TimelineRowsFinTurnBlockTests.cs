using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), :2480-2716 TurnFinishedBlock: the folded turn's header,
// its answer or its steps, the heads it sent out and its file card.
[Collection("Headless")]
public sealed class TimelineRowsFinTurnBlockTests(HeadlessSession headless)
{
    private const string Narration = "```\nnarration\n```";
    private const string Answer = "```\nthe answer\n```";

    private static double Top(Control control, Visual root) => control.TranslatePoint(default, root)!.Value.Y;

    private static TurnSummary SummaryOf(Guid turn, FileChangeSummary? changes = null) => Summary(turn) with { Duration = 64, Changes = changes };

    private static FileChangeSummary Files(int count) =>
        new(new ValueList<FileChangeSummary.File>(Enumerable.Range(1, count).Select(i => new FileChangeSummary.File("src/file" + i + ".swift", i, 1, false))), 40, 7);

    private static double Angle(Control chevron) => Assert.IsType<RotateTransform>(chevron.RenderTransform).Angle;

    private static ValueList<TimelineEntry> Mixed(Guid turn) => new(
    [
        Reply(Narration),
        ToolEntry(Call(ToolCall.Kind.Command, "make"), turn),
        ToolEntry(Call(ToolCall.Kind.Read, "Read a"), turn),
        Reply(Answer),
        PlanEntry(ProposedPlan.State.Accepted, turn: turn),
        NoticeEntry(Notice.Level.Warning, "careful"),
        NoticeEntry(Notice.Level.Info, "fyi"),
    ]);

    [Fact]
    [Trait("Req", "UI-CHAT-036")] // TimelineRows.swift:2598-2701
    public void A_finished_turn_opens_folded_to_a_header_a_rule_and_its_answer_and_unfolds_to_its_steps() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var locale = SwiftFormatters.CurrentLocale.Override("en_US");
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        var reveals = 0;
        TurnFinishedBlock block = null!;
        AvaloniaWindow window = null!;
        h.Main.OnMain(() =>
        {
            var turn = Guid.NewGuid();
            block = new TurnFinishedBlock(h.Runtime, turn, SummaryOf(turn), new ValueList<TimelineEntry>([UserEntry("do it", turn)]), Mixed(turn), null, canUndo: false, chronological: false);
            var host = new Border { Child = block };
            ThreadTimeline.SetRevealTimelineEnd(host, new TimelineRevealBox { Action = () => reveals++ });
            window = Show(new StackPanel { Children = { host } });
            try
            {
                var stack = Assert.IsType<StackPanel>(block.Child);
                // :2599-2602 the user's message first, a row gap (20) above the header.
                Assert.IsType<UserMessageRow>(stack.Children[0]);
                Assert.Equal(20, stack.Children[0].Margin.Bottom);
                var header = Assert.IsType<Button>(stack.Children[1]);
                var label = TextNamed(header, "Worked for 1m 4s");
                var chevron = Assert.Single(Mounted<SwiftImage>(header));
                // :2609-2617 the label and chevron 6 points apart: callout, and caption2 semibold, both Chrome.secondaryText.
                Face(label, 12, FontWeight.Normal, ".chat(.callout)");
                Paints(label.Foreground, ChromeTokens.SecondaryText, label, "header label");
                Assert.Equal("chevron.right", chevron.SymbolName);
                var chevronFont = SwiftImage.GetFont(chevron)!.Resolve();
                Assert.Equal(10, chevronFont.FontSize);
                Assert.Equal(FontWeight.SemiBold, chevronFont.FontWeight);
                Paints(chevron.Foreground, ChromeTokens.SecondaryText, chevron, "header chevron");
                Assert.Equal(6, Left(chevron, window) - (Left(label, window) + label.Bounds.Width), 0.5);
                OnOneCentreLine(window, label, chevron);
                // The button hugs its label (a header in a leading VStack).
                Assert.Equal(label.Bounds.Width + 6 + chevron.Bounds.Width, header.Bounds.Width, 0.5);
                // :2621-2622 the tooltip and the accessibility label; :2507-2508 collapsed to begin with.
                Assert.Equal("Show this turn's steps", ToolTip.GetTip(header));
                Assert.Equal("Show this turn's steps", AutomationProperties.GetName(header));
                Assert.Equal(0, Angle(chevron));
                // :2625-2629 a rule at 0.6 opacity with half a row gap (10) each side, between the header and the body.
                var rule = Assert.Single(Mounted<Border>(block), border => border.Height == 1 && border.Opacity == 0.6);
                Assert.Equal(new Thickness(0, 10), rule.Margin);
                Assert.Equal(10, Top(rule, window) - (Top(header, window) + header.Bounds.Height), 0.5);
                // :2664-2680 folded: the replies after the last tool call, the plans, then the warnings and errors, with a row gap below.
                var answers = Mounted<FoldedAnswerView>(block).Select(view => Assert.Single(Mounted<MarkdownView>(view)).Text).ToList();
                Assert.Equal([Answer], answers);
                Assert.Single(Mounted<PlanCard>(block));
                Assert.Equal(["careful"], Mounted<NoticeRow>(block).Select(row => Assert.Single(Mounted<SelectableTextBlock>(row)).Text));
                Assert.DoesNotContain("fyi", Texts(block));
                Assert.Empty(Mounted<ToolRow>(block));
                Assert.Empty(Mounted<WorkGroup>(block));
                var column = Assert.IsType<StackPanel>(Mounted<FoldedAnswerView>(block).First().Parent);
                Assert.Equal(20, column.Spacing);
                Assert.Equal(20, column.Margin.Bottom);
                Assert.Equal(0, reveals);
                Click(window, header);
                Assert.Equal(1, reveals); // :2606 opening asks the timeline to reveal the end
            }
            catch { Close(window); throw; }
        });
        // :2604 withAnimation(.snappy(duration: 0.24)).
        frames.Advance(TimeSpan.Zero);
        h.Main.AdvanceMilliseconds(2000);
        frames.Advance(TimeSpan.FromMilliseconds(2000));
        h.Main.OnMain(() =>
        {
            try
            {
                Settle(window);
                var header = Assert.Single(Mounted<Button>(block), button => ToolTip.GetTip(button) as string == "Hide this turn's steps");
                Assert.Equal(90, Angle(Assert.Single(Mounted<SwiftImage>(header))));
                Assert.Equal("Hide this turn's steps", AutomationProperties.GetName(header));
                // :2631-2663 expanded: the whole steps in a filled card; the answer's folded copy is gone.
                Assert.Empty(Mounted<FoldedAnswerView>(block));
                var card = Assert.Single(Mounted<Border>(block), border => border.Padding == new Thickness(14));
                Assert.Equal(new CornerRadius(18), card.CornerRadius);
                Paints(card.Background, SwiftColor.Quaternary.Opacity(0.32), card, "steps card");
                // The narration and the answer are replies of the work, the tools one work group, the plan and both notices rows.
                Assert.Equal(2, Mounted<AssistantMessageRow>(card).Count());
                Assert.Single(Mounted<WorkGroup>(card));
                Assert.Single(Mounted<PlanCard>(card));
                Assert.Equal(["careful", "fyi"], Mounted<NoticeRow>(card).Select(row => Assert.Single(Mounted<SelectableTextBlock>(row)).Text));
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-059")] // TimelineRows.swift:2631-2663 the turn steps card
    [Trait("Req", "PR-CHAT-155")] // TimelineRows.swift:2580-2584, :2652-2654, :2682-2686 the heads list under the response, never among the steps
    public void The_steps_card_holds_the_rows_at_the_row_gap_with_a_row_gap_below_and_the_heads_list_sits_under_it_eight_points_apart() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var locale = SwiftFormatters.CurrentLocale.Override("en_US");
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            var turn = Guid.NewGuid();
            var content = new ValueList<TimelineEntry>(
            [
                ToolEntry(Call(ToolCall.Kind.Read, "Read a"), turn),
                ToolEntry(Call(ToolCall.Kind.Agent, "Hank: first"), turn),
                ToolEntry(Call(ToolCall.Kind.Agent, "Walter: second"), turn),
                Reply(Answer),
            ]);
            var withHeads = new TurnFinishedBlock(h.Runtime, turn, SummaryOf(turn, Files(2)), ValueList<TimelineEntry>.Empty, content, null, canUndo: false, chronological: true);
            var noResponse = new TurnFinishedBlock(h.Runtime, turn, SummaryOf(turn), ValueList<TimelineEntry>.Empty,
                new ValueList<TimelineEntry>([ToolEntry(Call(ToolCall.Kind.Read, "Read a"), turn), ToolEntry(Call(ToolCall.Kind.Command, "make"), turn)]), null, canUndo: false, chronological: true);
            var folded = new TurnFinishedBlock(h.Runtime, turn, SummaryOf(turn, Files(2)), ValueList<TimelineEntry>.Empty, content, null, canUndo: false, chronological: false);
            var panel = new StackPanel { Spacing = 40 };
            foreach (var block in new Control[] { withHeads, noResponse, folded }) panel.Children.Add(block);
            var window = Show(panel);
            try
            {
                // chronological: true opens on the full steps (the timeline itself always passes false, ThreadTimeline.swift:330).
                var card = Assert.Single(Mounted<Border>(withHeads), border => border.Padding == new Thickness(14));
                // :2632 the rows of the card at the row gap; :2658 padded 14.
                var rows = Assert.IsType<StackPanel>(card.Child);
                Assert.Equal(20, rows.Spacing);
                // :2652-2654 the heads are not among the steps: no "Sent out" badge inside the card, one work group for the read.
                Assert.DoesNotContain(Texts(card), text => text.StartsWith("Sent out", StringComparison.Ordinal));
                Assert.Single(Mounted<WorkGroup>(card));
                // :2663 a row gap of room below the card when a response or heads follow, none when nothing does.
                Assert.Equal(20, card.Margin.Bottom);
                Assert.Equal(0, Assert.Single(Mounted<Border>(noResponse), border => border.Padding == new Thickness(14)).Margin.Bottom);
                // :2683-2686 the heads list sits under the response folded or not, with one row gap before the file card.
                foreach (var block in new Control[] { withHeads, folded })
                {
                    var heads = Assert.Single(Mounted<HydraHeadsList>(block));
                    var list = Assert.IsType<StackPanel>(heads.Child);
                    Assert.Equal(8, list.Spacing); // badges, not lines of text: 8, not the row gap
                    Assert.Equal(["Sent out Hank: first", "Sent out Walter: second"], Mounted<MiddleTruncatingTextBlock>(heads).Select(text => text.FullText).ToArray());
                    Assert.Equal(20, heads.Margin.Bottom);
                    Assert.Equal(20, Top(Assert.Single(Mounted<TurnFileCard>(block)), window) - (Top(heads, window) + heads.Bounds.Height), 0.5);
                    Assert.Empty(Mounted<ToolStatusIcon>(heads)); // never a spinner or a kind glyph for a head
                    // never folded: no header, no chevron of its own among the heads.
                    Assert.DoesNotContain(Mounted<SwiftImage>(heads), image => image.SymbolName == "chevron.right");
                }
                Assert.Empty(Mounted<WorkGroup>(folded)); // the folded turn shows no steps at all
            }
            finally { Close(window); }
        });
    });
}
