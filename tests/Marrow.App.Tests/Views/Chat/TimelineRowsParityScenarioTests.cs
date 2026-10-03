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
                Assert.True(items[2].IsConfirmed);
                Assert.Equal("Copied", Title(items[2]));
                ClickItem(menus[^1], items[3]);
            });
            h.Main.OnMain(() =>
            {
                Settle(menus[^1]);
                // :1406-1410 Add to reply confirms with Added and quotes the reply into the chat box's draft.
                Assert.True(items[3].IsConfirmed);
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
        Assert.True(popup.IsOpen);
        var content = popup.Child!;
        popup.Child = null;
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

    private static Control PencilLine(UserMessageRow row) =>
        row.GetVisualDescendants().OfType<Control>().Single(control => control.Height == TimelineMetrics.HoverLineHeight && double.IsNaN(control.Width));

    // :161 .opacity(isHovering || isEditing ? 1 : 0) cuts on the Mac (2-chat-use.mov 23.415-24.187 s: the pencil appears and
    // goes in one frame, catalogue hover.message-edit-pencil). OD-0076 rules that nothing cuts: the line fades in the Mac's
    // hover language, the reply row's own hover line (:1381 easeOut 0.12), and reduce motion stills it.
    [Fact]
    [Trait("Req", "PR-CHAT-080")] // TimelineRows.swift:158-161
    [Trait("Decision", "OD-0076")]
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
            h.Main.OnMain(() => Assert.InRange(PencilLine(row).Opacity, 0.01, 0.99));
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
            h.Main.OnMain(() => Assert.InRange(PencilLine(row).Opacity, 0.01, 0.99));
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
    [Fact]
    [Trait("Req", "PR-CHAT-113")] // TimelineRows.swift:2263-2316, :2287; ChatBadge.swift:20, :67-75, :138-147
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
                Assert.True(Assert.Single(LogicalQuery.SelfAndDescendants<MarkdownView>(popup.Child!)).IsStreaming);
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
                Assert.Contains("Implement plan", Words(reopened.Child!));
                Assert.Contains("Dismiss", Words(reopened.Child!));
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

    // :123 the bubble, and every block of a reply (MarkdownView.swift:318, :1131, :1229, :1308 MarkdownBlockSelection on the
    // prose, the inline text, the code and the table), carry .textSelection(.enabled) of their own: a drag selects inside
    // the block it starts in and never runs on into the next block, and there is no whole-reply selection layer
    // (whole-reply copying is the row menu's Copy message and Copy as markdown, PR-CHAT-089).
    [Fact]
    [Trait("Req", "PR-CHAT-166")] // TimelineRows.swift:123, :1364; MarkdownView.swift:318, :1131, :1229
    public void A_drag_selects_inside_the_block_it_starts_in_across_a_reply_its_code_block_and_a_bubble() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        AvaloniaWindow window = null!;
        try
        {
            h.Main.OnMain(() =>
            {
                var reply = new AssistantMessageRow(Reply("First words of the answer.\n\nSecond paragraph of the answer.\n\n```\nlet code = 1\n```"), null, h.Runtime);
                var bubble = new UserMessageRow(UserEntry("plain words in a bubble", Guid.NewGuid()), h.Runtime, canRevert: false);
                window = Show(new StackPanel { Spacing = 40, Children = { reply, bubble } });
                Settle(window);
                var prose = Assert.Single(reply.GetVisualDescendants().OfType<ParagraphTextPresenter>()); // one prose run, both paragraphs
                var code = Assert.Single(Assert.Single(reply.GetVisualDescendants().OfType<CodeBlock>()).GetVisualDescendants().OfType<SelectableTextBlock>());
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
            });
        }
        finally { if (window is not null) h.Main.OnMain(() => Close(window)); }
    });

    // ---------------------------------------------------------------- PR-HYDRAMERGE-090

    // :1111-1120 the outcome pill's report: the merge card only when the body leads with a merge request address that
    // parses AND the details hold a files line; anything else, the generic report of the whole body. Opened by a real
    // click on the title, for the merged note and for the merge-failed note (TimelineRowsFinMergeOutcomeTests covers the
    // remaining three-way cases and the report's geometry).
    [Theory]
    [InlineData("Hydra merged !7.\nhttps://gitlab.com/g/p/-/merge_requests/7\n1 file (+2 −1) landed on `main` from `head`.", true)]
    [InlineData("Hydra opened !7 but could not merge it.\nhttps://gitlab.com/g/p/-/merge_requests/7\nThe pipeline failed on lint.", false)]
    [Trait("Req", "PR-HYDRAMERGE-090")] // TimelineRows.swift:1111-1120, :607-640
    public void The_merged_note_opens_the_merge_card_and_the_merge_failed_note_the_generic_report(string text, bool merged) => headless.OnUiThread(() =>
    {
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
    // Partial proof PR-CHAT-149 (STOP #11): the reading, the titles, the rows and the never-JSON clauses hold here; the card's
    // changes do not animate with Chrome.panelSlide (:703-704), because every streamed flush rebuilds the reply's
    // MarkdownView and with it a new card (AssistantMessageRow.Body), where SwiftUI keeps the card's identity.
    [Trait("Rule", "R-TEST-4")] // TimelineRows.swift:652-707, :711-741, :745-753, :771-782
    public void A_streaming_delegation_block_is_read_into_the_card_flush_by_flush_and_never_shows_its_json() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
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
    // domain types as a link (MarkdownView.swift:1134-1191 LinkParagraphView, which puts the favicon in front once it is
    // fetched) and plain words stay the selectable veiled text; the bubble hugs either.
    [Fact]
    [Trait("Req", "PR-CHAT-077")] // TimelineRows.swift:118-129; MarkdownView.swift:1121-1208
    public void The_bubble_renders_its_words_with_the_answers_inline_renderer_so_a_bare_domain_is_a_link() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        AvaloniaWindow window = null!;
        try
        {
            h.Main.OnMain(() =>
            {
                var linked = new UserMessageRow(UserEntry("see example.com for it", Guid.NewGuid()), h.Runtime, canRevert: false);
                var plain = new UserMessageRow(UserEntry("plain words", Guid.NewGuid()), h.Runtime, canRevert: false);
                window = Show(new StackPanel { Spacing = 40, Children = { linked, plain } });
                var linkText = Assert.Single(linked.GetVisualDescendants().OfType<InlineText>());
                Assert.True(linkText.HugsContent);
                Assert.Single(linkText.GetVisualDescendants().OfType<LinkParagraphView>());
                var plainText = Assert.Single(plain.GetVisualDescendants().OfType<InlineText>());
                Assert.True(plainText.HugsContent);
                Assert.Empty(plainText.GetVisualDescendants().OfType<LinkParagraphView>());
                Assert.Single(plainText.GetVisualDescendants().OfType<SelectableTextBlock>());
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
