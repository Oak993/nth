using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Testing;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// Parity closing pass (2026-10-03) for App.Views.Chat.TimelineRows: the mounted interaction scenarios that the STOP
// notes in TimelineRowsTests.cs named as the missing proof. Source: DroppyCode/App/Views/Chat/TimelineRows.swift
// (B3 52bc792). Every click and right click here is real pointer input through hit testing in a headless window,
// and every popover opens in the window's own overlay layer (TimelineRowsPrPopupPresenter), so its items take
// real clicks too.
[Collection("Headless")]
public sealed class TimelineRowsParityScenarioTests(HeadlessSession headless)
{
    // ---------------------------------------------------------------- PR-CHAT-089

    private const string CodedReply = "Intro\n```\nfirst\n```\nprose\n```js\nlast block\n```\n```\nnever closed";

    [Fact]
    [Trait("Req", "PR-CHAT-089")] // TimelineRows.swift:38-54, :1394-1412, :1415-1429, :1431-1439
    public void Right_clicking_a_reply_or_a_folded_answer_offers_the_last_closed_block_and_Add_to_reply_quotes_it_once() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var presenter = new TimelineRowsPrPopupPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        AvaloniaWindow window = null!;
        var menus = new List<AvaloniaWindow>();
        AssistantMessageRow reply = null!;
        TurnFinishedBlock folded = null!;
        PopoverItem[] items = [];
        try
        {
            h.Main.OnMain(() =>
            {
                _ = AppCurrentEvent.CurrentWindow; // the app-wide event watch the copy actions read
                var turn = Guid.NewGuid();
                reply = new AssistantMessageRow(Reply(CodedReply), null, h.Runtime);
                // :2669-2671 a folded turn's answer is a FoldedAnswerView carrying the same menu (:1431-1439).
                folded = new TurnFinishedBlock(h.Runtime, turn, Summary(turn), ValueList<TimelineEntry>.Empty,
                    new ValueList<TimelineEntry>([Reply("The folded answer, with no code.")]), null, canUndo: false, chronological: false);
                window = Show(new StackPanel { Spacing = 60, Children = { reply, folded } });
                items = OpenMenu(window, Assert.Single(LogicalQuery.SelfAndDescendants<MarkdownView>(reply)), presenter, menus);
                // :1397-1411 the two copies, the last code block (one complete fence exists), then Add to reply in its own group.
                Assert.Equal(["Copy message", "Copy as markdown", "Copy last code block", "Add to reply"], items.Select(Title));
                ClickItem(menus[^1], items[2]);
            });
            h.Main.OnMain(() =>
            {
                Settle(menus[^1]);
                // :38-54 only the LAST CLOSED fence is copied: the opened-but-never-closed one at the end yields nothing.
                Assert.Equal("last block", window.Clipboard!.TryGetTextAsync().GetAwaiter().GetResult());
                Assert.True(items[2].IsConfirmed, "Copy last code block must confirm after the real item click.");
                Assert.Equal("Copied", Title(items[2]));
                ClickItem(menus[^1], items[3]);
            });
            h.Main.OnMain(() =>
            {
                Settle(menus[^1]);
                // :1406-1410 Add to reply confirms with Added and quotes the reply into the chat box's draft.
                Assert.True(items[3].IsConfirmed, "Add to reply must confirm after the real item click.");
                Assert.Equal("Added", Title(items[3]));
                Assert.Equal([CodedReply], h.Runtime.Draft.Quotes.Select(quote => quote.Text));
                // Opening the same menu again and adding the same reply again adds nothing (:1408).
                items = OpenMenu(window, Assert.Single(LogicalQuery.SelfAndDescendants<MarkdownView>(reply)), presenter, menus);
                ClickItem(menus[^1], items.Single(item => Title(item) == "Add to reply"));
            });
            h.Main.OnMain(() =>
            {
                Settle(menus[^1]);
                Assert.Equal([CodedReply], h.Runtime.Draft.Quotes.Select(quote => quote.Text));
                // The folded answer: the same menu wherever a reply's text is shown, without the code item for text with none.
                items = OpenMenu(window, Assert.Single(LogicalQuery.SelfAndDescendants<FoldedAnswerView>(folded)), presenter, menus);
                Assert.Equal(["Copy message", "Copy as markdown", "Add to reply"], items.Select(Title));
                ClickItem(menus[^1], items[2]);
            });
            h.Main.OnMain(() =>
            {
                Settle(menus[^1]);
                Assert.Equal([CodedReply, "The folded answer, with no code."], h.Runtime.Draft.Quotes.Select(quote => quote.Text));
            });
        }
        finally
        {
            h.Main.OnMain(() =>
            {
                foreach (var menu in menus) Close(menu);
                if (window is not null) Close(window);
            });
        }
        // The quotes live on the thread's runtime, not on the rows: they outlast every row that offered them.
        h.Main.OnMain(() => Assert.Equal(2, h.Runtime.Draft.Quotes.Count));
    });

    // A real right click opens the real popover; its content is then hosted in a window of its own so its items take
    // real clicks (the overlay popup has no translatable root in the headless window).
    private static PopoverItem[] OpenMenu(AvaloniaWindow window, Control on, TimelineRowsPrPopupPresenter presenter, List<AvaloniaWindow> menus)
    {
        RightClick(window, on);
        var popup = presenter.Calls.Last(call => call.Presented).Popup;
        Assert.True(popup.IsOpen, "The real secondary click must present the popup before its content is mounted separately.");
        var content = popup.Child!;
        popup.Child = null;
        // This fixture transports the content to its own menu window; the original empty popup must release its overlay.
        presenter.SetPresented(popup, false);
        Assert.False(popup.IsOpen);
        var menu = new AvaloniaWindow { Content = content, Width = 400, Height = 400 };
        menu.Show();
        Settle(menu);
        menus.Add(menu);
        return LogicalQuery.SelfAndDescendants<PopoverItem>(content).ToArray();
    }

    // ---------------------------------------------------------------- PR-CHAT-161

    // Partial proof PR-CHAT-161 (STOP #11): the record asks for a GC release of the entry after a menu was opened by a real
    // right click. Under Avalonia 12.1.2's headless input stack any element that took a pointer press (or keyboard focus and
    // the menu key) stays reachable after it leaves the tree (probed 2026-10-03 with a bare Border: pinned the same way;
    // Gestures' last press, focus and AppCurrentEvent ruled out), so a GC assertion cannot tell the row's own references
    // from the framework's. What the row and its menu hold is asserted directly instead: once the row scrolls away the menu
    // is dismissed and the popover keeps neither the row (its placement target) nor its content.
    [Fact]
    [Trait("Rule", "R-TEST-4")] // TimelineRows.swift:56-62, :165-178, :1415-1429; MessagePopoverMenu.swift:4-18
    public void A_rows_open_menu_is_dismissed_and_lets_go_of_the_row_when_the_row_scrolls_away() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var presenter = new TimelineRowsPrPopupPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var panel = new StackPanel { Spacing = 40 };
        AvaloniaWindow window = null!;
        h.Main.OnMain(() =>
        {
            window = Show(panel);
            try
            {
                foreach (var user in new[] { true, false })
                {
                    var entry = user ? UserEntry("a message with a menu", Guid.NewGuid()) : Reply("a reply with a menu");
                    Control row = user ? new UserMessageRow(entry, h.Runtime, canRevert: true) : new AssistantMessageRow(entry, null, h.Runtime);
                    panel.Children.Insert(0, row);
                    Settle(window);
                    Control target = user ? LogicalQuery.SelfAndDescendants<InlineText>(row).Single() : LogicalQuery.SelfAndDescendants<MarkdownView>(row).Single();
                    RightClick(window, target);
                    var popup = presenter.Calls.Last(call => call.Presented).Popup;
                    Assert.True(popup.IsOpen);
                    Assert.NotNull(popup.PlacementTarget);
                    Assert.NotEmpty(LogicalQuery.SelfAndDescendants<PopoverItem>(popup.Child!));
                    var calls = presenter.Calls.Count;
                    panel.Children.Remove(row); // the row scrolls out of the list while its menu is open
                    Settle(window);
                    Assert.Contains(presenter.Calls.Skip(calls), call => ReferenceEquals(call.Popup, popup) && !call.Presented);
                    Assert.False(popup.IsOpen);
                    Assert.Null(popup.PlacementTarget);
                    Assert.Null(popup.Child);
                }
            }
            finally { Close(window); }
        });
    });

    // Review round 1 (2026-10-03): the other half of the structural proof above. ThreadTimeline.Draw takes every kept row out of
    // its old stack and puts it into a new one on each change of the thread; SwiftUI keeps such a row's @State (eager VStack,
    // ThreadTimeline.swift:384-391), so its open menu stays open. Only a row that is still out of the tree after the pass is gone.
    // This isolates the row's own handler, so it runs on the recording presenter: a real Avalonia Popup closes itself the moment
    // its placement target leaves the tree (probed 2026-10-03: the overlay popup reports IsOpen false inside the Remove call, with
    // this handler disabled too), which no handler of the row can prevent. That limit is in STOP #11 (TimelineRowsTests).
    [Fact]
    [Trait("Rule", "R-TEST-4")] // TimelineRows.swift:56-62, :165-178, :1415-1429; MessagePopoverMenu.swift:4-18; ThreadTimeline.swift:384-391
    public void A_rows_own_handler_keeps_its_open_menu_while_the_row_is_only_moved_to_another_stack_and_ends_it_once_the_row_is_gone() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var before = new StackPanel { Spacing = 40 };
        var after = new StackPanel { Spacing = 40 };
        AvaloniaWindow window = null!;
        h.Main.OnMain(() =>
        {
            window = Show(new StackPanel { Children = { before, after } });
            try
            {
                foreach (var user in new[] { true, false })
                {
                    var entry = user ? UserEntry("a message with a menu", Guid.NewGuid()) : Reply("a reply with a menu");
                    Control row = user ? new UserMessageRow(entry, h.Runtime, canRevert: true) : new AssistantMessageRow(entry, null, h.Runtime);
                    before.Children.Add(row);
                    Settle(window);
                    Control target = user ? LogicalQuery.SelfAndDescendants<InlineText>(row).Single() : LogicalQuery.SelfAndDescendants<MarkdownView>(row).Single();
                    RightClick(window, target);
                    var popup = presenter.Calls.Last(call => call.Presented).Popup;
                    Assert.NotNull(popup.Child);
                    var calls = presenter.Calls.Count;

                    before.Children.Remove(row); // taken out of one stack and put into another, as ThreadTimeline.Draw does
                    after.Children.Add(row);
                    Settle(window);
                    Assert.DoesNotContain(presenter.Calls.Skip(calls), call => ReferenceEquals(call.Popup, popup) && !call.Presented);
                    Assert.NotNull(popup.PlacementTarget);
                    Assert.NotNull(popup.Child);

                    after.Children.Remove(row); // and now it is gone
                    Settle(window);
                    Assert.Contains(presenter.Calls.Skip(calls), call => ReferenceEquals(call.Popup, popup) && !call.Presented);
                    Assert.Null(popup.PlacementTarget);
                    Assert.Null(popup.Child);
                }
            }
            finally { Close(window); }
        });
    });

    // ---------------------------------------------------------------- PR-CHAT-144

    [Fact]
    [Trait("Req", "PR-CHAT-144")] // TimelineRows.swift:252-311, :375-387
    public void With_the_Open_link_beside_it_the_pill_still_opens_its_report_and_the_link_never_does() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var presenter = new TimelineRowsPrPopupPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var opened = new List<string>();
        using var browser = Marrow.Services.Port.Workspace.Open.Override(url => { opened.Add(url.AbsoluteString); return true; });
        const string address = "https://example.com/pr/1";
        AvaloniaWindow window = null!;
        HydraReportRow row = null!;
        try
        {
            h.Main.OnMain(() =>
            {
                row = new HydraReportRow(new UserMessage { Text = "Merge request opened.\n" + address + "\nThe details after the address." });
                window = Show(row);
                var open = Assert.Single(row.GetVisualDescendants().OfType<Button>(), button => Equals(ToolTip.GetTip(button), "Open in the browser"));
                Click(window, open);
                Assert.Equal([address], opened);
                Assert.DoesNotContain(presenter.Calls, call => call.Presented); // the link opened the browser, not the report
                Click(window, Assert.Single(row.GetVisualDescendants().OfType<Button>(), button => Equals(ToolTip.GetTip(button), "Show the message")));
            });
            h.Main.OnMain(() =>
            {
                Settle(window);
                var popup = presenter.Calls.Last(call => call.Presented).Popup;
                Assert.True(popup.IsOpen);
                // :272-278 a note with no merge card keeps the markdown view of its body (HydraReportPopover(text: body)).
                var report = Assert.IsType<HydraReportPopover>(Unwrap(popup.Child!));
                var blocks = LogicalQuery.SelfAndDescendants<MarkdownBlockView>(report).Select(block => block.Block).ToArray();
                Assert.Equal(MarkdownView.Blocks(address + "\nThe details after the address."), blocks);
                Assert.Equal([address], opened); // still only the one browser open
                Click(window, Assert.Single(row.GetVisualDescendants().OfType<Button>(), button => Equals(ToolTip.GetTip(button), "Show the message")));
            });
            h.Main.OnMain(() =>
            {
                Settle(window);
                Assert.False(presenter.Calls.Last().Presented); // :259-261 the button toggles the report shut again
            });
        }
        finally { if (window is not null) h.Main.OnMain(() => Close(window)); }
    });

    // ---------------------------------------------------------------- PR-CHAT-080 under OD-0076

    // SwiftUI's easeOut is the cubic Bezier (0, 0, 0.58, 1): the progress at a fraction of the duration, found independently
    // of the port's own timing code (x(t) solved by bisection, then y(t)).
    private static double EaseOutProgress(double fraction)
    {
        static double Bezier(double t, double p1, double p2) => 3 * (1 - t) * (1 - t) * t * p1 + 3 * (1 - t) * t * t * p2 + t * t * t;
        double low = 0, high = 1;
        for (var step = 0; step < 60; step++)
        {
            var mid = (low + high) / 2;
            if (Bezier(mid, 0, 0.58) < fraction) low = mid; else high = mid;
        }
        return Bezier((low + high) / 2, 0, 1);
    }

    private static Control PencilLine(UserMessageRow row) =>
        row.GetVisualDescendants().OfType<Control>().Single(control => control.Height == TimelineMetrics.HoverLineHeight && double.IsNaN(control.Width));

    // :161 .opacity(isHovering || isEditing ? 1 : 0) cuts on the Mac (2-chat-use.mov 23.415-24.187 s: the pencil appears and
    // goes in one frame, catalogue hover.message-edit-pencil). OD-0076 rules that nothing cuts: the line fades in the Mac's
    // hover language, the reply row's own hover line (:1381 easeOut 0.12), and reduce motion stills it.
    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-080 (STOP #11): TimelineRows.swift:158-161; OD-0076
    public void The_pencil_line_fades_in_and_out_over_the_hover_curve_and_reduce_motion_stills_it() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        var genie = new FakeGeniePlatform();
        using var motion = Chrome.ReduceMotionPlatform.Override(genie);
        UserMessageRow row = null!;
        AvaloniaWindow window = null!;
        void Step(int ms) { h.Main.AdvanceMilliseconds(ms); frames.Advance(TimeSpan.FromMilliseconds(ms)); }
        try
        {
            h.Main.OnMain(() =>
            {
                row = new UserMessageRow(UserEntry("edit me", Guid.NewGuid()), h.Runtime, canRevert: true);
                window = Show(new StackPanel { Children = { row, new Border { Height = 200, Background = Avalonia.Media.Brushes.Transparent } } });
                Assert.Equal(0, PencilLine(row).Opacity);
                Hover(window, Assert.Single(LogicalQuery.SelfAndDescendants<InlineText>(row)));
                Assert.Equal(0, PencilLine(row).Opacity); // no cut to 1 in the frame the pointer arrives
            });
            frames.Advance(TimeSpan.Zero);
            Step(60);
            // Halfway through the 0.12 s fade, on the ease-out curve (:1381 .easeOut(duration: 0.12)): 0.68 of the way, not the
            // half a linear fade would have reached, nor the third an ease-in would.
            h.Main.OnMain(() => Assert.Equal(EaseOutProgress(0.5), PencilLine(row).Opacity, 0.05));
            Step(80);
            h.Main.OnMain(() =>
            {
                Assert.Equal(1, PencilLine(row).Opacity);
                window.MouseMove(new Point(450, 650), RawInputModifiers.None); // off the row
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(1, PencilLine(row).Opacity);
            });
            frames.Advance(TimeSpan.Zero);
            Step(60);
            h.Main.OnMain(() => Assert.Equal(1 - EaseOutProgress(0.5), PencilLine(row).Opacity, 0.05)); // and out, on the same curve
            Step(80);
            h.Main.OnMain(() =>
            {
                Assert.Equal(0, PencilLine(row).Opacity);
                genie.AccessibilityDisplayShouldReduceMotion = true;
                Hover(window, Assert.Single(LogicalQuery.SelfAndDescendants<InlineText>(row)));
            });
            h.Main.OnMain(() => Assert.Equal(1, PencilLine(row).Opacity)); // reduce motion: lands at once, no frame advanced
        }
        finally { if (window is not null) h.Main.OnMain(() => Close(window)); }
    });

    // ---------------------------------------------------------------- PR-CHAT-113

    // :2263-2316 PlanCard re-renders as the plan streams and changes state, and ChatBadge's coordinator is its @State
    // (ChatBadge.swift:20), so the same badge and the same open popover outlive every re-render; the popover holds the
    // content it was opened with (BadgePopoverCoordinator.show -> setFixedContent, ChatBadge.swift:138-147) while the
    // badge's own label follows the plan. Opening it again builds the detail from the plan as it now is: the drafting
    // plan rendered by the streaming renderer, the proposed one by the finished surface with its two buttons (:2287).
    // The SOURCE's behaviour, which the amended record states (2026-10-04, 0f85fb3a1): the popover freezes its content when it opens (ChatBadge.swift:112-147
    // setFixedContent, detail() evaluated once at :69), so the switch to the finished-prose surface happens only in a popover opened again after the state left
    // drafting; one already open keeps the text and the streaming flag it was opened with. This is the record's verification method: a popover left open through
    // the transition keeps the streaming renderer and its text, and one opened again shows the finished-prose surface.
    [Fact]
    [Trait("Req", "PR-CHAT-113")] // TimelineRows.swift:2263-2316, :2287; ChatBadge.swift:20, :67-75, :112-147; Popovers.swift:29-40
    public void A_plan_opened_while_drafting_keeps_its_popover_through_the_switch_to_proposed_and_reopens_on_the_finished_surface() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var presenter = new TimelineRowsPrPopupPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var entry = new TimelineEntry(new TimelineItem(null, new TimelineItem.Content.Plan(new ProposedPlan
            { Markdown = "First paragraph.\n\nSecond paragraph.", ProposedPlanState = ProposedPlan.State.Drafting })));
        PlanCard card = null!;
        AvaloniaWindow window = null!;
        Popup popup = null!;
        string[] Words(Control root) => LogicalQuery.SelfAndDescendants<TextBlock>(root).Select(text => text.Text ?? "").ToArray();
        try
        {
            h.Main.OnMain(() =>
            {
                _ = AppCurrentEvent.CurrentWindow; // the badge's window anchor reads the current event's window
                card = new PlanCard(entry, h.Runtime) { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
                window = Show(card);
                Assert.Contains("Writing a plan", Words(card));
                Click(window, LogicalQuery.SelfAndDescendants<Button>(card).First());
            });
            h.Main.OnMain(() =>
            {
                popup = presenter.Calls.Last(call => call.Presented).Popup;
                Assert.True(popup.IsOpen);
                Assert.True(Assert.Single(LogicalQuery.SelfAndDescendants<MarkdownView>(popup.Child!)).IsStreaming); // :2287 drafting
                // The plan finishes drafting while its popover is open.
                entry.Item = entry.Item with { TimelineItemContent = new TimelineItem.Content.Plan(new ProposedPlan
                    { Markdown = "First paragraph.\n\nSecond paragraph.\n\nThird.", ProposedPlanState = ProposedPlan.State.Proposed }) };
            });
            h.Main.OnMain(() =>
            {
                Settle(window);
                // The same popover is still up (nothing closed it), holding what it opened with; the badge reads Plan ready.
                Assert.True(popup.IsOpen, "the open plan popover closed when the plan changed");
                Assert.True(presenter.Calls.Last(call => ReferenceEquals(call.Popup, popup)).Presented);
                var open = Assert.Single(LogicalQuery.SelfAndDescendants<MarkdownView>(popup.Child!));
                Assert.True(open.IsStreaming);
                Assert.Equal("First paragraph.\n\nSecond paragraph.", open.Text); // the text it opened with, not the plan's new one
                Assert.DoesNotContain("Third.", string.Join(" ", Words(popup.Child!)), StringComparison.Ordinal);
                Assert.Contains("Plan ready", Words(card));
                Assert.DoesNotContain("Writing a plan", Words(card));
                // Closed and opened again: the detail is built from the proposed plan.
                Click(window, LogicalQuery.SelfAndDescendants<Button>(card).First());
            });
            h.Main.OnMain(() =>
            {
                Settle(window);
                Assert.False(popup.IsOpen);
                Click(window, LogicalQuery.SelfAndDescendants<Button>(card).First());
            });
            h.Main.OnMain(() =>
            {
                Settle(window);
                var reopened = presenter.Calls.Last(call => call.Presented).Popup;
                Assert.True(reopened.IsOpen);
                var markdown = Assert.Single(LogicalQuery.SelfAndDescendants<MarkdownView>(reopened.Child!));
                Assert.False(markdown.IsStreaming); // :2287 the finished-prose surface once the state has left drafting
                Assert.Equal("First paragraph.\n\nSecond paragraph.\n\nThird.", markdown.Text); // built from the plan as it is now
                Assert.Contains("Implement plan", Words(reopened.Child!));
                Assert.Contains("Dismiss", Words(reopened.Child!));
            });
        }
        finally { if (window is not null) h.Main.OnMain(() => Close(window)); }
    });

    // PR-CHAT-113 review round 1 (2026-10-03): the card is one identity for as long as the plan is a row of the running turn
    // (ThreadTimeline.swift:1979-1984 ForEach(groups) in TurnRunningBlock), whichever render of the block holds it, so the
    // popover the reader opened is not closed by the turn's other rows arriving. The port builds the block's rows anew,
    // and with them a new PlanCard for the same plan entry: the popover is the identity's, not the control's. The same
    // scenario on the real timeline is REVIEW_An_open_plan_popover_stays_open_on_the_real_timeline_while_the_turn_keeps_streaming.
    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-113 (STOP #11); TimelineRows.swift:2261, :2263-2316; ChatBadge.swift:20, :74-75; ThreadTimeline.swift:1979-1984
    public void An_open_plan_popover_survives_the_card_being_built_again_for_the_same_plan_and_goes_when_the_card_does() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var presenter = new TimelineRowsPrPopupPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var entry = new TimelineEntry(new TimelineItem(null, new TimelineItem.Content.Plan(new ProposedPlan
            { Markdown = "First paragraph.", ProposedPlanState = ProposedPlan.State.Drafting })));
        var host = new StackPanel();
        AvaloniaWindow window = null!;
        Popup popup = null!;
        PlanCard second = null!;
        PlanCard third = null!;
        try
        {
            h.Main.OnMain(() =>
            {
                _ = AppCurrentEvent.CurrentWindow; // the badge's window anchor reads the current event's window
                host.Children.Add(new PlanCard(entry, h.Runtime));
                window = Show(host);
                Click(window, LogicalQuery.SelfAndDescendants<Button>(Assert.Single(host.Children)).First());
            });
            h.Main.OnMain(() =>
            {
                popup = presenter.Calls.Last(call => call.Presented).Popup;
                Assert.True(popup.IsOpen);
                // The running turn's block is built again: the old card is dropped, a new one is made for the same plan.
                host.Children.Clear();
                second = new PlanCard(entry, h.Runtime);
                host.Children.Add(second);
            });
            h.Main.OnMain(() =>
            {
                Settle(window);
                Assert.True(popup.IsOpen, "the open plan popover closed when its card was built again");
                Assert.Single(presenter.Calls, call => call.Presented); // the same popover, never shown a second time
                // The new card's pill is the one that now toggles it: a click shuts it.
                Click(window, LogicalQuery.SelfAndDescendants<Button>(second).First());
            });
            h.Main.OnMain(() =>
            {
                Settle(window);
                Assert.False(popup.IsOpen);
                // And opens it again, from the new card.
                Click(window, LogicalQuery.SelfAndDescendants<Button>(second).First());
            });
            h.Main.OnMain(() =>
            {
                Settle(window);
                var reopened = presenter.Calls.Last(call => call.Presented).Popup;
                Assert.True(reopened.IsOpen);
                // The turn ends: the running block's card goes and the finished turn's folded answer shows the plan in a card of
                // its own (:2672-2674), another view in SwiftUI with a state and a coordinator of its own. The old popover goes
                // with the old card, and the new card starts shut.
                host.Children.Clear();
                third = new PlanCard(entry, h.Runtime, "folded");
                host.Children.Add(third);
            });
            h.Main.OnMain(() =>
            {
                Settle(window);
                Assert.False(presenter.Calls.Last().Presented);
                Assert.False(presenter.Calls.Last().Popup.IsOpen);
                Assert.Equal(2, presenter.Calls.Count(call => call.Presented)); // shown twice so far, the second time from the new card's pill
                Click(window, LogicalQuery.SelfAndDescendants<Button>(third).First());
            });
            h.Main.OnMain(() =>
            {
                Settle(window);
                Assert.Equal(3, presenter.Calls.Count(call => call.Presented)); // the folded card opens a popover of its own
                // The card leaves the timeline for good: the popover goes with it.
                host.Children.Clear();
            });
            h.Main.OnMain(() =>
            {
                Settle(window);
                Assert.False(presenter.Calls.Last().Presented);
                Assert.False(presenter.Calls.Last().Popup.IsOpen);
            });
        }
        finally { if (window is not null) h.Main.OnMain(() => Close(window)); }
    });

    // The other half of that identity is its place. The running turn's block is one view in SwiftUI, and the finished turn's
    // folded answer another (ThreadTimeline.swift:1950-1957 against TimelineRows.swift:2664-2674), so when the turn ends the
    // plan card of the running block goes with its popover and the folded answer's card is a new view with a state of its own.
    // The port's per-plan state must not follow the plan from the one into the other.
    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-113 (STOP #11); TimelineRows.swift:2261, :2664-2674; ChatBadge.swift:20, :74-75
    public void A_plan_popover_open_in_the_running_turn_goes_when_the_turn_folds_and_the_folded_answers_card_starts_shut() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var presenter = new TimelineRowsPrPopupPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var turn = Guid.NewGuid();
        var plan = PlanEntry(ProposedPlan.State.Proposed, turn: turn);
        var host = new Border();
        AvaloniaWindow window = null!;
        Popup running = null!;
        TurnFinishedBlock block = null!;
        try
        {
            h.Main.OnMain(() =>
            {
                _ = AppCurrentEvent.CurrentWindow; // the badge's window anchor reads the current event's window
                host.Child = new PlanCard(plan, h.Runtime) { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
                window = Show(host);
                Click(window, LogicalQuery.SelfAndDescendants<Button>(host.Child).First());
            });
            h.Main.OnMain(() =>
            {
                running = presenter.Calls.Last(call => call.Presented).Popup;
                Assert.True(running.IsOpen);
                // The turn ends and folds: tool, answer, then the plan, which the folded answer shows after the reply.
                block = new TurnFinishedBlock(h.Runtime, turn, Summary(turn), new ValueList<TimelineEntry>([UserEntry("do it", turn)]),
                    new ValueList<TimelineEntry>([ToolEntry(Call(ToolCall.Kind.Command, "make"), turn), Reply("```\nthe answer\n```"), plan]),
                    null, canUndo: false, chronological: false);
                host.Child = block;
            });
            h.Main.OnMain(() =>
            {
                Settle(window);
                Assert.False(running.IsOpen, "the running turn's plan popover stayed open over the folded answer's card");
                Assert.Single(presenter.Calls, call => call.Presented); // and the new card did not show one of its own
                var folded = Assert.Single(block.GetVisualDescendants().OfType<PlanCard>());
                Click(window, LogicalQuery.SelfAndDescendants<Button>(folded).First());
            });
            h.Main.OnMain(() =>
            {
                Settle(window);
                Assert.Equal(2, presenter.Calls.Count(call => call.Presented)); // the folded card opens a popover of its own
                Assert.True(presenter.Calls.Last(call => call.Presented).Popup.IsOpen);
            });
        }
        finally { if (window is not null) h.Main.OnMain(() => Close(window)); }
    });

    // ---------------------------------------------------------------- PR-CHAT-166

    private static void Drag(AvaloniaWindow window, Control fromControl, Point from, Control toControl, Point to)
    {
        var start = fromControl.TranslatePoint(from, window) ?? throw new InvalidOperationException("not mounted");
        var end = toControl.TranslatePoint(to, window) ?? throw new InvalidOperationException("not mounted");
        window.MouseMove(start, RawInputModifiers.None);
        window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(new Point((start.X + end.X) / 2, (start.Y + end.Y) / 2), RawInputModifiers.LeftMouseButton);
        window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        window.MouseUp(end, MouseButton.Left, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    // The pointer over the middle of the first box of some words of a text block.
    private static void MoveOver(AvaloniaWindow window, SelectableTextBlock text, string words)
    {
        var at = (text.Inlines?.Text ?? text.Text ?? "").IndexOf(words, StringComparison.Ordinal);
        Assert.True(at >= 0, $"\"{words}\" is not in the text");
        var box = text.TextLayout.HitTestTextRange(at, words.Length).First();
        var point = text.TranslatePoint(new Point(text.Padding.Left + box.X + box.Width / 2, text.Padding.Top + box.Y + box.Height / 2), window)
            ?? throw new InvalidOperationException("not mounted");
        window.MouseMove(point, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    // :123 the bubble, and every block of a reply (MarkdownView.swift:318, :1131, :1229, :1308 MarkdownBlockSelection on the
    // prose, the inline text, the code and the table), carry .textSelection(.enabled) of their own: a drag selects inside
    // the block it starts in and never runs on into the next block, and there is no whole-reply selection layer
    // (whole-reply copying is the row menu's Copy message and Copy as markdown, PR-CHAT-089). The windows_path's link text (review
    // round 5): a sent bubble with a bare domain (:119-123) and a report paragraph with a link (:633) are InlineText's link branch,
    // LinkParagraphView, an NSTextView that "shows the native pointing hand over links and the I-beam over prose" (LinkParagraphView.swift:4-8)
    // and takes its own drag (isSelectable, :312-313): the hand over the link, the I-beam over the prose, a drag that starts on the
    // prose selects the paragraph's words, and that selection is drawn in the system's selected-text colour (the brush the text block
    // draws it with). No network: each link's host has a seeded in-flight favicon fetch and the flow's client is fail-closed
    // (NoLinkNetwork, R-TEST-10).
    [Fact]
    [Trait("Req", "PR-CHAT-166")] // TimelineRows.swift:119-123, :633, :1364; MarkdownView.swift:318, :1131, :1229; LinkParagraphView.swift:4-8, :312-313
    [Trait("Req", "UI-CHAT-007")] // text selection within the sent bubble (:119-123)
    [Trait("Req", "UI-CHAT-019")] // text selection per block of the reply (:1364)
    public void A_drag_selects_inside_the_block_it_starts_in_across_a_reply_its_code_block_and_a_bubble() => headless.OnUiThread(() =>
    {
        using var noNetwork = new NoLinkNetwork("hover-hand.com", "hover-hand-report.com"); // R-TEST-10, ED-256 (10)
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        AvaloniaWindow window = null!;
        try
        {
            h.Main.OnMain(() =>
            {
                var reply = new AssistantMessageRow(Reply("First words of the answer.\n\nSecond paragraph of the answer.\n\n```\nlet code = 1\n```"), null, h.Runtime);
                var bubble = new UserMessageRow(UserEntry("plain words in a bubble", Guid.NewGuid()), h.Runtime, canRevert: false);
                var linkBubble = new UserMessageRow(UserEntry("see hover-hand.com for it", Guid.NewGuid()), h.Runtime, canRevert: false);
                var report = new HydraReportPopover("Plain words of the report.\n\nThe pipeline failed: see [the lint job](https://hover-hand-report.com/jobs/12).");
                window = Show(new StackPanel { Spacing = 40, Children = { reply, bubble, linkBubble, report } });
                Settle(window);
                var prose = Assert.Single(reply.GetVisualDescendants().OfType<ParagraphTextPresenter>()); // one prose run, both paragraphs
                var code = Assert.Single(Assert.Single(Assert.Single(reply.GetVisualDescendants().OfType<CodeBlock>()).GetVisualDescendants().OfType<VeiledText>()).GetVisualDescendants().OfType<SelectableTextBlock>()); // the code lines' text, in the block's VeiledText (the header's caption is a SelectableTextBlock too, selectable only where an ancestor enables text selection: ED-259)
                var words = Assert.Single(bubble.GetVisualDescendants().OfType<SelectableTextBlock>());

                // Across the reply: from the first paragraph down into the code block. The selection stays in the prose.
                Drag(window, prose, new Point(1, 6), code, new Point(code.Bounds.Width - 1, code.Bounds.Height / 2));
                Assert.StartsWith("First words", prose.SelectedText, StringComparison.Ordinal);
                Assert.True(string.IsNullOrEmpty(code.SelectedText), "the drag ran on into the code block");
                Assert.True(string.IsNullOrEmpty(words.SelectedText));

                // Across the code block: its own text, and nothing in the prose run above it.
                Drag(window, code, new Point(1, code.Bounds.Height / 2), code, new Point(code.Bounds.Width - 1, code.Bounds.Height / 2));
                Assert.Equal("let code = 1", code.SelectedText);

                // The sent bubble selects inside itself too (:123).
                Drag(window, words, new Point(1, words.Bounds.Height / 2), words, new Point(words.Bounds.Width - 1, words.Bounds.Height / 2));
                Assert.Equal("plain words in a bubble", words.SelectedText);

                // Link text: the bubble with a bare domain and the report's paragraph with a link.
                var system = SwiftColor.SelectedTextBackgroundColor.Resolve(window.ActualThemeVariant);
                Assert.NotEqual(0, system.A);
                var linkWords = Assert.Single(Assert.Single(linkBubble.GetVisualDescendants().OfType<LinkParagraphView>()).GetVisualDescendants().OfType<SelectableTextBlock>());
                var reportLink = Assert.Single(Assert.Single(report.GetVisualDescendants().OfType<LinkParagraphView>()).GetVisualDescendants().OfType<SelectableTextBlock>());
                foreach (var (text, proseWords, link, whole) in new[]
                {
                    (linkWords, "see", "hover-hand.com", "see hover-hand.com for it"),
                    (reportLink, "pipeline", "the lint job", "The pipeline failed: see the lint job."),
                })
                {
                    Assert.Equal(whole, text.Inlines!.Text);
                    MoveOver(window, text, link);
                    Assert.Equal("Hand", text.Cursor?.ToString()); // the pointing hand over the link
                    MoveOver(window, text, proseWords);
                    Assert.Equal("Ibeam", text.Cursor?.ToString()); // the I-beam over the prose
                    // A drag that starts on the prose selects the paragraph's words in the paragraph itself, never anything around it ...
                    Drag(window, text, new Point(1, text.Bounds.Height / 2), text, new Point(text.Bounds.Width - 1, text.Bounds.Height / 2));
                    Assert.Equal(whole, text.SelectedText);
                    // ... and the selection is drawn: in the system's selected-text colour, as every other text of the timeline draws it (a
                    // SelectableTextBlock has no SelectionBrush with no control theme loaded, so a selection made but never drawn is no
                    // selection to the reader; ED-256 (5), review round 4).
                    Assert.True((text.SelectionBrush as Avalonia.Media.ISolidColorBrush)?.Color == system,
                        $"\"{whole}\": the selection is drawn with {text.SelectionBrush?.ToString() ?? "no brush (nothing is drawn)"}");
                }
            });
        }
        finally { if (window is not null) h.Main.OnMain(() => Close(window)); }
    });

    // Review round 1 (2026-10-03): a text the reader can select shows the I-beam (the reference video, 2-chat-use.mov 14.74 s,
    // under the error notice's message; REVIEW_The_pointer_is_an_I_beam_over_selectable_notice_bubble_and_code_text_as_over_a_replys_prose
    // holds the notice, the bubble and a code block). The same .textSelection(.enabled) is on a tool's detail line and its output
    // (:2121, :2124, :2152): both are SelectableTextBlocks, which carry no cursor of their own with no control theme loaded.
    [Fact]
    [Trait("Req", "PR-CHAT-166")] // TimelineRows.swift:2121, :2124, :2152
    public void The_pointer_is_an_I_beam_over_the_selectable_detail_and_output_of_a_tool() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        AvaloniaWindow window = null!;
        try
        {
            h.Main.OnMain(() =>
            {
                var tool = new ToolDetailView(new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = "cat", ToolCallStatus = ToolCall.Status.Completed,
                    Detail = "cat notes.txt", Output = "the printed result" });
                window = Show(tool);
                var texts = tool.GetVisualDescendants().OfType<SelectableTextBlock>().ToArray();
                Assert.Equal(["cat notes.txt", "the printed result"], texts.Select(text => text.Text ?? "").ToArray());
                // Review round 4: a selection in either is drawn too, in the system's selected-text colour (a SelectableTextBlock has
                // no SelectionBrush with no control theme loaded; ED-256 (5)).
                var system = SwiftColor.SelectedTextBackgroundColor.Resolve(window.ActualThemeVariant);
                Assert.NotEqual(0, system.A);
                foreach (var text in texts)
                {
                    Hover(window, text);
                    Assert.Equal("Ibeam", text.Cursor?.ToString());
                    text.SelectAll();
                    Assert.Equal(text.Text, text.SelectedText);
                    Assert.True((text.SelectionBrush as Avalonia.Media.ISolidColorBrush)?.Color == system,
                        $"\"{text.Text}\": the selection is drawn with {text.SelectionBrush?.ToString() ?? "no brush (nothing is drawn)"}");
                }
            });
        }
        finally { if (window is not null) h.Main.OnMain(() => Close(window)); }
    });

    // PR-CHAT-166 / PR-CHAT-077: the veiled text of a bubble or a code block is selectable, and a selection must be drawn. The
    // SelectionBrush of a SelectableTextBlock is null with no control theme loaded, so a drag selected text that never showed
    // it (the same default LinkParagraphView's presenter overrides, ProseRunViewTests A_selection_paints_something_the_unselected_frame_does_not).
    [Fact]
    [Trait("Req", "PR-CHAT-166")] // TimelineRows.swift:123; MarkdownView.swift:318, :1131, :1229
    public void A_selection_in_a_bubble_and_in_a_code_block_is_drawn_in_the_system_selected_text_colour() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        AvaloniaWindow window = null!;
        try
        {
            h.Main.OnMain(() =>
            {
                var reply = new AssistantMessageRow(Reply("```\nlet code = 1\n```"), null, h.Runtime);
                var bubble = new UserMessageRow(UserEntry("plain words in a bubble", Guid.NewGuid()), h.Runtime, canRevert: false);
                window = Show(new StackPanel { Spacing = 40, Children = { reply, bubble } });
                Settle(window);
                var code = Assert.Single(Assert.Single(Assert.Single(reply.GetVisualDescendants().OfType<CodeBlock>()).GetVisualDescendants().OfType<VeiledText>()).GetVisualDescendants().OfType<SelectableTextBlock>()); // the code lines' text, in the block's VeiledText (the header's caption is a SelectableTextBlock too, selectable only where an ancestor enables text selection: ED-259)
                var words = Assert.Single(bubble.GetVisualDescendants().OfType<SelectableTextBlock>());
                var system = SwiftColor.SelectedTextBackgroundColor.Resolve(window.ActualThemeVariant);
                Assert.NotEqual(0, system.A);
                foreach (var (text, expected) in new[] { (code, "let code = 1"), (words, "plain words in a bubble") })
                {
                    Assert.Equal(system, Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(text.SelectionBrush).Color);
                    text.SelectAll();
                    Assert.Equal(expected, text.SelectedText);
                }
            });
        }
        finally { if (window is not null) h.Main.OnMain(() => Close(window)); }
    });

    // ---------------------------------------------------------------- PR-HYDRAMERGE-090

    // :1111-1120 the outcome pill's report: the merge card only when the body leads with a merge request address that
    // parses AND the details hold a files line; anything else, the generic report of the whole body. Opened by a real
    // click on the title, for the merged note and for the merge-failed note (TimelineRowsFinMergeOutcomeTests covers the
    // remaining three-way cases and the report's geometry). The generic report has text selection on (:633): every text in
    // it selects, and a selection is drawn in the system's selected-text colour, a paragraph with a link among them (the
    // third note: the shape of AppModel+HydraMerge.swift's merge-failed body, its error naming a job with a link; the link
    // text's NSTextView, LinkParagraphView.swift:312-313; review round 4).
    [Theory]
    [InlineData("Hydra merged !7.\nhttps://gitlab.com/g/p/-/merge_requests/7\n1 file (+2 −1) landed on `main` from `head`.", true)]
    [InlineData("Hydra opened !7 but could not merge it.\nhttps://gitlab.com/g/p/-/merge_requests/7\nThe pipeline failed on lint.", false)]
    [InlineData("Hydra opened !7 but could not merge it.\nhttps://gitlab.com/g/p/-/merge_requests/7\n\nThe pipeline failed: see [the lint job](https://gitlab.com/g/p/-/jobs/12).\n\nThe next try brings the request up to date with `main` and merges it.", false)]
    [Trait("Req", "PR-HYDRAMERGE-090")] // TimelineRows.swift:1111-1120, :607-640
    public void The_merged_note_opens_the_merge_card_and_the_merge_failed_note_the_generic_report(string text, bool merged) => headless.OnUiThread(() =>
    {
        // No network (R-TEST-10; ED-256 (10)): the merge card and the open report's paragraph ask gitlab.com for its
        // favicon, which is a seeded in-flight fetch here, and the rig refuses anything else.
        using var noNetwork = new NoLinkNetwork("gitlab.com");
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var presenter = new TimelineRowsPrPopupPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        AvaloniaWindow window = null!;
        try
        {
            h.Main.OnMain(() =>
            {
                var row = new HydraMergeRow(h.Runtime, new HydraMergePhase.Outcome(new UserMessage { Text = text }));
                window = Show(row);
                Click(window, row.GetVisualDescendants().OfType<Button>().Single(button => Equals(ToolTip.GetTip(button), "Show the message")));
            });
            h.Main.OnMain(() =>
            {
                Settle(window);
                var popup = presenter.Calls.Last(call => call.Presented).Popup;
                Assert.True(popup.IsOpen);
                Assert.Equal(PlacementMode.Bottom, popup.Placement); // :1111 arrowEdge: .bottom
                var shown = Unwrap(popup.Child!);
                if (merged) Assert.IsType<HydraMergePopover>(shown);
                else
                {
                    var report = Assert.IsType<HydraReportPopover>(shown);
                    // :1117 HydraReportPopover(text: outcome.body): the whole body past the title, its address included.
                    Assert.Equal(MarkdownView.Blocks(text[(text.IndexOf('\n', StringComparison.Ordinal) + 1)..]),
                        LogicalQuery.SelfAndDescendants<MarkdownBlockView>(report).Select(block => block.Block).ToArray());
                    // :633 .textSelection(.enabled): selectable, and a selection is drawn.
                    if (text.Contains("](", StringComparison.Ordinal)) Assert.NotEmpty(report.GetVisualDescendants().OfType<LinkParagraphView>());
                    var system = SwiftColor.SelectedTextBackgroundColor.Resolve(window.ActualThemeVariant);
                    Assert.NotEqual(0, system.A);
                    var texts = report.GetVisualDescendants().OfType<SelectableTextBlock>().ToArray();
                    var runs = report.GetVisualDescendants().OfType<ParagraphTextPresenter>().ToArray();
                    Assert.NotEmpty(texts.Cast<Control>().Concat(runs));
                    Assert.Empty(runs); // the report's blocks are MarkdownBlockViews (:625-628), so every text is a SelectableTextBlock
                    foreach (var selectable in texts)
                    {
                        // :630-631 the callout text style and the markdown point size, both 12: every text of the report is drawn at
                        // 12 points, a paragraph with a link included (LinkParagraphView(pointSize: scaled), MarkdownView.swift:1094,
                        // :1114 at B3; review round 5 found the link paragraph drawn at the 13-point default).
                        Assert.True(DrawnProseSizes(selectable).SequenceEqual([12.0]),
                            $"the report's \"{selectable.Inlines?.Text ?? selectable.Text}\" is drawn at {string.Join(", ", DrawnProseSizes(selectable))} points");
                        selectable.SelectAll();
                        Assert.False(string.IsNullOrEmpty(selectable.SelectedText));
                        Assert.True((selectable.SelectionBrush as Avalonia.Media.ISolidColorBrush)?.Color == system,
                            $"the report's \"{selectable.Inlines?.Text ?? selectable.Text}\": the selection is drawn with {selectable.SelectionBrush?.ToString() ?? "no brush (nothing is drawn)"}");
                    }
                    foreach (var run in runs)
                        Assert.True((run.SelectionBrush as Avalonia.Media.ISolidColorBrush)?.Color == system, "a prose run of the report draws its selection with another brush");
                }
            });
        }
        finally { if (window is not null) h.Main.OnMain(() => Close(window)); }
    });

    // ---------------------------------------------------------------- PR-CHAT-149

    // The lead's reply streams in with its fenced hydra block (MarkdownView.swift:321-333 hands a hydra/hydra-sent fence to
    // MarkdownDelegationCard.Render, set by the app at start-up, AppDelegate.swift:14), then the block is sent. At every
    // flush the reply shows the card, never the JSON; each head appears as its brief closes; Writing the next brief shows
    // while one is still open; once sent the verb is Summoned with the check.
    [Fact]
    // PR-CHAT-149: the reading, the titles, the rows and the never-JSON clauses hold here; the card keeps its identity across the flushes and
    // its changes animate with Chrome.panelSlide (:703-704) in TimelineRowsBlitzAbDelegationTests (OD-0098, 2026-10-04).
    [Trait("Req", "PR-CHAT-149")] // TimelineRows.swift:652-707, :711-741, :745-753, :771-782
    public void A_streaming_delegation_block_is_read_into_the_card_flush_by_flush_and_never_shows_its_json() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var clock = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(clock);
        var previous = MarkdownDelegationCard.Render;
        MarkdownDelegationCard.Render = (json, sent) => new HydraDelegationBlock(json, sent); // AppDelegate.cs:50 (:14)
        AvaloniaWindow window = null!;
        const string hank = "{\"task\": \"Tidy the popover\", \"prompt\": \"Tidy it.\", \"name\": \"Hank\"}";
        const string walter = "{\"task\": \"Write the tests\", \"prompt\": \"Write them.\", \"name\": \"Walter\"}";
        var flushes = new (string Text, bool Streaming, string Title, string[] Tasks, bool Next, bool Check)[]
        {
            ("Sending two heads.\n\n```hydra\n[\n{\"task\": \"Ti", true, "Summoning heads", [], false, false),
            ("Sending two heads.\n\n```hydra\n[\n" + hank + ",\n{\"task\": \"Wri", true, "Summoning heads", ["Tidy the popover"], true, false),
            ("Sending two heads.\n\n```hydra\n[\n" + hank + ",\n" + walter + "\n]\n```", true, "Summoning 2 heads", ["Tidy the popover", "Write the tests"], false, false),
            ("Sending two heads.\n\n```hydra-sent\n[\n" + hank + ",\n" + walter + "\n]\n```", false, "Summoned 2 heads", ["Tidy the popover", "Write the tests"], false, true),
        };
        try
        {
            var entry = Reply(flushes[0].Text);
            h.Main.OnMain(() =>
            {
                entry.Item = entry.Item with { TimelineItemContent = new TimelineItem.Content.Assistant(new AssistantMessage { Text = flushes[0].Text, IsStreaming = true }) };
                window = Show(new AssistantMessageRow(entry, null, h.Runtime));
            });
            foreach (var flush in flushes)
            {
                h.Main.OnMain(() =>
                {
                    entry.Item = entry.Item with { TimelineItemContent = new TimelineItem.Content.Assistant(new AssistantMessage { Text = flush.Text, IsStreaming = flush.Streaming }) };
                });
                Animate(h.Main, clock, 2000); // the card's changes are animated (:703-704): what each flush shows is read once they have settled
                h.Main.OnMain(() =>
                {
                    Settle(window);
                    var card = Assert.Single(window.GetVisualDescendants().OfType<HydraDelegationBlock>());
                    var words = window.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text ?? text.Inlines?.Text ?? "").ToArray();
                    Assert.DoesNotContain(words, text => text.Contains("\"task\"", StringComparison.Ordinal) || text.Contains("```", StringComparison.Ordinal));
                    Assert.Empty(window.GetVisualDescendants().OfType<CodeBlock>()); // the block is never drawn as code
                    Assert.Contains(flush.Title, LogicalQuery.SelfAndDescendants<TextBlock>(card).Select(text => text.Text));
                    foreach (var task in flush.Tasks) Assert.Contains(task, LogicalQuery.SelfAndDescendants<TextBlock>(card).Select(text => text.Text));
                    Assert.Equal(flush.Next, LogicalQuery.SelfAndDescendants<TextBlock>(card).Any(text => text.Text == "Writing the next brief…"));
                    Assert.Equal(flush.Check, LogicalQuery.SelfAndDescendants<SwiftImage>(card).Any(image => image.SymbolName == "checkmark.circle.fill"));
                });
            }
        }
        finally
        {
            h.Main.OnMain(() => { if (window is not null) Close(window); });
            MarkdownDelegationCard.Render = previous;
        }
    });

    // ---------------------------------------------------------------- PR-CHAT-077 (renders like the answers)

    // :119-122 InlineText(parts.body, hugsContent: true): the user's own words take the answers' inline renderer, so a bare
    // domain types as a link (MarkdownView.swift, B3: :1095-1126 the link branch, LinkParagraphView) and plain words stay the
    // selectable veiled text; the bubble hugs either. The link's favicon goes in front of it once fetched: the paragraph's
    // .task(id: hostKey) (:1123-1125) awaits the host's icon (fetchLinkAssets :1153-1164) and bumps @State faviconRevision
    // (:1077, the bump :1163), and LinkParagraphView(revision: faviconRevision) (:1114) draws the text again, the icon, one
    // space and then the link (LinkParagraphView.swift:36-58, :134-150). A bubble is on screen before the icon of a host it
    // names for the first time arrives, so the icon reaches it through that redraw. No network: the host's fetch is a seeded
    // in-flight task (NoLinkNetwork.Pending, R-TEST-10). The text of either bubble is selectable (:123), and a selection in it is drawn in the system's
    // selected-text colour (the link text's NSTextView, LinkParagraphView.swift:312-313, like the veiled text;
    // ED-256 (5)).
    [Fact]
    [Trait("Req", "PR-CHAT-077")] // TimelineRows.swift:118-129; MarkdownView.swift (B3) :1065-1164; LinkParagraphView.swift:36-58, :134-150, :312-313
    public void The_bubble_renders_its_words_with_the_answers_inline_renderer_so_a_bare_domain_is_a_link() => headless.OnUiThread(() =>
    {
        const string host = "bubble-favicon.com";
        using var noNetwork = new NoLinkNetwork(); // R-TEST-10, ED-256 (10): the host's fetch is the one it names below
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var icon = new Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(4, 4), new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Unpremul);
        var pending = noNetwork.Pending(host);
        AvaloniaWindow window = null!;
        UserMessageRow linked = null!, plain = null!;
        void Pump()
        {
            for (var pass = 0; pass < 10; pass++) { h.Main.RunUntilIdle(); Settle(window); }
        }
        Avalonia.Media.IImage?[] Icons(SelectableTextBlock text) => (text.Inlines ?? [])
            .OfType<Avalonia.Controls.Documents.InlineUIContainer>().Select(container => (container.Child as Image)?.Source).ToArray();
        try
        {
            h.Main.OnMain(() =>
            {
                linked = new UserMessageRow(UserEntry("see " + host + " for it", Guid.NewGuid()), h.Runtime, canRevert: false);
                plain = new UserMessageRow(UserEntry("plain words", Guid.NewGuid()), h.Runtime, canRevert: false);
                window = Show(new StackPanel { Spacing = 40, Children = { linked, plain } });
            });
            Pump();
            h.Main.OnMain(() =>
            {
                var linkText = Assert.Single(linked.GetVisualDescendants().OfType<InlineText>());
                Assert.True(linkText.HugsContent);
                Assert.Single(linkText.GetVisualDescendants().OfType<LinkParagraphView>());
                var plainText = Assert.Single(plain.GetVisualDescendants().OfType<InlineText>());
                Assert.True(plainText.HugsContent);
                Assert.Empty(plainText.GetVisualDescendants().OfType<LinkParagraphView>());
                Assert.Single(plainText.GetVisualDescendants().OfType<SelectableTextBlock>());
                // The icon is still on its way: the link stands alone, no icon and no space (LinkParagraphView.swift:134-150).
                Assert.Empty(Icons(Assert.Single(linkText.GetVisualDescendants().OfType<SelectableTextBlock>())));
            });
            // The fetch lands: image(for:) keeps the icon (MarkdownView.swift:896-898, B3) and hands it to the task that awaited it.
            using var stored = new HydraMergeFaviconSeed(host, icon, null);
            pending.SetResult(icon);
            Pump();
            h.Main.OnMain(() =>
            {
                var words = Assert.Single(linked.GetVisualDescendants().OfType<SelectableTextBlock>());
                var inlines = words.Inlines!.ToList();
                var container = Assert.Single(inlines.OfType<Avalonia.Controls.Documents.InlineUIContainer>());
                Assert.Same(icon, Assert.IsType<Image>(container.Child).Source); // the favicon ...
                var at = inlines.IndexOf(container);
                Assert.Equal(" ", Assert.IsType<Avalonia.Controls.Documents.Run>(inlines[at + 1]).Text); // ... one space ...
                Assert.Equal(host, Assert.IsType<Avalonia.Controls.Documents.Run>(inlines[at + 2]).Text); // ... in front of the link
                // :123 .textSelection(.enabled): selectable, and a selection is drawn in the system's selected-text colour.
                var system = SwiftColor.SelectedTextBackgroundColor.Resolve(window.ActualThemeVariant);
                Assert.NotEqual(0, system.A);
                foreach (var text in new[] { words, Assert.Single(plain.GetVisualDescendants().OfType<SelectableTextBlock>()) })
                {
                    text.SelectAll();
                    Assert.False(string.IsNullOrEmpty(text.SelectedText));
                    Assert.True((text.SelectionBrush as Avalonia.Media.ISolidColorBrush)?.Color == system,
                        $"\"{text.Inlines?.Text ?? text.Text}\": the selection is drawn with {text.SelectionBrush?.ToString() ?? "no brush (nothing is drawn)"}");
                }
            });
        }
        finally { if (window is not null) h.Main.OnMain(() => Close(window)); }
    });

    // ---------------------------------------------------------------- helpers

    private static Control Unwrap(Control content)
    {
        foreach (var control in LogicalQuery.SelfAndDescendants<Control>(content))
            if (control is HydraReportPopover or HydraMergePopover) return control;
        return content;
    }

    private static void RightClick(AvaloniaWindow window, Control on)
    {
        Settle(window);
        var point = on.TranslatePoint(new Point(4, 4), window) ?? throw new InvalidOperationException("not in the window");
        window.MouseMove(point, RawInputModifiers.None);
        window.MouseDown(point, MouseButton.Right, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Right, RawInputModifiers.None);
        Settle(window);
    }

    private static void ClickItem(AvaloniaWindow window, PopoverItem item) =>
        Click(window, LogicalQuery.SelfAndDescendants<Button>(item).First());

    private static string Title(PopoverItem item) => LogicalQuery.SelfAndDescendants<MiddleTruncatingText>(item).First().FullText;
}
