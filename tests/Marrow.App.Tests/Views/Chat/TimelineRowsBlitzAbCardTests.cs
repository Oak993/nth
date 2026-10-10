using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
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

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), :2257-2338 PlanCard, :2340-2383 TodoListRow, :2385-2431 NoticeRow and
// :2433-2456 TurnEndRow. Where a record's capture comparison asks for a frame the reference videos do not contain, the Swift source is the
// proof (OD-0098): every state the code names is mounted and rendered, and the values the code states are read back from the layout and
// from the rendered frame. The pills and cards are drawn in a .continuous rounded rectangle, which ED-181 still draws with circular arcs:
// their corner is the one thing these renders do not claim.
[Collection("Headless")]
public sealed class TimelineRowsBlitzAbCardTests(HeadlessSession headless)
{
    private static readonly (ThemeVariant Theme, Color Backdrop)[] Themes =
    [
        (ThemeVariant.Dark, Color.FromRgb(24, 24, 27)),
        (ThemeVariant.Light, Color.FromRgb(244, 244, 246)),
    ];

    private static double Top(Control control, Visual root) => control.TranslatePoint(default, root)!.Value.Y;

    private static string[] Symbols(Control control) => Mounted<SwiftImage>(control).Select(image => image.SymbolName ?? "").ToArray();

    // ---- PR-CHAT-111: the four states of a plan badge ----

