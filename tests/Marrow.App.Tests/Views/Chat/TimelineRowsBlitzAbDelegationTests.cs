using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Runtime;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Markdown;
using Avalonia.VisualTree;
using AvaloniaWindow = Avalonia.Controls.Window;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), :648-783 HydraDelegationBlock. Where a record's capture comparison asks for a frame the
// reference videos do not contain, the Swift source is the proof (OD-0098): the card's five states are mounted, rendered headless in both themes and read back
// against the values the code states. The card is drawn in a .continuous rounded rectangle, which ED-181 still draws with circular arcs: its corner is the one
// thing these renders do not claim.
[Collection("Headless")]
public sealed class TimelineRowsBlitzAbDelegationTests(HeadlessSession headless)
{
    private static readonly (ThemeVariant Theme, Color Backdrop)[] Themes =
    [
        (ThemeVariant.Dark, Color.FromRgb(24, 24, 27)),
        (ThemeVariant.Light, Color.FromRgb(244, 244, 246)),
    ];

    // All the ink in the area: the summed distance of its pixels from the backdrop.
    private static double Mass(BlitzAbFrame frame, Rect area, Color backdrop)
    {
        var sum = 0.0;
        for (var x = (int)Math.Floor(area.Left); x < (int)Math.Ceiling(area.Right); x++)
            for (var y = (int)Math.Floor(area.Top); y < (int)Math.Ceiling(area.Bottom); y++)
                sum += BlitzAbFrame.Distance(frame.At(x, y), backdrop);
        return sum;
    }

    // The same words in the same font drawn alone in the ink the source gives them, in a window of their own.
    private static void IsInkedLike(BlitzAbFrame frame, Rect area, Color backdrop, string text, SwiftFont font, SwiftColor ink, ThemeVariant theme, string because, double? cutAt = null)
    {
        var label = new TextBlock { Text = text };
        font.Apply(label);
        label.Foreground = ink.Brush(label);
        if (cutAt is { } width) { label.Width = width; label.MaxLines = 1; label.TextTrimming = TextTrimming.CharacterEllipsis; }
        var reference = 0.0;
        BlitzAbRender.Render(new Border { Padding = new Thickness(8), Child = label }, theme, backdrop, 480, 60, (window, shot) => reference = Mass(shot, BlitzAbRender.BoxIn(label, window).Inflate(1), backdrop));
        Assert.True(reference > 300, $"{because}: the reference line is barely inked ({reference})");
        Assert.InRange(Mass(frame, area.Inflate(1), backdrop) / reference, 0.92, 1.08);
    }

    private static void SymbolInkedLike(BlitzAbFrame frame, SwiftImage actual, Rect area, Color backdrop, SwiftColor ink, ThemeVariant theme, string because)
    {
        var reference = SwiftImage.SystemName(actual.SymbolName!);
        if (SwiftImage.GetFont(actual) is { } font) reference.Font(font);
        reference.Foreground = ink.Brush(reference);
        var mass = 0.0;
        BlitzAbRender.Render(new Border { Padding = new Thickness(8), Child = reference }, theme, backdrop, 120, 60, (window, shot) => mass = Mass(shot, BlitzAbRender.BoxIn(reference, window).Inflate(2), backdrop));
        Assert.True(mass > 150, $"{because}: the reference symbol is barely inked ({mass})");
        Assert.InRange(Mass(frame, area.Inflate(2), backdrop) / mass, 0.9, 1.1);
    }

    private const string Hank = "{\"task\": \"Tidy the popover\", \"prompt\": \"Tidy it.\", \"name\": \"Hank\"}";
    private const string Walter = "{\"task\": \"Fix the merge pill\", \"prompt\": \"Fix it.\", \"name\": \"Walter\", \"project\": \"/Users/me/code/website\"}";

