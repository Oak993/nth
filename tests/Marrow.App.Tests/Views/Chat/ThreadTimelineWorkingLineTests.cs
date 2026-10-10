using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;
using static Marrow.App.Tests.Fixtures.TimelineMount;

namespace Marrow.App.Tests.Views.Chat;

// The running turn's working line (DroppyCode/App/Views/Chat/ThreadTimeline.swift, B3 52bc792, :2075-2197), mounted at the end of a real timeline in a
// shown window: the card or the badge, the label, the chevron and the body it opens, with the reader's place in the thread watched alongside.
[Collection("Headless")]
public sealed class ThreadTimelineWorkingLineTests(HeadlessSession headless)
{
    private static readonly Guid Turn = Guid.Parse("12345678-abcd-1234-abcd-123456789abc");

    private static TimelineEntry Reasoning(string id, string text, bool streaming = false) =>
        Entry(id, new TimelineItem.Content.Reasoning(new ReasoningBlock { Text = text, IsStreaming = streaming }), Turn);

    // A running turn with a prompt, two lines of thinking and three commands, and a long thread above it so the reader can be somewhere else.
    private static void RunningThread(TimelineMount mount, int steps = 3, bool thinking = true)
    {
        mount.Model.Settings.ShowReasoning = true;
        var entries = Turns(5, "old-", sentences: 10).ToList();
        entries.Add(User("prompt", Turn, "Please make the build faster"));
        if (thinking) { entries.Add(Reasoning("r1", "First I look at the workflow.")); entries.Add(Reasoning("r2", "Then the cache.", streaming: true)); }
        for (var i = 0; i < steps; i++) entries.Add(ToolEntry("tool-" + i, Turn));
        mount.SetEntries(entries);
        mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running);
        mount.Settle();
    }

    private static WorkingIndicator Line(TimelineMount mount) => LogicalQuery.SelfAndDescendants<WorkingIndicator>(mount.View).Single();

    private static Border Frame(WorkingIndicator line) => LogicalQuery.SelfAndDescendants<Border>(line).Single(border => border.Child is StackPanel { Spacing: 10 });

    private static Panel Header(WorkingIndicator line) => LogicalQuery.SelfAndDescendants<Panel>(line).Single(panel => panel.GetType().Name == "BaselineHeader");

    private static Button Chevron(WorkingIndicator line) => LogicalQuery.SelfAndDescendants<Button>(line).First();

    private static void Click(WorkingIndicator line) => Chevron(line).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    // The baseline of a header child's first line of text, in the header's coordinates.
    // OD-0136 amendment 2, step 2 B: the running line's title and clock are letters drawn on the render thread; they say where their first baseline is (IBaselineProvider).
    private static double BaselineY(Control child, Control header) =>
        LogicalQuery.SelfAndDescendants<IBaselineProvider>(child).FirstOrDefault() is { } provider
            ? ((Control)provider).TranslatePoint(default, header)!.Value.Y + provider.Baseline
            : child.TranslatePoint(default, header)!.Value.Y + LogicalQuery.SelfAndDescendants<TextBlock>(child).First().TextLayout.TextLines[0].Baseline;

    private static double AngleOf(WorkingIndicator line) =>
        Assert.IsType<Avalonia.Media.RotateTransform>(LogicalQuery.SelfAndDescendants<SwiftImage>(line).Single(image => image.SymbolName == "chevron.right").RenderTransform).Angle;

    private static double Height(WorkingIndicator line) => line.Bounds.Height;

    // The line's chevron angle and its height, every 20 ms until a second has passed.
    private static List<(double Angle, double Height)> Sample(TimelineMount mount, WorkingIndicator line, int milliseconds)
    {
        var samples = new List<(double, double)> { (AngleOf(line), Height(line)) };
        for (var elapsed = 0; elapsed < milliseconds; elapsed += 20) { mount.Advance(20); samples.Add((AngleOf(line), Height(line))); }
        return samples;
    }

    [Fact]
    [Trait("Req", "PR-CHAT-117")] // ThreadTimeline.swift:2096-2097,2106-2149,2281; capture clause: the card (the cousin's 1:28-1:34, map/CATALOGUE.md static.thread-timeline) and the badge (setting off: no video)
    public void The_line_is_a_380_point_card_or_a_word_hugging_badge_and_opens_and_closes_over_one_animation_following_the_end_only_on_opening() => ThreadTimelineRig.Run(headless, mount =>
    {
        RunningThread(mount);
        var line = Line(mount); var frame = Frame(line); var header = Header(line);
        var title = header.Children[0]; var time = header.Children[1]; var chevron = header.Children[2];
        // The card (:2144-2147): 380 wide, 14 leading and trailing, 11 above and below, a continuous rounded rectangle at the pill's radius for the hit shape.
        Assert.Equal(380, frame.Bounds.Width, 3); Assert.Equal(new Thickness(14, 11), frame.Padding); Assert.Equal(TimelineMetrics.PillRadius, frame.CornerRadius.TopLeft);
        Assert.NotNull(frame.Background);
        // :2178-2181 the fill behind it is the card's own width (the stack it backs hugs what it holds), not the row's: the cousin's card is 380 wide on a 820 column.
        var shell = LogicalQuery.SelfAndDescendants<Border>(line).First(border => border.CornerRadius.TopLeft == TimelineMetrics.PillRadius);
        Assert.Equal(380, shell.Bounds.Width, 3); Assert.True(line.Bounds.Width > 380, "on a row wider than the card");
        // :2122 an HStack aligned on the first text baseline, spacing 8; :2124 the title takes the full width, so the time and the chevron sit at the trailing edge.
        Assert.Equal(frame.Bounds.Width - 28, header.Bounds.Width, 3);
        Assert.Equal(title.Bounds.Right + 8, time.Bounds.Left, 1); Assert.Equal(time.Bounds.Right + 8, chevron.Bounds.Left, 1); Assert.Equal(header.Bounds.Width, chevron.Bounds.Right, 1);
        Assert.Equal(BaselineY(title, header), BaselineY(time, header), 0.3);
        // :2121 the inner column has spacing 10: the bar sits 10 under the header row.
        var bar = LogicalQuery.SelfAndDescendants<HydraProgressBar>(line).Single();
        Assert.Equal(10, bar.TranslatePoint(default, frame)!.Value.Y - (header.TranslatePoint(default, frame)!.Value.Y + header.Bounds.Height), 1);
        var cardWidth = Frame(line).Bounds.Width;
        // :2144-2147 the badge (the setting off): the pill's own insets, no fixed width, no bar, the title as wide as its words.
        mount.Model.Settings.ShowsWorkingCard = false; mount.Advance(300);
        Assert.Same(line, Line(mount));
        frame = Frame(line); header = Header(line); title = header.Children[0]; time = header.Children[1]; chevron = header.Children[2];
        Assert.True(double.IsNaN(frame.Width)); Assert.Equal(new Thickness(TimelineMetrics.PillLeading, TimelineMetrics.PillVertical, TimelineMetrics.PillTrailing, TimelineMetrics.PillVertical), frame.Padding);
        Assert.Empty(LogicalQuery.SelfAndDescendants<HydraProgressBar>(line));
        Assert.True(frame.Bounds.Width < 380 - 100, $"the badge hugs its words: {frame.Bounds.Width}");
        Assert.Equal(title.Bounds.Right + 8, time.Bounds.Left, 1); Assert.Equal(time.Bounds.Right + 8, chevron.Bounds.Left, 1);
        Assert.Equal(frame.Padding.Left + header.Bounds.Width + frame.Padding.Right, frame.Bounds.Width, 1);
        Assert.Equal(BaselineY(title, header), BaselineY(time, header), 0.3);
        mount.Model.Settings.ShowsWorkingCard = true; mount.Advance(300);
        Assert.Equal(cardWidth, Frame(line).Bounds.Width, 3);
        // :2107-2119 the press opens it over .snappy(duration: 0.24) and asks the timeline to follow the end; pressing it again closes it over the same animation and asks nothing.
        var tracking = mount.Field<TimelineScrollTracking>("tracking");
        mount.Scroll.Offset = new Vector(0, 100); mount.Advance(400); tracking.IsPinnedToBottom = false; tracking.EndActivity();
        var before = mount.Scroll.Offset.Y;
        Click(line);
        Assert.True(tracking.IsPinnedToBottom, "opening follows the end");
        var open = Sample(mount, line, 700);
        Assert.Equal(90, open[^1].Angle, 1); Assert.True(open[^1].Height > open[0].Height + 20);
        Assert.True(mount.Scroll.Offset.Y > before + 50, "the timeline went to the end with it");
        // :2106 the body sits one row spacing under the button, :2174-2175 its own padding 14 at the sides and 11 below.
        var body = LogicalQuery.SelfAndDescendants<Border>(line).Single(border => border.Padding == new Thickness(14, 0, 14, 11));
        Assert.Equal(open[0].Height + TimelineMetrics.RowSpacing + body.Bounds.Height, open[^1].Height, 1);
        tracking.IsPinnedToBottom = false; mount.Scroll.Offset = new Vector(0, 100); mount.Advance(400); tracking.EndActivity(); var held = mount.Scroll.Offset.Y;
        Click(line);
        var close = Sample(mount, line, 700);
        Assert.Equal(0, close[^1].Angle, 1); Assert.Equal(open[0].Height, close[^1].Height, 1);
        Assert.False(tracking.IsPinnedToBottom, "closing follows nothing");
        Assert.Equal(held, mount.Scroll.Offset.Y, 3);
        // The one animation both ways: the chevron's turn and the card's height follow the same curve forwards and backwards.
        var steps = Math.Min(open.Count, close.Count);
        for (var i = 0; i < steps; i++)
        {
            Assert.Equal(open[i].Angle / 90, 1 - close[i].Angle / 90, 0.02);
            Assert.Equal((open[i].Height - open[0].Height) / (open[^1].Height - open[0].Height), 1 - (close[i].Height - open[0].Height) / (open[^1].Height - open[0].Height), 0.03);
        }
        Assert.True(open.Count(sample => sample.Angle is > 1 and < 89) >= 3, "an animation, not a cut");
        // .snappy(duration: 0.24): it has arrived a quarter of a second in (one frame of the harness's lag), both ways.
        Assert.InRange(open[13].Angle, 88, 92); Assert.InRange(close[13].Angle, -2, 2);
    });

    // The words on the line: its text blocks and its letters (the running line's word and clock are letters, step 2 B).
    private static string[] Texts(Control control) => LogicalQuery.SelfAndDescendants<TextBlock>(control).Select(text => text.Text ?? "")
        .Concat(LogicalQuery.SelfAndDescendants<NthLetters>(control).Select(letters => letters.Text)).ToArray();

    private static double DistanceFromEnd(TimelineMount mount) => mount.Scroll.Extent.Height - mount.Scroll.Offset.Y - mount.Scroll.Viewport.Height;

    [Fact]
    [Trait("Req", "PR-CHAT-121")] // ThreadTimeline.swift:2107-2119,2152-2176,813-829 (the reveal that bypasses the jump-button guard); the cousin's 1:34-1:50 working line opened (no video of the open body)
    public void Opening_the_line_follows_the_end_unconditionally_opens_the_thinking_and_the_steps_and_rests_the_motion_while_it_glides() => ThreadTimelineRig.Run(headless, mount =>
    {
        RunningThread(mount);
        var line = Line(mount); var tracking = mount.Field<TimelineScrollTracking>("tracking");
        // Closed to start (:2091, the chronological flag is false), with the tooltip and the hint that name what a press does.
        Assert.Empty(LogicalQuery.SelfAndDescendants<Marrow.UI.Markdown.MarkdownView>(line)); Assert.Empty(LogicalQuery.SelfAndDescendants<WorkSteps>(line));
        var button = Chevron(line);
        Assert.Equal("Show these steps", ToolTip.GetTip(button)); Assert.Equal("Shows what the agent is doing", Avalonia.Automation.AutomationProperties.GetHelpText(button));
        // The reader has scrolled far up and the jump button is showing: the guarded reveal (:813-816) holds back there, the one the working line uses (:822-829) does not.
        mount.Scroll.Offset = new Vector(0, 50); mount.Advance(400); mount.State.ShowsJumpButton = true; tracking.IsPinnedToBottom = false; tracking.EndActivity();
        Assert.True(DistanceFromEnd(mount) > mount.Scroll.Viewport.Height);
        var box = ThreadTimeline.GetRevealTimelineEnd(line)!;
        box.Action(); mount.Advance(400);
        Assert.False(tracking.IsPinnedToBottom); Assert.True(DistanceFromEnd(mount) > mount.Scroll.Viewport.Height, "the guarded reveal left the reader where they were");
        Click(line);
        // :822-829 pins to the end, anchors growth to it, scrolls there and notes the movement: the shimmers and the card's staves rest while it glides.
        Assert.True(tracking.IsPinnedToBottom); Assert.True(tracking.IsReaderScrolling); Assert.True(ScrollActivity.Shared.Current.IsScrolling);
        // :2155-2176 the thinking and the steps arrive under the line with the soft appear, padded 14 at the sides and 11 below.
        var markdown = LogicalQuery.SelfAndDescendants<Marrow.UI.Markdown.MarkdownView>(line); Assert.Equal(2, markdown.Count());
        var thinking = Assert.IsType<StackPanel>(markdown.First().Parent); var steps = LogicalQuery.SelfAndDescendants<WorkSteps>(line).Single();
        Assert.Equal(new Thickness(24, 0, 0, 0), thinking.Margin); Assert.Equal(12, Marrow.UI.Markdown.MarkdownView.GetMarkdownPointSize(markdown.First())); Assert.True(Marrow.UI.Markdown.MarkdownView.GetMarkdownDimmed(markdown.First()));
        Assert.Equal(0, thinking.Opacity); Assert.Equal(0, steps.Opacity);
        var body = LogicalQuery.SelfAndDescendants<Border>(line).Single(border => border.Padding == new Thickness(14, 0, 14, 11));
        Assert.Contains(thinking, LogicalQuery.SelfAndDescendants<StackPanel>(body)); Assert.Contains(steps, LogicalQuery.SelfAndDescendants<WorkSteps>(body));
        Assert.Equal("Hide these steps", ToolTip.GetTip(button));
        mount.Advance(1000);
        Assert.Equal(1, thinking.Opacity); Assert.Equal(1, steps.Opacity);
        Assert.True(DistanceFromEnd(mount) < 2, "the timeline followed the end to show what opened");
        Assert.False(tracking.IsReaderScrolling); Assert.False(ScrollActivity.Shared.Current.IsScrolling);
        // :2091 the expansion is the line's own state: a step landing in the turn hands the line new work and leaves it open (the same line, its bar still the same bar).
        var bar = LogicalQuery.SelfAndDescendants<HydraProgressBar>(line).Single(); var rows = LogicalQuery.SelfAndDescendants<ToolRow>(line).Count();
        mount.SetEntries(mount.Runtime.Entries.Append(ToolEntry("tool-new", Turn)).ToList()); mount.Advance(600);
        Assert.Same(line, Line(mount)); Assert.Same(bar, LogicalQuery.SelfAndDescendants<HydraProgressBar>(line).Single());
        Assert.Equal(90, AngleOf(line), 1); Assert.Single(LogicalQuery.SelfAndDescendants<WorkSteps>(line)); Assert.Equal(2, LogicalQuery.SelfAndDescendants<ToolRow>(line).Count());
        Assert.Contains("4 commands", Texts(line)); // the bar was handed the new step and went on
        Assert.True(DistanceFromEnd(mount) < 2, "and the reader following it stays at the end");
        // :2108, :2152-2153 with nothing left to open the tooltip and the hint are empty and a press does nothing (the guard on canExpand).
        mount.SetEntries(new[] { User("prompt", Turn, "Please make the build faster") }); mount.Advance(800);
        Assert.Same(line, Line(mount)); Assert.Equal("", ToolTip.GetTip(Chevron(line))); Assert.Equal("", Avalonia.Automation.AutomationProperties.GetHelpText(Chevron(line)));
        var rest = line.Bounds.Height; Click(line); mount.Advance(500); Assert.Equal(rest, line.Bounds.Height, 3); Assert.Empty(LogicalQuery.SelfAndDescendants<WorkSteps>(line));
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-121 (the tooltip and the hint are empty when nothing can open, and the chevron's arrival; the opening, the follow and the body are proven apart): ThreadTimeline.swift:2152-2153
    public void A_running_line_with_nothing_to_open_has_no_chevron_tooltip_or_hint_and_its_time_sits_at_the_cards_edge() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.Model.Settings.ShowReasoning = true;
        mount.SetEntries(new[] { User("prompt", Turn, "hello") }); mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running); mount.Settle();
        var line = Line(mount); var header = Header(line); var button = Chevron(line);
        Assert.Equal(2, header.Children.Count);
        Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<SwiftImage>(line), image => image.SymbolName == "chevron.right");
        Assert.Equal("", ToolTip.GetTip(button)); Assert.Equal("", Avalonia.Automation.AutomationProperties.GetHelpText(button));
        // The time sits at the card's trailing padding (:2128 the empty slot a chevron would hold is not kept).
        Assert.Equal(header.Bounds.Width, header.Children[1].Bounds.Right, 1);
        // A press does nothing (:2108).
        var height = line.Bounds.Height; Click(line); mount.Advance(500);
        Assert.Equal(height, line.Bounds.Height, 3); Assert.Empty(LogicalQuery.SelfAndDescendants<WorkSteps>(line));
        // The first tool call gives it something to open: the chevron fades in and the time glides over to make room (:2128-2135, :2183).
        mount.SetEntries(new[] { User("prompt", Turn, "hello"), ToolEntry("tool-0", Turn) }); mount.Advance(20);
        var time = header.Children[1]; var chevron = Assert.Single(LogicalQuery.SelfAndDescendants<SwiftImage>(line), image => image.SymbolName == "chevron.right");
        Assert.Equal(0, chevron.Opacity);
        var timeShift = Assert.IsType<Avalonia.Media.TranslateTransform>(time.RenderTransform);
        mount.Advance(60); Assert.InRange(chevron.Opacity, 0.001, 0.999); Assert.True(timeShift.X > 1, $"the time glides over to make room: {timeShift.X}");
        mount.Advance(800); Assert.Equal(0, timeShift.X, 3);
        Assert.Equal(1, chevron.Opacity); Assert.Equal(header.Bounds.Width, chevron.Bounds.Right, 1); Assert.Equal(chevron.Bounds.Left - 8, time.Bounds.Right, 1);
        Assert.Equal("Show these steps", ToolTip.GetTip(Chevron(line)));
    });

    // A thread whose first turn ended without its marker (a run the next turn left behind), so its prose is an earlier segment's: a card of its own, finished.
    private static void EarlierSegmentThread(TimelineMount mount, bool withTool)
    {
        var earlier = Guid.NewGuid(); var date = SwiftDate.Now;
        var entries = new List<TimelineEntry> { User("first", earlier, "a question"), Reply("answer", earlier, "an answer the turn never closed") };
        if (withTool) entries.Insert(1, ToolEntry("ran", earlier));
        entries.Add(Entry("note", new TimelineItem.Content.Notice(new Marrow.Core.Models.Notice { NoticeLevel = Marrow.Core.Models.Notice.Level.Info, Message = "a note between the runs" })));
        entries.Add(User("second", Turn, "the next question")); entries.Add(Reply("second-answer", Turn, "an answer in progress"));
        mount.SetEntries(entries); mount.SetTurnStartedAt(date); mount.SetPhase(RuntimePhase.Running); mount.Settle();
    }

    [Fact]
    [Trait("Req", "UI-CHAT-043")] // ThreadTimeline.swift:2107-2119: click toggles; only OPENING follows the end (unconditionally), closing does not
    [Trait("Rule", "R-TEST-4")] // partial proof of UI-CHAT-043: the follow on open and not on close; the rest of the entry is the test below
    public void Only_opening_the_line_follows_the_end_and_closing_it_does_not() => ThreadTimelineRig.Run(headless, mount =>
    {
        RunningThread(mount);
        var line = Line(mount); var follows = 0;
        ThreadTimeline.GetRevealTimelineEnd(line)!.Follow = () => follows++;
        Click(line); mount.Advance(400);
        Assert.Equal(1, follows);
        Assert.Equal("Hide these steps", ToolTip.GetTip(Chevron(line)));
        Click(line); mount.Advance(400);
        Assert.Equal(1, follows);
        Assert.Equal("Show these steps", ToolTip.GetTip(Chevron(line)));
        Click(line); mount.Advance(400);
        Assert.Equal(2, follows);
    });

    [Fact]
    [Trait("Req", "UI-CHAT-043")] // ThreadTimeline.swift:2075-2197; HydraProgressBar.swift:504,558 (title and time fonts); Chrome.swift:79 (secondaryText); porting/extracts WorkingWords (the 22 words, 7 s)
    public void The_working_line_has_its_type_colours_and_each_of_its_states_the_word_the_summary_the_earlier_label_and_the_open_body() => ThreadTimelineRig.Run(headless, mount =>
    {
        // No tools yet, no reasoning: the rotating word with its ellipsis, in the card.
        mount.Model.Settings.ShowReasoning = false;
        mount.SetEntries(new[] { User("prompt", Turn, "hello") }); mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running); mount.Advance(100);
        var line = Line(mount); var seed = WorkingWords.Seed(SwiftUUID.UuidString(mount.Runtime.ThreadID));
        // Typography (:2123 the title .system(.body, .medium), HydraProgressBar.swift:504; :2125 the time .system(.subheadline), :558; :2131 the chevron).
        var header = Header(line); var theme = line.ActualThemeVariant;
        // OD-0136 amendment 2, step 2 B: the title is the thinking glyph (14) and the word in breathing letters (the title's type kept, the secondary colour: the page's --text2),
        // the time rolling digits in the mono face at the subheadline size, quieter than the word (the page's --text3).
        var titleLine = Assert.IsType<NthWorkingTitle>(Assert.Single(Assert.IsType<Grid>(header.Children[0]).Children)); var title = titleLine.Letters; var time = Assert.IsType<NthElapsedTime>(header.Children[1]).Letters;
        Assert.Equal(14, titleLine.Glyph.Width); Assert.Equal(14, titleLine.Glyph.Height); Assert.Equal(NthLettersMode.Breathe, title.Mode); Assert.Equal(NthLettersMode.Roll, time.Mode);
        Assert.Equal(13, TextElement.GetFontSize(title)); Assert.Equal(Avalonia.Media.FontWeight.Medium, TextElement.GetFontWeight(title)); Assert.Equal(11, TextElement.GetFontSize(time));
        Assert.Equal(SwiftFont.System(SwiftFont.TextStyle.Subheadline, SwiftFont.Design.Monospaced, SwiftFont.Weight.Medium).Resolve().FontFamily, TextElement.GetFontFamily(time));
        Assert.Equal(Marrow.UI.Chrome.Chrome.SecondaryText.Resolve(theme), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(title.Foreground).Color);
        Assert.Equal(Marrow.UI.Chrome.Chrome.PrimaryText.Opacity(0.58).Resolve(theme), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(time.Foreground).Color);
        // Colours (:2179-2180 the card's fill is the quaternary at 0.32).
        var shell = LogicalQuery.SelfAndDescendants<Border>(line).First(border => border.CornerRadius.TopLeft == TimelineMetrics.PillRadius);
        Assert.Equal(SwiftColor.Quaternary.Opacity(0.32).Resolve(theme), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(shell.Background).Color);
        Assert.Equal(18, shell.CornerRadius.TopLeft);
        // The word, and the whole line without a chevron (nothing to open).
        Assert.Contains(WorkingWords.Word(seed, 0) + "…", Texts(line)); Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<SwiftImage>(line), image => image.SymbolName == "chevron.right");
        // It changes every seven seconds (the fixed list of twenty-two, seeded by the thread): the new word takes the old one's place over the one motion, then it is alone.
        Assert.NotEqual(WorkingWords.Word(seed, 0), WorkingWords.Word(seed, 7));
        mount.Advance(6500); Assert.Contains(WorkingWords.Word(seed, 6) + "…", Texts(line));
        // The word changes on the clock's tick after its seventh second (not at a time the test can know to the frame): wait for it, and look at the moment it comes.
        var host = Assert.IsType<Grid>(header.Children[0]); var wordTitle = Assert.IsType<NthWorkingTitle>(Assert.Single(host.Children));
        for (var waited = 0; waited < 2000 && !Texts(line).Contains(WorkingWords.Word(seed, 7) + "…"); waited += 20) mount.Advance(20);
        Assert.Contains(WorkingWords.Word(seed, 7) + "…", Texts(line));
        // The new word arrives letter by letter inside the one title (it is not swapped for another): the old word is still the state's previous word while its letters leave.
        Assert.Same(wordTitle, Assert.Single(host.Children));
        Assert.Equal(WorkingWords.Word(seed, 6) + "…", Assert.IsType<NthLettersState>(wordTitle.Letters.Snapshot().State).Previous!.Text);
        mount.Advance(600); Assert.Single(host.Children);
        Assert.DoesNotContain(WorkingWords.Word(seed, 0) + "…", Texts(line));
        // A tool call is running: the label is the group's summary (:2104), and there is something to open.
        mount.SetEntries(new[] { User("prompt", Turn, "hello"), ToolEntry("tool-0", Turn) }); mount.Advance(800);
        Assert.Contains("Running commands", Texts(line)); Assert.DoesNotContain(WorkingWords.Word(seed, 7) + "…", Texts(line));
        var chevron = Assert.Single(LogicalQuery.SelfAndDescendants<SwiftImage>(line), image => image.SymbolName == "chevron.right");
        Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Caption2, SwiftFont.Weight.Semibold), SwiftImage.GetFont(chevron));
        Assert.Equal(SwiftColor.Tertiary.Resolve(theme), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(chevron.Foreground).Color);
        // The card on, the bar drawn; off, a badge with none (the layout is PR-CHAT-117's).
        var progress = Assert.Single(LogicalQuery.SelfAndDescendants<HydraProgressBar>(line));
        Assert.Equal(Marrow.UI.Chrome.Chrome.Accent, (SwiftColor)typeof(HydraProgressBar).GetField("tint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(progress)!); // :2139 the bar is tinted with Chrome.accent
        mount.Model.Settings.ShowsWorkingCard = false; mount.Advance(300); Assert.Empty(LogicalQuery.SelfAndDescendants<HydraProgressBar>(line)); mount.Model.Settings.ShowsWorkingCard = true; mount.Advance(300);
        // Open: the live steps beneath, padded 14 at the sides and 11 below (:2174-2175).
        Click(line); mount.Advance(900);
        var body = LogicalQuery.SelfAndDescendants<Border>(line).Single(border => border.Padding == new Thickness(14, 0, 14, 11));
        Assert.Single(LogicalQuery.SelfAndDescendants<WorkSteps>(body)); Assert.Equal("Hide these steps", ToolTip.GetTip(Chevron(line)));
        // An earlier segment, finished and holding no tool: "Earlier steps", its clock stopped at the span it ran (:2102-2104, :2125).
        EarlierSegmentThread(mount, withTool: false);
        var earlierLine = LogicalQuery.SelfAndDescendants<WorkingIndicator>(mount.View).First(candidate => Texts(candidate).Contains("Earlier steps"));
        var frozen = Texts(Header(earlierLine).Children[1]).Single(); mount.Advance(3000);
        Assert.Equal(frozen, Texts(Header(earlierLine).Children[1]).Single());
        // One that held a tool call: the label is the summary of its work, derived the same way (:2104).
        EarlierSegmentThread(mount, withTool: true);
        var work = new ValueList<TimelineEntry>(mount.Runtime.Entries.Where(entry => entry.Id is "ran" or "answer"));
        Assert.Contains(LogicalQuery.SelfAndDescendants<WorkingIndicator>(mount.View), candidate => Texts(candidate).Contains(WorkGroupSummary.Text(work)) && !Texts(candidate).Contains("Earlier steps"));
    });

    [Fact]
    [Trait("Req", "UI-CHAT-043")] // Partial proof UI-CHAT-043
    [Trait("Rule", "R-TEST-4")] // UI-CHAT-043 (the label's change, ThreadTimeline.swift:2182 .animation(Self.change, value: label)); OD-0136 amendment 2, step 2 B replaces the cross dissolve (capture comparison 2-chat-use.mov 94.943-95.20) with the breathing letters' word change: the old letters leave, the new ones rise in
    public void The_word_changes_letter_by_letter_inside_the_one_title_instead_of_cross_dissolving_two() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.Model.Settings.ShowReasoning = false;
        mount.SetEntries(new[] { User("prompt", Turn, "hello") }); mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running); mount.Advance(100);
        var line = Line(mount); var seed = WorkingWords.Seed(SwiftUUID.UuidString(mount.Runtime.ThreadID)); var host = Assert.IsType<Grid>(Header(line).Children[0]);
        var title = Assert.IsType<NthWorkingTitle>(Assert.Single(host.Children));
        var first = WorkingWords.Word(seed, 6) + "…"; var next = WorkingWords.Word(seed, 7) + "…"; mount.Advance(6700);
        while (!Texts(line).Contains(next)) mount.Advance(10);
        // One title throughout (no second one fades in beside it); its letters hold the new word and still know the old one, which leaves up 4 px over 0.24 s while the new rises from 5 px over 0.36 s.
        Assert.Same(title, Assert.Single(host.Children));
        var state = Assert.IsType<NthLettersState>(title.Letters.Snapshot().State);
        Assert.Equal(next, state.Current!.Text); Assert.Equal(first, state.Previous!.Text);
        Assert.True(state.ChangedAt > double.NegativeInfinity);
        Assert.Equal(NthLettersMode.Breathe, title.Letters.Mode);
    });

    [Fact]
    [Trait("Req", "UI-CHAT-043")] // Partial proof UI-CHAT-043
    [Trait("Rule", "R-TEST-4")] // Partial proof UI-CHAT-043 (the chevron comes with something to open and the time glides over to make room, ThreadTimeline.swift:2128-2135,2183 .animation(Self.change, value: canExpand)); capture comparison: 2-chat-use.mov 95.803-96.078 (the time's left edge, frame by frame: evidence/ThreadTimeline/chev)
    public void The_time_glides_over_for_the_chevron_along_the_reference_runtimes_curve() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.Model.Settings.ShowReasoning = true;
        mount.SetEntries(new[] { User("prompt", Turn, "hello") }); mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running); mount.Settle();
        var line = Line(mount); var time = Header(line).Children[1]; var restLeft = time.TranslatePoint(default, line)!.Value.X; var laidOutAt = time.Bounds.Left;
        // Something to open: the chevron joins the header and the time, pushed left by its width and the header's 8 points of spacing, glides back from where it stood.
        mount.SetEntries(new[] { User("prompt", Turn, "hello"), ToolEntry("tool-0", Turn) });
        var chevron = Assert.Single(LogicalQuery.SelfAndDescendants<SwiftImage>(line), image => image.SymbolName == "chevron.right");
        var room = laidOutAt - time.Bounds.Left; Assert.Equal(8 + chevron.Bounds.Width, room, 1); // the header's 8 points of spacing and the chevron's width. (On the reference runtime the whole shift is 17 points: the chevron's symbol is 9 wide there and the port's is 12, so the port's travel is 20: not compared, the curve's shape is.)
        double Shift() => restLeft - time.TranslatePoint(default, line)!.Value.X; // how far left of where it stood the time is now
        // The time's left edge on the reference runtime, in points left of where it stood (17 in all; its fit: smooth(duration: 0.3) from the first moving frame), at seconds after that frame.
        var measured = new (double Seconds, double Shift)[] { (0.0183, 0), (0.040, 4), (0.0617, 6), (0.0833, 9), (0.105, 11), (0.1267, 13), (0.148, 14), (0.168, 15), (0.19, 15), (0.2117, 16), (0.230, 16), (0.2517, 16), (0.273, 17), (0.2933, 17) };
        var elapsed = 0.0;
        foreach (var (seconds, shift) in measured)
        {
            while (elapsed < seconds - 0.0005) { mount.Advance(10); mount.Frames.Advance(TimeSpan.Zero); elapsed += 0.010; }
            Assert.True(Math.Abs(Shift() / room - shift / 17.0) <= 0.12, $"at {seconds}: the time is {Shift() / room} of the way over, the capture's is {shift / 17.0}");
        }
        mount.Advance(600); Assert.Equal(room, Shift(), 1);
    });

    [Fact]
    [Trait("Req", "UI-CHAT-043")] // Partial proof UI-CHAT-043 and PR-CHAT-117
    [Trait("Rule", "R-TEST-4")] // Partial proof UI-CHAT-043 and PR-CHAT-117 (the chevron goes with nothing left to open: ThreadTimeline.swift:2128-2135 `.transition(.opacity)`, :2183 `.animation(Self.change, value: canExpand)`; OD-0076: every change is transitioned; ED-255 (3))
    public void The_chevron_fades_out_where_it_stood_when_nothing_is_left_to_open_and_the_time_glides_back_over_the_same_motion() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.SetEntries(new[] { User("prompt", Turn, "hello"), ToolEntry("tool-0", Turn) }); mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running); mount.Settle();
        var line = Line(mount); var header = Header(line); var time = header.Children[1];
        var chevron = Assert.Single(LogicalQuery.SelfAndDescendants<SwiftImage>(line), image => image.SymbolName == "chevron.right"); Assert.Equal(1, chevron.Opacity); Assert.Equal(3, header.Children.Count);
        var stood = chevron.TranslatePoint(default, header)!.Value.X; var timeShift = Assert.IsType<Avalonia.Media.TranslateTransform>(time.RenderTransform);
        // The tool call is done and nothing is left on the line to open.
        mount.SetEntries(new[] { User("prompt", Turn, "hello") }); mount.Advance(80);
        Assert.Equal("", ToolTip.GetTip(Chevron(line))); // nothing opens at once, whatever the glyph still does
        // The chevron is still drawn where it stood, fading ...
        Assert.Contains(chevron, LogicalQuery.SelfAndDescendants<SwiftImage>(line)); Assert.InRange(chevron.Opacity, 0.001, 0.999);
        Assert.Equal(stood, chevron.TranslatePoint(default, header)!.Value.X, 1);
        // ... while the time is laid out at the card's edge and glides over from where it stood.
        Assert.Equal(header.Bounds.Width, time.Bounds.Right, 1); Assert.True(timeShift.X < -1, $"the time glides back from where it stood: {timeShift.X}");
        mount.Advance(800);
        Assert.DoesNotContain(chevron, LogicalQuery.SelfAndDescendants<SwiftImage>(line)); Assert.Equal(2, header.Children.Count); Assert.Equal(0, timeShift.X, 3);
        Assert.Equal(header.Bounds.Width, time.Bounds.Right, 1);
    });

    [Fact]
    [Trait("Req", "UI-CHAT-044")] // ThreadTimeline.swift:2157-2168,2278 (the thinking rows, only with the show-reasoning setting); TimelineRows.swift (reasoning is no row of its own)
    public void Thinking_rows_exist_only_behind_the_chevron_with_the_setting_on_indented_by_the_icon_column_and_arrive_softly() => ThreadTimelineRig.Run(headless, mount =>
    {
        // Reasoning is never a row of its own: the thinking is in the thread's entries and nowhere on screen until it is opened.
        RunningThread(mount, steps: 0);
        var line = Line(mount);
        Assert.DoesNotContain(mount.Window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text is { } shown && shown.Contains("First I look", StringComparison.Ordinal));
        Assert.Empty(LogicalQuery.SelfAndDescendants<Marrow.UI.Markdown.MarkdownView>(line));
        // The setting on and thinking produced: it opens, indented 24 (the icon width 16 and its 8), in rows 20 apart, at point size 12 and dimmed.
        Click(line);
        var markdown = LogicalQuery.SelfAndDescendants<Marrow.UI.Markdown.MarkdownView>(line).ToList(); Assert.Equal(2, markdown.Count);
        var column = Assert.IsType<StackPanel>(markdown[0].Parent); Assert.Same(column, markdown[1].Parent);
        Assert.Equal(new Thickness(TimelineMetrics.IconWidth + TimelineMetrics.IconSpacing, 0, 0, 0), column.Margin); Assert.Equal(24, column.Margin.Left); Assert.Equal(20, column.Spacing);
        Assert.All(markdown, view => { Assert.Equal(12, Marrow.UI.Markdown.MarkdownView.GetMarkdownPointSize(view)); Assert.True(Marrow.UI.Markdown.MarkdownView.GetMarkdownDimmed(view)); });
        // The soft appear: invisible and 4 points down at the start, settled over its own time (Chrome.swift:938-960).
        Assert.Equal(0, column.Opacity); Assert.Equal(4, Assert.IsType<Avalonia.Media.TranslateTransform>(column.RenderTransform).Y);
        mount.Advance((int)(Animation.SoftAppear.Duration * 1000) + 600); Assert.Equal(1, column.Opacity); Assert.Equal(0, Assert.IsType<Avalonia.Media.TranslateTransform>(column.RenderTransform).Y);
        Assert.Equal(20, markdown[1].TranslatePoint(default, column)!.Value.Y - (markdown[0].TranslatePoint(default, column)!.Value.Y + markdown[0].Bounds.Height), 1.5);
        // The setting off: the same thinking is in the thread and none of it can be opened (no tool run to open either, so no chevron).
        Click(line); mount.Advance(800);
        mount.Model.Settings.ShowReasoning = false; mount.Advance(400);
        var after = Line(mount);
        Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<SwiftImage>(after), image => image.SymbolName == "chevron.right"); Assert.Empty(LogicalQuery.SelfAndDescendants<Marrow.UI.Markdown.MarkdownView>(after));
        // The setting on and the turn has produced no reasoning: nothing to show.
        mount.Model.Settings.ShowReasoning = true; mount.SetEntries(new[] { User("prompt", Turn, "hello") }); mount.Advance(400);
        Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<SwiftImage>(Line(mount)), image => image.SymbolName == "chevron.right");
    });

    [Fact]
    [Trait("Req", "UI-CHAT-069")] // ThreadTimeline.swift:2104,2123; HydraProgressBar.swift:487-534 (HydraWorkingTitle at rest draws no breathing)
    public void The_earlier_steps_label_is_what_a_finished_segment_without_a_tool_call_shows_steady_and_it_still_opens() => ThreadTimelineRig.Run(headless, mount =>
    {
        EarlierSegmentThread(mount, withTool: false);
        var line = LogicalQuery.SelfAndDescendants<WorkingIndicator>(mount.View).First(candidate => Texts(candidate).Contains("Earlier steps"));
        var header = Header(line); var theme = line.ActualThemeVariant;
        // In the header in place of the rotating word, at the title's type, in the primary text colour.
        var label = LogicalQuery.SelfAndDescendants<TextBlock>(header.Children[0]).Single(text => text.Text == "Earlier steps");
        Assert.Equal(13, label.FontSize); Assert.Equal(Avalonia.Media.FontWeight.Medium, label.FontWeight);
        var primary = Marrow.UI.Chrome.Chrome.PrimaryText.Resolve(theme);
        Assert.All(label.Foreground switch { Avalonia.Media.IGradientBrush gradient => gradient.GradientStops.Select(stop => stop.Color), Avalonia.Media.ISolidColorBrush solid => new[] { solid.Color }, _ => [] }, color => Assert.Equal(primary, color));
        // No breathing: the label does not move in opacity through seconds of frames (a running line's does).
        var seen = new HashSet<double>(); for (var step = 0; step < 40; step++) { mount.Advance(100); seen.Add(label.Opacity); }
        Assert.Equal(new[] { 1.0 }, seen.ToArray());
        // It is never a rotating word and not a summary: the running line below it shows a word.
        Assert.DoesNotContain(Texts(line), text => text.EndsWith("…", StringComparison.Ordinal));
        // The line still opens: the segment's prose is behind it.
        Assert.Equal("Show these steps", ToolTip.GetTip(Chevron(line)));
        Click(line); mount.Advance(900);
        Assert.Single(LogicalQuery.SelfAndDescendants<WorkSteps>(line)); Assert.Equal("Hide these steps", ToolTip.GetTip(Chevron(line)));
        // A segment with a tool call shows the summary of its work instead; a running one the word.
        EarlierSegmentThread(mount, withTool: true);
        Assert.Contains(LogicalQuery.SelfAndDescendants<WorkingIndicator>(mount.View), candidate => Texts(candidate).Any(text => text.EndsWith("…", StringComparison.Ordinal)));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-CHAT-125")] // ThreadTimeline.swift:325-331 (every row gets chronological false),:2185-2188,:1912-1920,:1990-1997; TimelineRows.swift:1523-1536,:2499-2509; AppSettings.swift:128,237-239,719
    public void Whatever_the_chronological_setting_holds_the_working_card_and_a_finished_turn_open_folded(bool stored) => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.Model.Settings.ChronologicalTimeline = stored;
        RunningThread(mount, steps: 4);
        var line = Line(mount);
        // :2155 `if isExpanded, canExpand`: closed to start, no thinking and no steps shown until it is opened.
        Assert.Empty(LogicalQuery.SelfAndDescendants<Marrow.UI.Markdown.MarkdownView>(line)); Assert.Empty(LogicalQuery.SelfAndDescendants<WorkSteps>(line));
        Assert.Equal(0, AngleOf(line), 1);
        // Opened it shows only the two latest steps, with the earlier ones behind one line (TimelineRows.swift:1523-1536).
        Click(line); mount.Advance(900);
        var steps = LogicalQuery.SelfAndDescendants<WorkSteps>(line).Single();
        Assert.Equal(2, LogicalQuery.SelfAndDescendants<ToolRow>(steps).Count()); Assert.Contains("2 earlier steps", Texts(steps));
        // What goes into the card is the same either way: the work and the prose of the turn, never the prompt (:1959-1976).
        Assert.DoesNotContain("Please make the build faster", Texts(line));
        // The turn ends: its block folds to its answer, whatever the setting says (:2507-2508 `isExpanded` from the flag the timeline passes, which is false).
        var entries = mount.Runtime.Entries.Append(Reply("final", Turn, "The workflow now caches the store.")).Append(End("end", Turn)).ToList();
        mount.SetEntries(entries); mount.SetPhase(RuntimePhase.Idle); mount.Advance(900);
        var turn = mount.Blocks().Last(block => block.Block is DisplayBlock.Turn { Summary: not null });
        Assert.Empty(LogicalQuery.SelfAndDescendants<ToolRow>(turn.Row)); Assert.Empty(LogicalQuery.SelfAndDescendants<WorkingIndicator>(turn.Row));
        Assert.Contains(LogicalQuery.SelfAndDescendants<FoldedAnswerView>(turn.Row), answer => LogicalQuery.SelfAndDescendants<Marrow.UI.Markdown.MarkdownView>(answer).Any(view => view.Text.Contains("caches the store", StringComparison.Ordinal)));
        var chevron = Assert.Single(LogicalQuery.SelfAndDescendants<SwiftImage>(turn.Row), image => image.SymbolName == "chevron.right");
        Assert.Equal(0, Assert.IsType<Avalonia.Media.RotateTransform>(chevron.RenderTransform).Angle);
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-155 (the timeline's half: a running turn renders its heads after its other rows and before the working line, and under the folded turn once it ends); ThreadTimeline.swift:1955-1958,1985-1989; the folded turn's own layout is TimelineRowsFinTurnBlockTests
    public void A_running_turn_shows_its_heads_after_its_rows_and_above_the_working_line_and_the_heads_stay_under_the_answer_when_it_folds() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.Model.Settings.ShowReasoning = true;
        var entries = new List<TimelineEntry> { User("prompt", Turn, "send out two heads"), Entry("note", new TimelineItem.Content.Notice(new Marrow.Core.Models.Notice { NoticeLevel = Marrow.Core.Models.Notice.Level.Warning, Message = "a warning" }), Turn), ToolEntry("head-a", Turn, head: true), ToolEntry("run", Turn), ToolEntry("head-b", Turn, head: true) };
        mount.SetEntries(entries); mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running); mount.Settle();
        var block = mount.Blocks().Last().Row;
        double Top(Control control) => control.TranslatePoint(default, mount.Column)!.Value.Y;
        var user = LogicalQuery.SelfAndDescendants<UserMessageRow>(block).Single(); var notice = LogicalQuery.SelfAndDescendants<NoticeRow>(block).Single();
        var heads = LogicalQuery.SelfAndDescendants<HydraHeadsList>(block).Single(); var line = LogicalQuery.SelfAndDescendants<WorkingIndicator>(block).Single();
        // The heads are one list after the turn's own rows and before the working line, never inside it.
        Assert.True(Top(user) < Top(notice) && Top(notice) < Top(heads) && Top(heads) + heads.Bounds.Height <= Top(line), "rows, heads, working line");
        Assert.Equal(2, LogicalQuery.SelfAndDescendants<ToolRow>(heads).Count()); Assert.Empty(LogicalQuery.SelfAndDescendants<HydraHeadsList>(line));
        // Their badges stack 8 points apart (not at the row spacing).
        var badges = LogicalQuery.SelfAndDescendants<ToolRow>(heads).ToList();
        Assert.Equal(8, Top(badges[1]) - (Top(badges[0]) + badges[0].Bounds.Height), 1);
        // The turn ends with an answer: folded, the heads sit under the response and the working line is gone.
        mount.SetEntries(entries.Append(Reply("answer", Turn, "Both heads are out.")).Append(End("end", Turn)).ToList()); mount.SetPhase(RuntimePhase.Idle); mount.Advance(900);
        var folded = mount.Blocks().Last().Row; var answer = LogicalQuery.SelfAndDescendants<FoldedAnswerView>(folded).Single(); var under = LogicalQuery.SelfAndDescendants<HydraHeadsList>(folded).Single();
        Assert.True(Top(answer) + answer.Bounds.Height <= Top(under), "the heads under the response");
        Assert.Empty(LogicalQuery.SelfAndDescendants<WorkingIndicator>(folded));
    });
}
