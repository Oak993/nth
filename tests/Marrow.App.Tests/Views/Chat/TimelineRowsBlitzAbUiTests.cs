using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
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
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
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

    // The mean distance from the backdrop of the eight strongest pixels in the area.
    private static double RawPeak(BlitzAbFrame frame, Rect area, Color backdrop)
    {
        var distances = new List<int>();
        for (var x = (int)Math.Floor(area.Left); x < (int)Math.Ceiling(area.Right); x++)
            for (var y = (int)Math.Floor(area.Top); y < (int)Math.Ceiling(area.Bottom); y++)
                distances.Add(BlitzAbFrame.Distance(frame.At(x, y), backdrop));
        return distances.OrderByDescending(distance => distance).Take(8).Average();
    }

    // The same words in the same font drawn alone in the ink the source gives them: what the ink of a line should be however thin its strokes are.
    private static void IsInkedLike(BlitzAbFrame frame, Rect area, Color backdrop, string text, SwiftFont font, SwiftColor ink, ThemeVariant theme, string because)
    {
        var label = new TextBlock { Text = text };
        font.Apply(label);
        label.Foreground = ink.Brush(label);
        var reference = 0.0;
        BlitzAbRender.Render(new Border { Padding = new Thickness(8), Child = label }, theme, backdrop, 480, 60, (window, shot) => reference = RawPeak(shot, BlitzAbRender.BoxIn(label, window), backdrop));
        var drawn = RawPeak(frame, area, backdrop);
        Assert.True(reference > 60, $"{because}: the reference line is barely inked ({reference})");
        Assert.InRange(drawn / reference, 0.92, 1.08);
    }

    // A symbol drawn in the ink the source gives it, as strongly inked as the same symbol drawn alone in that ink in a window of its own (a thin stroke never
    // reaches the colour whole, so the symbol is compared with itself, not with the colour).
    private static void SymbolInkedLike(BlitzAbFrame frame, SwiftImage actual, Rect area, Color backdrop, SwiftColor ink, ThemeVariant theme, string because)
    {
        var reference = SwiftImage.SystemName(actual.SymbolName!);
        if (SwiftImage.GetFont(actual) is { } font) reference.Font(font);
        reference.Foreground = ink.Brush(reference);
        var peak = 0.0;
        BlitzAbRender.Render(new Border { Padding = new Thickness(8), Child = reference }, theme, backdrop, 120, 60, (window, shot) => peak = RawPeak(shot, BlitzAbRender.BoxIn(reference, window).Inflate(2), backdrop));
        Assert.True(peak > 40, $"{because}: the reference symbol is barely inked ({peak})");
        Assert.InRange(RawPeak(frame, area.Inflate(2), backdrop) / peak, 0.9, 1.1);
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
}
