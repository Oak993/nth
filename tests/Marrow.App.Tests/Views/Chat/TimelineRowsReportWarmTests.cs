using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// PR-CHAT-145: TimelineRows.swift:317-324 `.task(id: body)` - "The report parses off the main thread as the pill appears, so the tap that opens it finds the blocks ready
// rather than parsing the whole batch first. A heads' report is read one head at a time, so it is each head's body that warms." and :1030-1033 for the merge pill.
// TimelineRowsPrRecordsTests proves WHAT is warmed (the parse cache holds it); these prove HOW, through MarkdownView.ParseObserver, which is told every text the parser is
// asked to parse on the thread that parses it: the parse is not on the UI thread, it begins when the pill is on screen and not before, it is one parse per head of a batch
// (never the batch as one text), and nothing is parsed twice. And the tap that follows: a warmed pill's tap parses nothing; a cold tap parses on the UI thread
// (:598-601 "a cold tap parses now") and a popover is not built until the tap.
[Collection("Headless")]
public sealed class TimelineRowsReportWarmTests(HeadlessSession headless)
{
    private static string Nonce() => Guid.NewGuid().ToString("N");

    private static UserMessage Note(string text) => new() { Text = text, HydraHeads = null };

    private static UserMessage Report(string text, params long[] heads) => new() { Text = text, HydraHeads = new ValueList<long>(heads) };

    private static Button Pill(Control row) => Assert.Single(row.GetVisualDescendants().OfType<Button>(), button => Equals(ToolTip.GetTip(button), "Show the message"));

