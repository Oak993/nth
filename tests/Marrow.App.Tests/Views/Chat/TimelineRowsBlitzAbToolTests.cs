using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), the tool rows: :1446-1634 WorkGroup, WorkSteps and the group summary,
// :1636-1853 ToolRow, :2067-2092 ToolStatusIcon, :2184-2233 ToolPresentation. Where a record's capture comparison asks for a frame the
// reference videos do not contain, the Swift source is the proof (OD-0098): the states the code names are mounted and rendered here,
// and the values the code states (glyphs, colours, sizes, gaps, text) are read back from the layout and from the rendered frame.
[Collection("Headless")]
public sealed class TimelineRowsBlitzAbToolTests(HeadlessSession headless)
{
    private static readonly (ThemeVariant Theme, Color Backdrop)[] Themes =
    [
        (ThemeVariant.Dark, Color.FromRgb(24, 24, 27)),
        (ThemeVariant.Light, Color.FromRgb(244, 244, 246)),
    ];

    private static readonly (ToolCall.Kind Kind, string Symbol)[] Kinds =
    [
        (ToolCall.Kind.Command, "terminal"), (ToolCall.Kind.Read, "doc.text"), (ToolCall.Kind.Edit, "pencil"), (ToolCall.Kind.Search, "magnifyingglass"),
        (ToolCall.Kind.Web, "globe"), (ToolCall.Kind.Mcp, "puzzlepiece.extension"), (ToolCall.Kind.Agent, "person.2"), (ToolCall.Kind.Other, "wrench.and.screwdriver"),
    ];

    private static string Signature(bool[,] mask)
    {
        var bits = new System.Text.StringBuilder().Append(mask.GetLength(0)).Append('x').Append(mask.GetLength(1)).Append(':');
        for (var y = 0; y < mask.GetLength(1); y++)
            for (var x = 0; x < mask.GetLength(0); x++) bits.Append(mask[x, y] ? '#' : '.');
        return bits.ToString();
    }

    private static void Frames(TestRuntime main, ManualFrameSource frames, double milliseconds)
    {
        frames.Advance(TimeSpan.Zero);
        main.AdvanceMilliseconds(milliseconds);
        frames.Advance(TimeSpan.FromMilliseconds(milliseconds));
    }

    // ---- PR-CHAT-094: the eight glyphs, drawn ----

