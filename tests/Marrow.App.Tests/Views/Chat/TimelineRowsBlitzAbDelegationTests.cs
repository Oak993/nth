using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
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
                            Assert.Equal(6, BlitzAbRender.BoxIn(glyphs[0], window).Top - BlitzAbRender.BoxIn(marks[0], window).Bottom, 0.75); // :658 the card's spacing 6
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
}
