using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.Services.Providers;
using Marrow.UI.Port;
using Marrow.UI.Common;
using Marrow.UI.Markdown;
using Xunit;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using AvaloniaWindow = Avalonia.Controls.Window;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;

namespace Marrow.App.Tests.Views.Chat;

// REVIEW (adversarial review of unit blitz-timelinerows-ab). Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792) :527-595 HydraBriefRow.
// The pill is one Button for the life of the row (:552-582): its `.badgePopover(isPresented: $isShowingBrief)` stays presented while the body re-evaluates
// for a new entry count (:540-546), so a brief opened while the head works stays open. The port rebuilds the button, its badge popover and the popover's
// content on every change of the entry count (TimelineRows.cs HydraBriefRow.Build, tracked on runtime.Entries.Count): the open popover is dismissed at
// the head's next step (seen red 2026-10-04: the presenter is told Presented=false and nothing presents it again).
[Collection("Headless")]
public sealed class TimelineRowsReviewAbTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Rule", "R-TEST-4")] // PR-CHAT-147 ("Tapping opens the full brief"): the open brief outlives the head's next step
    public void REVIEW_an_open_brief_stays_presented_while_the_head_takes_its_next_step() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        HydraBriefRow? row = null;
        AvaloniaWindow? window = null;
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            _ = AppCurrentEvent.CurrentWindow;
            h.App.UpdateThread(h.Thread.Id, thread => thread with { Hydra = new HydraHeadInfo(1, "the task", HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated) });
            h.Runtime.RehearseTurn("Build the thing, in full");
            var entry = h.Runtime.Entries.Last(candidate => candidate.TimelineEntryKind == TimelineEntry.Kind.User);
            var message = ((TimelineItem.Content.User)entry.Item.TimelineItemContent).Value;
            row = new HydraBriefRow(message, entry, h.Runtime);
            window = Show(row);
            LogicalQuery.SelfAndDescendants<Button>(row).Single().RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        });
        try
        {
            h.Main.OnMain(() =>
            {
                Settle(window!);
                Assert.Single(presenter.Calls, call => call.Presented);
                // The head takes a step: one more entry on the brief's thread.
                h.Runtime.Rehearse(new ProviderEvent.ToolStarted("tool-1", Call(ToolCall.Kind.Read, "Read a", ToolCall.Status.Running)));
            });
            h.Main.OnMain(() =>
            {
                Settle(window!);
                // The same popover, still presented: never dismissed, never presented a second time.
                Assert.DoesNotContain(presenter.Calls, call => !call.Presented);
                Assert.Single(presenter.Calls, call => call.Presented);
            });
        }
        finally { h.Main.OnMain(() => Close(window!)); }
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

    private static double Effective(Visual visual) => visual.GetSelfAndVisualAncestors().Aggregate(1.0, (product, at) => product * at.Opacity);

    // TimelineRows.swift:663-664 `Text(title).contentTransition(.opacity)` under :703-704 `.animation(Chrome.panelSlide, value: sent)`: the words of the
    // title crossfade when the card is sent ("Summoning 2 heads" to "Summoned 2 heads"); OD-0076: content that swaps crossfades. The port changes the text
    // in place at once (ED-NEW-timelinerows-ab (11): "the source's .contentTransition(.opacity) crossfade is not drawn").
    [Fact]
    [Trait("Rule", "R-TEST-4")] // PR-CHAT-149: the card's changes arrive by animation, its title included
    public void REVIEW_the_delegation_cards_title_crossfades_when_the_card_is_sent() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var clock = new ManualFrameSource();
        using var clockSeam = FrameClock.Source.Override(clock);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        var previous = MarkdownDelegationCard.Render;
        MarkdownDelegationCard.Render = (json, sent) => new HydraDelegationBlock(json, sent);
        AvaloniaWindow window = null!;
        const string hank = "{\"task\": \"Tidy the popover\", \"prompt\": \"Tidy it.\", \"name\": \"Hank\"}";
        const string walter = "{\"task\": \"Write the tests\", \"prompt\": \"Write them.\", \"name\": \"Walter\"}";
        var closed = "Sending two heads.\n\n```hydra\n[\n" + hank + ",\n" + walter + "\n]\n```";
        var sentBlock = "Sending two heads.\n\n```hydra-sent\n[\n" + hank + ",\n" + walter + "\n]\n```";
        try
        {
            var entry = Reply(closed);
            h.Main.OnMain(() =>
            {
                entry.Item = entry.Item with { TimelineItemContent = new TimelineItem.Content.Assistant(new AssistantMessage { Text = closed, IsStreaming = true }) };
                window = Show(new AssistantMessageRow(entry, null, h.Runtime));
            });
            Frames(h.Main, clock, 2000);
            h.Main.OnMain(() =>
            {
                Settle(window);
                entry.Item = entry.Item with { TimelineItemContent = new TimelineItem.Content.Assistant(new AssistantMessage { Text = sentBlock, IsStreaming = false }) };
            });
            h.Main.OnMain(() => Settle(window));
            Frames(h.Main, clock, 60);
            h.Main.OnMain(() =>
            {
                Settle(window);
                var card = Assert.Single(window.GetVisualDescendants().OfType<HydraDelegationBlock>());
                var titles = card.GetVisualDescendants().OfType<TextBlock>().Where(text => text.Text?.StartsWith("Summon", StringComparison.Ordinal) == true).ToList();
                // Part way through the crossfade: the old words still fading out beside the new, or the new words not yet whole.
                Assert.True(titles.Count == 2 || titles.Any(title => Effective(title) is > 0.001 and < 0.999),
                    $"the title cut from one text to the other: [{string.Join("|", titles.Select(title => title.Text + "@" + Effective(title)))}]");
            });
        }
        finally
        {
            h.Main.OnMain(() => { if (window is not null) Close(window); });
            MarkdownDelegationCard.Render = previous;
        }
    });

    // TimelineRows.swift:2352-2358 `Image(systemName: Self.symbol(for: step.status))` under :2372 `.animation(.snappy(duration: 0.2), value: steps)`: checking a
    // step off swaps its symbol under the animation, which SwiftUI draws as a transition of the symbol, never a cut; OD-0076: content that swaps crossfades.
    // The port puts the new glyph in at once at full opacity (ED-NEW-timelinerows-ab (1): "The glyph itself ... is replaced at once").
    [Fact]
    [Trait("Rule", "R-TEST-4")] // PR-CHAT-114: "checking an item off animates the same list"
    public void REVIEW_checking_a_step_off_transitions_its_glyph_rather_than_cutting_to_the_check() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var clock = new ManualFrameSource();
        using var clockSeam = FrameClock.Source.Override(clock);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        TimelineEntry entry = null!;
        TodoListRow row = null!;
        AvaloniaWindow window = null!;
        h.Main.OnMain(() =>
        {
            entry = new TimelineEntry(new TimelineItem(null, Steps(("One", TodoStep.Status.Active), ("Two", TodoStep.Status.Pending))));
            row = new TodoListRow(entry);
            window = Show(new Border { Child = row, Width = 600 });
            entry.Item = entry.Item with { TimelineItemContent = Steps(("One", TodoStep.Status.Done), ("Two", TodoStep.Status.Pending)) };
        });
        try
        {
            h.Main.OnMain(() => { });
            Frames(h.Main, clock, 50);
            h.Main.OnMain(() =>
            {
                Settle(window);
                var glyphs = Mounted<SwiftImage>(row).Where(image => image.SymbolName is "checkmark.circle.fill" or "circle.dotted.circle").ToList();
                // 50 ms into a 0.2 s change: the old glyph still going or the check still arriving.
                Assert.True(glyphs.Count == 2 || glyphs.Any(glyph => Effective(glyph) is > 0.001 and < 0.999),
                    $"the step's glyph cut to the check: [{string.Join("|", glyphs.Select(glyph => glyph.SymbolName + "@" + Effective(glyph)))}]");
            });
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });
}
