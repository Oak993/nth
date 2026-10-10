using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Views.Composer;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Store;
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

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), :2257-2338 PlanCard (with ChatBadge.swift:22-80).
[Collection("Headless")]
public sealed class TimelineRowsFinPlanTests(HeadlessSession headless)
{
    private static Control Detail(PlanCard card, ProposedPlan plan) =>
        Assert.IsAssignableFrom<Control>(typeof(PlanCard).GetMethod("Detail", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(card, [plan]));

    private static ProposedPlan PlanOf(TimelineEntry entry) => ((TimelineItem.Content.Plan)entry.Item.TimelineItemContent).Value;

    private static double Top(Control control, Visual root) => control.TranslatePoint(default, root)!.Value.Y;

    [Fact]
    [Trait("Req", "UI-CHAT-029")] // TimelineRows.swift:2263-2337; ChatBadge.swift:22-80
    public void The_plan_badge_is_a_tinted_pill_with_a_glyph_title_caption_and_chevron_for_each_of_its_four_states() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            var states = new[] { ProposedPlan.State.Drafting, ProposedPlan.State.Proposed, ProposedPlan.State.Accepted, ProposedPlan.State.Dismissed };
            var cards = states.Select(state => new PlanCard(PlanEntry(state), h.Runtime)).ToArray();
            var empty = new PlanCard(PlanEntry(ProposedPlan.State.Drafting, markdown: ""), h.Runtime);
            var panel = new StackPanel { Spacing = 30, Width = 700 };
            foreach (var card in cards) panel.Children.Add(card);
            panel.Children.Add(empty);
            var narrow = new PlanCard(PlanEntry(ProposedPlan.State.Proposed), h.Runtime);
            var narrowHost = new Border { Width = 250, Child = narrow };
            panel.Children.Add(narrowHost);
            var window = Show(panel);
            try
            {
                // :2318-2333 the words of each state.
                string[] Words(Control card) => Texts(card).Where(text => text.Length != 0).ToArray();
                Assert.Equal(["Writing a plan"], Words(cards[0]));
                Assert.Equal(["Plan ready", "Implement or dismiss"], Words(cards[1]));
                Assert.Equal(["Plan", "Approved"], Words(cards[2]));
                Assert.Equal(["Plan", "Dismissed"], Words(cards[3]));
                foreach (var card in cards)
                {
                    // ChatBadge :45-49 the pill: 12 leading, 14 trailing, 8 vertical, radius 18, the tint at 0.1.
                    var pill = Mounted<Border>(card).First(border => border.Padding == new Thickness(12, 8, 14, 8));
                    Assert.Equal(new CornerRadius(18), pill.CornerRadius);
                    Paints(pill.Background, ChromeTokens.Accent.Opacity(0.1), pill, "pill tint");
                    // :27-31 the title: callout medium in Chrome.primaryText at 0.9, one line.
                    var title = Mounted<TextBlock>(card).First(text => text.Text is "Writing a plan" or "Plan ready" or "Plan");
                    Face(title, 12, FontWeight.Medium, ".chat(.callout, weight: .medium)");
                    Paints(title.Foreground, ChromeTokens.PrimaryText.Opacity(0.9), title, "title");
                    Assert.Equal(1, title.MaxLines);
                    // :26 the glyph is framed 18 by 18, 8 points before the title.
                    var glyphSlot = (Control)((Visual)title.Parent!).GetVisualChildren().OfType<Control>().First();
                    Assert.Equal(18, glyphSlot.Bounds.Width);
                    Assert.Equal(18, glyphSlot.Bounds.Height);
                    Assert.Equal(8, Left(title, window) - (Left(glyphSlot, window) + glyphSlot.Bounds.Width), 0.5);
                    // :24 HStack(spacing: 8): glyph, title, caption and chevron share a centre line.
                    var items = ((Visual)title.Parent!).GetVisualChildren().OfType<Control>().ToArray();
                    OnOneCentreLine(window, items);
                    Assert.Contains(Mounted<SwiftImage>(card), image => image.SymbolName == "chevron.right");
                    var chevron = Mounted<SwiftImage>(card).Single(image => image.SymbolName == "chevron.right");
                    var chevronFont = SwiftImage.GetFont(chevron)!.Resolve();
                    Assert.Equal(10, chevronFont.FontSize);
                    Assert.Equal(FontWeight.SemiBold, chevronFont.FontWeight);
                    Paints(chevron.Foreground, ChromeTokens.SecondaryText, chevron, "chevron");
                    Assert.True(Assert.Single(Mounted<Button>(card)).IsEnabled);
                }
                // :33-37 the caption: caption type in Chrome.secondaryText.
                var caption = TextNamed(cards[1], "Implement or dismiss");
                Face(caption, 10, FontWeight.Normal, ".chat(.caption)");
                Paints(caption.Foreground, ChromeTokens.SecondaryText, caption, "caption");
                // :2272-2277 drafting: the working spinner in a 14-point frame; otherwise the clipboard drawn in the tint.
                var spinner = Assert.Single(Mounted<Marrow.UI.Port.Nth.NthStatusMark>(cards[0])); // OD-0136 amendment 2, step 8: the 14 px spinner of the nth status slot (was the 3-cell working spinner)
                Assert.Equal(18, Assert.IsType<Border>(spinner.Parent).Width); // ChatBadge gives its glyph an 18 x 18 frame (:26); the spinner inside is 14 Assert.Equal(Marrow.UI.Port.Nth.NthStatus.Working, spinner.Status); Assert.Equal(14, spinner.MarkSize);
                Assert.DoesNotContain(Mounted<SwiftImage>(cards[0]), image => image.SymbolName == "list.bullet.clipboard");
                foreach (var card in cards.Skip(1))
                {
                    var clipboard = Mounted<SwiftImage>(card).Single(image => image.SymbolName == "list.bullet.clipboard");
                    Paints(clipboard.Foreground, ChromeTokens.Accent, clipboard, "clipboard glyph");
                    Assert.Empty(Mounted<Marrow.UI.Port.Nth.NthStatusMark>(card));
                }
                // :2335 only a proposed plan draws the accent dot wave, along the top quarter of the badge (ChatBadge :55-61).
                Assert.Equal([false, true, false, false], cards.Select(card => Mounted<DotFieldControl>(card).Any()));
                var proposedPill = Mounted<Border>(cards[1]).First(border => border.Padding == new Thickness(12, 8, 14, 8));
                var wave = Assert.Single(Mounted<DotFieldControl>(cards[1]));
                Assert.Equal(proposedPill.Bounds.Height * 0.25, wave.Bounds.Height, 1);
                Assert.Equal("Needs your attention", Avalonia.Automation.AutomationProperties.GetItemStatus(Assert.Single(Mounted<Button>(cards[1]))));
                Assert.Equal("", Avalonia.Automation.AutomationProperties.GetItemStatus(Assert.Single(Mounted<Button>(cards[2]))));
                // :2268-2270 empty markdown: no chevron and the badge is disabled.
                Assert.DoesNotContain(Mounted<SwiftImage>(empty), image => image.SymbolName == "chevron.right");
                Assert.False(Assert.Single(Mounted<Button>(empty)).IsEnabled);
                // ChatBadge :75-76 full width lead-aligned with 96 points of trailing padding: in a 250-point column the pill
                // is cut 96 points short of the edge.
                var narrowPill = Mounted<Border>(narrow).First(border => border.Padding == new Thickness(12, 8, 14, 8));
                Assert.Equal(Left(narrowHost, window) + 250 - 96, Left(narrowPill, window) + narrowPill.Bounds.Width, 1);
                Assert.Equal(Left(narrowHost, window), Left(narrowPill, window), 0.5);
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-029")] // TimelineRows.swift:2265-2271 the click toggles the plan popover
    public void Clicking_the_plan_badge_opens_the_plan_popover_and_a_second_click_closes_it() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        PlanCard card = null!;
        AvaloniaWindow window = null!;
        h.Main.OnMain(() =>
        {
            _ = AppCurrentEvent.CurrentWindow; // starts the app-wide event watch the badge popover finds its window by
            card = new PlanCard(PlanEntry(ProposedPlan.State.Proposed), h.Runtime);
            window = Show(new StackPanel { Children = { card } });
            Click(window, Assert.Single(Mounted<Button>(card)));
        });
        h.Main.OnMain(() =>
        {
            try
            {
                Settle(window);
                if (presenter.Calls.Count == 0) Assert.Fail("no popover calls");
                var shown = Assert.Single(presenter.Calls, call => call.Presented);
                Assert.Equal(520, Mounted<Control>(shown.Popup.Child!).First(control => control.Width == 520).Width);
                Click(window, Assert.Single(Mounted<Button>(card)));
            }
            catch { Close(window); throw; }
        });
        h.Main.OnMain(() =>
        {
            try
            {
                Settle(window);
                Assert.Contains(presenter.Calls, call => !call.Presented);
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-030")] // TimelineRows.swift:2279-2313 the plan popover
    public void The_plan_panel_is_520_wide_with_a_headline_a_copy_button_a_420_point_scroller_and_the_two_actions_only_while_proposed() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            var proposed = PlanEntry(ProposedPlan.State.Proposed);
            var drafting = PlanEntry(ProposedPlan.State.Drafting);
            var accepted = PlanEntry(ProposedPlan.State.Accepted);
            var card = new PlanCard(proposed, h.Runtime);
            var detail = Detail(card, PlanOf(proposed));
            var host = new StackPanel { Spacing = 20 };
            host.Children.Add(detail);
            var window = Show(host);
            try
            {
                // :2311-2312 padded 16 and 520 wide.
                Assert.Equal(520, detail.Width);
                var inset = Assert.IsType<Border>(detail);
                Assert.Equal(new Thickness(16), inset.Padding);
                var column = Assert.IsType<StackPanel>(inset.Child);
                // :2280 the header, the markdown scroller and the actions are 12 points apart.
                Assert.Equal(12, column.Spacing);
                var header = Assert.IsType<DockPanel>(column.Children[0]);
                var scroll = Assert.IsType<PopoverScroll>(column.Children[1]);
                var actions = Assert.IsType<HStack>(column.Children[2]);
                Assert.Equal(12, Top(scroll, window) - (Top(header, window) + header.Bounds.Height), 0.5);
                Assert.Equal(12, Top(actions, window) - (Top(scroll, window) + scroll.Bounds.Height), 0.5);
                // :2282 the heading: Font.chat(.headline), which is semibold (ChatFont.swift:7), 13 points.
                var heading = TextNamed(header, "Plan");
                Face(heading, 13, FontWeight.SemiBold, ".chat(.headline)");
                // :2281-2284 HStack { Text, Spacer, CopyButton }: the text and the copy button share one centre line.
                var copy = Assert.Single(Mounted<CopyButton>(header));
                OnOneCentreLine(window, heading, copy);
                Assert.Equal(Left(header, window) + header.Bounds.Width, Left(copy, window) + copy.Bounds.Width, 0.5);
                Assert.Equal(Left(header, window), Left(heading, window), 0.5);
                // :2286-2289 the markdown scrolls in at most 420 points with 16 points of horizontal padding.
                var markdown = Assert.Single(Mounted<MarkdownView>(scroll));
                Assert.Equal(new Thickness(16, 0), markdown.Margin);
                Assert.True(Assert.IsType<ScrollViewer>(scroll.Child).Height <= 420);
                Assert.False(markdown.IsStreaming);
                // :2295 the action row: 8 points apart.
                Assert.Equal(8, actions.Spacing);
                Assert.Equal(["Implement plan", "Dismiss"], actions.Children.Select(button => Texts((Control)button).Single(text => text.Length != 0)));
                var implement = (Control)actions.Children[0];
                var dismiss = (Control)actions.Children[1];
                Assert.Equal(8, Left(dismiss, window) - (Left(implement, window) + implement.Bounds.Width), 0.5);
                // :2300, :2305 .glassProminent for Implement plan, .glass for Dismiss (OD-0005: the named material deviation tints the
                // prominent one with the accent and leaves the plain one clear).
                Border Face_(Control button) => Mounted<Border>(button).First(border => border.Child is TextBlock);
                Paints(Face_(implement).Background, ChromeTokens.Accent.Opacity(0.3), implement, "Implement plan is prominent");
                Assert.Null(Face_(dismiss).Background);
                // :2290 only while proposed; the plan being drafted streams its markdown; an accepted one has no actions.
                var streaming = Detail(new PlanCard(drafting, h.Runtime), PlanOf(drafting));
                Assert.True(Assert.Single(LogicalQuery.SelfAndDescendants<MarkdownView>(streaming)).IsStreaming);
                Assert.Equal(2, Assert.IsType<StackPanel>(Assert.IsType<Border>(streaming).Child).Children.Count);
                var done = Detail(new PlanCard(accepted, h.Runtime), PlanOf(accepted));
                Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<TextBlock>(done), text => text.Text is "Implement plan" or "Dismiss");
                Assert.Single(Assert.IsType<StackPanel>(Assert.IsType<Border>(done).Child).Children.OfType<DockPanel>());
                // :2286 a long plan scrolls in a panel of at most 420 points.
                var longPlan = PlanEntry(ProposedPlan.State.Proposed, string.Join("\n\n", Enumerable.Range(1, 80).Select(i => "paragraph " + i)));
                var long_ = Detail(new PlanCard(longPlan, h.Runtime), PlanOf(longPlan));
                host.Children.Add(long_);
                Settle(window);
                Assert.Equal(420, Assert.IsType<ScrollViewer>(Assert.Single(Mounted<PopoverScroll>(long_)).Child).Height);
                Assert.Equal(2, Assert.IsType<StackPanel>(Assert.IsType<Border>(done).Child).Children.Count);
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-030")] // TimelineRows.swift:2291-2308 greyed, not removed, while a turn runs
    [Trait("Req", "UI-CHAT-031")] // TimelineRows.swift:2296-2300, :2307 Implement plan is disabled while busy
    [Trait("Req", "UI-CHAT-032")] // TimelineRows.swift:2301-2305, :2307 Dismiss is disabled while busy
    public void The_plan_actions_stay_in_place_greyed_while_the_thread_is_busy_with_the_wait_tooltip_and_carry_none_when_free() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        Control detail = null!;
        AvaloniaWindow window = null!;
        StackPanel Actions() => Assert.IsType<HStack>(Assert.IsType<StackPanel>(Assert.IsType<Border>(detail).Child).Children[2]);
        h.Main.OnMain(() =>
        {
            var entry = PlanEntry(ProposedPlan.State.Proposed);
            detail = Detail(new PlanCard(entry, h.Runtime), PlanOf(entry));
            window = Show(detail);
            var actions = Actions();
            Assert.True(actions.IsEnabled);
            Assert.Null(ToolTip.GetTip(actions));
            Assert.All(actions.Children, button => Assert.True(((Control)button).IsEffectivelyEnabled));
            // :2294 a plan approval still pending is busy as well, with no turn running.
            var request = new ApprovalRequest("approval-1", ApprovalRequest.Kind.Plan, "Plan?", ValueList<ApprovalRequest.Option>.Empty);
            typeof(ThreadRuntime).GetProperty("Approvals")!.SetValue(h.Runtime, new ValueList<ApprovalRequest>([request]));
            Assert.False(h.Runtime.IsRunning);
        });
        h.Main.OnMain(() =>
        {
            try
            {
                Settle(window);
                var actions = Actions();
                // :2307-2308 greyed rather than removed: both buttons are still there, in place, and the tooltip says why.
                Assert.False(actions.IsEnabled);
                Assert.Equal("Wait for the current turn to finish", ToolTip.GetTip(actions));
                Assert.Equal(["Implement plan", "Dismiss"], actions.Children.Select(button => Texts((Control)button).Single(text => text.Length != 0)));
                Assert.All(actions.Children, button => Assert.False(((Control)button).IsEffectivelyEnabled));
                // The approval resolves: free again. Then a running turn is busy on its own.
                typeof(ThreadRuntime).GetProperty("Approvals")!.SetValue(h.Runtime, ValueList<ApprovalRequest>.Empty);
            }
            catch { Close(window); throw; }
        });
        h.Main.OnMain(() =>
        {
            try
            {
                Settle(window);
                Assert.True(Actions().IsEnabled);
                Assert.Null(ToolTip.GetTip(Actions()));
                h.Runtime.RehearseTurn("busy");
            }
            catch { Close(window); throw; }
        });
        h.Main.OnMain(() =>
        {
            try
            {
                Settle(window);
                Assert.False(Actions().IsEnabled);
                Assert.Equal("Wait for the current turn to finish", ToolTip.GetTip(Actions()));
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-031")] // TimelineRows.swift:2296-2300 Implement plan closes the popover, then implements
    [Trait("Req", "UI-CHAT-032")] // TimelineRows.swift:2301-2305 Dismiss closes the popover, then dismisses the plan
    public void Dismiss_and_Implement_plan_close_the_popover_first_and_then_dismiss_or_accept_the_plan() => headless.OnUiThread(() =>
    {
        Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        using var store = new AppModelHarness();
        var project = AppModelSamples.Project("Plans", store.NewFolder("plans"));
        var thread = AppModelSamples.Thread(project);
        store.WriteLibrary([project], [thread]);
        var turn = Guid.NewGuid();
        var items = new[] { ProposedPlan.State.Proposed, ProposedPlan.State.Proposed }
            .Select(state => new TimelineItem(turn, new TimelineItem.Content.Plan(new ProposedPlan { Markdown = "```\nstep one\n```", ProposedPlanState = state }))).ToArray();
        DiskWriter.Shared.WriteSynchronously(
            new ThreadDocument(thread.Id)
            {
                Turns = new ValueList<TurnRecord>([new TurnRecord { Id = turn, Index = 0, Status = TurnStatus.Completed }]),
                Items = new ValueList<TimelineItem>([new TimelineItem(turn, new TimelineItem.Content.User(new UserMessage { Text = "plan it" })), .. items]),
            },
            Storage.ThreadURL(thread.Id));
        var model = store.MakeModel();
        store.PrepareBootstrap(model);
        var runtime = store.Main.OnMain(() => model.Runtime(thread.Id));
        store.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        ProposedPlan.State StateOf(TimelineItem item) => ((TimelineItem.Content.Plan)runtime.Entries.Single(entry => entry.Id == item.Id).Item.TimelineItemContent).Value.ProposedPlanState;
        store.Main.OnMain(() =>
        {
            foreach (var (item, label, expected) in new[] { (items[0], "Dismiss", ProposedPlan.State.Dismissed), (items[1], "Implement plan", ProposedPlan.State.Accepted) })
            {
                var entry = runtime.Entries.Single(candidate => candidate.Id == item.Id);
                var card = new PlanCard(entry, runtime);
                var shown = typeof(PlanCard).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(card)!;
                var isShowing = shown.GetType().GetProperty("IsShowingPlan")!;
                isShowing.SetValue(shown, true);
                var detail = Detail(card, PlanOf(entry));
                var actions = Assert.IsType<HStack>(Assert.IsType<StackPanel>(Assert.IsType<Border>(detail).Child).Children[2]);
                var button = actions.Children.OfType<Button>().Single(candidate => LogicalQuery.SelfAndDescendants<TextBlock>(candidate).Any(text => text.Text == label));
                button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(false, isShowing.GetValue(shown)); // the popover closes first
                Assert.Equal(expected, StateOf(item)); // then the plan is dealt with
            }
            runtime.CancelPersistence();
        });
    });
}
