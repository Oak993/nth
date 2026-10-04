using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinRig;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), :64-229 UserMessageRow, :1167-1207 UserBubble, :1353-1413 AssistantMessageRow and
// :1446-1559 WorkGroup / WorkSteps. The UI records of the timeline's rows ask for a capture of every state they list. Where no reference frame shows a
// state the pinned Swift source is the proof (OD-0098): each state the code names is mounted, rendered headless in both themes and read back against the
// values the code states (the bubble against a shape built from the numbers of :1167-1207 alone, the inks, the type, the geometry).
[Collection("Headless")]
public sealed class TimelineRowsBlitzAbUiTests(HeadlessSession headless)
{
    private static readonly (ThemeVariant Theme, Color Backdrop)[] Themes =
    [
        (ThemeVariant.Dark, Color.FromRgb(24, 24, 27)),
        (ThemeVariant.Light, Color.FromRgb(244, 244, 246)),
    ];

    private static Border Bubble(Control row) => Mounted<Border>(row).First(border => border.Padding == new Thickness(14, 9, 14 + 4, 9));

    private static StackPanel HoverLine(Control row) => Mounted<StackPanel>(row).Single(stack => stack.Height == 22 && double.IsNaN(stack.Width));

    private static double Top(Control control, Visual root) => control.TranslatePoint(default, root)!.Value.Y;

    private static void Frames(TestRuntime main, ManualFrameSource frames, double milliseconds)
    {
        frames.Advance(TimeSpan.Zero);
        main.AdvanceMilliseconds(milliseconds);
        frames.Advance(TimeSpan.FromMilliseconds(milliseconds));
    }

    // All the ink in the area: the summed distance of its pixels from the backdrop. Unlike a peak it does not change when a mark is drawn half a pixel to one side.
    private static double RawPeak(BlitzAbFrame frame, Rect area, Color backdrop)
    {
        var sum = 0.0;
        for (var x = (int)Math.Floor(area.Left); x < (int)Math.Ceiling(area.Right); x++)
            for (var y = (int)Math.Floor(area.Top); y < (int)Math.Ceiling(area.Bottom); y++)
                sum += BlitzAbFrame.Distance(frame.At(x, y), backdrop);
        return sum;
    }

    // The same words in the same font drawn alone in the ink the source gives them, in a window of their own: what the ink of a line should be (its mass, so a thin stroke
    // that never reaches the colour whole, or a mark half a pixel off its grid, is judged as the same mark).
    private static void IsInkedLike(BlitzAbFrame frame, Rect area, Color backdrop, string text, SwiftFont font, SwiftColor ink, ThemeVariant theme, string because, double? cutAt = null)
    {
        var label = new TextBlock { Text = text };
        if (cutAt is { } width) { label.Width = width; label.MaxLines = 1; label.TextTrimming = TextTrimming.CharacterEllipsis; } // the line as it is cut at the tail inside its frame
        font.Apply(label);
        label.Foreground = ink.Brush(label);
        var reference = 0.0;
        BlitzAbRender.Render(new Border { Padding = new Thickness(8), Child = label }, theme, backdrop, 480, 60, (window, shot) => reference = RawPeak(shot, BlitzAbRender.BoxIn(label, window).Inflate(1), backdrop));
        var drawn = RawPeak(frame, area.Inflate(1), backdrop);
        Assert.True(reference > 600, $"{because}: the reference line is barely inked ({reference})");
        Assert.InRange(drawn / reference, 0.92, 1.08);
    }

    // A symbol drawn in the ink the source gives it, as inked as the same symbol drawn alone in that ink in a window of its own.
    private static void SymbolInkedLike(BlitzAbFrame frame, SwiftImage actual, Rect area, Color backdrop, SwiftColor ink, ThemeVariant theme, string because)
    {
        var reference = SwiftImage.SystemName(actual.SymbolName!);
        if (SwiftImage.GetFont(actual) is { } font) reference.Font(font);
        reference.Foreground = ink.Brush(reference);
        var mass = 0.0;
        BlitzAbRender.Render(new Border { Padding = new Thickness(8), Child = reference }, theme, backdrop, 120, 60, (window, shot) => mass = RawPeak(shot, BlitzAbRender.BoxIn(reference, window).Inflate(2), backdrop));
        Assert.True(mass > 150, $"{because}: the reference symbol is barely inked ({mass})");
        Assert.InRange(RawPeak(frame, area.Inflate(2), backdrop) / mass, 0.9, 1.1);
    }

    // ---- UI-CHAT-007: the sent message bubble ----

    // :1167-1207 UserBubble.path(in:), written out again from the numbers the Swift states, with none of the port's code in it.
    private static Geometry SwiftBubble(double width, double height)
    {
        const double r = 18, tail = 4;
        var edge = width - tail;
        var geometry = new StreamGeometry();
        using (var c = geometry.Open())
        {
            c.BeginFigure(new Point(r, 0), true);
            c.LineTo(new Point(edge - r, 0));
            c.ArcTo(new Point(edge, r), new Size(r, r), 0, false, SweepDirection.Clockwise);
            c.LineTo(new Point(edge, height - 11));
            c.CubicBezierTo(new Point(edge, height - 1), new Point(edge + tail, height), new Point(edge + tail, height));
            c.CubicBezierTo(new Point(edge, height + 0.5), new Point(edge - 4, height - 1), new Point(edge - 7, height - 4));
            c.CubicBezierTo(new Point(edge - 12, height), new Point(edge - 16, height), new Point(edge - 21, height));
            c.LineTo(new Point(r, height));
            c.ArcTo(new Point(0, height - r), new Size(r, r), 0, false, SweepDirection.Clockwise);
            c.LineTo(new Point(0, r));
            c.ArcTo(new Point(r, 0), new Size(r, r), 0, false, SweepDirection.Clockwise);
            c.EndFigure(true);
        }
        return geometry;
    }