    [Fact]
    [Trait("Req", "PR-CHAT-111")] // TimelineRows.swift:2263-2278, :2318-2337; ChatBadge.swift:23-65; OD-0098: the four states drawn in both themes
    public void A_plan_badge_reads_its_states_title_and_caption_draws_the_spinner_or_the_clipboard_and_only_a_proposed_plan_wears_the_attention_dots() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            var cases = new (ProposedPlan.State State, string Title, string? Caption, bool Attention)[]
            {
                (ProposedPlan.State.Drafting, "Writing a plan", null, false),
                (ProposedPlan.State.Proposed, "Plan ready", "Implement or dismiss", true),
                (ProposedPlan.State.Accepted, "Plan", "Approved", false),
                (ProposedPlan.State.Dismissed, "Plan", "Dismissed", false),
            };
            foreach (var (theme, backdrop) in Themes)
            {
                foreach (var (state, title, caption, attention) in cases)
                {
                    var card = new PlanCard(PlanEntry(state), h.Runtime);
                    var host = new Border { Padding = new Thickness(24), Child = card };
                    BlitzAbRender.Render(host, theme, backdrop, 560, 90, (window, frame) =>
                    {
                        var texts = Texts(card);
                        Assert.Equal(caption is null ? [title] : [title, caption], texts); // :2266-2267, verbatim, no caption while drafting
                        var pill = Mounted<Border>(card).First(border => border.Padding == new Thickness(12, 8, 14, 8));
                        var box = BlitzAbRender.BoxIn(pill, window);
                        var fill = BlitzAbFrame.Over(ChromeTokens.Accent.Opacity(0.1).Resolve(theme), backdrop); // :49 .tint.opacity(0.1)
                        Assert.True(BlitzAbFrame.Distance(frame.At((int)box.Left + 5, (int)(box.Top + box.Height / 2)), fill) <= 6, $"{state} in {theme}: the pill's fill");
                        var heading = TextNamed(card, title);
                        BlitzAbRender.TextIsColour(frame, BlitzAbRender.BoxIn(heading, window), fill, ChromeTokens.PrimaryText.Opacity(0.9).Resolve(theme), $"{state} title in {theme}", closest: 120);
                        // :2272-2277 the glyph: the working spinner in a 14-point frame while drafting, else the tinted clipboard.
                        if (state == ProposedPlan.State.Drafting)
                        {
                            Assert.Single(Mounted<Marrow.UI.Port.Nth.NthStatusMark>(card)); // OD-0136 amendment 2, step 8: the nth status slot's spinner
                            Assert.DoesNotContain("list.bullet.clipboard", Symbols(card));
                        }
                        else
                        {
                            Assert.Empty(Mounted<Marrow.UI.Port.Nth.NthStatusMark>(card));
                            var clip = Assert.Single(Mounted<SwiftImage>(card), image => image.SymbolName == "list.bullet.clipboard");
                            BlitzAbRender.InkIsColour(frame, BlitzAbRender.BoxIn(clip, window), fill, ChromeTokens.Accent.Resolve(theme), $"{state} clipboard in {theme}", most: 1.6);
                        }
                        // The chevron and the enabled state follow the markdown (:2268-2270): this plan has markdown.
                        Assert.Contains("chevron.right", Symbols(card));
                        Assert.True(Assert.Single(Mounted<Button>(card)).IsEnabled);
                        // :2269, :2335-2337 only a proposed plan needs attention: the dot field over the pill's top quarter (ChatBadge :55-62).
                        var quarters = Mounted<Control>(card).Where(control => control.GetType().Name == "BadgeAttentionQuarter").ToList();
                        Assert.Equal(attention ? 1 : 0, quarters.Count);
                        if (attention) Assert.True(Math.Abs(box.Height * 0.25 - ((Decorator)quarters[0]).Child!.Bounds.Height) <= 0.75, $"{theme}: the dot field is {((Decorator)quarters[0]).Child!.Bounds.Height} tall in a {box.Height} pill");
                    });
                }
            }
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-111")] // TimelineRows.swift:2268-2270: a plan with empty markdown has no chevron and cannot be opened
    public void A_plan_with_empty_markdown_has_no_chevron_and_cannot_be_opened_and_gains_both_when_its_markdown_arrives() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        TimelineEntry entry = null!;
        PlanCard card = null!;
        AvaloniaWindow window = null!;
        h.Main.OnMain(() =>
        {
            entry = PlanEntry(ProposedPlan.State.Drafting, markdown: "");
            card = new PlanCard(entry, h.Runtime);
            window = Show(card);
            Assert.DoesNotContain("chevron.right", Symbols(card));
            var button = Assert.Single(Mounted<Button>(card));
            Assert.False(button.IsEnabled);
            Click(window, button);
        });
        h.Main.OnMain(() =>
        {
            Settle(window);
            Assert.DoesNotContain(presenter.Calls, call => call.Presented);
            entry.Item = entry.Item with { TimelineItemContent = new TimelineItem.Content.Plan(new ProposedPlan { Markdown = "step one", ProposedPlanState = ProposedPlan.State.Proposed }) };
        });
        h.Main.OnMain(() =>
        {
            Settle(window);
            Assert.Contains("chevron.right", Symbols(card));
            Assert.True(Assert.Single(Mounted<Button>(card)).IsEnabled);
            Close(window);
        });
    });

    // ---- PR-CHAT-112: the plan popover opened by a real click, its buttons pressed ----

    // The scroller has no control theme in this headless app: the minimal template a themed app has.
    private static void GiveScrollersATemplate(AvaloniaWindow window) =>
        window.Styles.Add(new Style(x => Selectors.OfType(x, typeof(ScrollViewer)))
        {
            Setters = { new Setter(Avalonia.Controls.Primitives.TemplatedControl.TemplateProperty, new Avalonia.Controls.Templates.FuncControlTemplate<ScrollViewer>((viewer, scope) =>
                new Avalonia.Controls.Presenters.ScrollContentPresenter { Name = "PART_ContentPresenter", CanVerticallyScroll = true, [!Avalonia.Controls.Presenters.ScrollContentPresenter.ContentProperty] = viewer[!ContentControl.ContentProperty] })) },
        });

    [Fact]
    [Trait("Req", "PR-CHAT-112")] // TimelineRows.swift:2279-2313; OD-0095 (content through PopoverHost.Content): the badge's click opens the 520-point popover with its buttons
    public void A_click_on_the_plan_badge_opens_the_520_point_popover_headed_Plan_with_a_copy_button_and_the_two_buttons_only_while_proposed() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var presenter = new TimelineRowsPrPopupPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var cards = new List<PlanCard>();
        AvaloniaWindow window = null!;
        var states = new[] { ProposedPlan.State.Proposed, ProposedPlan.State.Accepted, ProposedPlan.State.Dismissed, ProposedPlan.State.Drafting };
        h.Main.OnMain(() =>
        {
            _ = AppCurrentEvent.CurrentWindow; // the badge's window anchor reads the current event's window
            var panel = new StackPanel { Spacing = 20 };
            foreach (var state in states)
            {
                var card = new PlanCard(PlanEntry(state, markdown: "step one"), h.Runtime);
                cards.Add(card);
                panel.Children.Add(card);
            }
            window = Show(panel);
        });
        try
        {
            foreach (var (card, state) in cards.Zip(states))
            {
                h.Main.OnMain(() => Click(window, Assert.Single(Mounted<Button>(card))));
                h.Main.OnMain(() =>
                {
                    Settle(window);
                    var popup = presenter.Calls.Last(call => call.Presented).Popup;
                    Assert.True(popup.IsOpen);
                    var content = PopoverHost.Content(popup) ?? throw new Xunit.Sdk.XunitException("no content");
                    var sheet = Mounted<Control>(content).Prepend(content).First(control => control.Width == 520); // :2312 fixed at 520 points wide
                    Assert.Contains(Mounted<Decorator>(sheet).Prepend(sheet as Decorator), decorator => decorator?.Padding == new Thickness(16)); // :2311
                    Assert.Equal("Plan", TextNamed(content, "Plan").Text);
                    Assert.Single(Mounted<CopyButton>(content), copy => copy.GetVisualAncestors().OfType<DockPanel>().Any());
                    Assert.Single(Mounted<PopoverScroll>(content)); // the markdown's scroller (its 420-point height is measured below)
                    var buttons = Mounted<Button>(content).Select(button => Texts(button).FirstOrDefault(text => text is "Implement plan" or "Dismiss")).Where(text => text is not null).ToList();
                    Assert.Equal(state == ProposedPlan.State.Proposed ? ["Implement plan", "Dismiss"] : [], buttons);
                    Click(window, Assert.Single(Mounted<Button>(card))); // closes it again
                });
                h.Main.OnMain(() => Settle(window));
            }
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-112")] // TimelineRows.swift:2279-2313: the popover's own layout, measured (the builder's content hosted in a window)
    public void The_plan_popover_is_520_wide_padded_16_with_Plan_and_a_copy_button_over_a_420_point_scroller_then_the_two_buttons_12_and_8_points_apart() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            var markdown = string.Join(Environment.NewLine + Environment.NewLine, Enumerable.Range(1, 80).Select(i => "paragraph " + i));
            var entry = PlanEntry(ProposedPlan.State.Proposed, markdown: markdown);
            var content = Assert.IsAssignableFrom<Control>(Invoke(new PlanCard(entry, h.Runtime), "Detail", ((TimelineItem.Content.Plan)entry.Item.TimelineItemContent).Value));
            var shown = BlitzAbRender.Open(content, ThemeVariant.Dark, Color.FromRgb(24, 24, 27), 600, 900, GiveScrollersATemplate);
            try
            {
                BlitzAbRender.Capture(shown);
                var sheet = Mounted<Control>(content).Prepend(content).First(control => control.Width == 520);
                Assert.Equal(520, sheet.Bounds.Width, 0.5); // :2312 fixed at 520 points wide
                var heading = TextNamed(content, "Plan");
                Assert.Equal(Left(sheet, shown) + 16, Left(heading, shown), 0.5); // :2311 padded 16
                var header = Mounted<DockPanel>(content).First();
                Assert.Equal(Top(sheet, shown) + 16, Top(header, shown), 0.5);
                var copy = Assert.Single(Mounted<CopyButton>(header));
                Assert.Equal(Left(sheet, shown) + 520 - 16, Left(copy, shown) + copy.Bounds.Width, 0.5); // the copy button trails the header
                var scroll = Assert.Single(Mounted<PopoverScroll>(content));
                Assert.Equal(420, Assert.IsType<ScrollViewer>(scroll.Child).Bounds.Height, 0.5); // :2286 maxHeight 420: a tall plan fills it
                Assert.Equal(12, Top(scroll, shown) - (Top(header, shown) + header.Bounds.Height), 0.5); // :2280 spacing 12
                var buttons = Mounted<Button>(content).Where(button => Texts(button).Any(text => text is "Implement plan" or "Dismiss")).ToList();
                Assert.Equal(["Implement plan", "Dismiss"], buttons.Select(button => Texts(button).First(text => text.Length > 0)));
                Assert.Equal(12, Top(buttons[0], shown) - (Top(scroll, shown) + scroll.Bounds.Height), 0.75);
                Assert.Equal(8, Left(buttons[1], shown) - (Left(buttons[0], shown) + buttons[0].Bounds.Width), 0.75); // :2295 spacing 8
                Assert.Equal(Left(sheet, shown) + 16, Left(buttons[0], shown), 0.75);
            }
            finally { BlitzAbRender.Shut(shown); }
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-112")] // TimelineRows.swift:2296-2305: both buttons close the popover first, then implement or dismiss; closing leaves the plan proposed
    public void Dismiss_closes_the_popover_and_then_dismisses_the_plan_and_closing_without_choosing_leaves_it_proposed() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var presenter = new TimelineRowsPrPopupPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        PlanCard card = null!;
        TimelineEntry entry = null!;
        AvaloniaWindow window = null!;
        Popup popup = null!;
        ProposedPlan.State Plan() => ((TimelineItem.Content.Plan)entry.Item.TimelineItemContent).Value.ProposedPlanState;
        h.Main.OnMain(() =>
        {
            _ = AppCurrentEvent.CurrentWindow;
            h.Runtime.Rehearse(new ProviderEvent.PlanDelta("plan-a", "step one"));
            h.Runtime.Rehearse(new ProviderEvent.PlanCompleted("plan-a", "step one"));
            entry = h.Runtime.Entries.Single(item => item.Id == "plan-a");
            card = new PlanCard(entry, h.Runtime) { VerticalAlignment = VerticalAlignment.Top };
            window = Show(card);
            Assert.Equal(ProposedPlan.State.Proposed, Plan());
            Click(window, Assert.Single(Mounted<Button>(card)));
        });
        try
        {
            h.Main.OnMain(() =>
            {
                Settle(window);
                popup = presenter.Calls.Last(call => call.Presented).Popup;
                Assert.True(popup.IsOpen);
                Click(window, Assert.Single(Mounted<Button>(card))); // closing without choosing
            });
            h.Main.OnMain(() =>
            {
                Settle(window);
                Assert.False(popup.IsOpen);
                Assert.Equal(ProposedPlan.State.Proposed, Plan()); // the plan is as it was
                Click(window, Assert.Single(Mounted<Button>(card)));
            });
            h.Main.OnMain(() =>
            {
                Settle(window);
                popup = presenter.Calls.Last(call => call.Presented).Popup;
                Assert.True(popup.IsOpen);
                Mounted<Button>(PopoverHost.Content(popup)!).Single(button => Texts(button).Contains("Dismiss")).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(ProposedPlan.State.Dismissed, Plan()); // :2303 runtime.dismissPlan(entry.id)
            });
            h.Main.OnMain(() =>
            {
                Settle(window);
                Assert.False(popup.IsOpen); // :2302 the popover was closed first
                Assert.Equal(["Plan", "Dismissed"], Texts(card)); // :2319-2331
            });
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-112")] // TimelineRows.swift:2296-2299: Implement plan closes the popover, accepts the plan and starts the implementing turn
    public void Implement_plan_closes_the_popover_first_and_accepts_the_plan() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var presenter = new TimelineRowsPrPopupPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        PlanCard card = null!;
        TimelineEntry entry = null!;
        AvaloniaWindow window = null!;
        Popup popup = null!;
        h.Main.OnMain(() =>
        {
            _ = AppCurrentEvent.CurrentWindow;
            h.Runtime.Rehearse(new ProviderEvent.PlanDelta("plan-b", "step one"));
            h.Runtime.Rehearse(new ProviderEvent.PlanCompleted("plan-b", "step one"));
            entry = h.Runtime.Entries.Single(item => item.Id == "plan-b");
            card = new PlanCard(entry, h.Runtime) { VerticalAlignment = VerticalAlignment.Top };
            window = Show(card);
            Click(window, Assert.Single(Mounted<Button>(card)));
        });
        try
        {
            h.Main.OnMain(() =>
            {
                Settle(window);
                popup = presenter.Calls.Last(call => call.Presented).Popup;
                var implement = Mounted<Button>(PopoverHost.Content(popup)!).Single(button => Texts(button).Contains("Implement plan"));
                Assert.True(implement.IsEnabled);
                implement.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(ProposedPlan.State.Accepted, ((TimelineItem.Content.Plan)entry.Item.TimelineItemContent).Value.ProposedPlanState);
            });
            h.Main.OnMain(() =>
            {
                Settle(window);
                Assert.False(popup.IsOpen);
                Assert.Equal(["Plan", "Approved"], Texts(card));
            });
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    // ---- PR-CHAT-114: the checklist card ----

    [Fact]
    [Trait("Req", "PR-CHAT-114")] // TimelineRows.swift:2343-2382; OD-0098: the three statuses drawn in both themes
    public void The_checklist_card_is_a_quaternary_card_headed_N_of_M_done_with_tertiary_pending_accent_active_and_done_glyphs_and_a_struck_secondary_done_step_in_both_themes() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            foreach (var (theme, backdrop) in Themes)
            {
                var entry = new TimelineEntry(new TimelineItem(null, Steps(("Write the test", TodoStep.Status.Done), ("Fix the code", TodoStep.Status.Active), ("Ship it", TodoStep.Status.Pending))));
                var empty = new TodoListRow(new TimelineEntry(new TimelineItem(null, Steps())));
                var row = new TodoListRow(entry);
                var panel = new StackPanel { Spacing = 10 };
                panel.Children.Add(empty);
                panel.Children.Add(row);
                var host = new Border { Padding = new Thickness(24), Child = panel, Width = 640 };
                BlitzAbRender.Render(host, theme, backdrop, 640, 220, (window, frame) =>
                {
                    Assert.Empty(Mounted<Border>(empty)); // :2344 no steps: nothing at all, not an empty card
                    var card = Mounted<Border>(row).First(border => border.Padding == new Thickness(14));
                    Assert.Equal(new CornerRadius(18), card.CornerRadius); // :2364 pillRadius
                    var box = BlitzAbRender.BoxIn(card, window);
                    var fill = BlitzAbFrame.Over(SwiftColor.Quaternary.Opacity(0.32).Resolve(theme), backdrop);
                    Assert.True(BlitzAbFrame.Distance(frame.At((int)box.Left + 5, (int)(box.Top + box.Height / 2)), fill) <= 6, $"{theme}: the card's fill");
                    // :2368-2369 the row leads the card and keeps 96 points clear of the trailing edge, the card hugging its longest step.
                    Assert.Equal(Left(panel, window), box.Left, 0.5);
                    Assert.True(box.Width < 640 - 48 - 96, $"the card is {box.Width} wide: it does not hug its steps");
                    // :2346-2351 the header: the checklist glyph and the count, 6 apart, caption, secondary.
                    var count = TextNamed(row, "1 of 3 done");
                    BlitzAbRender.TextIsColour(frame, BlitzAbRender.BoxIn(count, window), fill, ChromeTokens.SecondaryText.Resolve(theme), $"the count in {theme}", closest: 130);
                    var headerGlyph = Assert.Single(Mounted<SwiftImage>(row), image => image.SymbolName == "checklist");
                    Assert.Equal(6, Left(count, window) - (Left(headerGlyph, window) + headerGlyph.Bounds.Width), 0.5);
                    Assert.Equal(box.Left + 14, Left(headerGlyph, window), 0.5);
                    // :2345 the steps sit 7 apart below the header.
                    var steps = new[] { "Write the test", "Fix the code", "Ship it" }.Select(text => TextNamed(row, text)).ToList();
                    Assert.Equal(7, Top(steps[0], window) - (Top(count, window) + count.Bounds.Height), 3.5);
                    // :2354-2355 the status glyphs: done and active in the tint, pending in .tertiary.
                    var glyphs = new[] { "checkmark.circle.fill", "circle.dotted.circle", "circle" }.Select(name => Assert.Single(Mounted<SwiftImage>(row), image => image.SymbolName == name)).ToList();
                    BlitzAbRender.InkIsColour(frame, BlitzAbRender.BoxIn(glyphs[0], window), fill, ChromeTokens.Accent.Resolve(theme), $"done glyph in {theme}", most: 1.6);
                    BlitzAbRender.InkIsColour(frame, BlitzAbRender.BoxIn(glyphs[1], window), fill, ChromeTokens.Accent.Resolve(theme), $"active glyph in {theme}", most: 1.6);
                    BlitzAbRender.InkIsColour(frame, BlitzAbRender.BoxIn(glyphs[2], window), fill, SwiftColor.Tertiary.Resolve(theme), $"pending glyph in {theme}", most: 1.6);
                    Assert.Equal(8, Left(steps[0], window) - (Left(glyphs[0], window) + glyphs[0].Bounds.Width), 0.5);
                    // :2357-2358 a done step is .secondary and struck through in .secondary; the others .primary and not struck.
                    var area = BlitzAbRender.BoxIn(steps[0], window);
                    BlitzAbRender.TextIsColour(frame, area, fill, SwiftColor.Secondary.Resolve(theme), $"the done step in {theme}", slack: 70, closest: 130); // the strike drawn over a letter doubles its coverage
                    var struck = Enumerable.Range((int)area.Top, (int)area.Height).Any(y =>
                        Enumerable.Range((int)area.Left, (int)area.Width).Count(x => BlitzAbFrame.Distance(frame.At(x, y), fill) > 60) >= area.Width * 0.9);
                    Assert.True(struck, $"{theme}: no line runs through the done step");
                    foreach (var other in steps.Skip(1))
                    {
                        var otherArea = BlitzAbRender.BoxIn(other, window);
                        Assert.False(Enumerable.Range((int)otherArea.Top, (int)otherArea.Height).Any(y =>
                            Enumerable.Range((int)otherArea.Left, (int)otherArea.Width).Count(x => BlitzAbFrame.Distance(frame.At(x, y), fill) > 60) >= otherArea.Width * 0.9), $"{theme}: a line runs through {other.Text}");
                        BlitzAbRender.TextIsColour(frame, otherArea, fill, SwiftColor.Primary.Resolve(theme), $"{other.Text} in {theme}", closest: 130);
                    }
                });
            }
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-114")] // TimelineRows.swift:2372 .animation(.snappy(duration: 0.2), value: steps), :2355-2358; OD-0076 (the glyph crossfades, the colours move; the card's size and the glyph and count crossfades are proved in TimelineRowsBlitzAbMotionTests)
    public void Checking_an_item_off_moves_the_same_lines_colours_along_the_snappy_animation_and_a_new_step_fades_in_without_adding_a_second_list() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var clock = new ManualFrameSource();
        using var clockSeam = FrameClock.Source.Override(clock);
        TimelineEntry entry = null!;
        TodoListRow row = null!;
        AvaloniaWindow window = null!;
        TextBlock one = null!, two = null!;
        SwiftImage twoGlyph = null!;
        double Effective(Control control)
        {
            var opacity = 1d;
            for (Visual? at = control; at is not null && !ReferenceEquals(at, row); at = at.GetVisualParent()) opacity *= at.Opacity;
            return opacity;
        }
        Color Ink(TextBlock text) => ((ISolidColorBrush)text.Foreground!).Color;
        void Frames(double milliseconds)
        {
            clock.Advance(TimeSpan.Zero);
            h.Main.AdvanceMilliseconds(milliseconds);
            clock.Advance(TimeSpan.FromMilliseconds(milliseconds));
        }
        h.Main.OnMain(() =>
        {
            entry = new TimelineEntry(new TimelineItem(null, Steps(("One", TodoStep.Status.Active), ("Two", TodoStep.Status.Pending))));
            row = new TodoListRow(entry);
            window = Show(new Border { Child = row, Width = 600 });
            one = TextNamed(row, "One"); two = TextNamed(row, "Two");
            twoGlyph = Mounted<SwiftImage>(row).Single(image => image.SymbolName == "circle");
            Assert.Equal(1, Mounted<StackPanel>(row).Count(stack => stack.Spacing == 7)); // one list
            entry.Item = entry.Item with { TimelineItemContent = Steps(("One", TodoStep.Status.Done), ("Two", TodoStep.Status.Active)) };
        });
        try
        {
            h.Main.OnMain(() => { });
            Frames(50);
            h.Main.OnMain(() =>
            {
                var primaryColour = SwiftColor.Primary.Resolve(window.ActualThemeVariant);
                var secondaryColour = SwiftColor.Secondary.Resolve(window.ActualThemeVariant);
                // :2357 the checked line's text moves from primary to secondary: part way along the animation, in neither.
                Assert.NotEqual(primaryColour, Ink(one));
                Assert.NotEqual(secondaryColour, Ink(one));
                Assert.Same(one, TextNamed(row, "One")); // the same line, not a new one
                // :2355 the next step's glyph moves from tertiary to the tint.
                // The step's own line (the first step's glyph, which was the dotted one, is still fading out in its own line while it turns to the check).
                var glyph = Mounted<SwiftImage>((Control)two.GetVisualParent()!).Single(image => image.SymbolName == "circle.dotted.circle");
                Assert.NotEqual(SwiftColor.Tertiary.Resolve(window.ActualThemeVariant), ((ISolidColorBrush)glyph.Foreground!).Color);
                Assert.NotEqual(ChromeTokens.Accent.Resolve(window.ActualThemeVariant), ((ISolidColorBrush)glyph.Foreground!).Color);
            });
            Frames(450);
            h.Main.OnMain(() =>
            {
                Assert.Equal(SwiftColor.Secondary.Resolve(window.ActualThemeVariant), Ink(one)); // settled: a done step in secondary
                Assert.Equal(SwiftColor.Primary.Resolve(window.ActualThemeVariant), Ink(two));
                var glyph = Mounted<SwiftImage>(row).Single(image => image.SymbolName == "circle.dotted.circle");
                Assert.Equal(ChromeTokens.Accent.Resolve(window.ActualThemeVariant), ((ISolidColorBrush)glyph.Foreground!).Color);
                Assert.Equal("1 of 2 done", Assert.Single(Mounted<TextBlock>(row), block => block.Text?.EndsWith("done", StringComparison.Ordinal) == true).Text);
                // A step added to the list: one more line in the same list, fading in rather than appearing.
                entry.Item = entry.Item with { TimelineItemContent = Steps(("One", TodoStep.Status.Done), ("Two", TodoStep.Status.Done), ("Three", TodoStep.Status.Pending)) };
            });
            h.Main.OnMain(() => { });
            Frames(30);
            h.Main.OnMain(() =>
            {
                var three = TextNamed(row, "Three");
                Assert.InRange(Effective(three), 0, 0.999); // arriving: not yet in
                Assert.Equal(1, Mounted<StackPanel>(row).Count(stack => stack.Spacing == 7));
                Assert.Same(one, TextNamed(row, "One"));
            });
            Frames(450);
            h.Main.OnMain(() =>
            {
                Assert.Equal(1, Effective(TextNamed(row, "Three")));
                Assert.Equal(3, Mounted<SwiftImage>(row).Count(image => image.SymbolName != "checklist"));
            });
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    // ---- PR-CHAT-115: the notice at three levels ----

    [Fact]
    [Trait("Req", "PR-CHAT-115")] // TimelineRows.swift:2388-2430; OD-0098: the three levels drawn in both themes (the pill's continuous corner is ED-181's)
    public void An_info_notice_is_bare_secondary_text_and_a_warning_or_error_a_tinted_pill_with_primary_text_and_the_themes_own_status_colours_in_both_themes() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            foreach (var (theme, backdrop) in Themes)
            {
                var levels = new (Notice.Level Level, string Symbol, SwiftColor Glyph, SwiftColor Text, SwiftColor? Tint)[]
                {
                    (Notice.Level.Info, "info.circle", SwiftColor.Secondary, SwiftColor.Secondary, null),
                    (Notice.Level.Warning, "exclamationmark.triangle", ChromeTokens.Warning, SwiftColor.Primary, ChromeTokens.Warning),
                    (Notice.Level.Error, "exclamationmark.octagon", ChromeTokens.Danger, SwiftColor.Primary, ChromeTokens.Danger),
                };
                var rows = levels.Select(level => new NoticeRow(NoticeEntry(level.Level, level.Level + " message of the notice"))).ToList();
                var panel = new StackPanel { Spacing = 16, Width = 560 };
                foreach (var row in rows) panel.Children.Add(row);
                var host = new Border { Padding = new Thickness(24), Child = panel };
                BlitzAbRender.Render(host, theme, backdrop, 608, 220, (window, frame) =>
                {
                    for (var i = 0; i < levels.Length; i++)
                    {
                        var (level, symbol, glyphColour, textColour, tint) = levels[i];
                        var row = rows[i];
                        var pill = Mounted<Border>(row).First(border => border.Child is TimelineBaselineRow);
                        var box = BlitzAbRender.BoxIn(pill, window);
                        Assert.Equal(560, box.Width, 0.5); // :2401 full width, lead-aligned
                        Assert.Equal(new CornerRadius(18), pill.CornerRadius); // :2402
                        var message = TextNamed(row, level + " message of the notice");
                        Assert.IsAssignableFrom<SelectableTextBlock>(message); // :2394 selectable at every level
                        Assert.Equal(12, message.FontSize); // :2397 callout
                        var glyph = Assert.Single(Mounted<SwiftImage>(row));
                        Assert.Equal(symbol, glyph.SymbolName); // :2406-2412
                        Assert.Equal(8, Left(message, window) - (Left(glyph, window) + glyph.Bounds.Width), 0.5); // :2390 spacing 8
                        Color under;
                        if (tint is { } status)
                        {
                            Assert.Equal(new Thickness(12, 8, 14, 8), pill.Padding); // :2398-2400
                            under = BlitzAbFrame.Over(status.Opacity(0.1).Resolve(theme), backdrop); // :2427-2428
                            Assert.True(BlitzAbFrame.Distance(frame.At((int)box.Left + 5, (int)(box.Top + box.Height / 2)), under) <= 6, $"{level} in {theme}: the pill's tint");
                            Assert.Equal(box.Left + 12, Left(glyph, window), 0.5);
                        }
                        else
                        {
                            Assert.Equal(default, pill.Padding); // :2398-2400 no insets at info
                            under = backdrop; // :2425 .clear
                            Assert.True(BlitzAbFrame.Distance(frame.At((int)box.Left + 5, (int)(box.Top + box.Height / 2)), backdrop) <= 6, $"{theme}: an info notice draws a background");
                            Assert.Equal(box.Left, Left(glyph, window), 0.5);
                        }
                        BlitzAbRender.InkIsColour(frame, BlitzAbRender.BoxIn(glyph, window), under, glyphColour.Resolve(theme), $"{level} glyph in {theme}", most: 1.6);
                        BlitzAbRender.TextIsColour(frame, BlitzAbRender.BoxIn(message, window), under, textColour.Resolve(theme), $"{level} message in {theme}", closest: 130);
                    }
                });
            }
        });
    });

    // 2-chat-use.mov C 9.0 (paired rows, so a reading is good to a point): the two error pills "No message found with message.uuid of: ..."
    // in the 820-point column are tinted over rows 276-308 and 420/422-454, 33 +-1 tall: one callout line of 17 (ED-290 (1), the explicit
    // .font(.chat(.callout)) on the row, TimelineRows.swift:2397) between pillVertical 8 above and below (:26, :2398-2400). The glyph, smaller
    // than the line in a first-baseline row (:2390), adds nothing. An info notice has no insets, so it is the 17-point line alone.
    [Fact]
    [Trait("Req", "PR-CHAT-115")] // TimelineRows.swift:2388-2403; ED-290 (1); 2-chat-use.mov C 9.0
    public void At_C_9_0_a_one_line_error_or_warning_notice_is_33_tall_and_an_info_notice_its_17_point_callout_line() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            const string message = "No message found with message.uuid of: 17366422-8494-489a-ab37-d976b3a0fb0b"; // C 9.0
            var (theme, backdrop) = Themes[0];
            var levels = new[] { Notice.Level.Error, Notice.Level.Warning, Notice.Level.Info };
            var rows = levels.Select(level => new NoticeRow(NoticeEntry(level, message))).ToList();
            var panel = new StackPanel { Spacing = 24, Width = 820 };
            foreach (var row in rows) panel.Children.Add(row);
            BlitzAbRender.Render(new Border { Padding = new Thickness(24), Child = panel }, theme, backdrop, 868, 200, (window, frame) =>
            {
                for (var i = 0; i < levels.Length; i++)
                {
                    var pill = Mounted<Border>(rows[i]).First(border => border.Child is TimelineBaselineRow);
                    var text = TextNamed(rows[i], message);
                    Assert.Single(text.TextLayout.TextLines); // one line, as at C 9.0
                    var box = BlitzAbRender.BoxIn(pill, window);
                    if (levels[i] == Notice.Level.Info)
                    {
                        Assert.Equal(17, box.Height, 0.5); // the bare callout line
                        continue;
                    }
                    Assert.InRange(box.Height, 32, 34);
                    // The tint as the frame shows it: the rows of the pill's flat middle that differ from the backdrop.
                    var x = (int)(box.Right - 60);
                    var tinted = Enumerable.Range((int)box.Top - 4, (int)box.Height + 8).Count(y => BlitzAbFrame.Distance(frame.At(x, y), backdrop) > 3);
                    Assert.InRange(tinted, 32, 34);
                }
            });
        });
    });

    // ---- PR-CHAT-116: the turn-end line ----

    [Theory]
    [Trait("Req", "PR-CHAT-116")] // TimelineRows.swift:2448-2455; Components.swift:36-41; ED-197 (swift-foundation 6.2 Duration.UnitsFormatStyle, en narrow); OD-0098
    [InlineData(TurnStatus.Completed, 0d, "Worked for 0s")]
    [InlineData(TurnStatus.Completed, -7d, "Worked for 0s")]
    [InlineData(TurnStatus.Completed, 0.4d, "Worked for 0s")]
    [InlineData(TurnStatus.Completed, 0.5d, "Worked for 1s")]
    [InlineData(TurnStatus.Completed, 1.4d, "Worked for 1s")]
    [InlineData(TurnStatus.Running, 45d, "Worked for 45s")]
    [InlineData(TurnStatus.Completed, 60d, "Worked for 1m")]
    [InlineData(TurnStatus.Completed, 64d, "Worked for 1m 4s")]
    [InlineData(TurnStatus.Completed, 90d, "Worked for 1m 30s")]
    [InlineData(TurnStatus.Completed, 3599d, "Worked for 59m 59s")]
    [InlineData(TurnStatus.Completed, 3600d, "Worked for 1h")]
    [InlineData(TurnStatus.Completed, 3725d, "Worked for 1h 2m")]
    [InlineData(TurnStatus.Interrupted, 0d, "Stopped after 0s")]
    [InlineData(TurnStatus.Interrupted, 64d, "Stopped after 1m 4s")]
    [InlineData(TurnStatus.Failed, 90d, "Failed after 1m 30s")]
    [InlineData(TurnStatus.Failed, 3725d, "Failed after 1h 2m")]
    public void The_turn_end_line_reads_its_phrasing_and_the_duration_rounded_clamped_and_formatted_as_the_source_states(TurnStatus status, double seconds, string expected)
    {
        using var locale = SwiftFormatters.CurrentLocale.Override("en_US");
        Assert.Equal(expected, TurnEndRow.Label(new TurnSummary { TurnID = Guid.NewGuid(), Status = status, Duration = seconds, FilesChanged = 0, Additions = 0, Deletions = 0 }));
    }

    [Fact]
    [Trait("Req", "PR-CHAT-116")] // TimelineRows.swift:2439-2445, :1368-1372, :2609-2617: the three places the line is drawn, in both themes
    public void The_line_is_drawn_in_caption_secondary_text_as_a_standalone_row_and_on_a_replys_hover_line_and_in_callout_as_the_folded_turns_header_and_never_beside_a_reply() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var locale = SwiftFormatters.CurrentLocale.Override("en_US");
        h.Main.OnMain(() =>
        {
            foreach (var (theme, backdrop) in Themes)
            {
                var turn = Guid.NewGuid();
                var summary = new TurnSummary { TurnID = turn, Status = TurnStatus.Failed, Duration = 83, FilesChanged = 0, Additions = 0, Deletions = 0 };
                var standalone = new TurnEndRow(summary, hasReply: false);
                var withReply = new TurnEndRow(summary, hasReply: true);
                var block = new TurnFinishedBlock(h.Runtime, turn, summary with { Status = TurnStatus.Completed }, ValueList<TimelineEntry>.Empty, new ValueList<TimelineEntry>([Reply("the answer")]), null, false, false);
                var panel = new StackPanel { Spacing = 24, Width = 560 };
                foreach (var item in new Control[] { standalone, withReply, block }) panel.Children.Add(item);
                var host = new Border { Padding = new Thickness(24), Child = panel };
                BlitzAbRender.Render(host, theme, backdrop, 608, 240, (window, frame) =>
                {
                    var label = TextNamed(standalone, "Failed after 1m 23s");
                    Assert.Equal(10, label.FontSize); // :2442 caption
                    Assert.Equal(Left(standalone, window), Left(label, window), 0.5); // :2444 full width, lead-aligned
                    BlitzAbRender.TextIsColour(frame, BlitzAbRender.BoxIn(label, window), backdrop, ChromeTokens.SecondaryText.Resolve(theme), $"the standalone line in {theme}", closest: 130);
                    Assert.Null(withReply.Child); // :2440 the duration lives on the reply's hover line instead
                    var header = TextNamed(block, "Worked for 1m 23s");
                    Assert.Equal(12, header.FontSize); // :2611 callout
                    BlitzAbRender.TextIsColour(frame, BlitzAbRender.BoxIn(header, window), backdrop, ChromeTokens.SecondaryText.Resolve(theme), $"the folded header in {theme}", closest: 130);
                });
            }
        });
    });
}