    [Fact]
    [Trait("Req", "PR-CHAT-145")]
    public void A_notes_markdown_is_parsed_whole_on_a_worker_thread_that_starts_when_its_pill_appears_and_not_before() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var log = new MarkdownParseLog(Environment.CurrentManagedThreadId);
        using var seam = MarkdownView.ParseObserver.Override(log.Record);
        var body = "```\nnote " + Nonce() + "\n```";
        HydraReportRow row = null!;
        AvaloniaWindow window = null!;
        h.Main.OnMain(() => row = new HydraReportRow(Note("Held back.\n" + body)));
        // The row is built but its pill is not on screen: nothing is parsed, however long the loop and a worker are given.
        h.Main.Loop.RunUntilIdle();
        Thread.Sleep(150);
        h.Main.Loop.RunUntilIdle();
        Assert.Empty(log.Parses);
        h.Main.OnMain(() => window = Show(row));
        try
        {
            h.Main.Loop.RunUntil(() => MarkdownView.WarmTexts([body]).Count == 0, AppModelHarness.Bound);
            h.Main.Loop.RunUntilIdle();
            // The pill's whole body, once, and on a thread that is not the UI thread - and the UI thread parsed nothing at all.
            Assert.Equal([body], log.Texts);
            var parse = Assert.Single(log.Parses);
            Assert.NotEqual(log.UiThread, parse.Thread);
            Assert.Empty(log.OnUiThread);
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-145")]
    public void A_batch_is_parsed_one_head_at_a_time_on_a_worker_thread_never_as_one_text() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var log = new MarkdownParseLog(Environment.CurrentManagedThreadId);
        using var seam = MarkdownView.ParseObserver.Override(log.Record);
        var nonce = Nonce();
        var batch = "## Hank: a\n```\nhank " + nonce + "\n```\n## Walter: b\n```\nwalter " + nonce + "\n```\n## Rex: c\n```\nrex " + nonce + "\n```";
        var heads = HydraReportDigest.Parse(batch).Heads.Select(head => head.Body).ToList();
        HydraReportRow row = null!;
        AvaloniaWindow window = null!;
        h.Main.OnMain(() =>
        {
            Assert.Equal(3, heads.Count);
            row = new HydraReportRow(Report(batch, 0, 1, 2));
        });
        h.Main.Loop.RunUntilIdle();
        Thread.Sleep(150);
        h.Main.Loop.RunUntilIdle();
        Assert.Empty(log.Parses); // not on screen, not parsed
        h.Main.OnMain(() => window = Show(row));
        try
        {
            h.Main.Loop.RunUntil(() => MarkdownView.WarmTexts(heads).Count == 0, AppModelHarness.Bound);
            h.Main.Loop.RunUntilIdle();
            // Every head's own body is parsed (the batch is covered), in the order the heads are read, each once, and the batch itself never.
            Assert.Equal(heads, log.Texts);
            Assert.DoesNotContain(batch, log.Texts);
            Assert.All(log.Parses, parse => Assert.NotEqual(log.UiThread, parse.Thread));
            Assert.Empty(log.OnUiThread);
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-145")]
    public void Nothing_is_parsed_twice_two_heads_with_one_body_a_pill_that_leaves_and_returns_and_a_second_pill_of_the_same_report() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var log = new MarkdownParseLog(Environment.CurrentManagedThreadId);
        using var seam = MarkdownView.ParseObserver.Override(log.Record);
        var nonce = Nonce();
        var shared = "```\nshared " + nonce + "\n```";
        var batch = "## Hank: a\n" + shared + "\n## Walter: b\n" + shared;
        var heads = HydraReportDigest.Parse(batch).Heads.Select(head => head.Body).ToList();
        var noteBody = "```\nnote " + nonce + "\n```";
        var panel = new StackPanel();
        HydraReportRow batchRow = null!, noteRow = null!;
        AvaloniaWindow window = null!;
        h.Main.OnMain(() =>
        {
            Assert.Equal([heads[0], heads[0]], heads); // two heads, one body
            batchRow = new HydraReportRow(Report(batch, 0, 1));
            noteRow = new HydraReportRow(Note("Held back.\n" + noteBody));
            panel.Children.Add(batchRow);
            panel.Children.Add(noteRow);
            window = Show(panel);
        });
        try
        {
            h.Main.Loop.RunUntil(() => MarkdownView.WarmTexts([heads[0], noteBody]).Count == 0, AppModelHarness.Bound);
            h.Main.Loop.RunUntilIdle();
            // The body two heads share is one parse.
            Assert.Equal(2, log.Parses.Count);
            Assert.Equal(1, log.Texts.Count(text => text == heads[0]));
            Assert.Equal(1, log.Texts.Count(text => text == noteBody));
            // Both pills leave the screen and come back (the timeline re-parents rows): their tasks start again, and find everything cached.
            h.Main.OnMain(() =>
            {
                panel.Children.Remove(batchRow);
                panel.Children.Remove(noteRow);
                Settle(window);
                panel.Children.Add(batchRow);
                panel.Children.Add(noteRow);
                // A second pill of the same report, on screen for the first time.
                panel.Children.Add(new HydraReportRow(Note("Held back.\n" + noteBody)));
                panel.Children.Add(new HydraReportRow(Report(batch, 0, 1)));
                Settle(window);
            });
            h.Main.Loop.RunUntilIdle();
            Thread.Sleep(150);
            h.Main.Loop.RunUntilIdle();
            Assert.Equal(2, log.Parses.Count);
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-145")]
    public void A_pill_with_nothing_behind_its_address_parses_nothing() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var log = new MarkdownParseLog(Environment.CurrentManagedThreadId);
        using var seam = MarkdownView.ParseObserver.Override(log.Record);
        var nonce = Nonce();
        var control = "```\ncontrol " + nonce + "\n```";
        AvaloniaWindow window = null!;
        h.Main.OnMain(() =>
        {
            // The second pill, queued after the first, is the control: once its parse has been seen the first has had its turn.
            var panel = new StackPanel();
            panel.Children.Add(new HydraReportRow(Note("Merged.\nhttps://x.example/only-" + nonce)));
            panel.Children.Add(new HydraReportRow(Note("Held back.\n" + control)));
            window = Show(panel);
        });
        try
        {
            h.Main.Loop.RunUntil(() => MarkdownView.WarmTexts([control]).Count == 0, AppModelHarness.Bound);
            h.Main.Loop.RunUntilIdle();
            Assert.Equal([control], log.Texts);
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    [Theory]
    [Trait("Req", "PR-CHAT-145")]
    [InlineData(false)]
    [InlineData(true)]
    public void The_tap_on_a_pill_whose_report_has_been_warmed_parses_nothing(bool batch) => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var presenter = new TimelineRowsPrPopupPresenter();
        using var presenting = Presentation.Presenter.Override(presenter);
        _ = AppCurrentEvent.CurrentWindow; // the pill's window anchor reads the current event's window, which is only tracked from the first read
        using var log = new MarkdownParseLog(Environment.CurrentManagedThreadId);
        using var seam = MarkdownView.ParseObserver.Override(log.Record);
        var nonce = Nonce();
        var text = batch
            ? "## Hank: a\n```\nhank " + nonce + "\n```\n## Walter: b\n```\nwalter " + nonce + "\n```"
            : "Held back.\n```\nnote " + nonce + "\n```";
        var warmed = batch ? HydraReportDigest.Parse(text).Heads.Select(head => head.Body).ToList() : ["```\nnote " + nonce + "\n```"];
        HydraReportRow row = null!;
        AvaloniaWindow window = null!;
        h.Main.OnMain(() =>
        {
            row = new HydraReportRow(batch ? Report(text, 0, 1) : Note(text));
            window = Show(row);
        });
        try
        {
            h.Main.Loop.RunUntil(() => MarkdownView.WarmTexts(warmed).Count == 0, AppModelHarness.Bound);
            h.Main.Loop.RunUntilIdle();
            var before = log.Parses.Count;
            Assert.Equal(warmed.Count, before);
            h.Main.OnMain(() => Click(window, Pill(row)));
            h.Main.OnMain(() =>
            {
                Settle(window);
                // The popover opened ...
                Assert.True(presenter.Calls.Last(call => call.Presented).Popup.IsOpen);
                // ... and the tap found the blocks ready: the parser was not asked for anything, on any thread.
                Assert.Equal(before, log.Parses.Count);
            });
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-145")]
    public void The_popover_is_not_built_before_the_tap_and_a_tap_before_the_warm_has_landed_parses_now_on_the_UI_thread() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var presenter = new TimelineRowsPrPopupPresenter();
        using var presenting = Presentation.Presenter.Override(presenter);
        _ = AppCurrentEvent.CurrentWindow; // the pill's window anchor reads the current event's window, which is only tracked from the first read
        using var log = new MarkdownParseLog(Environment.CurrentManagedThreadId);
        using var seam = MarkdownView.ParseObserver.Override(log.Record);
        var body = "```\ncold " + Nonce() + "\n```";
        HydraReportRow row = null!;
        AvaloniaWindow window = null!;
        log.Hold(); // the warm's parse is begun and parked: the pill is on screen, its report is not yet in the cache
        h.Main.OnMain(() =>
        {
            row = new HydraReportRow(Note("Held back.\n" + body));
            window = Show(row);
        });
        try
        {
            h.Main.Loop.RunUntil(() => log.OffUiThread.Count == 1, AppModelHarness.Bound);
            h.Main.Loop.RunUntilIdle();
            // The pill is on screen and the warm has begun, but the popover is not built (its blocks would have been parsed on this thread) and none is presented.
            Assert.Empty(log.OnUiThread);
            Assert.Empty(presenter.Calls);
            h.Main.OnMain(() => Click(window, Pill(row)));
            h.Main.OnMain(() =>
            {
                Settle(window);
                Assert.True(presenter.Calls.Last(call => call.Presented).Popup.IsOpen);
                // :598-601 "a cold tap parses now": the popover's initializer parsed the body, on the UI thread, once.
                Assert.Equal([body], log.OnUiThread);
            });
            log.Release();
            h.Main.Loop.RunUntilIdle();
            Assert.Equal([body], log.OnUiThread); // the warm that was parked lands on a cache that has the body, and parses nothing more on the UI thread
        }
        finally { log.Release(); h.Main.OnMain(() => Close(window)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-145")] // windows_path: "Same warm-on-appear behavior for the merge pill (TimelineRows.swift:1030-1033)"
    public void The_merge_pills_report_is_parsed_whole_on_a_worker_thread_as_the_pill_appears() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var log = new MarkdownParseLog(Environment.CurrentManagedThreadId);
        using var seam = MarkdownView.ParseObserver.Override(log.Record);
        var body = "```\nmerge " + Nonce() + "\n```";
        HydraMergeRow row = null!;
        AvaloniaWindow window = null!;
        h.Main.OnMain(() => row = new HydraMergeRow(h.Runtime, new HydraMergePhase.Outcome(Note("Held back.\n" + body))));
        h.Main.Loop.RunUntilIdle();
        Thread.Sleep(150);
        h.Main.Loop.RunUntilIdle();
        Assert.Empty(log.Parses);
        h.Main.OnMain(() => window = Show(row));
        try
        {
            h.Main.Loop.RunUntil(() => MarkdownView.WarmTexts([body]).Count == 0, AppModelHarness.Bound);
            h.Main.Loop.RunUntilIdle();
            Assert.Equal([body], log.Texts);
            Assert.NotEqual(log.UiThread, Assert.Single(log.Parses).Thread);
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });
}