    [Fact]
    [Trait("Req", "UI-CHAT-007")] // TimelineRows.swift:88-129, :163-164, :1167-1207; OD-0098: the bubble drawn at two lengths in both themes, its silhouette against the shape of the source's own numbers
    public void The_sent_bubble_is_drawn_in_the_shape_of_the_source_tinted_at_14_percent_hugging_a_short_message_and_wrapping_a_long_one_in_both_themes() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            foreach (var (theme, backdrop) in Themes)
            {
                var fillColour = ChromeTokens.Accent.Opacity(0.14).Resolve(theme);
                var fill = BlitzAbFrame.Over(fillColour, backdrop);
                Assert.True(BlitzAbFrame.Distance(fill, backdrop) > 20, "the bubble's fill is visible against the backdrop");
                var rows = new[]
                {
                    new UserMessageRow(UserEntry("hello there", Guid.NewGuid()), h.Runtime, canRevert: false),
                    new UserMessageRow(UserEntry(string.Join(" ", Enumerable.Repeat("a long line of words", 12)), Guid.NewGuid()), h.Runtime, canRevert: false),
                };
                var column = new StackPanel { Spacing = 24 };
                foreach (var row in rows) column.Children.Add(row);
                BlitzAbRender.Render(new Border { Padding = new Thickness(24), Child = column }, theme, backdrop, 560, 400, (window, frame) =>
                {
                    var boxes = new List<Rect>();
                    for (var i = 0; i < rows.Length; i++)
                    {
                        var row = rows[i];
                        var because = $"{theme}, message {i}";
                        var bubble = Bubble(row);
                        var box = BlitzAbRender.BoxIn(bubble, window);
                        boxes.Add(box);
                        // :124-127, :163-164 the bubble hugs its text (14 leading, 14 plus the tail trailing) and sits on the row's trailing edge; a long one stops 96 short of the leading edge.
                        var text = BlitzAbRender.BoxIn(Assert.Single(Mounted<InlineText>(row)), window);
                        Assert.Equal(text.Width + 14 + 14 + 4, box.Width, 1.0);
                        Assert.Equal(560 - 24, box.Right, 0.5);
                        Assert.True(box.Left >= 24 + 96 - 0.5, because);
                        if (i == 0) Assert.True(box.Width < 200, $"{because}: a short message's bubble is {box.Width} wide");
                        else Assert.True(box.Height > 60, $"{because}: the long message wraps (the bubble is {box.Height} tall)");
                        // :128 .tint.opacity(0.14): the fill inside the bubble, left of the text.
                        Assert.True(BlitzAbFrame.Distance(frame.At((int)box.Left + 5, (int)(box.Top + box.Height / 2)), fill) <= 6, $"{because}: the bubble's fill");
                        Assert.True(BlitzAbFrame.Distance(frame.At((int)box.Left - 4, (int)(box.Top + box.Height / 2)), backdrop) <= 6, $"{because}: the bubble ends where it measures");
                    }
                    // The silhouette: the same bubble drawn again with its text taken out, against a shape built from the Swift's own numbers.
                    for (var i = 0; i < rows.Length; i++)
                    {
                        var box = boxes[i];
                        var inkless = new UserMessageRow(UserEntry(i == 0 ? "hello there" : string.Join(" ", Enumerable.Repeat("a long line of words", 12)), Guid.NewGuid()), h.Runtime, canRevert: false);
                        Mounted<InlineText>(inkless).Single().Opacity = 0; // laid out as it was, drawn without its words: the shape alone
                        var holder = new Border { Padding = new Thickness(24), Child = new Border { Width = 512, Child = inkless } };
                        Rect shapeBox = default; BlitzAbFrame? actual = null;
                        BlitzAbRender.Render(holder, theme, backdrop, 560, 200, (win, shot) => { actual = shot; shapeBox = BlitzAbRender.BoxIn(Mounted<Path>(inkless).Single(), win); });
                        var path = new Path { Data = SwiftBubble(shapeBox.Width, shapeBox.Height), Fill = new SolidColorBrush(fillColour) };
                        Rect pathBox = default; BlitzAbFrame? expected = null;
                        BlitzAbRender.Render(new Border { Padding = new Thickness(shapeBox.Left, shapeBox.Top, 0, 0), Child = new Border { Width = shapeBox.Width, Height = shapeBox.Height, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = path } }, theme, backdrop, 560, 200,
                            (win, shot) => { expected = shot; pathBox = BlitzAbRender.BoxIn(path, win); });
                        Assert.Equal(shapeBox.Width, pathBox.Width, 0.5);
                        var threshold = BlitzAbFrame.Distance(fill, backdrop) / 2;
                        var wrong = 0; var inked = 0; var coverage = 0.0; var full = (double)BlitzAbFrame.Distance(fill, backdrop);
                        for (var x = (int)shapeBox.Left - 2; x < (int)Math.Ceiling(shapeBox.Right) + 2; x++)
                        {
                            for (var y = (int)shapeBox.Top - 2; y < (int)Math.Ceiling(shapeBox.Bottom) + 2; y++)
                            {
                                var a = BlitzAbFrame.Distance(actual!.At(x, y), backdrop) > threshold;
                                var b = BlitzAbFrame.Distance(expected!.At(x, y), backdrop) > threshold;
                                if (b) inked++;
                                coverage += Math.Abs(BlitzAbFrame.Distance(actual!.At(x, y), backdrop) - BlitzAbFrame.Distance(expected!.At(x, y), backdrop)) / full; // in pixels of fill
                                if (a != b) wrong++;
                            }
                        }
                        Assert.True(inked > 500, $"{theme}: the source's shape inks {inked} pixels; the bubble's shape box {shapeBox}, the source shape box {pathBox}");
                        Assert.True(wrong <= 0 && coverage <= 0.3, $"{theme}, message {i}: {wrong} pixels and {coverage:F1} pixels of coverage of the bubble differ from the shape of :1167-1207 ({inked} are inked): the corners or the tail are not as drawn");
                    }
                });
            }
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-007")] // TimelineRows.swift:130-161, :87: hover shows the pencil line under a revertable message, an open editor keeps it up and tints the pencil
    [Trait("Req", "UI-CHAT-010")] // TimelineRows.swift:130-161, :179-183: 22 by 22, hidden unless hovered or editing, tinted while editing, absent when not revertable; OD-0098
    public void The_pencil_is_absent_hidden_secondary_on_hover_and_accent_while_the_editor_is_open_and_none_of_it_moves_the_message_in_both_themes() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var clock = new ManualFrameSource();
        using var clockSeam = FrameClock.Source.Override(clock);
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        foreach (var (theme, backdrop) in Themes)
        {
            var turn = Guid.NewGuid();
            UserMessageRow row = null!, locked = null!;
            AvaloniaWindow window = null!;
            h.Main.OnMain(() =>
            {
                row = new UserMessageRow(UserEntry("same words", turn), h.Runtime, canRevert: true);
                locked = new UserMessageRow(UserEntry("same words", Guid.NewGuid()), h.Runtime, canRevert: false);
                var column = new StackPanel { Spacing = 50 };
                column.Children.Add(row); column.Children.Add(locked);
                window = BlitzAbRender.Open(new Border { Padding = new Thickness(24), Child = column }, theme, backdrop, 560, 260);
            });
            BlitzAbFrame Shot() => BlitzAbRender.Capture(window);
            Rect PencilBox() => BlitzAbRender.BoxIn(Assert.Single(Mounted<SwiftImage>(HoverLine(row))), window);
            int PencilInk(BlitzAbFrame frame) => frame.InkCount(PencilBox().Inflate(2), backdrop);
            Rect bubbleBox = default; int bubbleInk = 0;
            // Idle: the line is there but clear (opacity 0), the pencil not drawn; a message that cannot be edited has no pencil at all (:131).
            h.Main.OnMain(() =>
            {
                var frame = Shot();
                Assert.Empty(Mounted<Button>(HoverLine(locked)));
                Assert.Equal(0, HoverLine(row).Opacity);
                Assert.Equal(0, PencilInk(frame));
                bubbleBox = BlitzAbRender.BoxIn(Bubble(row), window);
                bubbleInk = frame.InkCount(bubbleBox, backdrop, 8);
                // :137, :160 a 22-point line right under the bubble (6 under), the pencil in its 22 by 22 hit area, trailing.
                var line = HoverLine(row);
                Assert.Equal(6, Top(line, window) - (bubbleBox.Bottom), 0.5);
                var frameBox = BlitzAbRender.BoxIn(Mounted<Border>(line).First(border => border.Width == 22), window);
                Assert.Equal(22, frameBox.Width, 0.5); Assert.Equal(22, frameBox.Height, 0.5);
                Assert.Equal(bubbleBox.Right, frameBox.Right, 0.5);
                Hover(window, Assert.Single(Mounted<InlineText>(row))); // the pointer enters the row
            });
            Frames(h.Main, clock, 220);
            // Hovered: the pencil in the secondary ink (:141 .secondary), the message where it was.
            h.Main.OnMain(() =>
            {
                var frame = Shot();
                Assert.Equal(1, HoverLine(row).Opacity);
                Assert.True(PencilInk(frame) > 12, $"{theme}: the pencil is not drawn on hover");
                SymbolInkedLike(frame, Assert.Single(Mounted<SwiftImage>(HoverLine(row))), PencilBox(), backdrop, SwiftColor.Secondary, theme, $"{theme}: the pencil on hover");
                Assert.Equal(bubbleBox, BlitzAbRender.BoxIn(Bubble(row), window));
                Assert.Equal(bubbleInk, frame.InkCount(bubbleBox, backdrop, 8)); // the bubble's own ink is untouched
                // The pointer leaves and the editor opens: the line stays up with no pointer over it, and the pencil is tinted (:141, :161).
                window.MouseMove(new Point(500, 250), Avalonia.Input.RawInputModifiers.None);
                h.Runtime.MessageEdit = new PromptEdit(turn, "same words", ValueList<Attachment>.Empty);
            });
            Frames(h.Main, clock, 220);
            h.Main.OnMain(() =>
            {
                Settle(window);
                var frame = Shot();
                Assert.Equal(1, HoverLine(row).Opacity);
                SymbolInkedLike(frame, Assert.Single(Mounted<SwiftImage>(HoverLine(row))), PencilBox(), backdrop, ChromeTokens.Accent, theme, $"{theme}: the pencil while the editor is open");
                BlitzAbRender.InkIsColour(frame, PencilBox(), backdrop, ChromeTokens.Accent.Resolve(theme), $"{theme}: the pencil is the accent while the editor is open", most: 1.6); // the hue: tinted, not grey
                Assert.True(PencilInk(frame) > 12);
                Assert.Equal(bubbleBox, BlitzAbRender.BoxIn(Bubble(row), window));
                Assert.Equal(bubbleInk, frame.InkCount(bubbleBox, backdrop, 8));
                h.Runtime.MessageEdit = null;
            });
            Frames(h.Main, clock, 220);
            h.Main.OnMain(() =>
            {
                Settle(window);
                Assert.Equal(0, HoverLine(row).Opacity); // closed again, no pointer: clear
                Assert.Equal(0, PencilInk(Shot()));
                BlitzAbRender.Shut(window);
            });
        }
    });

    // ---- UI-CHAT-008: the quote chips above a sent message ----

    [Fact]
    [Trait("Req", "UI-CHAT-008")] // TimelineRows.swift:92-117, ComposerView.swift:962-970 ChipChrome; OD-0098: the chips drawn at their own widths, their glyph and excerpt in their type and inks, in both themes
    public void A_quote_chip_is_a_capsule_9_in_and_7_out_around_a_footnote_glyph_and_a_subheadline_excerpt_capped_at_260_points_in_both_themes() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            foreach (var (theme, backdrop) in Themes)
            {
                var longQuote = new string('x', 30) + " " + new string('y', 60);
                var row = new UserMessageRow(UserEntry("> Hello    big   world\n> second line\n\n> short\n\n> " + longQuote + "\n\nthe typed words", Guid.NewGuid()), h.Runtime, canRevert: false);
                BlitzAbRender.Render(new Border { Padding = new Thickness(24, 20, 24, 20), Child = row }, theme, backdrop, 640, 300, (window, frame) =>
                {
                    var capsule = BlitzAbFrame.Over(ChromeTokens.Overlay(0.12).Resolve(theme), backdrop); // ChipChrome: Chrome.overlay(0.12)
                    var chips = Mounted<Border>(row).Where(border => AutomationProperties.GetName(border)?.StartsWith("Quote: ", StringComparison.Ordinal) == true).ToList();
                    Assert.Equal(3, chips.Count);
                    var bubble = BlitzAbRender.BoxIn(Bubble(row), window);
                    var excerpts = new[] { "Hello big world", "short", longQuote[..72] + "…" };
                    for (var i = 0; i < chips.Count; i++)
                    {
                        var chip = chips[i];
                        var box = BlitzAbRender.BoxIn(chip, window);
                        var because = $"{theme}: chip {i}";
                        // :113 the accessibility label and :111 the whole quote as the tooltip; the excerpt is the first non-empty line, collapsed, cut at 72 characters with an ellipsis.
                        Assert.Equal("Quote: " + excerpts[i], AutomationProperties.GetName(chip));
                        var glyph = Assert.Single(Mounted<SwiftImage>(chip));
                        var words = Assert.Single(Mounted<TextBlock>(chip));
                        var glyphBox = BlitzAbRender.BoxIn(glyph, window);
                        var wordsBox = BlitzAbRender.BoxIn(words, window);
                        // ChipChrome: 9 leading, 7 trailing, 5 vertical; :98 the glyph and the words 5 apart on one centre line.
                        Assert.Equal(box.Left + 9, glyphBox.Left, 0.5);
                        Assert.Equal(glyphBox.Right + 5, wordsBox.Left, 0.5);
                        Assert.Equal(box.Right - 7, wordsBox.Right, 0.5);
                        Assert.Equal(Math.Max(glyphBox.Height, wordsBox.Height) + 10, box.Height, 1.0);
                        Assert.Equal(box.Center.Y, wordsBox.Center.Y, 1.0);
                        // :103-106 the words are capped at 260 points and cut with an ellipsis at the tail; a short excerpt is as wide as it is.
                        if (i == 2) { Assert.Equal(260, wordsBox.Width, 0.5); Assert.Equal(TextTrimming.CharacterEllipsis, words.TextTrimming); }
                        else Assert.True(wordsBox.Width < 260);
                        Assert.Equal(1, words.MaxLines);
                        // A continuous capsule filled with the overlay at 0.12: its corner is bare, its middle filled, its ends round.
                        Assert.True(BlitzAbFrame.Distance(frame.At((int)box.Left + 3, (int)(box.Top + box.Height / 2)), capsule) <= 6, $"{because}: the capsule's fill");
                        Assert.True(BlitzAbFrame.Distance(frame.At((int)box.Left + 1, (int)box.Top + 1), backdrop) <= 6, $"{because}: the capsule's corner is filled");
                        Assert.True(BlitzAbFrame.Distance(frame.At((int)(box.Left + box.Height / 2), (int)box.Top + 1), capsule) <= 6, $"{because}: the capsule's top edge");
                        // :99-105 the glyph in .footnote semibold (10 points) in the secondary ink, the words in .subheadline (11) in the primary ink at 0.9.
                        var font = SwiftImage.GetFont(glyph)!.Resolve();
                        Assert.Equal(10, font.FontSize);
                        Assert.Equal(FontWeight.SemiBold, font.FontWeight);
                        Assert.Equal("text.quote", glyph.SymbolName);
                        SymbolInkedLike(frame, glyph, glyphBox, capsule, ChromeTokens.SecondaryText, theme, $"{because}: the glyph");
                        IsInkedLike(frame, wordsBox, capsule, excerpts[i], SwiftFont.System(SwiftFont.TextStyle.Subheadline), ChromeTokens.PrimaryText.Opacity(0.9), theme, $"{because}: the excerpt", cutAt: i == 2 ? 260 : null);
                        // :96, :116 each chip at its own width flush right, 4 short of the bubble's tail edge, and 4 between chips.
                        Assert.Equal(bubble.Right - 4, box.Right, 0.5);
                        if (i > 0) Assert.Equal(4, box.Top - BlitzAbRender.BoxIn(chips[i - 1], window).Bottom, 0.5);
                    }
                    Assert.True(BlitzAbRender.BoxIn(chips[1], window).Width < BlitzAbRender.BoxIn(chips[0], window).Width);
                });
            }
        });
    });

    // ---- UI-CHAT-019: the assistant reply ----

    private const string Fenced = "```swift\nlet x = 1\n```";

    [Fact]
    [Trait("Req", "UI-CHAT-019")] // TimelineRows.swift:1353-1390; OD-0098: the reply at rest, hovered and streaming, drawn in both themes
    public void A_reply_shows_its_turns_duration_under_its_text_on_hover_only_for_the_turns_last_reply_and_never_while_streaming_in_both_themes() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var clock = new ManualFrameSource();
        using var clockSeam = FrameClock.Source.Override(clock);
        using var locale = SwiftFormatters.CurrentLocale.Override("en_US");
        foreach (var (theme, backdrop) in Themes)
        {
            AssistantMessageRow settled = null!, streaming = null!, middle = null!;
            AvaloniaWindow window = null!;
            var summary = Summary(Guid.NewGuid()) with { Duration = 64 };
            h.Main.OnMain(() =>
            {
                settled = new AssistantMessageRow(Reply("The settled reply."), summary, h.Runtime);
                streaming = new AssistantMessageRow(new TimelineEntry(new TimelineItem(null, new TimelineItem.Content.Assistant(new AssistantMessage { Text = "The streaming reply", IsStreaming = true }))), summary, h.Runtime);
                middle = new AssistantMessageRow(Reply("The middle reply."), null, h.Runtime); // only the turn's last reply carries a summary
                var column = new StackPanel { Spacing = 70 };
                foreach (var row in new Control[] { settled, streaming, middle }) column.Children.Add(row);
                window = BlitzAbRender.Open(new Border { Padding = new Thickness(24), Child = column }, theme, backdrop, 560, 400);
            });
            Rect LabelArea(AssistantMessageRow row) => BlitzAbRender.BoxIn(HoverLine(row), window);
            // At rest: the line is there, clear, under the text 2 points down, and takes no room from the row (:1363, :1377, :1388).
            h.Main.OnMain(() =>
            {
                var frame = BlitzAbRender.Capture(window);
                foreach (var row in new[] { settled, streaming, middle })
                {
                    var markdown = Assert.Single(Mounted<MarkdownView>(row));
                    var line = HoverLine(row);
                    Assert.Equal(22, line.Height);
                    Assert.Equal(2, Top(line, window) - (Top(markdown, window) + markdown.Bounds.Height), 0.5);
                    Assert.Equal(markdown.Bounds.Height, row.Bounds.Height, 0.5);
                    Assert.Equal(0, line.Opacity);
                    Assert.Equal(0, frame.InkCount(LabelArea(row).Deflate(new Thickness(0, 1)), backdrop));
                }
                Hover(window, Assert.Single(Mounted<MarkdownView>(settled)));
            });
            Frames(h.Main, clock, 60);
            h.Main.OnMain(() => Assert.InRange(HoverLine(settled).Opacity, 0.01, 0.99)); // :1381 .easeOut(duration: 0.12): part way at 60 ms
            Frames(h.Main, clock, 120);
            h.Main.OnMain(() =>
            {
                var frame = BlitzAbRender.Capture(window);
                var line = HoverLine(settled);
                Assert.Equal(1, line.Opacity);
                // :1368-1372 "Worked for 1m 4s" at caption in the secondary ink on the leading edge of the line, centred in its 22 points.
                var label = Assert.Single(Mounted<TextBlock>(line));
                Assert.Equal("Worked for 1m 4s", label.Text);
                var box = BlitzAbRender.BoxIn(label, window);
                Assert.Equal(Left(settled, window), box.Left, 0.5);
                Assert.Equal(Top(line, window) + 11, box.Center.Y, 0.75);
                IsInkedLike(frame, box, backdrop, "Worked for 1m 4s", SwiftFont.Caption, ChromeTokens.SecondaryText, theme, $"{theme}: the duration");
                // Another reply of the turn has no summary: its line stays empty even with the pointer on it.
                Hover(window, Assert.Single(Mounted<MarkdownView>(middle)));
            });
            Frames(h.Main, clock, 250);
            h.Main.OnMain(() =>
            {
                var frame = BlitzAbRender.Capture(window);
                Assert.Empty(Mounted<TextBlock>(HoverLine(middle)));
                Assert.Equal(0, frame.InkCount(LabelArea(middle).Deflate(new Thickness(0, 1)), backdrop));
                // :1378 a streaming reply never shows the line: the pointer on it changes nothing.
                Hover(window, Assert.Single(Mounted<MarkdownView>(streaming)));
            });
            Frames(h.Main, clock, 250);
            h.Main.OnMain(() =>
            {
                var frame = BlitzAbRender.Capture(window);
                Assert.Equal(0, HoverLine(streaming).Opacity);
                Assert.Equal(0, frame.InkCount(LabelArea(streaming).Deflate(new Thickness(0, 1)), backdrop));
                BlitzAbRender.Shut(window);
            });
        }
    });

    // ---- UI-CHAT-020, UI-CHAT-021: the reply's context menu ----

    // The recorded popup's content hosted in a themed window of its own so that its rows take real clicks and are drawn.
    private static AvaloniaWindow HostMenu(Popup popup, ThemeVariant theme, Color backdrop)
    {
        var content = popup.Child!;
        popup.Child = null;
        return BlitzAbRender.Open(new Border { Padding = new Thickness(12), Child = content }, theme, backdrop, 360, 260);
    }

    [Fact]
    [Trait("Req", "UI-CHAT-020")] // TimelineRows.swift:1401-1405, :38-54; OD-0098: the menu of a reply with a fenced block drawn, Copy last code block replaced in place by Copied, in both themes
    [Trait("Req", "UI-CHAT-021")] // TimelineRows.swift:1406-1410: Add to reply behind a divider, replaced in place by Added, drawn in both themes
    public void The_reply_menu_draws_Copy_last_code_block_only_with_a_whole_fence_and_Add_to_reply_behind_a_divider_each_reading_Copied_or_Added_in_its_own_row_in_both_themes() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        foreach (var (theme, backdrop) in Themes)
        {
            AvaloniaWindow window = null!, menu = null!;
            RowActionItems items = null!;
            h.Main.OnMain(() =>
            {
                _ = AppCurrentEvent.CurrentWindow; // starts the app-wide event watch before the click
                var withCode = new AssistantMessageRow(Reply("answer\n\n" + Fenced), null, h.Runtime);
                var without = new AssistantMessageRow(Reply("answer\n\n```\nunfinished"), null, h.Runtime);
                var panel = new StackPanel { Spacing = 80 };
                panel.Children.Add(withCode); panel.Children.Add(without);
                window = Show(panel);
                // :1401-1405 an unfinished fence offers no code to copy.
                var (plainPopup, plain) = RightClick(presenter, window, Assert.Single(Mounted<MarkdownView>(without)));
                Assert.Equal(["Copy message", "Copy as markdown", "Add to reply"], ItemTitles(plain));
                var (popup, actions) = RightClick(presenter, window, Assert.Single(Mounted<MarkdownView>(withCode)));
                Assert.Equal(["Copy message", "Copy as markdown", "Copy last code block", "Add to reply"], ItemTitles(actions));
                items = actions;
                menu = HostMenu(popup, theme, backdrop);
            });
            h.Main.OnMain(() =>
            {
                Settle(menu);
                var frame = BlitzAbRender.Capture(menu);
                var rows = LogicalQuery.SelfAndDescendants<PopoverItem>(items).ToList();
                var boxes = rows.Select(row => BlitzAbRender.BoxIn(row, menu)).ToList();
                // Four rows in the order the source builds them, one under another, each with its symbol and its title inked.
                for (var i = 1; i < boxes.Count; i++) Assert.True(boxes[i].Top >= boxes[i - 1].Bottom - 0.5, $"{theme}: row {i} starts above the row before it");
                var symbols = ItemSymbols(items);
                Assert.Equal(["doc.on.doc", "number", "chevron.left.forwardslash.chevron.right", "arrow.turn.down.left"], symbols);
                foreach (var box in boxes) Assert.True(frame.InkCount(box, backdrop) > 40, $"{theme}: a row of the menu draws nothing");
                // :1406 startsGroup: a divider between the third row and Add to reply, none between the others.
                var column = Assert.IsType<StackPanel>(items.Child);
                Assert.IsType<PopoverDivider>(column.Children[3]);
                Assert.DoesNotContain(column.Children.Take(3), child => child is PopoverDivider);
                var divider = BlitzAbRender.BoxIn(column.Children[3], menu);
                Assert.True(divider.Top >= boxes[2].Bottom - 0.5 && divider.Bottom <= boxes[3].Top + 0.5, $"{theme}: the divider is not between the rows");
                Assert.True(frame.InkCount(divider.Inflate(new Thickness(0, 1)), backdrop, 8) > 100, $"{theme}: the divider is not drawn");
                // Copy last code block: pressed, its own row now reads Copied, with the rows beside it where they were.
                var titleBefore = frame.InkCount(boxes[2], backdrop);
                ClickButton(menu, Assert.Single(LogicalQuery.SelfAndDescendants<Button>(rows[2])));
            });
            h.Main.OnMain(() =>
            {
                Settle(menu);
                var frame = BlitzAbRender.Capture(menu);
                var rows = LogicalQuery.SelfAndDescendants<PopoverItem>(items).ToList();
                Assert.Equal(["Copy message", "Copy as markdown", "Copied", "Add to reply"], rows.Select(Title));
                Assert.True(rows[2].IsConfirmed);
                Assert.True(frame.InkCount(BlitzAbRender.BoxIn(rows[2], menu), backdrop) > 20);
                ClickButton(menu, Assert.Single(LogicalQuery.SelfAndDescendants<Button>(rows[3])));
            });
            h.Main.OnMain(() =>
            {
                Settle(menu);
                var rows = LogicalQuery.SelfAndDescendants<PopoverItem>(items).ToList();
                // :1409 the reply is quoted into the draft; the confirmation is Added, not Copied.
                Assert.Equal(["Copy message", "Copy as markdown", "Copied", "Added"], rows.Select(Title));
                Assert.Equal(["answer\n\n" + Fenced], h.Runtime.Draft.Quotes.Select(quote => quote.Text));
                BlitzAbRender.Shut(menu);
                Close(window);
            });
            h.Main.OnMain(() => h.Runtime.Draft = h.Runtime.Draft with { Quotes = ValueList<ReplyQuote>.Empty });
        }
    });

    // ---- UI-CHAT-022, UI-CHAT-023: the work group's summary line and its earlier-steps line ----

    private static Rect Ink(BlitzAbFrame frame, Rect area, Color backdrop) =>
        frame.Ink(area, backdrop) is { } ink ? new Rect(ink.Left, ink.Top, ink.Right - ink.Left + 1, ink.Bottom - ink.Top + 1) : throw new Xunit.Sdk.XunitException("nothing is drawn in " + area);

    [Fact]
    [Trait("Req", "UI-CHAT-022")] // TimelineRows.swift:1463-1509, :1586-1634; OD-0098: the summary line collapsed and open in both themes, the chevron turned a quarter turn
    public void The_work_group_summary_line_draws_its_glyph_summary_and_chevron_in_the_secondary_ink_and_the_chevron_turns_a_quarter_when_it_opens_in_both_themes() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        foreach (var (theme, backdrop) in Themes)
        {
            WorkGroup group = null!;
            AvaloniaWindow window = null!;
            h.Main.OnMain(() =>
            {
                group = new WorkGroup(new ValueList<TimelineEntry>([Tool(ToolCall.Kind.Edit), Tool(ToolCall.Kind.Command)]), h.Runtime, startsCollapsed: true);
                var host = new Border { Padding = new Thickness(24), Child = group };
                ThreadTimeline.SetRevealTimelineEnd(host, new TimelineRevealBox { Action = () => { } });
                window = BlitzAbRender.Open(host, theme, backdrop, 560, 260);
            });
            Rect collapsed = default;
            h.Main.OnMain(() =>
            {
                var frame = BlitzAbRender.Capture(window);
                var header = Assert.Single(Mounted<Button>(group));
                var summary = TextNamed(header, "Edited files, ran commands");
                var glyph = Assert.Single(Mounted<SwiftImage>(header), image => image.SymbolName == "pencil");
                var chevron = Assert.Single(Mounted<SwiftImage>(header), image => image.SymbolName == "chevron.right");
                // :1480-1495 the 16-point icon column, the summary 8 on, the chevron 8 after it, 12 of trailing padding; one centre line.
                var g = BlitzAbRender.BoxIn(glyph, window); var s = BlitzAbRender.BoxIn(summary, window); var c = BlitzAbRender.BoxIn(chevron, window);
                Assert.Equal(24, g.Left, 0.5); Assert.Equal(16, g.Width, 0.5);
                Assert.Equal(g.Left + 16 + 8, s.Left, 0.5);
                Assert.Equal(s.Right + 8, c.Left, 0.5);
                Assert.Equal(c.Right + 12, BlitzAbRender.BoxIn(header, window).Right, 0.5);
                Assert.Equal(s.Center.Y, g.Center.Y, 1.0); Assert.Equal(s.Center.Y, c.Center.Y, 1.0);
                // The three marks in the secondary ink, in their own type (glyph caption, text callout, chevron caption2 semibold: 10, 12, 10).
                SymbolInkedLike(frame, glyph, g, backdrop, ChromeTokens.SecondaryText, theme, $"{theme}: the glyph");
                IsInkedLike(frame, s, backdrop, "Edited files, ran commands", ChatFont.Chat(SwiftFont.TextStyle.Callout), ChromeTokens.SecondaryText, theme, $"{theme}: the summary");
                SymbolInkedLike(frame, chevron, c, backdrop, ChromeTokens.SecondaryText, theme, $"{theme}: the chevron");
                Assert.Equal(10, SwiftImage.GetFont(glyph)!.Resolve().FontSize);
                Assert.Equal(FontWeight.SemiBold, SwiftImage.GetFont(chevron)!.Resolve().FontWeight);
                // Collapsed (:1490): the chevron at 0 degrees, pointing right: taller than wide, nothing below the header.
                collapsed = Ink(frame, c.Inflate(2), backdrop);
                Assert.True(collapsed.Height > collapsed.Width + 1, $"{theme}: the collapsed chevron is {collapsed.Width} by {collapsed.Height}");
                Assert.Empty(Mounted<WorkSteps>(group));
                Click(window, header);
            });
            Frames(h.Main, frames, 600); // :1470 .snappy(duration: 0.24): settled
            h.Main.OnMain(() =>
            {
                Settle(window);
                var frame = BlitzAbRender.Capture(window);
                var header = Mounted<Button>(group).First();
                var chevron = Assert.Single(Mounted<SwiftImage>(header), image => image.SymbolName == "chevron.right");
                // Open (:1490): turned 90 degrees, pointing down: the same mark on its side (wider than tall, its sides the collapsed one's swapped).
                var open = Ink(frame, BlitzAbRender.BoxIn(chevron, window).Inflate(3), backdrop);
                Assert.True(open.Width > open.Height + 1, $"{theme}: the open chevron is {open.Width} by {open.Height}");
                Assert.Equal(collapsed.Height, open.Width, 1.5);
                Assert.Equal(collapsed.Width, open.Height, 1.5);
                // The steps hang one row gap under the header, drawn.
                var steps = Assert.Single(Mounted<WorkSteps>(group));
                var headerBox = BlitzAbRender.BoxIn(header, window);
                Assert.Equal(20, BlitzAbRender.BoxIn(steps, window).Top - headerBox.Bottom, 0.5);
                Assert.True(frame.InkCount(BlitzAbRender.BoxIn(steps, window), backdrop) > 80, $"{theme}: the steps are not drawn");
                BlitzAbRender.Shut(window);
            });
        }
    });

    [Fact]
    [Trait("Req", "UI-CHAT-023")] // TimelineRows.swift:1522-1544; OD-0098: the earlier-steps line drawn over five steps in both themes, then gone once it is tapped
    public void The_earlier_steps_line_draws_an_ellipsis_and_its_count_in_the_secondary_ink_over_the_two_latest_steps_and_goes_when_tapped_in_both_themes() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        foreach (var (theme, backdrop) in Themes)
        {
            var showsAll = new ObservableBoolBox();
            WorkSteps five = null!;
            AvaloniaWindow window = null!;
            h.Main.OnMain(() =>
            {
                five = new WorkSteps(new ValueList<TimelineEntry>(Enumerable.Range(1, 5).Select(i => ToolEntry(Call(ToolCall.Kind.Read, "Read file" + i)))), h.Runtime, showsAll.AsBinding());
                var host = new Border { Padding = new Thickness(24), Child = five };
                ThreadTimeline.SetRevealTimelineEnd(host, new TimelineRevealBox { Action = () => { } });
                window = BlitzAbRender.Open(host, theme, backdrop, 560, 300);
            });
            h.Main.OnMain(() =>
            {
                var frame = BlitzAbRender.Capture(window);
                var line = Assert.Single(Mounted<Button>(five), button => Texts(button).Contains("3 earlier steps"));
                var glyph = Assert.Single(Mounted<SwiftImage>(line));
                var words = TextNamed(line, "3 earlier steps");
                var g = BlitzAbRender.BoxIn(glyph, window); var w = BlitzAbRender.BoxIn(words, window);
                // :1531-1540 the ellipsis in the 16-point icon column, the words 8 on, 12 of trailing padding; both in the secondary ink.
                Assert.Equal("ellipsis", glyph.SymbolName);
                Assert.Equal(24, g.Left, 0.5); Assert.Equal(16, g.Width, 0.5);
                Assert.Equal(g.Left + 16 + 8, w.Left, 0.5);
                Assert.Equal(w.Right + 12, BlitzAbRender.BoxIn(line, window).Right, 0.5);
                Assert.Equal(w.Center.Y, g.Center.Y, 1.0);
                SymbolInkedLike(frame, glyph, g, backdrop, ChromeTokens.SecondaryText, theme, $"{theme}: the ellipsis");
                IsInkedLike(frame, w, backdrop, "3 earlier steps", ChatFont.Chat(SwiftFont.TextStyle.Callout), ChromeTokens.SecondaryText, theme, $"{theme}: the count");
                // Only the two latest steps are drawn below it, a row gap on.
                var rows = Mounted<ToolRow>(five).ToList();
                Assert.Equal(2, rows.Count);
                Assert.Equal(20, BlitzAbRender.BoxIn(rows[0], window).Top - BlitzAbRender.BoxIn(line, window).Bottom, 0.5);
                Click(window, line);
            });
            Frames(h.Main, frames, 600);
            h.Main.OnMain(() =>
            {
                Settle(window);
                var frame = BlitzAbRender.Capture(window);
                Assert.DoesNotContain(Texts(five), text => text.Contains("earlier", StringComparison.Ordinal)); // the line is gone and every step shows
                Assert.Equal(5, Mounted<ToolRow>(five).Count());
                Assert.True(frame.InkCount(BlitzAbRender.BoxIn(five, window), backdrop) > 200);
                BlitzAbRender.Shut(window);
            });
        }
    });
}
