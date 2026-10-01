using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792). The shared geometry (:8-28) and the turn-end line
// (:2433-2456) that the reply's hover line (:1368-1372) and the folded turn's header (:2609-2617) also draw.
[Collection("Headless")]
public sealed class TimelineRowsFinTurnEndTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-CHAT-020")] // TimelineRows.swift:8-28 enum TimelineMetrics; the rows below read them at :1467, :1480, :1484, :1553, :2398-2402
    public void The_timeline_geometry_is_eight_named_numbers_and_the_column_derives_from_them() => headless.OnUiThread(() =>
    {
        // :10 :14 :17 :18 :24 :25 :26 :27 - the eight constants, literally.
        Assert.Equal(20d, TimelineMetrics.RowSpacing);
        Assert.Equal(22d, TimelineMetrics.HoverLineHeight);
        Assert.Equal(16d, TimelineMetrics.IconWidth);
        Assert.Equal(8d, TimelineMetrics.IconSpacing);
        Assert.Equal(12d, TimelineMetrics.PillLeading);
        Assert.Equal(14d, TimelineMetrics.PillTrailing);
        Assert.Equal(8d, TimelineMetrics.PillVertical);
        Assert.Equal(18d, TimelineMetrics.PillRadius);
        // "Trailing is deliberately wider than leading".
        Assert.True(TimelineMetrics.PillTrailing > TimelineMetrics.PillLeading);

        using var h = Harness();
        h.Main.OnMain(() =>
        {
            // rowSpacing is the gap between two consecutive rows of a work group (:1467); iconWidth + iconSpacing is the
            // icon column every transcript row starts with (:1480, :1484), on the reply text's own x (no leading inset).
            var group = new WorkGroup(new ValueList<TimelineEntry>([Tool(ToolCall.Kind.Edit), Tool(ToolCall.Kind.Command)]), h.Runtime);
            var notice = new NoticeRow(MakeNotice(Notice.Level.Warning));
            var reply = new AssistantMessageRow(Reply("```\nx\n```"), Summary(Guid.NewGuid()), h.Runtime);
            var user = new UserMessageRow(UserEntry("hi", Guid.NewGuid()), h.Runtime, true);
            var panel = new StackPanel { Spacing = 40 };
            foreach (var row in new Control[] { group, notice, reply, user }) panel.Children.Add(row);
            var window = Show(panel);
            try
            {
                var stack = Assert.IsType<StackPanel>(group.Child);
                Assert.Equal(20, stack.Spacing);
                Assert.Equal(Orientation.Vertical, stack.Orientation);
                var glyph = Mounted<SwiftImage>(group).First(image => image.SymbolName == "pencil");
                Assert.Equal(16, glyph.Width);
                Assert.Equal(8, Assert.IsAssignableFrom<StackPanel>(glyph.Parent!.Parent).Spacing);
                Assert.Equal(Left(group, window), Left(glyph, window), 0.5);
                var summary = TextNamed(group, "Edited files, ran commands");
                Assert.Equal(16 + 8, Left(summary, window) - Left(group, window), 0.5);
                // pillLeading 12, pillTrailing 14, pillVertical 8, pillRadius 18 (:2398-2402).
                var pill = Mounted<Border>(notice).First(border => border.Padding == new Thickness(12, 8, 14, 8));
                Assert.Equal(new CornerRadius(18), pill.CornerRadius);
                // hoverLineHeight 22 is the room a message keeps for its hover line, taken back from its height (:160, :183, :1377, :1388).
                Assert.Equal(-(22 + 2), Assert.IsAssignableFrom<Control>(reply.Child).Margin.Bottom);
                Assert.Equal(-(22 + 6), Assert.IsAssignableFrom<Control>(user.Child).Margin.Bottom);
                Assert.Contains(Mounted<Control>(reply), control => control.Height == 22 && double.IsNaN(control.Width));
                Assert.Contains(Mounted<Control>(user), control => control.Height == 22 && double.IsNaN(control.Width));
            }
            finally { Close(window); }
        });
    });

    [Theory]
    [InlineData(TurnStatus.Completed, 64d, "Worked for 1m 4s")]
    [InlineData(TurnStatus.Running, 64d, "Worked for 1m 4s")] // a turn still marked running reads Worked for, the same as completed
    [InlineData(TurnStatus.Interrupted, 59d, "Stopped after 59s")]
    [InlineData(TurnStatus.Failed, 8100d, "Failed after 2h 15m")]
    [InlineData(TurnStatus.Completed, 0d, "Worked for 0s")]
    [InlineData(TurnStatus.Completed, -5d, "Worked for 0s")] // clamped at zero
    [InlineData(TurnStatus.Completed, 4.4d, "Worked for 4s")] // rounded to whole seconds
    [Trait("Req", "PR-CHAT-116")] // TimelineRows.swift:2448-2455 label(for:); UI/Common/Components.swift:36-41 duration
    [Trait("Req", "UI-CHAT-035")] // TimelineRows.swift:2448-2455
    [Trait("Req", "UI-RUNTIME-004")] // TimelineRows.swift:2448-2455 TurnEndRow.label(for:)
    public void The_turn_end_line_is_one_of_three_phrasings_and_a_formatted_duration(TurnStatus status, double seconds, string expected) => headless.OnUiThread(() =>
    {
        using var locale = SwiftFormatters.CurrentLocale.Override("en_US");
        var summary = Summary(Guid.NewGuid()) with { Status = status, Duration = seconds };
        Assert.Equal(expected, TurnEndRow.Label(summary));
        // The same words are the standalone row, the reply's hover line and the folded header.
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var row = new TurnEndRow(summary, hasReply: false);
            var window = Show(row);
            try { Assert.Equal(expected, Assert.Single(Texts(row))); }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-116")] // TimelineRows.swift:2439-2445 the row renders only when the turn produced no reply
    [Trait("Req", "UI-CHAT-035")] // TimelineRows.swift:2433-2446
    [Trait("Req", "UI-RUNTIME-004")] // TimelineRows.swift:2439-2445 (caption, Chrome.secondaryText, full width lead-aligned, no padding)
    public void The_standalone_turn_end_row_is_caption_secondary_full_width_leading_and_hidden_after_a_reply() => headless.OnUiThread(() =>
    {
        using var locale = SwiftFormatters.CurrentLocale.Override("en_US");
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var summary = Summary(Guid.NewGuid()) with { Duration = 64 };
            var silent = new TurnEndRow(summary, hasReply: false);
            var replied = new TurnEndRow(summary, hasReply: true);
            var host = new StackPanel { Width = 400 };
            host.Children.Add(silent);
            host.Children.Add(replied);
            var window = Show(host);
            try
            {
                var line = TextNamed(silent, "Worked for 1m 4s");
                Face(line, 10, FontWeight.Normal, ".chat(.caption)"); // :2442; ED-059 caption 10 pt
                Paints(line.Foreground, ChromeTokens.PrimaryText.Opacity(0.7), line, "Chrome.secondaryText = primaryText at 0.7 (:2443)");
                Paints(line.Foreground, ChromeTokens.SecondaryText, line);
                // :2444 .frame(maxWidth: .infinity, alignment: .leading): as wide as the column, text on its leading edge, no padding.
                Assert.Equal(400, line.Bounds.Width);
                Assert.Equal(TextAlignment.Start, line.TextAlignment);
                Assert.Equal(default, line.Margin);
                Assert.Equal(default, line.Padding);
                Assert.Equal(0, Left(line, window) - Left(host, window), 0.5);
                // :2440 `if !hasReply`: with a reply the duration lives on the reply's hover line and the row draws nothing.
                Assert.Empty(Mounted<TextBlock>(replied));
                Assert.Equal(0, replied.Bounds.Height);
            }
            finally { Close(window); }
        });
    });
}