    [Fact]
    [Trait("Req", "PR-CHAT-094")] // TimelineRows.swift:2222-2233 ToolPresentation.symbol; :2085 the row's icon; :1481 the group's glyph via :1619; OD-0098
    public void Each_kind_draws_its_own_mark_as_its_groups_summary_glyph_in_both_themes_and_the_eight_marks_all_differ_while_the_rows_icon_is_the_done_tick() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            foreach (var (theme, backdrop) in Themes)
            {
                var marks = new List<string>();
                foreach (var (kind, symbol) in Kinds)
                {
                    // The row's own icon is the ToolStatusIcon of a completed call (:2084-2086, :1784), which OD-0136 amendment 2 (step 8) makes the done tick, the same for every kind;
                    // the summary glyph is the first image of the header of a group whose calls are all of this kind (:1612-1620).
                    var icon = new ToolStatusIcon(Call(kind, "x"));
                    var group = new WorkGroup(new ValueList<TimelineEntry>([Tool(kind), Tool(kind)]), h.Runtime);
                    var panel = new StackPanel { Spacing = 24, Margin = new Thickness(24) };
                    panel.Children.Add(icon);
                    panel.Children.Add(group);
                    BlitzAbRender.Render(panel, theme, backdrop, 320, 200, (window, frame) =>
                    {
                        // The row's icon: the nth status slot, done, drawn as the tick (a composition visual, so its pixels are the primitives' own tests'); no kind glyph.
                        Assert.Empty(Mounted<SwiftImage>(icon));
                        var tick = Assert.Single(Mounted<Marrow.UI.Port.Nth.NthStatusMark>(icon));
                        Assert.Equal(Marrow.UI.Port.Nth.NthStatus.Done, tick.Status);
                        Assert.Equal(Marrow.UI.Port.Nth.NthDoneMark.Tick, tick.DoneMark);
                        var groupGlyph = Mounted<SwiftImage>(group).First();
                        Assert.Equal(symbol, groupGlyph.SymbolName);
                        var groupArea = BlitzAbRender.BoxIn(groupGlyph, window);
                        var groupMask = frame.Mask(groupArea, backdrop) ?? throw new Xunit.Sdk.XunitException($"{kind} in {theme}: the group's glyph drew nothing");
                        // Chrome.secondaryText (:1483): the ink is the theme's secondary label colour at any coverage.
                        BlitzAbRender.InkIsColour(frame, groupArea, backdrop, ChromeTokens.SecondaryText.Resolve(theme), $"{kind} group glyph in {theme}");
                        marks.Add(Signature(groupMask));
                    });
                }
                // A kind always draws its own mark: no two of the eight read alike.
                Assert.Equal(8, marks.Distinct().Count());
            }
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-094")] // TimelineRows.swift:2222-2233, :1784, :1612-1620: through the real ToolRow, and the group's mixed kinds
    public void A_tool_row_wears_the_done_tick_whichever_way_the_call_is_made_and_a_group_takes_the_first_kind_of_the_fixed_order() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            // The six kinds whose row is the plain line (an agent's row is the head badge, an MCP call of a catalog server the MCP badge).
            foreach (var (kind, symbol) in Kinds.Where(item => item.Kind != ToolCall.Kind.Agent))
            {
                var row = new ToolRow(ToolEntry(Call(kind, "something")), h.Runtime);
                var window = Show(row);
                // OD-0136 amendment 2, step 8: the kind's glyph is no longer the row's done look; the tick is, for every kind (the group still names the kind: below).
                try { Assert.Equal(Marrow.UI.Port.Nth.NthStatus.Done, Assert.Single(Mounted<Marrow.UI.Port.Nth.NthStatusMark>(Assert.Single(Mounted<ToolStatusIcon>(row)))).Status); Assert.Empty(Mounted<SwiftImage>(Assert.Single(Mounted<ToolStatusIcon>(row)))); }
                finally { Close(window); }
            }
            // :1588 the order: edit, read, command, search, web, mcp, agent, other, whatever the order of the calls.
            var fixedOrder = new[] { ToolCall.Kind.Edit, ToolCall.Kind.Read, ToolCall.Kind.Command, ToolCall.Kind.Search, ToolCall.Kind.Web, ToolCall.Kind.Mcp, ToolCall.Kind.Agent, ToolCall.Kind.Other };
            for (var skip = 0; skip < fixedOrder.Length; skip++)
            {
                var entries = fixedOrder.Skip(skip).Reverse().Select(kind => Tool(kind)).ToArray();
                Assert.Equal(Kinds.Single(item => item.Kind == fixedOrder[skip]).Symbol, WorkGroupSummary.Symbol(entries));
            }
        });
    });

    // ---- PR-CHAT-098: the chevron, rendered ----

    [Fact]
    [Trait("Req", "PR-CHAT-098")] // TimelineRows.swift:1801-1810 and :1809 rotationEffect; :1801-1804 down for a popover; OD-0098
    public void The_in_place_chevron_is_drawn_pointing_right_then_turns_to_the_down_chevron_a_popover_row_draws_and_both_are_secondary_ink_in_both_themes() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var manual = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(manual);
        foreach (var (theme, backdrop) in Themes)
        {
            ToolRow? output = null, popover = null;
            AvaloniaWindow? window = null;
            (int Width, int Height) right = default, down = default;
            h.Main.OnMain(() =>
            {
                output = new ToolRow(ToolEntry(Call(ToolCall.Kind.Command, "npm test") with { Output = "a\nb" }), h.Runtime);
                var edits = new ValueList<FileEdit>([new FileEdit("src/a.swift", "@@ -1 +1 @@\n-a\n+b\n", 1, 1)]);
                popover = new ToolRow(ToolEntry(Call(ToolCall.Kind.Edit, "Edit a.swift") with { Edits = edits }), h.Runtime);
                var panel = new StackPanel { Spacing = 24, Margin = new Thickness(24) };
                panel.Children.Add(output);
                panel.Children.Add(popover);
                var host = new Border { Child = panel };
                Marrow.App.Port.AppEnvironment.SetAppModel(host, h.App);
                window = BlitzAbRender.Open(host, theme, backdrop, 420, 200);
                var frame = BlitzAbRender.Capture(window);
                var rightChevron = Assert.Single(Mounted<SwiftImage>(output), image => image.SymbolName == "chevron.right");
                var downChevron = Assert.Single(Mounted<SwiftImage>(popover), image => image.SymbolName == "chevron.down");
                var rightArea = BlitzAbRender.BoxIn(rightChevron, window);
                var downArea = BlitzAbRender.BoxIn(downChevron, window);
                var rightInk = frame.Ink(rightArea, backdrop) ?? throw new Xunit.Sdk.XunitException("the right chevron drew nothing");
                var downInk = frame.Ink(downArea, backdrop) ?? throw new Xunit.Sdk.XunitException("the down chevron drew nothing");
                right = (rightInk.Right - rightInk.Left + 1, rightInk.Bottom - rightInk.Top + 1);
                down = (downInk.Right - downInk.Left + 1, downInk.Bottom - downInk.Top + 1);
                // ">" is taller than it is wide, "v" is wider than it is tall.
                Assert.True(right.Height > right.Width, $"the in-place chevron is {right} in {theme}: not pointing right");
                Assert.True(down.Width > down.Height, $"the popover chevron is {down} in {theme}: not pointing down");
                BlitzAbRender.InkIsColour(frame, rightArea, backdrop, ChromeTokens.SecondaryText.Resolve(theme), $"the right chevron in {theme}");
                BlitzAbRender.InkIsColour(frame, downArea, backdrop, ChromeTokens.SecondaryText.Resolve(theme), $"the down chevron in {theme}");
                // The popover row's chevron never turns (:1801-1804 has no rotationEffect).
                Assert.Null(downChevron.RenderTransform);
                Click(window, Assert.Single(Mounted<Button>(output)));
            });
            Frames(h.Main, manual, 480); // .snappy(duration: 0.24) settles well inside it
            h.Main.OnMain(() =>
            {
                try
                {
                    var frame = BlitzAbRender.Capture(window!);
                    var rightChevron = Assert.Single(Mounted<SwiftImage>(output!), image => image.SymbolName == "chevron.right");
                    Assert.Equal(90, Assert.IsType<RotateTransform>(rightChevron.RenderTransform).Angle); // :1809 .rotationEffect(.degrees(isExpanded ? 90 : 0))
                    var area = BlitzAbRender.BoxIn(rightChevron, window!, 3);
                    var turned = frame.Ink(area, backdrop) ?? throw new Xunit.Sdk.XunitException("the turned chevron drew nothing");
                    var turnedSize = (Width: turned.Right - turned.Left + 1, Height: turned.Bottom - turned.Top + 1);
                    // Turned a quarter turn the right chevron is the down chevron: wider than tall, the size the popover row's is, a pixel of anti-aliasing apart.
                    Assert.True(turnedSize.Width > turnedSize.Height, $"the open chevron is {turnedSize} in {theme}: not pointing down");
                    Assert.InRange(turnedSize.Width - down.Width, -1, 1);
                    Assert.InRange(turnedSize.Height - down.Height, -1, 1);
                    BlitzAbRender.InkIsColour(frame, area, backdrop, ChromeTokens.SecondaryText.Resolve(theme), $"the open chevron in {theme}");
                }
                finally { BlitzAbRender.Shut(window!); }
            });
        }
    });

    // ---- PR-CHAT-099: the four states, drawn, and a call that changes status ----

    [Fact]
    [Trait("Req", "PR-CHAT-099")] // TimelineRows.swift:2072-2091 the four states in the theme's own colours; :1784 the photo override; OD-0098 (the spinner's own size is the mini control's, OWNER)
    public void The_failed_cross_and_the_raised_hand_are_drawn_in_the_themes_danger_and_warning_ink_in_a_sixteen_point_column_in_both_themes() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            foreach (var (theme, backdrop) in Themes)
            {
                var states = new (ToolCall.Status Status, SwiftColor Colour, string Symbol)[]
                {
                    (ToolCall.Status.Failed, ChromeTokens.Danger, "xmark.circle.fill"),
                    (ToolCall.Status.Declined, ChromeTokens.Warning, "hand.raised.slash"),
                };
                var icons = states.Select(state => new ToolStatusIcon(Call(ToolCall.Kind.Read, "x", state.Status))).ToArray();
                var panel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 24, Margin = new Thickness(24) };
                foreach (var icon in icons) panel.Children.Add(icon);
                BlitzAbRender.Render(panel, theme, backdrop, 260, 80, (window, frame) =>
                {
                    for (var index = 0; index < states.Length; index++)
                    {
                        var glyph = Assert.Single(Mounted<SwiftImage>(icons[index]));
                        Assert.Equal(states[index].Symbol, glyph.SymbolName);
                        Assert.Equal(16, Assert.IsType<Border>(icons[index].Child).Bounds.Width); // the fixed icon column (:2090)
                        var area = BlitzAbRender.BoxIn(glyph, window);
                        Assert.NotNull(frame.Ink(area, backdrop));
                        // The theme's own status hue, not the system's red and orange (chrome-design-tokens 6.1).
                        var strongest = BlitzAbRender.InkIsColour(frame, area, backdrop, states[index].Colour.Resolve(theme), $"{states[index].Status} in {theme}");
                        Assert.True(strongest > 0.4, $"{states[index].Status} in {theme}: no stroke is drawn in its colour");
                    }
                });
            }
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-099")] // TimelineRows.swift:2077 ProgressView().controlSize(.mini) in the 16 pt column (:2090); 2-chat-use.mov 145.0-145.6 s, crop 1894,1148,64x64: 10x10 px (the same control, UsageViews.swift:22)
    public void The_running_icon_is_the_nth_status_slot_with_its_14_px_spinner_centred_in_the_sixteen_point_column_in_both_themes() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            foreach (var (theme, backdrop) in Themes)
            {
                var icon = new ToolStatusIcon(Call(ToolCall.Kind.Command, "make", ToolCall.Status.Running));
                var panel = new StackPanel { Margin = new Thickness(24) };
                panel.Children.Add(icon);
                BlitzAbRender.Render(panel, theme, backdrop, 120, 80, (window, frame) =>
                {
                    var column = BlitzAbRender.BoxIn(Assert.IsType<Border>(icon.Child), window);
                    // OD-0136 amendment 2, step 8: the 10 point mini spinner with eight spokes (2-chat-use.mov 145.0-145.6 s) gives way to the nth 16 px status slot, whose spinner is 14 px
                    // (a composition visual: its pixels are the primitives' own tests').
                    Assert.Empty(Mounted<ProgressBar>(icon));
                    var mark = Assert.Single(Mounted<Marrow.UI.Port.Nth.NthStatusMark>(icon));
                    var box = BlitzAbRender.BoxIn(mark, window);
                    Assert.Equal(16, column.Width); // the fixed icon column (:2090)
                    Assert.Equal(Marrow.UI.Port.Nth.NthStatus.Working, mark.Status);
                    Assert.Equal(14, mark.MarkSize);
                    Assert.Equal(16, box.Width);
                    Assert.Equal(16, box.Height);
                    Assert.Equal(column.Center.X, box.Center.X, 0.5); // centred in the column
                    Assert.Equal(column.Center.Y, box.Center.Y, 0.5);
                });
            }
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-099")] // TimelineRows.swift:2072-2091 and :1658-1660: the status changes while the row is on screen; :1784 the photo override
    public void A_row_whose_call_changes_status_swaps_its_icon_in_place_from_the_spinner_to_the_cross_the_hand_and_the_tick() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        TimelineEntry entry = null!;
        ToolRow row = null!;
        AvaloniaWindow window = null!;
        string Shown()
        {
            var icon = Assert.Single(Mounted<ToolStatusIcon>(row));
            if (Mounted<Marrow.UI.Port.Nth.NthStatusMark>(icon).SingleOrDefault() is { } mark) return mark.Status == Marrow.UI.Port.Nth.NthStatus.Working ? "spinner" : "tick"; // step 8: the nth status slot
            return Assert.Single(Mounted<SwiftImage>(icon)).SymbolName ?? "";
        }
        h.Main.OnMain(() =>
        {
            entry = ToolEntry(Call(ToolCall.Kind.Search, "grep foo", ToolCall.Status.Running));
            row = new ToolRow(entry, h.Runtime);
            window = Show(row);
            Assert.Equal("spinner", Shown());
        });
        try
        {
            foreach (var (status, expected) in new[] { (ToolCall.Status.Failed, "xmark.circle.fill"), (ToolCall.Status.Declined, "hand.raised.slash"),
                                                       (ToolCall.Status.Running, "spinner"), (ToolCall.Status.Completed, "tick") })
            {
                h.Main.OnMain(() => entry.Item = entry.Item with { TimelineItemContent = new TimelineItem.Content.Tool(Call(ToolCall.Kind.Search, "grep foo", status)) });
                h.Main.OnMain(() =>
                {
                    Settle(window);
                    Assert.Equal(expected, Shown());
                    Assert.Equal(16, Assert.IsType<Border>(Assert.Single(Mounted<ToolStatusIcon>(row)).Child).Bounds.Width);
                });
            }
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    // ---- PR-CHAT-100: the outcome at the trailing edge ----

    [Theory]
    [Trait("Req", "PR-CHAT-100")] // TimelineRows.swift:1833-1836 "exit \(exitCode)" at caption with monospaced digits in Chrome.danger; :1832 the spacer; :1844 12 trailing
    [InlineData(1L)]
    [InlineData(127L)]
    [InlineData(255L)]
    [InlineData(-1L)]
    [InlineData(0L)]
    [InlineData(1234567890123L)]
    public void A_failed_call_shows_exit_a_space_and_the_integer_with_tabular_figures_in_the_danger_colour_at_the_rows_trailing_edge(long code) => headless.OnUiThread(() =>
    {
        using var h = Harness();
        h.Main.OnMain(() =>
        {
            foreach (var (theme, backdrop) in Themes)
            {
                var row = new ToolRow(ToolEntry(Call(ToolCall.Kind.Command, "make", ToolCall.Status.Failed) with { ExitCode = code }), h.Runtime);
                var host = new Border { Width = 480, Child = row, Margin = new Thickness(0, 12) };
                BlitzAbRender.Render(host, theme, backdrop, 480, 60, (window, frame) =>
                {
                    var exit = TextNamed(row, "exit " + code.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    // :1835 .font(.chat(.caption).monospacedDigit()): tabular numerals on the caption face.
                    Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Caption).MonospacedDigit().Resolve().FontFeatures, exit.FontFeatures);
                    Assert.Equal(10, exit.FontSize);
                    Paints(exit.Foreground, ChromeTokens.Danger, exit, "the exit code");
                    // At the row's trailing edge, 12 points in (:1844), on the line's centre with the label.
                    Assert.Equal(Left(row, window) + 480 - 12, Left(exit, window) + exit.Bounds.Width, 0.5);
                    OnOneCentreLine(window, Mounted<ToolStatusIcon>(row).Single(), Mounted<MiddleTruncatingTextBlock>(row).Single(), exit);
                    var area = BlitzAbRender.BoxIn(exit, window);
                    Assert.NotNull(frame.Ink(area, backdrop));
                    BlitzAbRender.TextIsColour(frame, area, backdrop, ChromeTokens.Danger.Resolve(theme), $"the exit code in {theme}");
                });
            }
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-100")] // TimelineRows.swift:1837-1840: the literal Declined at caption size in Chrome.warning, at the trailing edge
    public void Declined_reads_exactly_Declined_in_the_warning_colour_at_the_trailing_edge_and_a_declined_call_never_reads_an_exit_code() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        h.Main.OnMain(() =>
        {
            foreach (var (theme, backdrop) in Themes)
            {
                var declined = new ToolRow(ToolEntry(Call(ToolCall.Kind.Command, "rm -rf /", ToolCall.Status.Declined)), h.Runtime);
                var declinedWithCode = new ToolRow(ToolEntry(Call(ToolCall.Kind.Command, "rm", ToolCall.Status.Declined) with { ExitCode = 5 }), h.Runtime);
                var panel = new StackPanel { Width = 480, Spacing = 16, Margin = new Thickness(0, 12) };
                panel.Children.Add(declined);
                panel.Children.Add(declinedWithCode);
                BlitzAbRender.Render(panel, theme, backdrop, 480, 120, (window, frame) =>
                {
                    foreach (var row in new[] { declined, declinedWithCode })
                    {
                        var word = TextNamed(row, "Declined");
                        Paints(word.Foreground, ChromeTokens.Warning, word, "Declined");
                        Assert.Equal(10, word.FontSize);
                        Assert.Empty(word.FontFeatures ?? []); // :1839 plain caption: no tabular figures on the word
                        Assert.Equal(Left(row, window) + 480 - 12, Left(word, window) + word.Bounds.Width, 0.5);
                        var area = BlitzAbRender.BoxIn(word, window);
                        BlitzAbRender.TextIsColour(frame, area, backdrop, ChromeTokens.Warning.Resolve(theme), $"Declined in {theme}");
                        Assert.DoesNotContain(Texts(row), text => text.StartsWith("exit", StringComparison.Ordinal)); // a declined call never reads an exit code
                    }
                });
            }
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-100")] // TimelineRows.swift:1833-1841: a failed call with no exit code, a completed one and a running one show neither the code nor Declined
    public void A_failed_call_with_no_code_and_a_completed_or_running_one_with_a_code_show_neither_an_exit_code_nor_Declined() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        h.Main.OnMain(() =>
        {
            var failedNoCode = new ToolRow(ToolEntry(Call(ToolCall.Kind.Command, "make", ToolCall.Status.Failed)), h.Runtime);
            var completedWithCode = new ToolRow(ToolEntry(Call(ToolCall.Kind.Command, "make") with { ExitCode = 3 }), h.Runtime);
            var runningWithCode = new ToolRow(ToolEntry(Call(ToolCall.Kind.Command, "make", ToolCall.Status.Running) with { ExitCode = 3 }), h.Runtime);
            var panel = new StackPanel { Width = 480, Spacing = 16 };
            foreach (var row in new[] { failedNoCode, completedWithCode, runningWithCode }) panel.Children.Add(row);
            var window = Show(panel);
            try
            {
                foreach (var row in new[] { failedNoCode, completedWithCode, runningWithCode })
                    Assert.DoesNotContain(Texts(row), text => text.StartsWith("exit", StringComparison.Ordinal) || text == "Declined");
            }
            finally { Close(window); }
        });
    });
}
