using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/UI/Markdown/StreamVeil.swift:4-24 (a flush is committed to layout at once and the characters that
// just arrived dissolve over the next beat), :200-339 VeiledText, :344-431 LinkTextVeil; DroppyCode/UI/Markdown/
// LinkParagraphView.swift:42, :55-57 (the veil is fed after every render); DroppyCode/App/Views/Chat/TimelineRows.swift:
// 1361-1390 (AssistantMessageRow's body is evaluated again on every flush; its MarkdownView and the veil's @State survive).
//
// UI-THEME-047: the veil's state machine matched the Swift and was proven in isolation, but in the running port every
// flush replaced the whole markdown tree, so each VeiledText was born adopting its text as the baseline and no chunk
// could ever register: the fade never ran. And the shared veil clock never told the paragraphs that it had moved. These
// tests drive the REAL rows the way the timeline does, flush after flush, on the manual frame and time sources.
[Collection("Headless")]
public sealed class TimelineRowsStreamVeilTests(HeadlessSession headless)
{
    private static TimelineEntry Streaming(string text) =>
        new(new TimelineItem(null, new TimelineItem.Content.Assistant(new AssistantMessage { Text = text, IsStreaming = true })));

    private static void Flush(TimelineEntry entry, string text, bool streaming = true) =>
        entry.Item = entry.Item with { TimelineItemContent = new TimelineItem.Content.Assistant(new AssistantMessage { Text = text, IsStreaming = streaming }) };

    // The text of every run the row paints, with the opacity it is painted at (brush opacity times the colour's alpha).
    private static (string Text, double Opacity)[] Runs(Control row) =>
        row.GetVisualDescendants().OfType<TextBlock>()
            .SelectMany(block => block.Inlines?.OfType<Run>() ?? [])
            .Where(run => !string.IsNullOrEmpty(run.Text))
            .Select(run => (run.Text!, run.Foreground is ISolidColorBrush brush ? brush.Opacity * brush.Color.A / 255.0 : 1.0))
            .ToArray();

    // StreamVeil.swift:42, :93: the text shows through at 1 - (1 - p)^1.6 after p of the chunk's fade.
    private static double Alpha(double progress) => 1 - Math.Pow(1 - progress, 1.6);

    // VeilClock.shared is one clock for the whole process (StreamVeil.swift:164) whose display link belongs to the frame source that was current when it
    // started: a link a previous test left running would never be ticked by this test's frames. Each test starts the clock afresh.
    private static void FreshVeilClock()
    {
        foreach (var name in new[] { "link", "deadline" })
            typeof(VeilClock).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(VeilClock.Shared, null);
    }

    private static void Tick(ThreadRuntimePart12Harness h, ManualFrameSource frames, Avalonia.Controls.Window window, double milliseconds)
    {
        h.Main.AdvanceMilliseconds(milliseconds);
        h.Main.OnMain(() => { frames.Advance(TimeSpan.FromMilliseconds(milliseconds)); Settle(window); });
    }

    [Fact]
    [Trait("Req", "UI-THEME-047")] // DroppyCode/UI/Markdown/StreamVeil.swift:10-21, :273-279
    public void A_streaming_reply_fades_in_each_flushs_words_once_through_the_real_row() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        FreshVeilClock();
        TimelineEntry entry = null!;
        AssistantMessageRow row = null!;
        Avalonia.Controls.Window window = null!;
        h.Main.OnMain(() =>
        {
            entry = Streaming("Hello");
            row = new AssistantMessageRow(entry, null, h.Runtime);
            window = Show(row);
        });

        // :18-19 what is on screen when a veil attaches is adopted as the baseline and never fades.
        var seen = Runs(row);
        Assert.Equal("Hello", Assert.Single(seen).Text);
        var settled = seen[0].Opacity;
        Assert.True(settled > 0.5, "the settled text is drawn in the primary text colour");
        Assert.Equal(0, frames.Pending); // nothing dissolves, so nothing ticks

        // A flush appends " there": it is on screen at once (nothing moves, nothing resizes) but veiled, and the veil clock runs.
        h.Main.OnMain(() => { Flush(entry, "Hello there"); Settle(window); });
        var atFlush = Runs(row);
        Assert.Equal(["Hello", " there"], atFlush.Select(run => run.Text));
        Assert.Equal(settled, atFlush[0].Opacity, 0.001); // the text already there is untouched
        Assert.Equal(0, atFlush[1].Opacity, 0.001); // :93 a chunk starts at nothing

        // :39-41 the first chunk fades over the seed cadence 0.16 s x 3, clamped to 0.12-0.40 s: 0.40 s; at 0.1 s it is a quarter through.
        Tick(h, frames, window, 100);
        var quarter = Runs(row);
        Assert.Equal(["Hello", " there"], quarter.Select(run => run.Text));
        Assert.Equal(settled, quarter[0].Opacity, 0.001);
        Assert.Equal(settled * Alpha(0.25), quarter[1].Opacity, 0.01);
        Assert.Equal(1, frames.Pending); // the veil clock is running

        // A second flush while the first still dissolves: a rolling veil. The first chunk fades exactly once (it carries on from where it was,
        // not from the start), the new tail begins at nothing.
        h.Main.OnMain(() => { Flush(entry, "Hello there again"); Settle(window); });
        var rolling = Runs(row);
        Assert.Equal(["Hello", " there", " again"], rolling.Select(run => run.Text));
        Assert.Equal(settled, rolling[0].Opacity, 0.001);
        Assert.Equal(settled * Alpha(0.25), rolling[1].Opacity, 0.01);
        Assert.Equal(0, rolling[2].Opacity, 0.001);

        // The first chunk settles 0.4 s after it arrived (0.3 s from here); the second is three quarters through by then.
        Tick(h, frames, window, 300);
        var firstDone = Runs(row);
        Assert.Equal("Hello there again", string.Concat(firstDone.Select(run => run.Text)));
        Assert.Equal(settled, firstDone.Single(run => run.Text.Contains("there", StringComparison.Ordinal)).Opacity, 0.001);
        Assert.Equal(settled * Alpha(0.75), firstDone.Single(run => run.Text == " again").Opacity, 0.01);

        // Everything has settled: one plain run in the primary colour, and the veil clock has stopped.
        Tick(h, frames, window, 200);
        var done = Runs(row);
        Assert.Equal("Hello there again", Assert.Single(done).Text);
        Assert.Equal(settled, done[0].Opacity, 0.001);
        Assert.Equal(0, frames.Pending);
        h.Main.OnMain(() => Close(window));
    });

    [Fact]
    [Trait("Req", "UI-THEME-047")] // DroppyCode/UI/Markdown/StreamVeil.swift:291-295 (once the reply has finished the veil is emptied, a settled paragraph keeps no copy of itself); MarkdownView.swift:69-83
    public void The_row_of_a_reply_that_has_finished_streaming_keeps_no_copy_of_it_and_still_shows_it() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        FreshVeilClock();
        TimelineEntry entry = null!;
        AssistantMessageRow row = null!;
        Avalonia.Controls.Window window = null!;
        h.Main.OnMain(() =>
        {
            entry = Streaming("Hello");
            row = new AssistantMessageRow(entry, null, h.Runtime);
            window = Show(row);
            Flush(entry, "Hello there");
            Settle(window);
        });
        Tick(h, frames, window, 600); // "Hello there" has settled: a quiet beat between two flushes
        // While the reply streams the baseline stays, or the next flush would arrive unveiled (StreamVeil.swift:291-292).
        Assert.True(row.Veils.HoldsText);
        Assert.True(row.Veils.Places > 0);

        // The reply is done: the row's veils go, the text stays (the row draws prose runs, MarkdownView.swift:71-83).
        try
        {
            h.Main.OnMain(() => { Flush(entry, "Hello there", streaming: false); Settle(window); });
            Assert.False(row.Veils.HoldsText, "no copy of the finished reply is kept");
            Assert.Equal(0, row.Veils.Places);
            Assert.Equal("Hello there", Assert.Single(row.GetVisualDescendants().OfType<ParagraphTextPresenter>()).Text.String);
        }
        finally
        {
            h.Main.OnMain(() => Close(window)); // a window left open makes the next layout fail on its own, and hides the assertion that failed
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-047")] // DroppyCode/UI/Markdown/StreamVeil.swift:18-19 (a row scrolled back into view adopts what is on screen)
    public void A_row_that_scrolls_away_and_back_adopts_what_it_shows_instead_of_fading_it_all_in() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        FreshVeilClock();
        TimelineEntry entry = null!;
        AssistantMessageRow row = null!;
        StackPanel panel = null!;
        Avalonia.Controls.Window window = null!;
        h.Main.OnMain(() =>
        {
            entry = Streaming("Hello");
            row = new AssistantMessageRow(entry, null, h.Runtime);
            panel = new StackPanel();
            panel.Children.Add(row);
            window = Show(panel);
            Flush(entry, "Hello there");
            Settle(window);
        });
        Tick(h, frames, window, 600); // "Hello there" has settled
        var settled = Runs(row)[0].Opacity;

        // The row leaves the screen while the reply goes on streaming, and comes back.
        h.Main.OnMain(() =>
        {
            panel.Children.Remove(row);
            Settle(window);
            Flush(entry, "Hello there and then some");
            Settle(window);
            panel.Children.Add(row);
            Settle(window);
        });
        // What it shows when it comes back is adopted as the baseline: nothing of it dissolves, not even the words that arrived while it was away.
        var back = Runs(row);
        Assert.Equal("Hello there and then some", Assert.Single(back).Text);
        Assert.Equal(settled, back[0].Opacity, 0.001);

        // Only what arrives from then on dissolves.
        h.Main.OnMain(() => { Flush(entry, "Hello there and then some more"); Settle(window); });
        var more = Runs(row);
        Assert.Equal(["Hello there and then some", " more"], more.Select(run => run.Text));
        Assert.Equal(settled, more[0].Opacity, 0.001);
        Assert.Equal(0, more[1].Opacity, 0.001);
        Tick(h, frames, window, 1000); // let the veil clock stop before the next test
        Assert.Equal(0, frames.Pending);
        h.Main.OnMain(() => Close(window));
    });

    [Fact]
    [Trait("Req", "UI-THEME-047")] // DroppyCode/App/Views/Chat/TimelineRows.swift:1546-1553 (the narration is a MarkdownView of the streaming entry)
    public void The_narration_between_two_steps_fades_in_its_streamed_words() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        FreshVeilClock();
        TimelineEntry said = null!;
        WorkSteps steps = null!;
        Avalonia.Controls.Window window = null!;
        h.Main.OnMain(() =>
        {
            said = Streaming("now the build");
            steps = new WorkSteps(new ValueList<TimelineEntry>([ToolEntry(Call(ToolCall.Kind.Read, "Read a")), said, ToolEntry(Call(ToolCall.Kind.Command, "make"))]), h.Runtime, Binding<bool>.Constant(true));
            window = Show(steps);
        });
        var narration = Assert.Single(Mounted<MarkdownView>(steps));
        var settled = Runs(narration)[0].Opacity;
        h.Main.OnMain(() => { Flush(said, "now the build runs"); Settle(window); });
        var atFlush = Runs(Assert.Single(Mounted<MarkdownView>(steps)));
        Assert.Equal(["now the build", " runs"], atFlush.Select(run => run.Text));
        Assert.Equal(settled, atFlush[0].Opacity, 0.001);
        Assert.Equal(0, atFlush[1].Opacity, 0.001);
        Tick(h, frames, window, 100);
        Assert.Equal(settled * Alpha(0.25), Runs(Assert.Single(Mounted<MarkdownView>(steps)))[1].Opacity, 0.01);
        Tick(h, frames, window, 1000);
        Assert.Equal(0, frames.Pending);
        h.Main.OnMain(() => Close(window));
    });

    [Fact]
    [Trait("Req", "UI-THEME-047")] // DroppyCode/App/Views/Chat/ThreadTimeline.swift:2156-2168 (the thinking steps are MarkdownViews of the streaming text)
    public void The_thinking_steps_fade_in_their_streamed_words() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        FreshVeilClock();
        WorkingIndicator indicator = null!;
        Avalonia.Controls.Window window = null!;
        h.Main.OnMain(() =>
        {
            indicator = new WorkingIndicator(h.Runtime, SwiftDate.Now, 1, new ValueList<ThinkingStep>([new ThinkingStep("Hmm", true)]), new([]), new([]), false, chronological: true);
            window = Show(indicator);
        });
        var settled = Runs(indicator)[0].Opacity;
        h.Main.OnMain(() => { indicator.UpdateValues(new ValueList<ThinkingStep>([new ThinkingStep("Hmm let me see", true)]), new([]), new([]), false); Settle(window); });
        var atFlush = Runs(Assert.Single(Mounted<MarkdownView>(indicator)));
        Assert.Equal(["Hmm", " let me see"], atFlush.Select(run => run.Text));
        Assert.Equal(settled, atFlush[0].Opacity, 0.001);
        Assert.Equal(0, atFlush[1].Opacity, 0.001);
        // The steps are dimmed (\.markdownDimmed): what arrives dissolves up to the dimmed primary, 0.7 of the primary colour (StreamVeil.swift:311).
        Tick(h, frames, window, 100);
        var quarter = Runs(Assert.Single(Mounted<MarkdownView>(indicator)))[1].Opacity;
        Assert.InRange(quarter, 0.01, settled - 0.01);
        Tick(h, frames, window, 100);
        var half = Runs(Assert.Single(Mounted<MarkdownView>(indicator)))[1].Opacity;
        Assert.True(half > quarter, "the arriving words keep dissolving");
        Tick(h, frames, window, 1000);
        Assert.Equal(["Hmm let me see"], Runs(Assert.Single(Mounted<MarkdownView>(indicator))).Select(run => run.Text));
        h.Main.OnMain(() => Close(window));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "UI-THEME-047")] // DroppyCode/App/Views/Chat/ThreadTimeline.swift:2156-2168 (ForEach over the steps: each step keeps its own @State, StreamVeil.swift:245)
    public void Each_thinking_step_keeps_a_veil_of_its_own_so_words_arriving_in_the_second_veil_only_there(bool firstStillStreaming) => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        FreshVeilClock();
        WorkingIndicator indicator = null!;
        Avalonia.Controls.Window window = null!;
        const string first = "First I read the file"; // longer than the second step's words, so a veil that was shared would reach into it
        void Steps(string second) =>
            indicator.UpdateValues(new ValueList<ThinkingStep>([new ThinkingStep(first, firstStillStreaming), new ThinkingStep(second, true)]), new([]), new([]), false);
        h.Main.OnMain(() =>
        {
            indicator = new WorkingIndicator(h.Runtime, SwiftDate.Now, 1, new ValueList<ThinkingStep>([new ThinkingStep(first, firstStillStreaming), new ThinkingStep("Then", true)]), new([]), new([]), false, chronological: true);
            window = Show(indicator);
        });
        MarkdownView Step(int index) => Mounted<MarkdownView>(indicator).ElementAt(index);
        Assert.Equal(2, Mounted<MarkdownView>(indicator).Count());
        var settled = Runs(Step(1))[0].Opacity; // what a step shows when it attaches is the baseline: the dimmed primary colour

        // A step that has finished is drawn as prose runs (MarkdownView.swift:71-83), not as veiled text; one that still streams is veiled text.
        void FirstStepIsOneSettledRun()
        {
            if (!firstStillStreaming) { Assert.Empty(Runs(Step(0))); return; }
            Assert.Equal(first, Assert.Single(Runs(Step(0))).Text);
            Assert.Equal(settled, Runs(Step(0))[0].Opacity, 0.001);
        }

        // The second step grows: its words dissolve and the first step, which gained nothing, is untouched.
        h.Main.OnMain(() => { Steps("Then I edit"); Settle(window); });
        Assert.Equal(["Then", " I edit"], Runs(Step(1)).Select(run => run.Text));
        Assert.Equal(settled, Runs(Step(1))[0].Opacity, 0.001);
        Assert.Equal(0, Runs(Step(1))[1].Opacity, 0.001);
        FirstStepIsOneSettledRun();

        // It grows again while its first words are still dissolving (the first step is built again for every flush, with a veil in flight): the
        // words that arrived before carry on, only the new ones start from nothing, and none of it reaches the first step.
        Tick(h, frames, window, 100);
        h.Main.OnMain(() => { Steps("Then I edit it"); Settle(window); });
        var second = Runs(Step(1));
        Assert.Equal(["Then", " I edit", " it"], second.Select(run => run.Text));
        Assert.Equal(settled, second[0].Opacity, 0.001);
        Assert.InRange(second[1].Opacity, 0.01, settled - 0.01);
        Assert.Equal(0, second[2].Opacity, 0.001);
        FirstStepIsOneSettledRun();

        Tick(h, frames, window, 1000);
        Assert.Equal(["Then I edit it"], Runs(Step(1)).Select(run => run.Text));
        h.Main.OnMain(() => Close(window));
    });

    [Fact]
    [Trait("Req", "UI-THEME-047")] // DroppyCode/UI/Markdown/StreamVeil.swift:18-19 (what is on screen when a veil attaches is the baseline)
    public void The_thinking_steps_adopt_what_they_show_when_they_are_opened_again() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        FreshVeilClock();
        WorkingIndicator indicator = null!;
        Avalonia.Controls.Window window = null!;
        void Steps(string text) => indicator.UpdateValues(new ValueList<ThinkingStep>([new ThinkingStep(text, true)]), new([]), new([]), true);
        h.Main.OnMain(() =>
        {
            indicator = new WorkingIndicator(h.Runtime, SwiftDate.Now, 1, new ValueList<ThinkingStep>([new ThinkingStep("Hmm", true)]), new([]), new([]), true);
            window = Show(indicator);
        });
        Button Header() => Mounted<Button>(indicator).First(); // every draw makes a new header button
        h.Main.OnMain(() => Click(window, Header())); // open
        Assert.Equal(["Hmm"], Runs(Assert.Single(Mounted<MarkdownView>(indicator))).Select(run => run.Text));
        h.Main.OnMain(() => { Steps("Hmm let me see"); Settle(window); });
        Assert.Equal(["Hmm", " let me see"], Runs(Assert.Single(Mounted<MarkdownView>(indicator))).Select(run => run.Text));
        Tick(h, frames, window, 1000);

        h.Main.OnMain(() => Click(window, Header())); // fold: the steps leave the screen
        Assert.Empty(Mounted<MarkdownView>(indicator));
        h.Main.OnMain(() => { Steps("Hmm let me see what"); Settle(window); }); // the reasoning goes on while they are folded
        h.Main.OnMain(() => Click(window, Header())); // open again
        var again = Runs(Assert.Single(Mounted<MarkdownView>(indicator)));
        // What they show when they come back is the baseline: nothing of it dissolves, not even the words that arrived while they were away.
        Assert.Equal("Hmm let me see what", Assert.Single(again).Text);
        Tick(h, frames, window, 1000);
        h.Main.OnMain(() => Close(window));
    });

    [Fact]
    [Trait("Req", "UI-THEME-047")] // DroppyCode/UI/Markdown/StreamVeil.swift:18-19 (a row scrolled back into view adopts what is on screen); TimelineRows.swift:1546-1553
    public void The_narration_that_scrolls_away_and_back_adopts_what_it_shows_instead_of_fading_it_all_in() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        FreshVeilClock();
        TimelineEntry said = null!;
        WorkSteps steps = null!;
        StackPanel panel = null!;
        Avalonia.Controls.Window window = null!;
        h.Main.OnMain(() =>
        {
            said = Streaming("now the build");
            steps = new WorkSteps(new ValueList<TimelineEntry>([ToolEntry(Call(ToolCall.Kind.Read, "Read a")), said, ToolEntry(Call(ToolCall.Kind.Command, "make"))]), h.Runtime, Binding<bool>.Constant(true));
            panel = new StackPanel();
            panel.Children.Add(steps);
            window = Show(panel);
            Flush(said, "now the build runs");
            Settle(window);
        });
        Tick(h, frames, window, 600); // "now the build runs" has settled
        var settled = Runs(Assert.Single(Mounted<MarkdownView>(steps)))[0].Opacity;

        // The steps leave the screen while the narration goes on streaming, and come back.
        h.Main.OnMain(() =>
        {
            panel.Children.Remove(steps);
            Settle(window);
            Flush(said, "now the build runs fine");
            Settle(window);
            panel.Children.Add(steps);
            Settle(window);
        });
        var back = Runs(Assert.Single(Mounted<MarkdownView>(steps)));
        Assert.Equal("now the build runs fine", Assert.Single(back).Text); // not even the word that arrived while it was away dissolves
        Assert.Equal(settled, back[0].Opacity, 0.001);

        h.Main.OnMain(() => { Flush(said, "now the build runs fine today"); Settle(window); });
        var more = Runs(Assert.Single(Mounted<MarkdownView>(steps)));
        Assert.Equal(["now the build runs fine", " today"], more.Select(run => run.Text));
        Assert.Equal(0, more[1].Opacity, 0.001);
        Tick(h, frames, window, 1000);
        Assert.Equal(0, frames.Pending);
        h.Main.OnMain(() => Close(window));
    });

    [Fact]
    [Trait("Req", "UI-THEME-047")] // DroppyCode/UI/Markdown/StreamVeil.swift:18-19; ThreadTimeline.swift:2156-2168 (the thinking steps leave the hierarchy when there are none, and their @State with them)
    public void The_thinking_steps_that_drain_while_the_work_stays_open_adopt_what_they_show_when_they_return() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        FreshVeilClock();
        WorkingIndicator indicator = null!;
        Avalonia.Controls.Window window = null!;
        var work = new ValueList<TimelineEntry>([ToolEntry(Call(ToolCall.Kind.Read, "Read a"))]);
        void Steps(string? text) => indicator.UpdateValues(text is null ? new ValueList<ThinkingStep>([]) : new ValueList<ThinkingStep>([new ThinkingStep(text, true)]), work, new([]), false);
        h.Main.OnMain(() =>
        {
            indicator = new WorkingIndicator(h.Runtime, SwiftDate.Now, 1, new ValueList<ThinkingStep>([new ThinkingStep("Hmm", true)]), work, new([]), false, chronological: true);
            window = Show(indicator);
        });
        h.Main.OnMain(() => { Steps("Hmm let me see"); Settle(window); });
        Assert.Equal(["Hmm", " let me see"], Runs(Assert.Single(Mounted<MarkdownView>(indicator))).Select(run => run.Text));
        Tick(h, frames, window, 1000);

        h.Main.OnMain(() => { Steps(null); Settle(window); }); // the reasoning ends; the work stays open
        Tick(h, frames, window, 1000); // the leaving steps are gone
        Assert.Empty(Mounted<MarkdownView>(indicator));
        h.Main.OnMain(() => { Steps("Hmm let me see what"); Settle(window); }); // a new step arrives
        var again = Runs(Assert.Single(Mounted<MarkdownView>(indicator)));
        Assert.Equal("Hmm let me see what", Assert.Single(again).Text); // the baseline: nothing of it dissolves
        Tick(h, frames, window, 1000);
        h.Main.OnMain(() => Close(window));
    });

    [Fact]
    [Trait("Req", "UI-THEME-047")] // DroppyCode/UI/Markdown/StreamVeil.swift:18-19 (what is on screen when a veil attaches is the baseline)
    public void The_thinking_indicator_that_scrolls_away_and_back_adopts_what_it_shows_instead_of_fading_it_all_in() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        FreshVeilClock();
        WorkingIndicator indicator = null!;
        StackPanel panel = null!;
        Avalonia.Controls.Window window = null!;
        h.Main.OnMain(() =>
        {
            indicator = new WorkingIndicator(h.Runtime, SwiftDate.Now, 1, new ValueList<ThinkingStep>([new ThinkingStep("Hmm", true)]), new([]), new([]), false, chronological: true);
            panel = new StackPanel();
            panel.Children.Add(indicator);
            window = Show(panel);
            indicator.UpdateValues(new ValueList<ThinkingStep>([new ThinkingStep("Hmm let me see", true)]), new([]), new([]), false);
            Settle(window);
        });
        Tick(h, frames, window, 1000); // "Hmm let me see" has settled
        h.Main.OnMain(() =>
        {
            panel.Children.Remove(indicator);
            Settle(window);
            indicator.UpdateValues(new ValueList<ThinkingStep>([new ThinkingStep("Hmm let me see what", true)]), new([]), new([]), false); // the reasoning goes on while it is away
            Settle(window);
            panel.Children.Add(indicator);
            Settle(window);
        });
        Assert.Equal("Hmm let me see what", Assert.Single(Runs(Assert.Single(Mounted<MarkdownView>(indicator)))).Text);
        Tick(h, frames, window, 1000);
        h.Main.OnMain(() => Close(window));
    });
}
