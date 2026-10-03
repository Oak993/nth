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