    [Fact]
    [Trait("Req", "PR-CHAT-149")] // TimelineRows.swift:652-707, :711-741, :745-753; OD-0098: the card at each of its states drawn in both themes, never the JSON
    public void The_delegation_card_is_drawn_streaming_with_nothing_closed_with_one_head_closed_complete_with_one_or_two_heads_and_sent_and_never_shows_the_json_in_both_themes() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        foreach (var (theme, backdrop) in Themes)
        {
            var fill = BlitzAbFrame.Over(ChromeTokens.Accent.Opacity(0.1).Resolve(theme), backdrop);
            var cases = new (string Name, string Json, bool Sent, string Title, string[] Tasks, string? Project, bool Writing)[]
            {
                ("nothing closed yet", "[{\"task\": \"Tid", false, "Summoning heads", [], null, false),
                ("one head's brief closed, the next streaming", "[" + Hank + ", {\"task\": \"Fix the me", false, "Summoning heads", ["Tidy the popover"], null, true),
                ("the block closed with two heads", "[" + Hank + ", " + Walter + "]", false, "Summoning 2 heads", ["Tidy the popover", "Fix the merge pill"], "website", false),
                ("the block closed with one head", "[" + Hank + "]", false, "Summoning Hank", ["Tidy the popover"], null, false),
                ("sent", "[" + Hank + "]", true, "Summoned Hank", ["Tidy the popover"], null, false),
            };
            h.Main.OnMain(() =>
            {
                var blocks = cases.Select(item => new HydraDelegationBlock(item.Json, item.Sent)).ToList();
                var column = new StackPanel { Spacing = 20 };
                foreach (var block in blocks) column.Children.Add(block);
                BlitzAbRender.Render(new Border { Padding = new Thickness(24) , Child = column }, theme, backdrop, 560, 640, (window, frame) =>
                {
                    for (var i = 0; i < cases.Length; i++)
                    {
                        var (name, json, sent, title, tasks, project, writing) = cases[i];
                        var block = blocks[i];
                        var because = $"{theme}: {name}";
                        // :1012-1019 the standard pill: a tint at 0.1 in a pill-radius shape, hugging its contents from the leading edge.
                        var pill = Mounted<Border>(block).First(border => border.Padding == new Thickness(12, 8, 14, 8));
                        var box = BlitzAbRender.BoxIn(pill, window);
                        Assert.Equal(24, box.Left, 0.5);
                        Assert.True(BlitzAbFrame.Distance(frame.At((int)box.Left + 5, (int)(box.Top + box.Height / 2)), fill) <= 6, $"{because}: the card's fill");
                        Assert.True(BlitzAbFrame.Distance(frame.At((int)box.Right + 4, (int)(box.Top + box.Height / 2)), backdrop) <= 6, $"{because}: the card ends where it measures");
                        // The JSON is never shown: no text of the card holds a brace, a quote or the prompt's words.
                        Assert.DoesNotContain(Texts(block), text => text.Contains('{', StringComparison.Ordinal) || text.Contains('"', StringComparison.Ordinal) || text.Contains("Tidy it.", StringComparison.Ordinal));
                        // :657-666 the mark at 16 by 16 and the title at callout medium, 8 apart on one centre line.
                        var header = Texts(block).First();
                        Assert.Equal(title, header);
                        var titleText = TextNamed(block, title);
                        var titleBox = BlitzAbRender.BoxIn(titleText, window);
                        Assert.Equal(box.Left + 12 + 16 + 8, titleBox.Left, 0.5);
                        Assert.Equal(box.Top + 8 + 8, titleBox.Center.Y, 1.5); // the header's 16-point line, its words centred in it
                        IsInkedLike(frame, titleBox, fill, title, ChatFont.Chat(SwiftFont.TextStyle.Callout, SwiftFont.Weight.Medium), SwiftColor.Primary, theme, $"{because}: the title");
                        var marks = Mounted<HydraMarkImage>(block).ToList();
                        var mark = marks[0];
                        Assert.Equal(16, BlitzAbRender.BoxIn(mark, window).Width, 0.5);
                        Assert.Equal(box.Left + 12, BlitzAbRender.BoxIn(mark, window).Left, 0.5);
                        // :667-675 once sent, a green check follows the title, labelled "sent"; before that none.
                        var checks = Mounted<SwiftImage>(block).Where(image => image.SymbolName == "checkmark.circle.fill").ToList();
                        if (sent)
                        {
                            var check = Assert.Single(checks);
                            Assert.Equal("sent", AutomationProperties.GetName(check));
                            SymbolInkedLike(frame, check, BlitzAbRender.BoxIn(check, window), fill, SwiftColor.Rgb(0x34C759), theme, $"{because}: the check");
                            BlitzAbRender.InkIsColour(frame, BlitzAbRender.BoxIn(check, window), fill, SwiftColor.Rgb(0x34C759).Resolve(theme), $"{because}: the check is green", most: 1.6);
                            Assert.Single(Mounted<DotFieldSweep>(block)); // :694-701 one wave through the card, once sent
                        }
                        else
                        {
                            Assert.Empty(checks);
                            Assert.Empty(Mounted<DotFieldSweep>(block));
                        }
                        // :677-692 one row per head under the header 6 down, 5 apart: the head's own glyph (16) or the mark, its task at caption in the primary ink at 0.75.
                        var rowTops = new List<double>();
                        foreach (var task in tasks)
                        {
                            var taskText = TextNamed(block, task);
                            var taskBox = BlitzAbRender.BoxIn(taskText, window);
                            Assert.Equal(box.Left + 12 + 16 + 8, taskBox.Left, 0.5);
                            IsInkedLike(frame, taskBox, fill, task, ChatFont.Chat(SwiftFont.TextStyle.Caption), ChromeTokens.PrimaryText.Opacity(0.75), theme, $"{because}: {task}");
                            rowTops.Add(taskBox.Center.Y);
                        }
                        var glyphs = Mounted<HydraGlyph>(block).ToList();
                        Assert.Equal(tasks.Length, glyphs.Count);
                        if (glyphs.Count > 0)
                        {
                            Assert.All(glyphs, glyph => Assert.Equal(16, BlitzAbRender.BoxIn(glyph, window).Width, 0.5));
                            Assert.Equal(box.Left + 12, BlitzAbRender.BoxIn(glyphs[0], window).Left, 0.5); // each glyph in the mark's column
                            Assert.Equal(6 + (17 - 16) / 2.0, BlitzAbRender.BoxIn(glyphs[0], window).Top - BlitzAbRender.BoxIn(marks[0], window).Bottom, 0.75); // :658 the card's spacing 6 under the header line, the callout's 17 (ED-290 (1)), in which the 16-point mark is centred (:659-663)
                        }
                        if (glyphs.Count > 1) Assert.Equal(5, BlitzAbRender.BoxIn(glyphs[1], window).Top - BlitzAbRender.BoxIn(glyphs[0], window).Bottom, 0.75); // :677 the heads' spacing 5
                        // :720-732 a brief for another project: an arrow and the project's name at caption2 medium in the secondary ink, 2 more points in.
                        var tag = Mounted<StackPanel>(block).Where(panel => AutomationProperties.GetName(panel) is { } label && label.StartsWith("in ", StringComparison.Ordinal)).ToList();
                        if (project is null) Assert.Empty(tag);
                        else
                        {
                            var found = Assert.Single(tag);
                            Assert.Equal("in " + project, AutomationProperties.GetName(found));
                            var tagText = TextNamed(found, project);
                            IsInkedLike(frame, BlitzAbRender.BoxIn(tagText, window), fill, project, ChatFont.Chat(SwiftFont.TextStyle.Caption2, SwiftFont.Weight.Medium), ChromeTokens.SecondaryText, theme, $"{because}: the project");
                            Assert.Contains(Mounted<SwiftImage>(found), image => image.SymbolName == "arrow.right");
                            // :721-736 after the task by the row's 8 and 2 points of its own leading padding; the arrow and the name 3 apart.
                            Assert.Equal(BlitzAbRender.BoxIn(TextNamed(block, tasks[1]), window).Right + 8 + 2, BlitzAbRender.BoxIn(found, window).Left, 1.0);
                        }
                        // :677-692 while a brief is still streaming a line says so at caption in the secondary ink, in line with the tasks (a 16-point clear column before it).
                        var next = Texts(block).Where(text => text == "Writing the next brief…").ToList();
                        if (writing)
                        {
                            Assert.Single(next);
                            var nextBox = BlitzAbRender.BoxIn(TextNamed(block, "Writing the next brief…"), window);
                            Assert.Equal(box.Left + 12 + 16 + 8, nextBox.Left, 0.5);
                            IsInkedLike(frame, nextBox, fill, "Writing the next brief…", ChatFont.Chat(SwiftFont.TextStyle.Caption), ChromeTokens.SecondaryText, theme, $"{because}: the next brief");
                        }
                        else Assert.Empty(next);
                    }
                });
            });
        }
    });

    private static void Frames(TestRuntime main, ManualFrameSource frames, double milliseconds)
    {
        frames.Advance(TimeSpan.Zero);
        for (var elapsed = 0.0; elapsed < milliseconds; elapsed += 10)
        {
            main.AdvanceMilliseconds(10);
            frames.Advance(TimeSpan.FromMilliseconds(10));
        }
    }

    [Fact]
    [Trait("Req", "PR-CHAT-149")] // TimelineRows.swift:703-704 `.animation(Chrome.panelSlide, value: sent)` and `value: delegations.count`; :1364: the card keeps its identity across the streaming flushes
    public void The_card_is_the_same_card_for_every_flush_of_the_streaming_reply_and_its_height_its_new_rows_and_its_check_arrive_by_animation() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var clock = new ManualFrameSource();
        using var clockSeam = FrameClock.Source.Override(clock);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        var previous = MarkdownDelegationCard.Render;
        MarkdownDelegationCard.Render = (json, sent) => new HydraDelegationBlock(json, sent); // AppDelegate.cs:50 (:14)
        AvaloniaWindow window = null!;
        const string hank = "{\"task\": \"Tidy the popover\", \"prompt\": \"Tidy it.\", \"name\": \"Hank\"}";
        const string walter = "{\"task\": \"Write the tests\", \"prompt\": \"Write them.\", \"name\": \"Walter\"}";
        void Flush(TimelineEntry entry, string text, bool streaming) => h.Main.OnMain(() =>
            entry.Item = entry.Item with { TimelineItemContent = new TimelineItem.Content.Assistant(new AssistantMessage { Text = text, IsStreaming = streaming }) });
        HydraDelegationBlock Card() => Assert.Single(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<HydraDelegationBlock>());
        try
        {
            var entry = Reply("Sending two heads.\n\n```hydra\n[\n{\"task\": \"Ti");
            HydraDelegationBlock first = null!;
            double nothing = 0;
            h.Main.OnMain(() =>
            {
                entry.Item = entry.Item with { TimelineItemContent = new TimelineItem.Content.Assistant(new AssistantMessage { Text = "Sending two heads.\n\n```hydra\n[\n{\"task\": \"Ti", IsStreaming = true }) };
                window = Show(new AssistantMessageRow(entry, null, h.Runtime));
                first = Card();
                nothing = first.Bounds.Height;
                Assert.Equal(["Summoning heads"], LogicalQuery.SelfAndDescendants<TextBlock>(first).Select(text => text.Text));
            });
            // A brief closes: the same card, a row more, the card taller by the row and the gap, arriving by animation.
            Flush(entry, "Sending two heads.\n\n```hydra\n[\n" + hank + ",\n{\"task\": \"Wri", true);
            h.Main.OnMain(() => Settle(window)); // the layout pass that starts the height's spring
            Frames(h.Main, clock, 60);
            double mid = 0, rowOpacity = 0;
            h.Main.OnMain(() =>
            {
                Settle(window);
                Assert.Same(first, Card()); // the card is the one on show: its state and its animation carry across the flush
                mid = Card().Bounds.Height;
                var row = Mounted<TextBlock>(first).Single(text => text.Text == "Tidy the popover");
                rowOpacity = row.GetSelfAndVisualAncestors().Aggregate(1.0, (product, visual) => product * visual.Opacity);
            });
            Frames(h.Main, clock, 2000);
            double one = 0;
            h.Main.OnMain(() =>
            {
                Settle(window);
                Assert.Same(first, Card());
                one = Card().Bounds.Height;
                Assert.True(one > nothing + 20, $"the card grew from {nothing} to {one}");
                Assert.InRange(mid, nothing + 0.5, one - 0.5); // part way at 60 ms: a spring, not a jump
                Assert.InRange(rowOpacity, 0.001, 0.999); // the new row fades in
                Assert.Equal(1, Mounted<TextBlock>(first).Single(text => text.Text == "Tidy the popover").GetSelfAndVisualAncestors().Aggregate(1.0, (product, visual) => product * visual.Opacity), 0.001);
            });
            // The block closes: still the one card, retitled in place, a second head.
            Flush(entry, "Sending two heads.\n\n```hydra\n[\n" + hank + ",\n" + walter + "\n]\n```", true);
            h.Main.OnMain(() => Settle(window));
            Frames(h.Main, clock, 60);
            h.Main.OnMain(() =>
            {
                Settle(window);
                // The second head's row, joining a list already on show, fades in by the opacity transition of :736 (the list itself was there).
                var second = Mounted<TextBlock>(first).Single(text => text.Text == "Write the tests");
                var opacity = second.GetSelfAndVisualAncestors().Aggregate(1.0, (product, visual) => product * visual.Opacity);
                Assert.InRange(opacity, 0.001, 0.999);
                Assert.Equal(1, Mounted<TextBlock>(first).Single(text => text.Text == "Tidy the popover").GetSelfAndVisualAncestors().Aggregate(1.0, (product, visual) => product * visual.Opacity), 0.001);
            });
            Frames(h.Main, clock, 2000);
            h.Main.OnMain(() =>
            {
                Settle(window);
                Assert.Same(first, Card());
                Assert.Contains("Summoning 2 heads", LogicalQuery.SelfAndDescendants<TextBlock>(first).Select(text => text.Text));
                Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<TextBlock>(first), text => text.Text == "Writing the next brief\u2026");
                Assert.True(Card().Bounds.Height > one - 0.5);
            });
            // Sent: the same card again; the verb changes, the check scales in, one wave arrives.
            Flush(entry, "Sending two heads.\n\n```hydra-sent\n[\n" + hank + ",\n" + walter + "\n]\n```", false);
            h.Main.OnMain(() => Settle(window));
            Frames(h.Main, clock, 80);
            double checkScale = 0;
            h.Main.OnMain(() =>
            {
                Settle(window);
                Assert.Same(first, Card());
                Assert.Contains("Summoned 2 heads", LogicalQuery.SelfAndDescendants<TextBlock>(first).Select(text => text.Text));
                var check = Assert.Single(Mounted<SwiftImage>(first), image => image.SymbolName == "checkmark.circle.fill");
                checkScale = check.GetSelfAndVisualAncestors().Aggregate(1.0, (product, visual) => product * visual.Opacity);
                Assert.Single(Mounted<Marrow.App.Views.Composer.DotFieldSweep>(first));
            });
            Frames(h.Main, clock, 2000);
            h.Main.OnMain(() =>
            {
                Settle(window);
                var check = Assert.Single(Mounted<SwiftImage>(first), image => image.SymbolName == "checkmark.circle.fill");
                Assert.InRange(checkScale, 0.001, 0.999); // arriving by its scale-and-opacity transition
                Assert.Equal(1, check.GetSelfAndVisualAncestors().Aggregate(1.0, (product, visual) => product * visual.Opacity), 0.001);
            });
        }
        finally
        {
            h.Main.OnMain(() => { if (window is not null) Close(window); });
            MarkdownDelegationCard.Render = previous;
        }
    });
}
